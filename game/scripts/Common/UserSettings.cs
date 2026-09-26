using Godot;

namespace Headland.Game.Common;

/// <summary>
/// The player's settings in user://settings.cfg (a Godot ConfigFile: INI sections, Godot value syntax). Read once
/// at startup, written with the defaults when missing so it can be found and edited; a missing or invalid value
/// falls back to its default. The settings screen will edit these and call <see cref="Save"/>.
/// </summary>
public sealed class UserSettings
{
    public const string DefaultPath = "user://settings.cfg";

    public static readonly string[] WindowModes = ["windowed", "maximized", "fullscreen", "exclusive_fullscreen"];
    public static readonly string[] AntialiasingModes = ["off", "fxaa", "msaa2", "msaa4"];
    public static readonly string[] ShadowLevels = ["off", "low", "medium", "high"];
    /// <summary>Audio buses by setting name (only Master exists until the audio work adds the others).</summary>
    public static readonly (string setting, string bus)[] Buses =
        [("master", "Master"), ("music", "Music"), ("vehicles", "Vehicles"), ("environment", "Environment"), ("ui", "UI")];

    // [graphics]
    public string WindowMode { get; set; } = "windowed";
    /// <summary>Window size in windowed mode.</summary>
    public Vector2I Resolution { get; set; } = new(1600, 900);
    public bool Vsync { get; set; } = true;
    /// <summary>Frame rate cap; 0 = none.</summary>
    public int MaxFps { get; set; }
    /// <summary>3D resolution relative to the window (0.5..1); below 1 uses FSR upscaling.</summary>
    public float RenderScale { get; set; } = 1f;
    public string Antialiasing { get; set; } = "fxaa";
    public string Shadows { get; set; } = "high";

    // [audio]: linear volumes 0..1 per bus.
    public Dictionary<string, float> Volumes { get; } = new()
    {
        ["master"] = 1f, ["music"] = 0.7f, ["vehicles"] = 1f, ["environment"] = 1f, ["ui"] = 1f,
    };

    // [controls]: action → physical key, named as on a US QWERTY keyboard (Godot key names, e.g. "W", "Shift+Tab").
    public Dictionary<string, Key> Keys { get; } = InputSetup.Bindings.ToDictionary(b => b.action, b => b.key);

    // [gameplay]
    /// <summary>Real minutes between autosaves; 0 turns autosave off.</summary>
    public float AutosaveMinutes { get; set; } = 10f;

    public static UserSettings Load(string path = DefaultPath)
    {
        var s = new UserSettings();
        var cfg = new ConfigFile();
        var err = cfg.Load(path);
        if (err == Error.FileNotFound)
        {
            s.Save(path);
            return s;
        }
        if (err != Error.Ok)
        {
            GD.PushWarning($"{path}: cannot read ({err}); using the default settings");
            return s;
        }

        s.WindowMode = OneOf(cfg.GetValue("graphics", "window_mode", s.WindowMode).AsString(), WindowModes, s.WindowMode);
        var res = cfg.GetValue("graphics", "resolution", s.Resolution).AsVector2I();
        if (res is { X: >= 640, Y: >= 360 }) s.Resolution = res;
        s.Vsync = cfg.GetValue("graphics", "vsync", s.Vsync).AsBool();
        s.MaxFps = Math.Clamp(cfg.GetValue("graphics", "max_fps", s.MaxFps).AsInt32(), 0, 1000);
        s.RenderScale = Math.Clamp(cfg.GetValue("graphics", "render_scale", s.RenderScale).AsSingle(), 0.5f, 1f);
        s.Antialiasing = OneOf(cfg.GetValue("graphics", "antialiasing", s.Antialiasing).AsString(), AntialiasingModes, s.Antialiasing);
        s.Shadows = OneOf(cfg.GetValue("graphics", "shadows", s.Shadows).AsString(), ShadowLevels, s.Shadows);

        foreach (var (setting, _) in Buses)
            s.Volumes[setting] = Math.Clamp(cfg.GetValue("audio", setting, s.Volumes[setting]).AsSingle(), 0f, 1f);

        foreach (var (action, _, _) in InputSetup.Bindings)
        {
            if (!cfg.HasSectionKey("controls", action)) continue;
            var name = cfg.GetValue("controls", action).AsString();
            var key = OS.FindKeycodeFromString(name);
            if (key != Key.None) s.Keys[action] = key;
            else GD.PushWarning($"{path}: unknown key '{name}' for {action}; keeping {OS.GetKeycodeString(s.Keys[action])}");
        }

        s.AutosaveMinutes = Math.Clamp(cfg.GetValue("gameplay", "autosave_minutes", s.AutosaveMinutes).AsSingle(), 0f, 240f);
        return s;
    }

    public void Save(string path = DefaultPath)
    {
        var cfg = new ConfigFile();
        cfg.SetValue("graphics", "window_mode", WindowMode);
        cfg.SetValue("graphics", "resolution", Resolution);
        cfg.SetValue("graphics", "vsync", Vsync);
        cfg.SetValue("graphics", "max_fps", MaxFps);
        cfg.SetValue("graphics", "render_scale", RenderScale);
        cfg.SetValue("graphics", "antialiasing", Antialiasing);
        cfg.SetValue("graphics", "shadows", Shadows);
        foreach (var (setting, _) in Buses) cfg.SetValue("audio", setting, Volumes[setting]);
        foreach (var (action, _, _) in InputSetup.Bindings) cfg.SetValue("controls", action, OS.GetKeycodeString(Keys[action]));
        cfg.SetValue("gameplay", "autosave_minutes", AutosaveMinutes);
        var err = cfg.Save(path);
        if (err != Error.Ok) GD.PushWarning($"{path}: cannot write ({err})");
    }

    /// <summary>Window, frame pacing, 3D resolution, anti-aliasing and the directional shadow atlas.</summary>
    public void ApplyDisplay(Viewport viewport)
    {
        switch (WindowMode)
        {
            case "fullscreen": DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen); break;
            case "exclusive_fullscreen": DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen); break;
            case "maximized": DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized); break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(Resolution);
                var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
                DisplayServer.WindowSetPosition(screen.Position + (screen.Size - Resolution) / 2);
                break;
        }
        DisplayServer.WindowSetVsyncMode(Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = MaxFps;

        viewport.Scaling3DScale = RenderScale;
        viewport.Scaling3DMode = RenderScale < 0.999f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        viewport.ScreenSpaceAA = Antialiasing == "fxaa" ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.Msaa3D = Antialiasing switch
        {
            "msaa2" => Viewport.Msaa.Msaa2X,
            "msaa4" => Viewport.Msaa.Msaa4X,
            _ => Viewport.Msaa.Disabled,
        };

        var (atlas, filter) = Shadows switch
        {
            "low" => (1024, RenderingServer.ShadowQuality.Hard),
            "medium" => (2048, RenderingServer.ShadowQuality.SoftVeryLow),
            _ => (4096, RenderingServer.ShadowQuality.SoftLow),
        };
        RenderingServer.DirectionalShadowAtlasSetSize(atlas, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(filter);
    }

    /// <summary>Sets every existing bus's volume (buses added later pick theirs up the same way).</summary>
    public void ApplyAudio()
    {
        foreach (var (setting, bus) in Buses)
        {
            var index = AudioServer.GetBusIndex(bus);
            if (index < 0) continue;
            var volume = Volumes[setting];
            AudioServer.SetBusMute(index, volume <= 0f);
            AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(volume, 0.0001f)));
        }
    }

    private static string OneOf(string value, string[] allowed, string fallback) => allowed.Contains(value) ? value : fallback;
}

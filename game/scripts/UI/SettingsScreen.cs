using Headland.Core;
using Headland.Core.Input;
using Headland.Game.Common;
using Headland.Game.Controls;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The settings (FS: the settings pages), from the main menu or the in-game menu's game tab: graphics, audio volumes, the
/// controls (rebinding each action's keys, buttons and gamepad inputs) and gameplay. Each change applies at once and goes
/// into settings.cfg.
/// </summary>
public partial class SettingsScreen : Screen
{
    private static readonly string[] Tabs = ["Graphics", "Audio", "Controls", "Gameplay"];
    private static int _tab;

    private readonly List<Button> _tabs = [];
    private ScreenFrame _frame = null!;
    private VBoxContainer? _page;
    /// <summary>The binding being changed: the action, and which of its bindings (-1: a new one).</summary>
    private (string action, int index)? _capture;
    private Label _captureHint = null!;

    public UserSettings Settings { get; init; } = UserSettings.Current;
    /// <summary>The game under the menu, if there's one: the frame shows its date and balance.</summary>
    public Simulation? Sim { get; init; }
    /// <summary>Called after a setting changed, for the game to follow it (the autosave, the minimap, the shadows).</summary>
    public Action? Applied { get; init; }

    public override bool CoversView => true;

    protected override void Build()
    {
        _frame = new ScreenFrame { Sim = Sim, Hints = [(GameActions.Menu, "Back")] };
        var group = new ButtonGroup();
        _frame.Header.AddChild(Widgets.Label("Settings", "TitleLabel"));
        for (var i = 0; i < Tabs.Length; i++)
        {
            var index = i;
            var tab = Widgets.Tab(Tabs[i], group, false, () => Show(index));
            _tabs.Add(tab);
            _frame.Header.AddChild(tab);
        }
        AddChild(_frame);
        Show(Math.Clamp(_tab, 0, Tabs.Length - 1));
    }

    /// <summary>Shows the tab titled <paramref name="title"/>, if there's one.</summary>
    public void ShowTab(string title)
    {
        if (Array.IndexOf(Tabs, title) is var index and >= 0) Show(index);
    }

    private void Show(int index)
    {
        _tab = index;
        _capture = null;
        _tabs[index].ButtonPressed = true;
        if (_page != null)
        {
            _frame.Body.RemoveChild(_page);
            _page.QueueFree();
        }
        _page = new VBoxContainer { ThemeTypeVariation = "DialogBox", SizeFlagsVertical = SizeFlags.ExpandFill };
        switch (index)
        {
            case 0: Graphics(_page); break;
            case 1: Audio(_page); break;
            case 2: Controls(_page); break;
            default: Gameplay(_page); break;
        }
        _frame.Body.AddChild(_page);
    }

    /// <summary>Keeps a change: in the file, and in the game.</summary>
    private void Changed()
    {
        Settings.Save();
        Applied?.Invoke();
    }

    private void ApplyWindow()
    {
        Settings.ApplyWindow();
        Changed();
    }

    private void ApplyRendering()
    {
        Settings.ApplyRendering(GetViewport());
        Changed();
    }

    // ------------------------------------------------------------------ Pages

    private void Graphics(VBoxContainer page)
    {
        _frame.Subtitle.Text = "The window and how the world is drawn. The resolution is the window's size, when windowed.";
        var s = Settings;
        var table = Table(page);
        Row(table, "Window", Widgets.Dropdown(["Windowed", "Maximized", "Fullscreen", "Exclusive fullscreen"], Array.IndexOf(UserSettings.WindowModes, s.WindowMode), i =>
        {
            s.WindowMode = UserSettings.WindowModes[i];
            ApplyWindow();
        }));
        var sizes = UserSettings.Resolutions.Contains(s.Resolution) ? UserSettings.Resolutions : [.. UserSettings.Resolutions, s.Resolution];
        Row(table, "Resolution", Widgets.Dropdown(sizes.Select(r => $"{r.X} × {r.Y}"), Array.IndexOf(sizes, s.Resolution), i =>
        {
            s.Resolution = sizes[i];
            ApplyWindow();
        }));
        Row(table, "Vertical sync", Check(s.Vsync, on =>
        {
            s.Vsync = on;
            ApplyRendering();
        }));
        var caps = UserSettings.FpsCaps.Contains(s.MaxFps) ? UserSettings.FpsCaps : [.. UserSettings.FpsCaps, s.MaxFps];
        Row(table, "Frame rate cap", Widgets.Dropdown(caps.Select(c => c == 0 ? "None" : $"{c} fps"), Array.IndexOf(caps, s.MaxFps), i =>
        {
            s.MaxFps = caps[i];
            ApplyRendering();
        }));
        Row(table, "Render scale", Slider(s.RenderScale, 0.5f, 1f, 0.05f, v => $"{v * 100f:0}%", v =>
        {
            s.RenderScale = v;
            ApplyRendering();
        }), "Below 100%, FSR upscales a smaller image: faster, softer.");
        Row(table, "Anti-aliasing", Widgets.Dropdown(["Off", "FXAA", "MSAA 2×", "MSAA 4×"], Array.IndexOf(UserSettings.AntialiasingModes, s.Antialiasing), i =>
        {
            s.Antialiasing = UserSettings.AntialiasingModes[i];
            ApplyRendering();
        }));
        Row(table, "Shadows", Widgets.Dropdown(["Off", "Low", "Medium", "High"], Array.IndexOf(UserSettings.ShadowLevels, s.Shadows), i =>
        {
            s.Shadows = UserSettings.ShadowLevels[i];
            ApplyRendering();
        }));
    }

    private void Audio(VBoxContainer page)
    {
        _frame.Subtitle.Text = "Volumes, from silent to full.";
        var table = Table(page);
        foreach (var (setting, _) in UserSettings.Buses)
        {
            var name = setting == "ui" ? "Interface" : $"{char.ToUpperInvariant(setting[0])}{setting[1..]}";
            Row(table, name, Slider(Settings.Volumes[setting], 0f, 1f, 0.05f, v => $"{v * 100f:0}%", v =>
            {
                Settings.Volumes[setting] = v;
                Settings.ApplyAudio();
                Changed();
            }));
        }
    }

    private void Gameplay(VBoxContainer page)
    {
        _frame.Subtitle.Text = "How the game runs, and what the HUD shows.";
        var s = Settings;
        var table = Table(page);
        var steps = UserSettings.AutosaveSteps.Contains(s.AutosaveMinutes) ? UserSettings.AutosaveSteps : [.. UserSettings.AutosaveSteps, s.AutosaveMinutes];
        Row(table, "Autosave", Widgets.Dropdown(steps.Select(m => m <= 0f ? "Off" : $"Every {m:0} minutes"), Array.IndexOf(steps, s.AutosaveMinutes), i =>
        {
            s.AutosaveMinutes = steps[i];
            Changed();
        }));
        Row(table, "Pause in menus", Check(s.PauseInMenus, on =>
        {
            s.PauseInMenus = on;
            Changed();
        }), "Time stands still while the menu or the shop covers the view.");
        Row(table, "Minimap", Widgets.Dropdown(["Small", "Large", "Off"], Array.IndexOf(UserSettings.MinimapSizes, s.Minimap), i =>
        {
            s.Minimap = UserSettings.MinimapSizes[i];
            Changed();
        }), $"{InputLayer.Label(GameActions.Minimap)} steps through them in the game.");
    }

    /// <summary>Every action with its bindings, by where it works: a binding clicked takes the next key pressed.</summary>
    private void Controls(VBoxContainer page)
    {
        _frame.Subtitle.Text = "Click a binding, then press the key, mouse button or gamepad input for it. Keys go by their place on the keyboard.";
        _captureHint = Widgets.Label(variation: "DimLabel");
        page.AddChild(_captureHint);
        ShowCapture();
        var table = new GridContainer { Columns = 3, ThemeTypeVariation = "TableGrid" };
        foreach (var (title, holds, _) in ControlsPage.Sections)
        {
            var actions = Settings.Controls.Actions.Where(a => holds(a.Contexts)).ToList();
            if (actions.Count == 0) continue;
            table.AddChild(Widgets.Label(title, "StrongLabel"));
            table.AddChild(Widgets.Label());
            table.AddChild(Widgets.Label());
            foreach (var a in actions)
            {
                table.AddChild(Widgets.Label(a.Help));
                var keys = new HBoxContainer();
                var bindings = Settings.Controls.Of(a.Id);
                for (var k = 0; k < bindings.Count; k++)
                {
                    var index = k;
                    keys.AddChild(Widgets.Button(InputLayer.Label(bindings[k]), () => Capture(a.Id, index)));
                }
                keys.AddChild(Widgets.Button("+", () => Capture(a.Id, -1)));
                table.AddChild(keys);
                var reset = Widgets.Button("Default", () =>
                {
                    Settings.Controls.Reset(a.Id);
                    Changed();
                    Show(_tab);
                });
                reset.Disabled = bindings.SequenceEqual(a.Defaults);
                table.AddChild(reset);
            }
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(table);
        page.AddChild(scroll);
        page.AddChild(Widgets.Button("All to their defaults", () =>
        {
            foreach (var a in Settings.Controls.Actions) Settings.Controls.Reset(a.Id);
            Changed();
            Show(_tab);
        }));
    }

    /// <summary>The next input pressed becomes <paramref name="action"/>'s binding number <paramref name="index"/> (-1: a new one).</summary>
    public void Capture(string action, int index)
    {
        _capture = (action, index);
        ShowCapture();
    }

    private void ShowCapture(string? said = null)
    {
        if (_capture is not var (action, index))
        {
            _captureHint.Text = said ?? "";
            return;
        }
        var help = Settings.Controls.Def(action)!.Help;
        _captureHint.Text = index < 0
            ? $"Press the new key for “{help}”. Esc cancels."
            : $"Press the new key for “{help}”. Esc cancels, Delete removes this one.";
    }

    /// <summary>The next input while a binding is being changed is that binding, unless another action of the same moment has it.</summary>
    public override void _Input(InputEvent e)
    {
        if (_capture is not var (action, index)) return;
        if (e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
        {
            _capture = null;
            ShowCapture();
        }
        else if (e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Delete } && index >= 0)
        {
            Settings.Controls.Set(action, Settings.Controls.Of(action).Where((_, k) => k != index));
            Done();
        }
        else if (InputLayer.BindingOf(e) is { } binding)
        {
            if (Settings.Controls.Def(action)!.Analog) binding = binding with { Modifiers = Modifiers.None };
            var clash = Settings.Controls.Conflicts(action, binding).FirstOrDefault();
            if (clash != null)
            {
                _capture = null;
                ShowCapture($"{InputLayer.Label(binding)} is taken by “{Settings.Controls.Def(clash)!.Help}”: change that one first.");
            }
            else
            {
                var bindings = Settings.Controls.Of(action).ToList();
                if (index >= 0 && index < bindings.Count) bindings[index] = binding;
                else bindings.Add(binding);
                Settings.Controls.Set(action, bindings);
                Done();
            }
        }
        else return;
        GetViewport().SetInputAsHandled();

        void Done()
        {
            _capture = null;
            Changed();
            Show(_tab);
        }
    }

    // ------------------------------------------------------------------ Widgets

    private static GridContainer Table(VBoxContainer page)
    {
        var table = new GridContainer { Columns = 3, ThemeTypeVariation = "TableGrid" };
        page.AddChild(table);
        return table;
    }

    private static void Row(GridContainer table, string label, Control control, string note = "")
    {
        table.AddChild(Widgets.Label(label));
        control.CustomMinimumSize = new Vector2(Mathf.Max(control.CustomMinimumSize.X, 260), 0);
        table.AddChild(control);
        table.AddChild(Widgets.Label(note, "DimLabel"));
    }

    private static CheckButton Check(bool on, Action<bool> changed)
    {
        var check = new CheckButton { ButtonPressed = on };
        check.Toggled += value => changed(value);
        return check;
    }

    /// <summary>A slider with its value beside it.</summary>
    private static HBoxContainer Slider(float value, float min, float max, float step, Func<float, string> text, Action<float> changed)
    {
        var row = new HBoxContainer();
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        slider.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var label = Widgets.Label(text(value));
        label.CustomMinimumSize = new Vector2(48, 0);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += v =>
        {
            label.Text = text((float)v);
            changed((float)v);
        };
        row.AddChild(slider);
        row.AddChild(label);
        return row;
    }
}

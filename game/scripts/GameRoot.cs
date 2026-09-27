using System.Globalization;
using Headland.Game.Camera;
using Headland.Game.Common;
using Headland.Game.Debug;
using Headland.Game.Saves;
using Headland.Game.UI;
using Headland.Game.Vehicles;
using Headland.Game.Weather;
using Headland.Game.World;
using Headland.Core;
using Headland.Core.Content;
using Headland.Core.Machines;
using Headland.Core.Saves;
using Headland.Core.Time;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game;

/// <summary>
/// Entry point: loads content, starts a new game or a saved one, builds the renderers, and routes input.
/// The simulation ticks at the physics rate (60 Hz); visuals read its state every frame. Loading a save reloads
/// this scene around the loaded game.
/// </summary>
public partial class GameRoot : Node3D
{
    /// <summary>Read once per run: loading a save reloads the scene, but not the settings.</summary>
    private static UserSettings? _settings;

    public UserSettings Settings { get; private set; } = null!;
    public Simulation Sim { get; private set; } = null!;
    public IsoCamera Camera { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public ScreenStack Screens { get; private set; } = null!;
    public SaveManager Saves { get; private set; } = null!;

    /// <summary>Simulation ticks per physics frame (scenarios fast-forward with this).</summary>
    public int SimSubsteps { get; set; } = 1;
    /// <summary>When set, the camera follows this point instead of the player.</summary>
    public Func<NVec2>? FocusOverride { get; set; }
    public bool PlayerInputEnabled { get; set; } = true;

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var args = OS.GetCmdlineUserArgs();
        var scenario = args.FirstOrDefault(a => a.StartsWith("--scenario="))?.Split('=', 2)[1];
        if (_settings == null)
        {
            _settings = UserSettings.Load();
            // Scenarios keep the project's window and quality, so their screenshots compare.
            if (scenario == null) _settings.ApplyDisplay(GetViewport());
            _settings.ApplyAudio();
        }
        Settings = _settings;
        InputSetup.Register(Settings.Keys);

        var (sim, slot, warnings) = StartGame(args.FirstOrDefault(a => a.StartsWith("--load="))?.Split('=', 2)[1],
            args.FirstOrDefault(a => a.StartsWith("--difficulty="))?.Split('=', 2)[1]);
        Sim = sim;

        AddChild(new TerrainRenderer { Sim = Sim, Name = "Terrain" });
        AddChild(new NetworkRenderer { Sim = Sim, Name = "Networks" });
        AddChild(new CropRenderer { Sim = Sim, Name = "Crops" });
        AddChild(new PropsRenderer { Sim = Sim, Name = "Props" });
        AddChild(new EntityRenderer { Sim = Sim, Name = "Entities" });
        AddChild(new EnvironmentController { Sim = Sim, Shadows = scenario != null || Settings.Shadows != "off", Name = "Environment" });
        Camera = new IsoCamera { Name = "Camera" };
        AddChild(Camera);
        Camera.SnapTo(Sim.World.OnGround(Sim.Player.Position));
        Hud = new Hud { Sim = Sim, Name = "Hud" };
        AddChild(Hud);
        Screens = new ScreenStack { Name = "Screens" };
        AddChild(Screens);
        Saves = new SaveManager { Sim = Sim, AutosaveMinutes = Settings.AutosaveMinutes, Name = "Saves" };
        AddChild(Saves);

        if (scenario != null)
        {
            Saves.AutosaveMinutes = 0;
            var shots = args.FirstOrDefault(a => a.StartsWith("--shots="))?.Split('=', 2)[1] ?? "user://shots";
            AddChild(new ScenarioRunner { Game = this, Scenario = scenario, ShotsDir = shots, Name = "Scenario" });
        }
        else if (slot != null) Sim.Notifications.Post($"Loaded {slot}: {Sim.Clock.Date} {Sim.Clock.TimeString}", Severity.Info, 0);
        else Sim.Notifications.Post($"Welcome to {Sim.Map.Name} ({Sim.Difficulty.Name}). Press {InputSetup.Label("toggle_help")} for controls.", Severity.Info, 0);
        foreach (var w in warnings) Sim.Notifications.Post(w, Severity.Warning, 0);
    }

    /// <summary>The game a quickload just built, else the save named by --load, else a new game (on --difficulty).</summary>
    private static (Simulation sim, string? slot, IReadOnlyList<string> warnings) StartGame(string? loadSlot, string? difficulty)
    {
        if (SaveManager.TakePending() is var (pending, pendingSlot)) return (pending.Sim, pendingSlot, pending.Warnings);
        var content = ContentDatabase.Load(new GodotContentSource("res://data"));
        if (loadSlot != null)
        {
            try
            {
                var game = SaveManager.Read(content, loadSlot);
                return (game.Sim, loadSlot, game.Warnings);
            }
            catch (SaveException e)
            {
                GD.PrintErr($"Could not load '{loadSlot}': {e.Message}");
            }
        }
        if (difficulty != null && !content.Difficulties.ContainsKey(difficulty))
            GD.PrintErr($"No difficulty '{difficulty}' ({string.Join(", ", content.Difficulties.Keys)}): playing on {content.Game.Difficulty}");
        else if (difficulty != null) content.Game.Difficulty = difficulty;
        return (Simulation.Create(content), null, []);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (PlayerInputEnabled && Screens.BlocksInput) ReleaseControls();
        else if (PlayerInputEnabled) ApplyMovementInput();
        for (var i = 0; i < SimSubsteps; i++) Sim.Tick((float)delta);
    }

    /// <summary>A modal screen is open: the farmer stands still and the vehicle brakes.</summary>
    private void ReleaseControls()
    {
        Sim.Player.MoveInput = NVec2.Zero;
        Sim.Player.Controls.Input = new VehicleInput { Brake = true };
    }

    private void ApplyMovementInput()
    {
        var fwd = Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back");
        var right = Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left");
        var p = Sim.Player;
        if (p.Vehicle != null)
        {
            p.MoveInput = NVec2.Zero;
            p.Controls.Input = new VehicleInput { Throttle = fwd, Steer = -right, Brake = Input.IsActionPressed("brake") };
        }
        else
        {
            p.MoveInput = Camera.GroundForward * fwd + Camera.GroundRight * right;
            p.Running = Input.IsActionPressed("run");
        }
    }

    public override void _Process(double delta)
    {
        var p = Sim.Player;
        var focus = FocusOverride?.Invoke() ?? (p.Vehicle?.Footprint.Center ?? p.Position);
        Camera.Follow = Sim.World.OnGround(focus);
        Camera.FollowSpeed = FocusOverride != null ? 0f : p.Vehicle != null ? MathF.Abs(p.Vehicle.Speed) : p.Velocity.Length();
        UpdateHover();
    }

    private void UpdateHover()
    {
        var vp = GetViewport();
        var mouse = vp.GetMousePosition();
        if (vp.GuiGetHoveredControl() != null || !vp.GetVisibleRect().HasPoint(mouse))
        {
            Hud.Hover = null;
            return;
        }
        var origin = Camera.ProjectRayOrigin(mouse);
        var dir = Camera.ProjectRayNormal(mouse);
        Hud.Hover = Sim.World.Raycast(origin.ToCore(), dir.ToCore(), out var hit) ? new NVec2(hit.X, hit.Z) : null;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!PlayerInputEnabled || e is not InputEventKey { Pressed: true, Echo: false }) return;
        if (e.IsActionPressed("toggle_help")) Screens.Toggle(() => new HelpScreen());
        else if (e.IsActionPressed("toggle_finances")) Screens.Toggle(() => new FinancesScreen { Sim = Sim });
        else if (e.IsActionPressed("toggle_farmland")) Screens.Toggle(() => new FarmlandScreen { Sim = Sim });
        else if (e.IsActionPressed("toggle_contracts")) Screens.Toggle(() => new ContractsScreen { Sim = Sim });
        else if (Screens.BlocksInput) return;
        else if (e.IsActionPressed("enter")) Sim.ToggleEnterExit();
        // Before next_vehicle: Shift+Tab also matches the plain Tab binding.
        else if (e.IsActionPressed("prev_vehicle")) Sim.SwitchVehicle(-1);
        else if (e.IsActionPressed("next_vehicle")) Sim.SwitchVehicle(1);
        else if (e.IsActionPressed("attach")) Sim.CommandAttach();
        else if (e.IsActionPressed("lower")) Sim.CommandLower();
        else if (e.IsActionPressed("turn_on")) Sim.CommandTurnOn();
        else if (e.IsActionPressed("unload")) Sim.CommandUnload();
        else if (e.IsActionPressed("cycle_seed")) Sim.CommandCycleSeed();
        else if (e.IsActionPressed("steering")) Sim.CommandSteering();
        else if (e.IsActionPressed("use")) Use();
        else if (e.IsActionPressed("helper")) Sim.CommandHelper();
        else if (e.IsActionPressed("cam_rotate_left")) Camera.RotateStep(-1);
        else if (e.IsActionPressed("cam_rotate_right")) Camera.RotateStep(1);
        else if (e.IsActionPressed("pause")) Sim.Clock.Paused = !Sim.Clock.Paused;
        else if (e.IsActionPressed("skip_day")) SleepUntilMorning();
        else if (e.IsActionPressed("quicksave")) Saves.Save(SaveManager.QuickSlot);
        else if (e.IsActionPressed("quickload")) Saves.Load(SaveManager.QuickSlot);
        else if (e.IsActionPressed("toggle_debug")) Hud.DebugVisible = !Hud.DebugVisible;
        else if (e.IsActionPressed("screenshot")) SaveScreenshot($"user://shots/shot_{Time.GetUnixTimeFromSystem():0}.png");
        else
        {
            for (var i = 0; i < GameClock.Speeds.Length; i++)
            {
                if (!e.IsActionPressed($"time_{i + 1}")) continue;
                Sim.Clock.TimeScale = GameClock.Speeds[i];
                Sim.Clock.Paused = false;
            }
        }
    }

    /// <summary>
    /// The use key: asks what to load when a silo holds several goods the trailer takes, and opens the workshop when
    /// machines of the chain have options to change there.
    /// </summary>
    private void Use()
    {
        if (Sim.PlayerVehicle is { } v && !Sim.Pois.IsLoading(v) && Sim.Pois.LoadChoices(v) is { Count: > 1 } choices)
            Screens.Push(new LoadScreen { Sim = Sim, Vehicle = v, Choices = choices });
        else if (Sim.PlayerVehicle is { } w && Sim.Pois.Workshop(w) is var (bay, action) && w.Chain().Any(m => m.Def.Configurations.Count > 0))
            Screens.Push(new WorkshopScreen { Sim = Sim, Vehicle = w, Bay = bay, Action = action });
        else Sim.CommandUse();
    }

    public void SleepUntilMorning()
    {
        var h = Sim.Clock.HourOfDay;
        var hours = h < 6f ? 6f - h : 30f - h;
        Sim.SkipHours((int)MathF.Ceiling(hours));
        Sim.Notifications.Post($"Slept until {Sim.Clock.Date} {Sim.Clock.TimeString}");
    }

    public string SaveScreenshot(string path)
    {
        var abs = ProjectSettings.GlobalizePath(path);
        DirAccess.MakeDirRecursiveAbsolute(System.IO.Path.GetDirectoryName(abs)!);
        GetViewport().GetTexture().GetImage().SavePng(abs);
        GD.Print($"screenshot: {abs}");
        return abs;
    }
}

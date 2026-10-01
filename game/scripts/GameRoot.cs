using System.Globalization;
using Headland.Game.Camera;
using Headland.Game.Common;
using Headland.Game.Controls;
using Headland.Game.Debug;
using Headland.Game.Saves;
using Headland.Game.UI;
using Headland.Game.Vehicles;
using Headland.Game.Weather;
using Headland.Game.World;
using Headland.Core;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Pois.Components;
using Headland.Core.Saves;
using Headland.Core.Time;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game;

/// <summary>A new game as the main menu sets it up: the map, the difficulty and the farm's name.</summary>
public sealed record GameSetup(string Map, string Difficulty, string FarmName);

/// <summary>
/// The game scene: loads content, starts a new game (as the main menu set it up) or a saved one, builds the renderers,
/// and routes input. The simulation ticks at the physics rate (60 Hz), standing still while a menu covers the view (as
/// the settings say); visuals read its state every frame. Loading a save reloads this scene around the loaded game, and
/// quitting to the menu goes back to the main menu's scene.
/// </summary>
public partial class GameRoot : Node3D
{
    public const string ScenePath = "res://scenes/Main.tscn";

    /// <summary>The command line (--load, --difficulty) starts the first game of the run only.</summary>
    private static bool _commandLineUsed;

    /// <summary>The game the main menu set up, for this scene to start (taken once).</summary>
    public static GameSetup? NextGame { get; set; }

    /// <summary>The command line hasn't started a game yet: the main menu hands over to it.</summary>
    public static bool CommandLineWaiting => !_commandLineUsed;

    private EnvironmentController _environment = null!;

    /// <summary>The tool keys' travel for a pixel of the mouse's motion in a simulation tick, the mouse on the tool.</summary>
    private const float MouseToolPerPixel = 0.3f;

    /// <summary>Pixels the mouse moved on the tool since the last physics step.</summary>
    private Vector2 _toolMouse;
    /// <summary>Where the cursor was when the mouse took the tool: it comes back there.</summary>
    private Vector2? _cursorAt;

    public UserSettings Settings { get; private set; } = null!;
    public InputLayer InputLayer { get; private set; } = null!;
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
        var scenario = _commandLineUsed ? null : args.FirstOrDefault(a => a.StartsWith("--scenario="))?.Split('=', 2)[1];
        Settings = UserSettings.Current;
        // Scenarios keep the project's window and quality, so their screenshots compare.
        if (scenario == null) Settings.ApplyAtStart(GetViewport());
        else Settings.ApplyAudio();
        InputLayer = new InputLayer { Bindings = Settings.Controls, Name = "Input" };
        AddChild(InputLayer);
        InputLayer.Fired += OnAction;

        var firstGame = !_commandLineUsed;
        _commandLineUsed = true;
        var (sim, slot, warnings) = StartGame(firstGame ? args.FirstOrDefault(a => a.StartsWith("--load="))?.Split('=', 2)[1] : null,
            firstGame ? args.FirstOrDefault(a => a.StartsWith("--difficulty="))?.Split('=', 2)[1] : null);
        Sim = sim;

        AddChild(new TerrainRenderer { Sim = Sim, Name = "Terrain" });
        AddChild(new NetworkRenderer { Sim = Sim, Name = "Networks" });
        AddChild(new CropRenderer { Sim = Sim, Name = "Crops" });
        AddChild(new WindrowRenderer { Sim = Sim, Name = "Windrows" });
        AddChild(new PropsRenderer { Sim = Sim, Name = "Props" });
        AddChild(new EntityRenderer { Sim = Sim, Name = "Entities" });
        _environment = new EnvironmentController { Sim = Sim, Shadows = scenario != null || Settings.Shadows != "off", Name = "Environment" };
        AddChild(_environment);
        Camera = new IsoCamera { Name = "Camera" };
        AddChild(Camera);
        Camera.SnapTo(Sim.World.OnGround(Sim.Player.Position));
        Hud = new Hud { Sim = Sim, MinimapSize = Settings.Minimap, Name = "Hud" };
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
        else Sim.Notifications.Post($"Welcome to {Sim.Map.Name} ({Sim.Difficulty.Name}). Press {InputLayer.Label(GameActions.ToggleHelp)} for controls.", Severity.Info, 0);
        foreach (var w in warnings) Sim.Notifications.Post(w, Severity.Warning, 0);
    }

    /// <summary>
    /// The game a load just built (the main menu's, a quickload), else the main menu's new game, else the save named by
    /// --load, else a new game (on --difficulty).
    /// </summary>
    private static (Simulation sim, string? slot, IReadOnlyList<string> warnings) StartGame(string? loadSlot, string? difficulty)
    {
        if (SaveManager.TakePending() is var (pending, pendingSlot)) return (pending.Sim, pendingSlot, pending.Warnings);
        var content = ContentDatabase.Load(new GodotContentSource("res://data"));
        Models.LeaveOutBroken(content);
        if (NextGame is { } setup)
        {
            NextGame = null;
            if (content.Maps.ContainsKey(setup.Map)) content.Game.Map = setup.Map;
            if (content.Difficulties.ContainsKey(setup.Difficulty)) content.Game.Difficulty = setup.Difficulty;
            content.Game.FarmName = setup.FarmName;
            return (Simulation.Create(content), null, []);
        }
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
        // A menu covering the view stops the clock, as the settings say (scenarios run on).
        if (PlayerInputEnabled && Settings.PauseInMenus && Screens.CoversView) return;
        for (var i = 0; i < SimSubsteps; i++) Sim.Tick((float)delta);
    }

    /// <summary>The settings changed (the settings screen): the game follows them.</summary>
    public void ApplySettings()
    {
        Saves.AutosaveMinutes = Settings.AutosaveMinutes;
        Hud.MinimapSize = Settings.Minimap;
        _environment.Shadows = Settings.Shadows != "off";
    }

    /// <summary>Back to the main menu (what isn't saved is lost).</summary>
    public void QuitToMenu() => GetTree().ChangeSceneToFile(MainMenu.ScenePath);

    /// <summary>A modal screen is open: the farmer stands still and the vehicle brakes.</summary>
    private void ReleaseControls()
    {
        Sim.Player.MoveInput = NVec2.Zero;
        Sim.Player.Controls.Input = new VehicleInput { Brake = true };
    }

    private void ApplyMovementInput()
    {
        var fwd = InputLayer.Strength(InputActions.MoveForward) - InputLayer.Strength(InputActions.MoveBack);
        var right = InputLayer.Strength(InputActions.MoveRight) - InputLayer.Strength(InputActions.MoveLeft);
        var p = Sim.Player;
        if (p.Vehicle != null)
        {
            p.MoveInput = NVec2.Zero;
            // The mouse on the tool: forward lifts, right turns right; Ctrl and Shift take the next control groups.
            var mouse = _toolMouse * (MouseToolPerPixel / SimSubsteps);
            _toolMouse = Vector2.Zero;
            var onTool = InputLayer.MouseMode == MouseMode.Tool;
            p.Controls.Input = new VehicleInput
            {
                Throttle = fwd, Steer = -right, Brake = InputLayer.Held(InputActions.Brake),
                ToolY = InputLayer.Strength(InputActions.ToolUp) - InputLayer.Strength(InputActions.ToolDown) - mouse.Y,
                ToolX = InputLayer.Strength(InputActions.ToolLeft) - InputLayer.Strength(InputActions.ToolRight) - mouse.X,
                ToolGroupOffset = !onTool ? 0 : Input.IsKeyPressed(Key.Ctrl) ? 1 : Input.IsKeyPressed(Key.Shift) ? 2 : 0,
            };
        }
        else
        {
            p.MoveInput = Camera.GroundForward * fwd + Camera.GroundRight * right;
            p.Running = InputLayer.Held(InputActions.Run);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && InputLayer.MouseMode == MouseMode.Tool) _toolMouse += motion.Relative;
    }

    public override void _Process(double delta)
    {
        var onTool = PlayerInputEnabled && !Screens.BlocksInput && Sim.Player.Vehicle != null && InputLayer.MouseMode == MouseMode.Tool;
        InputLayer.Context = !PlayerInputEnabled ? InputContext.None
            : Screens.BlocksInput ? InputContext.Menu
            : InputContext.World | (Sim.Player.Vehicle != null ? InputContext.Vehicle : InputContext.OnFoot) | (onTool ? InputContext.MouseTool : 0);
        HoldCursor(onTool);
        Camera.Dragging = InputLayer.MouseMode == MouseMode.Drag;
        Hud.Visible = !Screens.CoversView;
        Hud.CameraYaw = Camera.Yaw;
        var p = Sim.Player;
        var focus = FocusOverride?.Invoke() ?? (p.Vehicle?.Footprint.Center ?? p.Position);
        Camera.Follow = Sim.World.OnGround(focus);
        Camera.FollowSpeed = FocusOverride != null ? 0f : p.Vehicle != null ? MathF.Abs(p.Vehicle.Speed) : p.Velocity.Length();
        UpdateHover();
    }

    /// <summary>The mouse on the tool: the cursor hides where it is, so the mouse moves freely, and comes back after.</summary>
    private void HoldCursor(bool onTool)
    {
        if (onTool && _cursorAt == null)
        {
            _cursorAt = GetViewport().GetMousePosition();
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (!onTool && _cursorAt is { } at)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Input.WarpMouse(at);
            _cursorAt = null;
            _toolMouse = Vector2.Zero;
        }
    }

    private void UpdateHover()
    {
        // The mouse on the tool isn't pointing at the ground: what it last showed stays.
        if (_cursorAt != null) return;
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

    /// <summary>What an action fired by the input layer does: those of the game here, the rest in the simulation.</summary>
    private void OnAction(string action)
    {
        switch (action)
        {
            case GameActions.ToggleHelp: Screens.Toggle(() => new HelpScreen { Sim = Sim }); break;
            case GameActions.Menu when Screens.Top is { } top: top.Close(); break;
            case GameActions.Menu: Screens.Push(new MenuScreen { Game = this }); break;
            case GameActions.MenuPrevTab: (Screens.Top as MenuScreen)?.Step(-1); break;
            case GameActions.MenuNextTab: (Screens.Top as MenuScreen)?.Step(1); break;
            case GameActions.Map: MenuTab("Map"); break;
            case GameActions.Shop: MenuTab("Shop"); break;
            case GameActions.Minimap: StepMinimap(); break;
            case GameActions.ToggleDebug: Hud.DebugVisible = !Hud.DebugVisible; break;
            case GameActions.Screenshot: SaveScreenshot($"user://shots/shot_{Time.GetUnixTimeFromSystem():0}.png"); break;
            case InputActions.Use: Use(); break;
            case GameActions.CamRotateLeft: Camera.RotateStep(-1); break;
            case GameActions.CamRotateRight: Camera.RotateStep(1); break;
            case GameActions.ZoomIn: Camera.Zoom /= 1.12f; break;
            case GameActions.ZoomOut: Camera.Zoom *= 1.12f; break;
            case GameActions.Pause: Sim.Clock.Paused = !Sim.Clock.Paused; break;
            case GameActions.SkipDay: SleepUntilMorning(); break;
            case GameActions.Quicksave: Saves.Save(SaveManager.QuickSlot); break;
            case GameActions.Quickload: Saves.Load(SaveManager.QuickSlot); break;
            default:
                var speed = Array.IndexOf(GameActions.TimeSpeeds, action);
                if (speed >= 0 && speed < GameClock.Speeds.Length)
                {
                    Sim.Clock.TimeScale = GameClock.Speeds[speed];
                    Sim.Clock.Paused = false;
                }
                else if (InputActions.Def(action) != null) Sim.Perform(action);
                break;
        }
    }

    /// <summary>
    /// A shortcut to a tab of the menu: it opens the menu on it, switches to it, or closes the menu showing it already.
    /// Another screen on top keeps it.
    /// </summary>
    private void MenuTab(string tab)
    {
        if (Screens.Top is MenuScreen menu)
        {
            if (menu.TabTitle == tab) menu.Close();
            else menu.ShowTab(tab);
        }
        else if (Screens.Top == null) Screens.Push(new MenuScreen { Game = this, Tab = tab });
    }

    /// <summary>The minimap's next size (small, large, off), kept in the settings.</summary>
    private void StepMinimap()
    {
        var sizes = UserSettings.MinimapSizes;
        Settings.Minimap = sizes[(Array.IndexOf(sizes, Settings.Minimap) + 1) % sizes.Length];
        Hud.MinimapSize = Settings.Minimap;
        Settings.Save();
    }

    /// <summary>The use key: the nearest activation's screen when it asks the player first, else what it does.</summary>
    private void Use()
    {
        switch (Sim.Activations().FirstOrDefault(a => a.Usable)?.Menu)
        {
            case LoadMenu load: Screens.Push(new LoadScreen { Sim = Sim, Vehicle = load.Vehicle, Choices = load.Choices }); break;
            case WorkshopMenu shop: Screens.Push(new WorkshopScreen { Sim = Sim, Machines = shop.Vehicle.Chain().ToList() }); break;
            default: Sim.Perform(InputActions.Use); break;
        }
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

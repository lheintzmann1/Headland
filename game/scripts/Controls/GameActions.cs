using Headland.Core.Input;

namespace Headland.Game.Controls;

/// <summary>
/// The actions the game answers itself (the camera, time, saves, the menu), bound beside the simulation's
/// (<see cref="InputActions"/>).
/// </summary>
public static class GameActions
{
    public const string CamRotateLeft = "cam_rotate_left";
    public const string CamRotateRight = "cam_rotate_right";
    public const string ZoomIn = "zoom_in";
    public const string ZoomOut = "zoom_out";
    public const string Pan = "pan";
    public const string Pause = "pause";
    public const string SkipDay = "skip_day";
    public const string Quicksave = "quicksave";
    public const string Quickload = "quickload";
    public const string ToggleHelp = "toggle_help";
    public const string Menu = "menu";
    public const string MenuPrevTab = "menu_prev_tab";
    public const string MenuNextTab = "menu_next_tab";
    public const string ToggleDebug = "toggle_debug";
    public const string Screenshot = "screenshot";

    /// <summary>The time speed keys, one per <c>GameClock.Speeds</c>.</summary>
    public static readonly string[] TimeSpeeds = ["time_1", "time_2", "time_3", "time_4", "time_5", "time_6"];

    private const InputContext World = InputContext.World;
    private const InputContext Screens = InputContext.World | InputContext.Menu;

    public static readonly IReadOnlyList<InputActionDef> Defs =
    [
        new(CamRotateLeft, "Rotate the camera left", World, "Q", "Joy LB"),
        new(CamRotateRight, "Rotate the camera right", World, "E", "Joy RB"),
        new(ZoomIn, "Zoom in", World, "Mouse Wheel Up", "Joy RY-"),
        new(ZoomOut, "Zoom out", World, "Mouse Wheel Down", "Joy RY+"),
        new(Pan, "Pan the camera (hold and drag)", World, "Mouse Middle") { Analog = true },
        new(TimeSpeeds[0], "Time ×1", World, "1"),
        new(TimeSpeeds[1], "Time ×5", World, "2"),
        new(TimeSpeeds[2], "Time ×15", World, "3"),
        new(TimeSpeeds[3], "Time ×60", World, "4"),
        new(TimeSpeeds[4], "Time ×120", World, "5"),
        new(TimeSpeeds[5], "Time ×240", World, "6"),
        new(Pause, "Pause time", World, "P"),
        new(SkipDay, "Sleep until tomorrow 6:00", World, "F9"),
        new(Quicksave, "Quicksave", World, "F5"),
        new(Quickload, "Quickload", World, "F8"),
        new(ToggleHelp, "Help: the keys for what you're doing", World, "F1", "Joy Back"),
        new(Menu, "Menu: contracts, finances, farmland, the shop, controls, save and load; closes the screen on top", Screens, "Escape", "Joy Start"),
        new(MenuPrevTab, "Previous tab", InputContext.Menu, "Q", "Joy LB"),
        new(MenuNextTab, "Next tab", InputContext.Menu, "E", "Joy RB"),
        new(ToggleDebug, "Debug overlay", Screens, "F3"),
        new(Screenshot, "Screenshot", Screens, "F12"),
    ];

    /// <summary>Every bindable action with its default bindings: the simulation's, then the game's.</summary>
    public static Bindings Catalog() => new(InputActions.Defs.Concat(Defs));
}

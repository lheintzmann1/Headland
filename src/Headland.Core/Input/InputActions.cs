namespace Headland.Core.Input;

/// <summary>
/// The actions the simulation answers: walking, driving and what the player does with the machines. The game adds
/// its own (the camera, time, screens) to the same <see cref="Bindings"/>.
/// </summary>
public static class InputActions
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Run = "run";
    public const string Brake = "brake";
    public const string Enter = "enter";
    public const string NextVehicle = "next_vehicle";
    public const string PrevVehicle = "prev_vehicle";
    public const string Attach = "attach";
    public const string SelectImplement = "select_implement";
    public const string Lower = "lower";
    public const string Fold = "fold";
    public const string MoveParts = "move_parts";
    public const string Cover = "cover";
    public const string TurnOn = "turn_on";
    public const string Unload = "unload";
    public const string TipSide = "tip_side";
    public const string CycleSeed = "cycle_seed";
    public const string Steering = "steering";
    public const string ToolUp = "tool_up";
    public const string ToolDown = "tool_down";
    public const string ToolLeft = "tool_left";
    public const string ToolRight = "tool_right";
    public const string ToolIk = "tool_ik";
    public const string ToolMouse = "tool_mouse";
    public const string ToolAction = "tool_action";
    public const string Lights = "lights";
    public const string LightsBack = "lights_back";
    public const string RoadLights = "road_lights";
    public const string WorkLightsFront = "work_lights_front";
    public const string WorkLightsRear = "work_lights_rear";
    public const string HighBeam = "high_beam";
    public const string Beacons = "beacons";
    public const string TurnLeft = "turn_left";
    public const string TurnRight = "turn_right";
    public const string Hazards = "hazards";
    public const string Use = "use";
    public const string Helper = "helper";

    private const InputContext Moving = InputContext.OnFoot | InputContext.Vehicle;
    private const InputContext Driving = InputContext.Vehicle;

    /// <summary>In the order the help lists them.</summary>
    public static readonly IReadOnlyList<InputActionDef> Defs =
    [
        new(MoveForward, "Move / accelerate", Moving, "W", "Joy LY-") { Analog = true },
        new(MoveBack, "Move back / brake, reverse", Moving, "S", "Joy LY+") { Analog = true },
        new(MoveLeft, "Move / steer left", Moving, "A", "Joy LX-") { Analog = true },
        new(MoveRight, "Move / steer right", Moving, "D", "Joy LX+") { Analog = true },
        new(Run, "Run", InputContext.OnFoot, "Shift", "Joy L3") { Analog = true },
        new(Brake, "Handbrake", Driving, "Space", "Joy LT") { Analog = true },
        new(Attach, "Attach / detach implement", Driving, "G", "Joy Down") { Unavailable = "Nothing to attach nearby: back up to an implement's hitch" },
        new(SelectImplement, "Select the next implement: the tool keys act on it (the vehicle: on all)", Driving, "T", "Joy R3")
        {
            Unavailable = "Nothing attached to select",
        },
        new(Lower, "Lower / raise implements", Driving, "V", "Joy A")
        {
            Unavailable = "No implement to lower", Several = ("Lower all", "Lift all"),
        },
        new(Fold, "Fold / unfold implements", Driving, "N") { Unavailable = "Nothing to fold", Several = ("Fold all", "Unfold all") },
        new(MoveParts, "Move a tool's other parts: ridge markers", Driving, "Shift+N")
        {
            Unavailable = "Nothing to move", Several = ("Move all", "Move all back"),
        },
        new(Cover, "Open / close covers", Driving, "C") { Unavailable = "No cover to open" },
        new(TurnOn, "Turn on / off", Driving, "B", "Joy B") { Unavailable = "Nothing to turn on", Several = ("Turn on all", "Turn off all") },
        new(Unload, "Pipe / tip trailer", Driving, "U", "Joy Up") { Unavailable = "Nothing to unload" },
        new(TipSide, "Tip side: the side a trailer tips to", Driving, "Shift+U") { Unavailable = "Nothing here tips to another side now" },
        new(CycleSeed, "Change seed", Driving, "X") { Unavailable = "No seeder attached" },
        new(Steering, "Steering: normal, all-wheel, crab", Driving, "K") { Unavailable = "It has only one way to steer" },
        new(ToolUp, "Tool up: a loader's arm, a crane's boom", Driving, "Up") { Analog = true },
        new(ToolDown, "Tool down", Driving, "Down") { Analog = true },
        new(ToolLeft, "Tool left: a loader's tool tilts back, a crane turns left", Driving, "Left") { Analog = true },
        new(ToolRight, "Tool right", Driving, "Right") { Analog = true },
        new(ToolIk, "Crane: move its tip, or each joint", Driving, "I") { Unavailable = "No crane to steer by its tip" },
        new(ToolMouse, "Hold to move the tool with the mouse (Ctrl, Shift: its next control groups)", Driving, "Mouse Right") { Analog = true },
        new(ToolAction, "With the mouse on the tool: its action (a saw's cut)", InputContext.MouseTool, "Mouse Left")
        {
            Unavailable = "The tool has no action for the mouse",
        },
        new(Lights, "Lights: off, headlights, work lights too", Driving, "L") { Unavailable = "No lights to switch" },
        new(LightsBack, "Lights back a step", Driving, "Alt+L") { Unavailable = "No lights to switch" },
        new(HighBeam, "High beams on / off", Driving, "Ctrl+Shift+L") { Unavailable = "No high beams" },
        new(RoadLights, "Headlights and tail lights alone", Driving) { Unavailable = "No headlights" },
        new(WorkLightsFront, "Front work lights alone", Driving) { Unavailable = "No front work lights" },
        new(WorkLightsRear, "Rear work lights alone", Driving) { Unavailable = "No rear work lights" },
        new(Beacons, "Beacons on / off", Driving, "Shift+L") { Unavailable = "No beacons: a workshop fits them" },
        new(TurnLeft, "Turn signal left", Driving, "Ctrl+Q") { Unavailable = "No turn signals" },
        new(TurnRight, "Turn signal right", Driving, "Ctrl+E") { Unavailable = "No turn signals" },
        new(Hazards, "Hazard lights", Driving, "Ctrl+L") { Unavailable = "No turn signals" },
        new(Use, "Use a POI: buy, load, refuel, repair, change options, wash", Moving, "R", "Joy X")
        {
            Unavailable = "Park in a marked area first: a shop, silo, gas station, workshop or wash bay",
        },
        new(Helper, "Hire / dismiss a field helper", Driving, "H"),
        new(Enter, "Enter / exit vehicle", Moving, "F", "Joy Y") { Unavailable = "No vehicle nearby" },
        new(NextVehicle, "Switch to the next vehicle", Moving, "Tab", "Joy Right"),
        new(PrevVehicle, "Switch to the previous vehicle", Moving, "Shift+Tab", "Joy Left"),
    ];

    public static InputActionDef? Def(string id) => Defs.FirstOrDefault(d => d.Id == id);
}

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
    public const string Lower = "lower";
    public const string Fold = "fold";
    public const string TurnOn = "turn_on";
    public const string Unload = "unload";
    public const string CycleSeed = "cycle_seed";
    public const string Steering = "steering";
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
        new(Enter, "Enter / exit vehicle", Moving, "F", "Joy Y"),
        new(NextVehicle, "Switch to the next vehicle", Moving, "Tab", "Joy Right"),
        new(PrevVehicle, "Switch to the previous vehicle", Moving, "Shift+Tab", "Joy Left"),
        new(Attach, "Attach / detach implement", Driving, "G", "Joy Down"),
        new(Lower, "Lower / raise implements", Driving, "V", "Joy A"),
        new(Fold, "Fold / unfold implements", Driving, "N"),
        new(TurnOn, "Turn on / off", Driving, "B", "Joy B"),
        new(Unload, "Pipe / tip trailer", Driving, "U", "Joy Up"),
        new(CycleSeed, "Change seed", Driving, "X"),
        new(Steering, "Steering: normal, all-wheel, crab", Driving, "K"),
        new(Use, "Use a POI: buy, load, refuel, repair, change options, wash", Moving, "R", "Joy X"),
        new(Helper, "Hire / dismiss a field helper", Driving, "H"),
    ];
}

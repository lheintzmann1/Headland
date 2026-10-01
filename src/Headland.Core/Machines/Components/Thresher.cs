using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;

namespace Headland.Core.Machines.Components;

/// <summary>Where a combine drops the straw (FS: its swath): a windrow <see cref="Width"/> wide, centered on x, z.</summary>
public sealed class SwathDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float Width { get; set; } = 1.5f;
}

/// <summary>
/// A combine's threshing drum: turned on, it threshes what the header hanging on it cuts into a fill unit, and drops what
/// the crop leaves (its windrow: the straw) in its swath, if it has one.
/// </summary>
public sealed class ThresherDef : MachineComponentDef
{
    public string FillUnit { get; set; } = "tank";
    /// <summary>Where the straw falls; none: it's chopped and spread, nothing for a baler.</summary>
    public SwathDef? Swath { get; set; }

    public override IEnumerable<string> Toggles => [InputActions.TurnOn];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (!HasUnit(machine, FillUnit)) yield return $"fill unit '{FillUnit}' missing";
        if (Swath is { Width: <= 0f }) yield return "swath: width must be > 0";
    }

    internal override Component Create(Machine machine) => new Thresher(machine, this);
}

public sealed class ThresherSave
{
    public bool On { get; set; }
}

public sealed class Thresher(Machine machine, ThresherDef def) : MachineComponent<ThresherDef, ThresherSave>(machine, def), ISwitchable, IConditionSource, IActionSource
{
    public bool CanTurnOn => true;
    public bool On { get; set; }

    public void AddActions(ActionList actions, Simulation sim) => actions.AddSwitch(this);

    public FillUnit Tank => Machine.Unit(Def.FillUnit)!;

    /// <summary>Why the tank took nothing more on the header's last pass (full, or holding another crop), if so.</summary>
    internal MachineCondition? Refused { get; set; }

    /// <summary>Why the tank took nothing more, while that's still so: until it's unloaded.</summary>
    public IEnumerable<MachineCondition> Conditions => Refused switch
    {
        TankFull when Tank.Free < 10f => [Refused],
        TankHolds holds when Tank.FillType == holds.FillType.Id => [Refused],
        _ => [],
    };

    internal override void OnDetached() => On = false;

    protected override ThresherSave Capture(ContentDatabase content) => new() { On = On };

    protected override void Restore(ThresherSave save, SaveContext context) => On = save.On;
}

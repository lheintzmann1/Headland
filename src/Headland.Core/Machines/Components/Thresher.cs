using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;

namespace Headland.Core.Machines.Components;

/// <summary>A combine's threshing drum: turned on, it threshes what the header hanging on it cuts into a fill unit.</summary>
public sealed class ThresherDef : MachineComponentDef
{
    public string FillUnit { get; set; } = "tank";

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (!HasUnit(machine, FillUnit)) yield return $"fill unit '{FillUnit}' missing";
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

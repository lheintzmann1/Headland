using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>A combine's threshing drum: turned on, it threshes what the header hanging on it cuts into a fill unit.</summary>
public sealed class ThresherDef : ComponentDef
{
    public string FillUnit { get; set; } = "tank";

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (!HasUnit(machine, FillUnit)) yield return $"fill unit '{FillUnit}' missing";
    }

    internal override MachineComponent Create(Machine machine) => new Thresher(machine, this);
}

public sealed class ThresherSave
{
    public bool On { get; set; }
}

public sealed class Thresher(Machine machine, ThresherDef def) : MachineComponent<ThresherDef, ThresherSave>(machine, def), ISwitchable
{
    public bool CanTurnOn => true;
    public bool On { get; set; }

    public FillUnit Tank => Machine.Unit(Def.FillUnit)!;

    internal override void OnDetached() => On = false;

    protected override ThresherSave Capture(ContentDatabase content) => new() { On = On };

    protected override void Restore(ThresherSave save, SaveContext context) => On = save.On;
}

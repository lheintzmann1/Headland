using Headland.Core.Content;

namespace Headland.Core.Machines;

/// <summary>
/// Something keeping a machine from working as it should, reported by its components (<see cref="Machine.Conditions"/>):
/// out of seed, a full tank, the wrong header, too little power. The HUD lists them under the machine; a helper stops
/// for those that <see cref="Stops"/>.
/// </summary>
public abstract record MachineCondition
{
    /// <summary>The work can't go on until the farmer sees to it: a helper stops.</summary>
    public virtual bool Stops => false;

    /// <summary>What the farmer is told.</summary>
    public abstract string Text { get; }
}

/// <summary>A component that reports <see cref="MachineCondition"/>s.</summary>
public interface IConditionSource
{
    IEnumerable<MachineCondition> Conditions { get; }
}

/// <summary>The engine stopped: its tank ran dry.</summary>
public sealed record OutOfFuel : MachineCondition
{
    public override bool Stops => true;
    public override string Text => "Out of fuel: refuel at a gas station";
}

/// <summary>The working implements ask for more power than the engine has: it goes slower.</summary>
public sealed record Underpowered(float NeedsHp, float HasHp) : MachineCondition
{
    public override string Text => $"Needs {NeedsHp:N0} hp, has {HasHp:N0} hp";
}

/// <summary>A work area ran out of what it spreads or sows.</summary>
public sealed record OutOf(FillTypeDef FillType) : MachineCondition
{
    public override bool Stops => true;
    public override string Text => $"Out of {FillType.Name.ToLowerInvariant()}: buy more at a shop";
}

/// <summary>A thresher's tank has no room for what the header cuts.</summary>
public sealed record TankFull : MachineCondition
{
    public override bool Stops => true;
    public override string Text => "Grain tank full: unload into a trailer";
}

/// <summary>A thresher's tank holds another crop than the one the header cuts.</summary>
public sealed record TankHolds(FillTypeDef FillType) : MachineCondition
{
    public override bool Stops => true;
    public override string Text => $"Tank holds {FillType.Name}: empty it first";
}

/// <summary>The header doesn't cut the ripe crop it passes over, which is left standing.</summary>
public sealed record WrongHeader(CropDef Crop) : MachineCondition
{
    public override string Text => $"Wrong header for {Crop.Name}";
}

/// <summary>Sowing outside the crop's sowing window: it grows, but yields poorly.</summary>
public sealed record OutOfSeason(CropDef Crop) : MachineCondition
{
    public override string Text => $"{Crop.Name} sown out of season: poor yield";
}

/// <summary>Worn below <see cref="Components.Wearable.WornBelow"/>: it does its work worse, and a workshop would repair it.</summary>
public sealed record Worn(float Condition) : MachineCondition
{
    public override string Text => $"Worn ({Condition * 100f:0}%): repair it at a workshop";
}

/// <summary>The ground under the work area may not be worked by its farm (<see cref="Ownership.Farms.WorkBlocker"/>).</summary>
public sealed record NotAllowed(string Why) : MachineCondition
{
    public override string Text => Why;
}

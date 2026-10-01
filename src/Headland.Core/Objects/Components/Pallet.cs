using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Objects.Components;

/// <summary>
/// A pallet (FS: pallets): what productions put their goods on (a production output in "pallet" mode), pallet forks
/// carry and selling stations buy by the pallet. What it holds is its fill unit's.
/// </summary>
public sealed class PalletDef : ObjectComponentDef
{
    internal override IEnumerable<string> Errors(ObjectDef o, ContentDatabase content)
    {
        if (o.Get<FillUnitsDef>()?.Units is not [_]) yield return "a pallet needs fillUnits with one unit: what it holds";
    }

    internal override Component Create(WorldObject o) => new Pallet(o, this);
}

public sealed class Pallet(WorldObject o, PalletDef def) : ObjectComponent<PalletDef>(o, def)
{
    /// <summary>What it holds.</summary>
    public FillUnit Content => Object.FillUnits[0];
}

using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Objects.Components;

/// <summary>
/// A bale (FS: Bale): what balers make, bale loaders pick up and selling stations buy by the bale. It's made of what its
/// fill unit holds; its <see cref="Shape"/> says which loaders take it.
/// </summary>
public sealed class BaleDef : ObjectComponentDef
{
    public static readonly string[] Shapes = ["round", "square"];

    /// <summary>Round (lying on its side, its axis along x) or square.</summary>
    public string Shape { get; set; } = "round";

    internal override IEnumerable<string> Errors(ObjectDef o, ContentDatabase content)
    {
        if (!Shapes.Contains(Shape)) yield return $"shape must be {string.Join(" or ", Shapes)}";
        if (o.Get<FillUnitsDef>()?.Units is not [_]) yield return "a bale needs fillUnits with one unit: what it's made of";
    }

    internal override Component Create(WorldObject o) => new Bale(o, this);
}

public sealed class Bale(WorldObject o, BaleDef def) : ObjectComponent<BaleDef>(o, def)
{
    /// <summary>What it's made of.</summary>
    public FillUnit Content => Object.FillUnits[0];
}

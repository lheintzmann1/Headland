using System.Numerics;
using Headland.Core.Content;

namespace Headland.Core.Components;

public sealed class HotspotDef
{
    /// <summary>Its map icon: a Material Symbols icon in assets/icons, by file name (e.g. "storefront").</summary>
    public string Icon { get; set; } = "";
    /// <summary>Where it is on the entity, in its space.</summary>
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>What the map calls it (the entity's name when left out).</summary>
    public string? Name { get; set; }
}

/// <summary>Icons on the map (FS: hotspots): a POI's, a machine's, each named and placed on its entity.</summary>
public sealed class HotspotsDef : ComponentDef
{
    public HotspotDef[] Spots { get; set; } = [];

    internal override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content)
    {
        if (Spots.Length == 0) yield return "needs spots";
        if (Spots.Any(s => string.IsNullOrWhiteSpace(s.Icon))) yield return "each spot needs an icon";
    }

    internal override Component Create(Entity owner) => new Hotspots(owner, this);
}

/// <summary>A map icon where it is now: its icon, position and name.</summary>
public readonly record struct Hotspot(string Icon, Vector2 Position, string Name);

public sealed class Hotspots(Entity owner, HotspotsDef def) : Component<HotspotsDef>(owner, def)
{
    /// <summary>Its icons, where its entity is now.</summary>
    public IEnumerable<Hotspot> Spots => Def.Spots.Select(s => new Hotspot(s.Icon, Owner.LocalToWorld(s.X, s.Z), s.Name ?? Owner.Name));

    /// <summary>The icon of its first spot: what stands for the entity (over a POI's trigger areas).</summary>
    public string Icon => Def.Spots[0].Icon;
}

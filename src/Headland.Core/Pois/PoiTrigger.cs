using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Machines;

namespace Headland.Core.Pois;

/// <summary>
/// An area of a placed POI where machines use it, and the component they use there (a selling station, a silo, a
/// workshop…): <see cref="Type"/> says how, "unload", "load", "fill", "repair", "wash" or "delivery".
/// </summary>
public sealed class PoiTrigger(Poi poi, Component station, string type, AreaDef def)
{
    public Poi Poi { get; } = poi;
    public Component Station { get; } = station;
    public string Type { get; } = type;
    public AreaDef Def { get; } = def;
    public Obb Area { get; } = def.On(poi);

    public bool Contains(Vector2 p) => Area.Contains(p);

    public override string ToString() => $"{Poi.Id}/{Station.Definition.Kind} {Type}";
}

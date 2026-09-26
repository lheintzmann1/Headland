using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Machines;

namespace Headland.Core.Pois;

/// <summary>An area of a placed POI where machines use it, with the actions it offers there.</summary>
public sealed class PoiTrigger
{
    public PoiTrigger(Poi poi, PoiTriggerDef def)
    {
        Poi = poi;
        Def = def;
        Area = new Obb(poi.LocalToWorld(def.X, def.Z), new Vector2(def.W * 0.5f, def.D * 0.5f), poi.Heading);
        Actions = poi.Def.Actions.Where(a => a.Trigger == def.Id).ToArray();
    }

    public Poi Poi { get; }
    public PoiTriggerDef Def { get; }
    public string Id => Def.Id;
    public string Type => Def.Type;
    public Obb Area { get; }
    public IReadOnlyList<PoiActionDef> Actions { get; }

    public bool Contains(Vector2 p) => Area.Contains(p);

    public override string ToString() => $"{Poi.Id}/{Id}";
}

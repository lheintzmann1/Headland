using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines;

namespace Headland.Core.Pois;

/// <summary>A fill type a POI pays more for until <paramref name="EndDay"/> (a day index).</summary>
public sealed record HighDemand(string FillType, float Factor, int EndDay);

/// <summary>A point of interest placed on the map: a building or site of some farm or NPC.</summary>
public sealed class Poi : Entity
{
    public Poi(string id, PoiDef def, Vector2 position, float heading, int farmId, string? name = null)
    {
        Id = id;
        Def = def;
        Position = position;
        Heading = heading;
        FarmId = farmId;
        Name = name ?? def.Name;
        Triggers = def.Triggers.Select(t => new PoiTrigger(this, t)).ToArray();
        Storage = def.Storage != null ? new PoiStorage(def.Storage) : null;
        Progress = new float[def.Actions.Length];
        CreateComponents();
    }

    /// <summary>The placement id, unique on the map.</summary>
    public string Id { get; }
    public override PoiDef Def { get; }
    /// <summary>The type's name, or the one the map gives it.</summary>
    public override string Name { get; }
    public IReadOnlyList<PoiTrigger> Triggers { get; }
    public PoiStorage? Storage { get; }
    /// <summary>Process actions: the part of a cycle done so far, by action index.</summary>
    internal float[] Progress { get; }
    /// <summary>Demand factors below 1 after big sales, by fill type (they recover over time).</summary>
    internal Dictionary<string, float> Demand { get; } = new();
    /// <summary>The fill type in high demand here, if any.</summary>
    public HighDemand? HighDemand { get; internal set; }

    /// <summary>How much of the full price sales of <paramref name="fillType"/> get: 1, less after big sales.</summary>
    public float DemandOf(string fillType) => Demand.GetValueOrDefault(fillType, 1f);

    /// <summary>Fill types an action trades or stores (a store action without any: all its storage keeps).</summary>
    public IReadOnlyList<string> FillTypesOf(PoiActionDef action) =>
        action.Type == "store" && action.FillTypes.Length == 0 ? Storage?.Def.FillTypes ?? [] : action.FillTypes;

    public Obb Footprint => new(Position, new Vector2(Def.W * 0.5f, Def.D * 0.5f), Heading);

    public PoiTrigger? Trigger(string id) => Triggers.FirstOrDefault(t => t.Id == id);

    /// <summary>The ground a collider covers (a round one: the box around it).</summary>
    public Obb ColliderBox(PoiColliderDef collider) =>
        new(LocalToWorld(collider.X, collider.Z), new Vector2(collider.W * 0.5f, collider.D * 0.5f), Heading + collider.RotDeg * MathUtil.Deg2Rad);

    public override string ToString() => $"{Name} ({Id})";
}

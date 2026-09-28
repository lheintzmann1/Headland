using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// A kind of field work a work area does (FS: work area types): what it does to each cell it passes over, what it needs
/// (seed, a thresher to cut for), and what helpers and contracts check. Work areas and contracts name it by its
/// <see cref="Id"/>; <see cref="WorkTypes"/> holds them all.
/// </summary>
public abstract class WorkType(string id)
{
    /// <summary>What work areas and contracts call it: "cultivator", "plow".</summary>
    public string Id { get; } = id;

    /// <summary>
    /// Its power is drawn through the ground (tines, a plow's bodies, coulters), holding the vehicle back, rather than
    /// taken from the engine (a header threshing, a mower's or a spreader's discs), leaving less for the wheels.
    /// </summary>
    public virtual bool Draft => true;

    /// <summary>It threshes the crop into a tank: its contracts can ask for the crop at a buyer.</summary>
    public virtual bool Harvests => false;

    /// <summary>It sows the crop the driver picks (<see cref="WorkAreas.Crop"/>).</summary>
    public virtual bool Sows => false;

    /// <summary>Model node roles its views move (a header's reel).</summary>
    public virtual IEnumerable<string> Roles => [];

    /// <summary>What's wrong with an area of this type on <paramref name="machine"/> (a seeder without its fill unit…).</summary>
    internal virtual IEnumerable<string> Errors(WorkAreaDef area, MachineDef machine, ContentDatabase content) => [];

    /// <summary>Whether the area works, its machine unfolded and lowered: turned on, when it has to be.</summary>
    internal virtual bool Working(WorkAreas areas, WorkAreaDef area) => !area.RequiresOn || areas.On;

    /// <summary>A helper starting the job: turns on what the area needs to work.</summary>
    internal virtual void Start(WorkAreas areas)
    {
        if (areas.CanTurnOn) areas.On = true;
    }

    /// <summary>Whether it can work <paramref name="crop"/> at all (a header cutting it), for contracts and leases.</summary>
    public virtual bool Handles(WorkAreaDef area, CropDef crop) => true;

    /// <summary>The crop its work puts on the field (a seeder's), which a contract may name; null for other work.</summary>
    public virtual CropDef? Crop(WorkAreas areas, ContentDatabase content) => null;

    /// <summary>Whether working cell <paramref name="i"/> would still change it: helpers leave out the lanes done already.</summary>
    public abstract bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i);

    /// <summary>Works the cells a work area passes over this tick; returns how many it changed.</summary>
    internal abstract int Work(WorkPass pass);

    /// <summary>A spreading work's rate and fill unit: both set, the unit's.</summary>
    protected static IEnumerable<string> SpreadErrors(WorkAreaDef area, MachineDef machine, string what)
    {
        if (area.FillUnit == null || machine.Get<FillUnitsDef>()?.Units.Any(u => u.Id == area.FillUnit) != true)
            yield return $"a {area.Type} needs the fillUnit its {what} comes from";
        if (area.RatePerHa <= 0f) yield return $"a {area.Type} needs ratePerHa > 0";
    }

    /// <summary>The fill types a fill unit of <paramref name="machine"/> takes (none if it has no such unit).</summary>
    protected static string[] FillTypesOf(MachineDef machine, string? unit) =>
        machine.Get<FillUnitsDef>()?.Units.FirstOrDefault(u => u.Id == unit)?.FillTypes ?? [];

    protected static void MarkDirty(WorldMap world, int i, bool crop) => world.MarkCellDirty(i % world.CellsX, i / world.CellsX, crop);
}

/// <summary>
/// A work area passing over the ground during a tick: the cells under it its farm may work (under where it is now and
/// where it was the tick before, so the ground swept in between has no gaps).
/// </summary>
internal sealed class WorkPass(Simulation sim, Machine machine, WorkAreas areas, WorkAreaDef area, List<int> cells, int fieldId, Obb? before)
{
    /// <summary>Fill taken this pass, removed from the unit at once when it's done: a cell's worth is too little for a full unit's precision.</summary>
    private float _used;

    public Simulation Sim { get; } = sim;
    public Machine Machine { get; } = machine;
    public WorkAreas Areas { get; } = areas;
    public WorkAreaDef Area { get; } = area;
    public List<int> Cells { get; } = cells;
    public int FieldId { get; } = fieldId;
    public WorldMap World => Sim.World;
    public FieldLayers Layers => Sim.World.Layers;
    public ContentDatabase Content => Sim.Content;

    /// <summary>The direction the work leaves its rows in.</summary>
    public byte Angle => WorldGen.AngleToByte(Machine.Heading);

    /// <summary>
    /// Whether the area reached cell <paramref name="i"/> this tick (it wasn't under it the tick before): work that adds
    /// to a cell each time (spreading) does it once as the area passes over.
    /// </summary>
    public bool Fresh(int i) => before is not { } b || !b.Contains(World.CellCenter(i % World.CellsX, i / World.CellsX));

    /// <summary>
    /// Takes what a cell needs from the area's fill unit, at <paramref name="perHa"/> units per hectare. False when
    /// there isn't enough: the area is out of it (<see cref="OutOf"/>) until filled up again.
    /// </summary>
    public bool Use(float perHa)
    {
        var unit = Machine.Unit(Area.FillUnit)!;
        var need = perHa * WorldMap.CellArea / 10000f;
        if (unit.Level - _used >= need)
        {
            _used += need;
            return true;
        }
        var what = Content.FillTypes[unit.FillType ?? unit.Def.FillTypes[0]];
        // Too little left for a cell: what's left is spilled, and the unit reads empty.
        unit.Remove(unit.Level);
        _used = 0f;
        Areas.Report(new OutOf(what));
        Sim.Notifications.Post($"{Machine.Def.Name} is out of {what.Name.ToLowerInvariant()}", Severity.Warning, 10);
        return false;
    }

    /// <summary>The pass is over: takes the fill it used from the unit.</summary>
    public void Finish()
    {
        if (_used > 0f) Machine.Unit(Area.FillUnit)!.Remove(_used);
        _used = 0f;
    }
}

/// <summary>
/// Every kind of field work, by the id work areas and contracts give it: tilling (cultivator, plow), sowing (seeder),
/// harvesting (harvester), fertilizing (spreader), spraying herbicide (sprayer) and mowing (mower). More can be
/// registered (mods, Lua).
/// </summary>
public static class WorkTypes
{
    public static readonly CultivatorWork Cultivator = new();
    public static readonly PlowWork Plow = new();
    public static readonly SeederWork Seeder = new();
    public static readonly HarvesterWork Harvester = new();
    public static readonly SpreaderWork Spreader = new();
    public static readonly SprayerWork Sprayer = new();
    public static readonly MowerWork Mower = new();

    private static readonly List<WorkType> Types = [Cultivator, Plow, Seeder, Harvester, Spreader, Sprayer, Mower];

    public static IReadOnlyList<WorkType> All => Types;

    public static WorkType? Find(string id) => Types.Find(t => t.Id == id);

    /// <summary>Adds a kind of work (a mod's); its id must be new.</summary>
    public static void Register(WorkType type)
    {
        if (Find(type.Id) != null) throw new ArgumentException($"Work type '{type.Id}' exists already", nameof(type));
        Types.Add(type);
    }

    /// <summary>The ids, for messages: "cultivator, plow, …".</summary>
    public static string Known => string.Join(", ", Types.Select(t => t.Id));
}

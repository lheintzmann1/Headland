using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Headland.Core.Machines.Components;

namespace Headland.Core.Content;

// Content definitions ("raws"), deserialized from game/data/**/*.json (camelCase, comments allowed).
// Local machine coordinates use model space: +z forward, +x left, origin at the fixed axle.

public sealed class GameConfig
{
    public int DaysPerMonth { get; set; } = 3;
    public int StartYear { get; set; } = 1;
    public int StartMonth { get; set; } = 8;
    public int StartDay { get; set; } = 1;
    public float StartHour { get; set; } = 7f;
    /// <summary>The difficulty preset (difficulties.json id): start money and loan, price level.</summary>
    public string Difficulty { get; set; } = "normal";
    public string FarmName { get; set; } = "My Farm";
    public string Map { get; set; } = "default";
    public string Climate { get; set; } = "temperate";
    public ulong WeatherSeed { get; set; } = 42;
}

/// <summary>A difficulty preset (difficulties.json), picked when a game starts.</summary>
public sealed class DifficultyDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Money in the account at the start.</summary>
    public float StartMoney { get; set; } = 100_000f;
    /// <summary>What the farm owes the bank at the start (up to the credit limit).</summary>
    public float StartLoan { get; set; }
    /// <summary>
    /// Multiplies what the farm pays: supplies, fuel, repairs, washing, running costs, land and wages. Sale prices
    /// and loan interest don't change.
    /// </summary>
    public float PriceLevel { get; set; } = 1f;
}

/// <summary>Money rules (economy.json). Unlike game.json, saved games use the current values.</summary>
public sealed class EconomyDef
{
    /// <summary>What the farm borrows or repays at a time.</summary>
    public float LoanStep { get; set; } = 5000f;
    /// <summary>The most the farm can owe the bank.</summary>
    public float CreditLimit { get; set; } = 500_000f;
    /// <summary>Interest on the loan per game year (0.05 = 5%), charged every day of the compressed year.</summary>
    public float LoanInterest { get; set; } = 0.05f;
    /// <summary>What a field helper earns per hour of work (real time: machines don't follow the clock speed).</summary>
    public float HelperWagePerHour { get; set; } = 150f;
    /// <summary>How the neighbors offer contracts (the jobs themselves are in contracts.json).</summary>
    public ContractRulesDef Contracts { get; set; } = new();
}

public sealed class ContractRulesDef
{
    /// <summary>Offers on the board at once.</summary>
    public int MaxOffers { get; set; } = 6;
    /// <summary>New offers posted every midnight while the board has room (a new game starts with as many).</summary>
    public int OffersPerDay { get; set; } = 2;
    /// <summary>Game days an offer stays on the board.</summary>
    public int OfferDays { get; set; } = 3;
    /// <summary>Contracts the farm can have under way at once.</summary>
    public int MaxActive { get; set; } = 3;
    /// <summary>Share of a field that must be in a job's done state for the job to be done (FS: 95%).</summary>
    public float Threshold { get; set; } = 0.95f;
    /// <summary>Share of the reward a contract costs when canceled, or not done by its due day.</summary>
    public float Penalty { get; set; } = 0.1f;
}

public sealed class FillTypeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "L";
    /// <summary>Mass per unit, used for vehicle load (kg per L or 1 for kg).</summary>
    public float MassPerUnit { get; set; } = 0.75f;
    public float PricePerUnit { get; set; }
    /// <summary>12 multipliers, January first. Missing means flat price.</summary>
    public float[]? MonthlyPriceFactor { get; set; }
    public string Color { get; set; } = "#c8a860";
}

/// <summary>A neighbor who owns the land no farm owns: sells it, and offers contracts on it.</summary>
public sealed class NpcDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class SoilDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Plant-available water held by the root zone at saturation (mm).</summary>
    public float WaterCapacityMm { get; set; } = 100f;
    /// <summary>Fraction of rain that soaks in (rest runs off).</summary>
    public float Infiltration { get; set; } = 0.9f;
    /// <summary>Relative moisture above which gravity drainage starts.</summary>
    public float FieldCapacity { get; set; } = 0.75f;
    /// <summary>Fraction of the excess above field capacity drained per game hour.</summary>
    public float DrainageRate { get; set; } = 0.1f;
    public float InitialNitrogen { get; set; } = 90f;
    /// <summary>Nitrogen mineralized per month (kg/ha), capped at InitialNitrogen.</summary>
    public float MineralizationPerMonth { get; set; } = 4f;
    public float InitialMoisture { get; set; } = 0.55f;
    public string Description { get; set; } = "";
}

public sealed class CropStageDef
{
    public string Name { get; set; } = "";
    /// <summary>Growing degree-days needed to leave this stage (0 on the final stage).</summary>
    public float Gdd { get; set; }
    /// <summary>Winter crops: this stage (bolting/stem elongation) can't start before the crop is vernalized.</summary>
    public bool RequiresVernalization { get; set; }
    /// <summary>Visual height in meters.</summary>
    public float Height { get; set; }
    public bool Harvestable { get; set; }
    /// <summary>Atlas column (0..6) of the billboard card; defaults to the stage index.</summary>
    public int? Card { get; set; }
}

public sealed class CropDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FillType { get; set; } = "";
    public string HarvestGroup { get; set; } = "grain";
    public int AtlasRow { get; set; }
    public float BaseTempC { get; set; }
    public float YieldPerHa { get; set; } = 8000f;
    public float SeedKgPerHa { get; set; } = 150f;
    public int[] SowingMonths { get; set; } = [];
    public int[] HarvestMonths { get; set; } = [];
    public float FrostKillC { get; set; } = -10f;
    public float WiltingPoint { get; set; } = 0.25f;
    public float OptimalMoistureMin { get; set; } = 0.4f;
    public float OptimalMoistureMax { get; set; } = 0.8f;
    public float NitrogenDemandKgPerHa { get; set; } = 150f;
    /// <summary>Chill (real days between −4 and 8 °C, counted from sowing) needed before bolting. 0 = none.</summary>
    public float VernalizationDays { get; set; }
    public CropStageDef[] Stages { get; set; } = [];
    public string Description { get; set; } = "";

    public int HarvestableStage
    {
        get
        {
            for (var i = 0; i < Stages.Length; i++)
                if (Stages[i].Harvestable) return i;
            return Stages.Length - 1;
        }
    }

    public int CardOf(int stage) => Stages[stage].Card ?? stage;
}

public sealed class ClimateDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float[] MonthlyMeanTemp { get; set; } = new float[12];
    public float[] MonthlyDailyRange { get; set; } = new float[12];
    public float[] MonthlyPrecipMm { get; set; } = new float[12];
    public float[] MonthlyPrecipChance { get; set; } = new float[12];
    public float[] MonthlyFogChance { get; set; } = new float[12];
    public float[] SunriseHour { get; set; } = new float[12];
    public float[] SunsetHour { get; set; } = new float[12];
    public float AnomalyStdDev { get; set; } = 2.5f;
    public float AnomalyPersistence { get; set; } = 0.7f;
}

public sealed class SizeDef
{
    public float Length { get; set; } = 4f;
    public float Width { get; set; } = 2f;
    public float Height { get; set; } = 2f;
    /// <summary>Local z of the footprint center.</summary>
    public float CenterZ { get; set; }
}

/// <summary>A glTF model shown instead of a procedural placeholder (machines, POIs).</summary>
public class ModelDef
{
    /// <summary>Optional glTF model (res:// path, e.g. a Blockbench .glb export). Replaces the placeholder.</summary>
    public string? Model { get; set; }
    /// <summary>Uniform scale applied to the model (1 = model units are meters).</summary>
    public float Scale { get; set; } = 1f;
    /// <summary>Turn the model around Y so its front faces +Z (180 for models built facing -Z).</summary>
    public float YawDeg { get; set; }
    /// <summary>Model offset [x, y, z] in meters, in local space (+Z forward, +X left).</summary>
    public float[] Offset { get; set; } = [0f, 0f, 0f];
}

public sealed class VisualDef : ModelDef
{
    /// <summary>Procedural placeholder archetype: tractor, combine, trailer, cultivator, seeder, header.</summary>
    public string Placeholder { get; set; } = "tractor";
    public string Color { get; set; } = "#7a3326";
    /// <summary>
    /// Moving parts: role → node name in the model. The machine's components give the roles (wheel0L, wheel0R… for
    /// each side of the running gear's axles, pipe, tipper, reel, load…). See docs/MODELING.md.
    /// </summary>
    public Dictionary<string, string>? Nodes { get; set; }
    /// <summary>
    /// Placeholder blocks the archetype doesn't draw, such as a front weight: named like model nodes, so that options
    /// show and hide them as they do a model's (<see cref="ConfigurationOptionDef.Show"/>).
    /// </summary>
    public PlaceholderPartDef[] Parts { get; set; } = [];
}

/// <summary>A block of a machine's placeholder, centered on x, y, z (machine space): w along x, h up, d along z.</summary>
public sealed class PlaceholderPartDef
{
    /// <summary>Its node name, unique on the machine.</summary>
    public string Id { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; } = 0.5f;
    public float H { get; set; } = 0.5f;
    public float D { get; set; } = 0.5f;
    /// <summary>Its paint; the machine's color when missing.</summary>
    public string? Color { get; set; }
}

/// <summary>
/// A machine type: what it is, its size and looks, the components it's built from, and the options it can have. Each
/// set of options makes a def of its own (<see cref="Configure"/>); the content lists each machine with its default
/// options. Unknown properties are refused, so a machine written in the format from before components (motorized,
/// wheels, workArea… at the top) doesn't load as an empty shell.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MachineDef
{
    private Variants? _variants;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Brand { get; set; } = "";
    /// <summary>With the chosen options' prices and masses added.</summary>
    public float Price { get; set; }
    public float Mass { get; set; } = 3000f;
    public SizeDef Size { get; set; } = new();
    /// <summary>Its components, by kind (see <see cref="ComponentKinds"/>), in the order they run.</summary>
    [JsonConverter(typeof(ComponentDefsConverter))]
    public List<ComponentDef> Components { get; set; } = [];
    public VisualDef Visual { get; set; } = new();
    public string Description { get; set; } = "";
    /// <summary>What it can be had with (FS configurations): wheels, engine, color… one option of each.</summary>
    public List<ConfigurationDef> Configurations { get; set; } = [];

    /// <summary>The option chosen in each of its configurations, by configuration id.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Choices { get; private set; } = new Dictionary<string, string>();

    /// <summary>Model nodes (and placeholder parts) its options show: each is visible with the options showing it.</summary>
    [JsonIgnore]
    public IEnumerable<string> OptionNodes => Configurations.SelectMany(c => c.Options).SelectMany(o => o.Show).Distinct();

    /// <summary>Model nodes (and placeholder parts) some option shows but none of the chosen ones: hidden on this machine.</summary>
    [JsonIgnore]
    public IReadOnlySet<string> HiddenNodes { get; private set; } = new HashSet<string>();

    /// <summary>The option chosen in <paramref name="configuration"/>.</summary>
    public ConfigurationOptionDef? Chosen(ConfigurationDef configuration) => configuration.Option(Choices.GetValueOrDefault(configuration.Id));

    /// <summary>
    /// The same machine with other options: <paramref name="choices"/> (configuration id → option id) over the ones this
    /// def has; configurations it doesn't have are left out, and an option it doesn't have gives the default. Each set of
    /// options is built once.
    /// </summary>
    public MachineDef Configure(IReadOnlyDictionary<string, string> choices)
    {
        if (_variants == null || choices.Count == 0) return this;
        var all = new Dictionary<string, string>(Choices);
        foreach (var (configuration, option) in choices) all[configuration] = option;
        return _variants.Get(all);
    }

    /// <summary>A machine type from its JSON, with its default options.</summary>
    internal static MachineDef Read(JsonObject json)
    {
        var source = (JsonObject)json.DeepClone();
        var configurations = source.Remove("configurations", out var node) && node != null
            ? node.Deserialize<List<ConfigurationDef>>(ContentDatabase.JsonOptions) ?? []
            : [];
        return new Variants(source, configurations).Get(new Dictionary<string, string>());
    }

    /// <summary>A machine type's JSON, and the defs built from it for each set of options chosen so far.</summary>
    private sealed class Variants(JsonObject json, List<ConfigurationDef> configurations)
    {
        private readonly Dictionary<string, MachineDef> _built = new();

        /// <summary>The def with each configuration's chosen option, or its default.</summary>
        public MachineDef Get(IReadOnlyDictionary<string, string> choices)
        {
            var options = configurations
                .Select(c => (configuration: c, option: c.Option(choices.GetValueOrDefault(c.Id)) ?? c.Default))
                .Where(x => x.option != null)
                .Select(x => (x.configuration, option: x.option!))
                .ToList();
            var key = string.Join('\n', options.Select(x => $"{x.configuration.Id}={x.option.Id}"));
            lock (_built)
            {
                if (!_built.TryGetValue(key, out var def)) _built[key] = def = Build(options);
                return def;
            }
        }

        private MachineDef Build(List<(ConfigurationDef configuration, ConfigurationOptionDef option)> options)
        {
            var merged = (JsonObject)json.DeepClone();
            foreach (var (_, option) in options)
                if (option.Changes != null)
                    JsonMerge.Into(merged, option.Changes);
            var def = merged.Deserialize<MachineDef>(ContentDatabase.JsonOptions) ?? throw new JsonException("a machine is empty");
            // An option changing the id is refused by validation; until then the machine keeps its own.
            if (json["id"] is JsonValue id && id.TryGetValue<string>(out var own)) def.Id = own;
            def.Configurations = configurations;
            def.Choices = options.ToDictionary(x => x.configuration.Id, x => x.option.Id);
            var shown = options.SelectMany(x => x.option.Show).ToHashSet();
            def.HiddenNodes = def.OptionNodes.Where(n => !shown.Contains(n)).ToHashSet();
            def.Price += options.Sum(x => x.option.Price);
            def.Mass += options.Sum(x => x.option.Mass);
            def._variants = this;
            return def;
        }
    }

    /// <summary>Its component def of type <typeparamref name="T"/> (or implementing it), if it has one.</summary>
    public T? Get<T>() where T : class => Components.OfType<T>().FirstOrDefault();

    /// <summary>Every joint implements hitch to.</summary>
    public IEnumerable<AttacherJointDef> Joints => Components.OfType<IJointSource>().SelectMany(s => s.Joints);

    /// <summary>Model node roles its components move.</summary>
    public IEnumerable<string> Roles => Components.SelectMany(c => c.Roles);
}

/// <summary>A choice a machine is had with (FS: configurations), such as its wheels, engine or color: one of its options.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfigurationDef
{
    /// <summary>Unique on the machine; saves refer to it.</summary>
    public string Id { get; set; } = "";
    /// <summary>What it's called: "Wheels", "Front hitch".</summary>
    public string Name { get; set; } = "";
    public ConfigurationOptionDef[] Options { get; set; } = [];

    /// <summary>The option the machine comes with: the one marked default, else the first.</summary>
    [JsonIgnore]
    public ConfigurationOptionDef? Default => Options.FirstOrDefault(o => o.Default) ?? Options.FirstOrDefault();

    public ConfigurationOptionDef? Option(string? id) => Options.FirstOrDefault(o => o.Id == id);
}

/// <summary>One option of a configuration: what it costs and weighs, and what it changes in the machine.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfigurationOptionDef
{
    /// <summary>Unique in its configuration; saves refer to it.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The machine comes with it (without one marked, the first option).</summary>
    public bool Default { get; set; }
    /// <summary>What it adds to the machine's price and mass (less when negative).</summary>
    public float Price { get; set; }
    public float Mass { get; set; }
    /// <summary>What it changes in the machine's JSON (its size, components, looks), merged in by <see cref="JsonMerge"/>.</summary>
    public JsonObject? Changes { get; set; }
    /// <summary>
    /// Model nodes (or placeholder parts) that make it up, such as a front weight or the tracks: hidden unless an option
    /// showing them is chosen, so that one model holds every configuration.
    /// </summary>
    public string[] Show { get; set; } = [];
}

// ---- Contracts (contracts.json)

/// <summary>
/// A kind of job the neighbors offer. A field job is done on one of their fields with a work area of type
/// <see cref="Work"/>; a delivery job (no work) asks for goods at a POI that buys them.
/// </summary>
public sealed class ContractTypeDef
{
    public string Id { get; set; } = "";
    /// <summary>What the job is called on the board: "Cultivate", "Harvest".</summary>
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Work area type doing a field job (cultivator, seeder, harvester); empty for a delivery job.</summary>
    public string Work { get; set; } = "";
    /// <summary>Months it is offered in (1..12). Empty: all year.</summary>
    public int[] Months { get; set; } = [];
    /// <summary>Field jobs: the state a field must mostly be in for the job to be offered.</summary>
    public FieldStateDef Offer { get; set; } = new();
    /// <summary>Field jobs: the state of a cell once the job is done there.</summary>
    public FieldStateDef Done { get; set; } = new();
    /// <summary>Field jobs: what the job pays for each hectare of the field.</summary>
    public float RewardPerHa { get; set; }
    /// <summary>[min, max] game days to finish the job once taken.</summary>
    public int[] Days { get; set; } = [3, 6];
    /// <summary>How often it comes up among the jobs that fit, relative to the others.</summary>
    public float Weight { get; set; } = 1f;
    /// <summary>Goods to bring to a buyer: a harvest's crop, or the goods of a delivery job.</summary>
    public ContractDeliveryDef? Deliver { get; set; }
    /// <summary>Field jobs: machine sets the job can be taken with, leased; an offer has the first that can do it.</summary>
    public ContractLeaseDef[] Leases { get; set; } = [];
}

/// <summary>Machines leased from a dealer for a contract, for a fee taken from the reward.</summary>
public sealed class ContractLeaseDef
{
    /// <summary>Machine ids. Implements come hitched to the first vehicle of the set with a joint for them.</summary>
    public string[] Machines { get; set; } = [];
    /// <summary>What the lease costs for each hectare of the field.</summary>
    public float FeePerHa { get; set; }
}

/// <summary>
/// What a field's cells look like: any of <see cref="Ground"/> and any of <see cref="Crop"/>, each ignored when empty.
/// </summary>
public sealed class FieldStateDef
{
    /// <summary>Crop states: none, dead, sown (a living crop at any stage), growing (not ripe yet), harvestable.</summary>
    public static readonly string[] CropStates = ["none", "dead", "sown", "growing", "harvestable"];

    /// <summary>Ground types of fields: grass, cultivated, seeded, stubble, plowed.</summary>
    public string[] Ground { get; set; } = [];
    /// <summary>
    /// Crop states (<see cref="CropStates"/>). In a job's done state, sown, growing and harvestable mean the job's own
    /// crop: the one ripe on the field, or the one to sow.
    /// </summary>
    public string[] Crop { get; set; } = [];

    public bool IsEmpty => Ground.Length == 0 && Crop.Length == 0;
}

/// <summary>Goods a contract wants brought to a buyer on the map (a POI with a sell action for them).</summary>
public sealed class ContractDeliveryDef
{
    /// <summary>Harvest jobs: the share of the crop harvested on the field that must reach the buyer.</summary>
    public float Share { get; set; }
    /// <summary>Delivery jobs: [min, max] units asked for, rounded to the thousand.</summary>
    public float[] Amount { get; set; } = [];
    /// <summary>Delivery jobs: the fill types it may ask for (empty: any a buyer takes).</summary>
    public string[] FillTypes { get; set; } = [];
    /// <summary>Delivery jobs: the reward, as the market price of the goods times this.</summary>
    public float PriceFactor { get; set; } = 1f;
}

// ---- Map layout (hand-authored), combined with procedural height/soil/trees ----

public sealed class RectDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
    public float H { get; set; }
}

public sealed class TileRunDef
{
    /// <summary>Axis-aligned run in tile units (tile size from MapDef.TileSize).</summary>
    public int X0 { get; set; }
    public int Z0 { get; set; }
    public int X1 { get; set; }
    public int Z1 { get; set; }
}

/// <summary>
/// A connected set of tiles drawn with one atlas (roads, farm tracks, paths, streams).
/// All networks share the map's tile grid; masks are computed within a network.
/// </summary>
public sealed class TileNetworkDef
{
    public string Id { get; set; } = "";
    /// <summary>Atlas set name in assets/textures/tiles/manifest.json.</summary>
    public string Style { get; set; } = "road_country";
    /// <summary>Ground type of the band: road, dirt or water.</summary>
    public string Ground { get; set; } = "road";
    /// <summary>Width of the band (meters) marked with <see cref="Ground"/>; should match the art.</summary>
    public float Width { get; set; } = 6.6f;
    /// <summary>Terrain shaping: flatten strength 0..1, and depth to carve (streams).</summary>
    public float Flatten { get; set; } = 1f;
    public float Carve { get; set; }
    public TileRunDef[] Runs { get; set; } = [];
}

/// <summary>A map area given as a rectangle (x, z, w, h) or, when <see cref="Polygon"/> is set, as a polygon.</summary>
public abstract class ShapeDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    /// <summary>Outline as [x, z] points in meters, in order (either winding). Replaces the rectangle.</summary>
    public float[][]? Polygon { get; set; }

    /// <summary>Why the shape can't be built, or null when it's valid.</summary>
    public string? ShapeError()
    {
        if (Polygon == null) return W > 0 && H > 0 ? null : "needs w and h > 0, or a polygon";
        if (Polygon.Length < 3) return "polygon needs at least 3 points";
        if (Polygon.Any(p => p.Length != 2)) return "polygon points are [x, z] pairs";
        return Shape().Area < 1f ? "polygon has no area" : null;
    }

    public World.Polygon Shape() => Polygon != null
        ? new World.Polygon(Polygon.Select(p => new System.Numerics.Vector2(p[0], p[1])))
        : World.Polygon.Rect(X, Z, W, H);
}

/// <summary>A parcel of land bought and sold as a whole (FS farmland). Fields are the crop areas inside it.</summary>
public sealed class FarmlandDef : ShapeDef
{
    /// <summary>Parcel number, unique per map (1..65535).</summary>
    public int Id { get; set; }
    /// <summary>NPC owning the parcel whenever no farm does (npcs.json id).</summary>
    public string Npc { get; set; } = "";
    /// <summary>Farm owning it at the start: 0 = its NPC (for sale), 1 = the player's farm.</summary>
    public int Farm { get; set; }
    /// <summary>Multiplies the map's price per hectare (better or worse land).</summary>
    public float PriceFactor { get; set; } = 1f;
}

public sealed class FieldDef : ShapeDef
{
    /// <summary>Field number shown to the player, unique per map (1..65535).</summary>
    public int Id { get; set; }
    /// <summary>Initial ground: grass, cultivated, stubble, seeded, plowed.</summary>
    public string Ground { get; set; } = "stubble";
    public string? Crop { get; set; }
    /// <summary>Initial crop stage index (or "harvestable").</summary>
    public string? Stage { get; set; }
    /// <summary>Work direction of the initial pattern, in degrees.</summary>
    public float AngleDeg { get; set; }
}

// ---- Points of interest (pois/*.json), placed by maps

/// <summary>
/// A point of interest: a building or site maps place, from a farmhouse to a grain elevator. Drawn from placeholder
/// parts or a model. Local space as for machines: +Z forward (the front), +X left, origin at the footprint's center.
/// </summary>
public sealed class PoiDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Map icon: a Material Symbols icon in assets/icons, by file name (e.g. "storefront").</summary>
    public string? Icon { get; set; }
    /// <summary>Ground the POI covers, centered on its origin: width along x, depth along z (meters).</summary>
    public float W { get; set; } = 10f;
    public float D { get; set; } = 10f;
    public PoiPartDef[] Parts { get; set; } = [];
    /// <summary>Optional model drawn instead of the parts (which still collide).</summary>
    public ModelDef? Visual { get; set; }
    /// <summary>Areas where machines use the POI.</summary>
    public PoiTriggerDef[] Triggers { get; set; } = [];
    /// <summary>Goods the POI keeps, which store and process actions use. Owned by the POI's farm.</summary>
    public PoiStorageDef? Storage { get; set; }
    /// <summary>What the POI does, for the machines at its triggers or on its own (processing).</summary>
    public PoiActionDef[] Actions { get; set; } = [];
}

public sealed class PoiStorageDef
{
    public string[] FillTypes { get; set; } = [];
    /// <summary>Room for each fill type (units).</summary>
    public float Capacity { get; set; } = 100_000f;
    /// <summary>Fill types whose room differs from <see cref="Capacity"/>.</summary>
    public Dictionary<string, float> Capacities { get; set; } = new();

    public float CapacityOf(string fillType) => Capacities.GetValueOrDefault(fillType, Capacity);
}

/// <summary>
/// Demand at a sell action: the price of a fill type drops as loads of it come in and recovers day by day, and now
/// and then a fill type is in high demand for a few days.
/// </summary>
public sealed class DemandDef
{
    /// <summary>Price drop for each 100,000 units sold (0.04 = 4%), down to <see cref="Floor"/>.</summary>
    public float Drop { get; set; } = 0.04f;
    /// <summary>The lowest the demand factor goes.</summary>
    public float Floor { get; set; } = 0.7f;
    /// <summary>Demand factor regained per game day.</summary>
    public float Recovery { get; set; } = 0.02f;
    /// <summary>Chance per game day that one of the fill types goes in high demand (one at a time per POI).</summary>
    public float HighChance { get; set; } = 0.03f;
    /// <summary>High demand: [min, max] price factor, and [min, max] game days it lasts.</summary>
    public float[] HighFactor { get; set; } = [1.2f, 1.5f];
    public int[] HighDays { get; set; } = [1, 3];
}

/// <summary>An amount of a fill type (processing inputs).</summary>
public class FillAmountDef
{
    public string FillType { get; set; } = "";
    public float Amount { get; set; }
}

/// <summary>What a processing cycle makes. It goes into the POI's storage first.</summary>
public sealed class ProcessOutputDef : FillAmountDef
{
    /// <summary>"store": kept for the owner's trailers at a load trigger. "sell": sold every hour, for the owner.</summary>
    public string Mode { get; set; } = "store";
}

/// <summary>An area of a POI where machines do something: unload, load, fill up, get washed or repaired, get delivered.</summary>
public sealed class PoiTriggerDef
{
    /// <summary>Unique within the POI; actions refer to it.</summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// How machines use it: "unload" (a trailer tipping inside it, a combine's pipe over it); "load" (the owner's
    /// trailers inside it fill up from the POI's storage), "fill", "wash" and "repair" (machines parked inside it,
    /// with the use key); "delivery" (where new machines appear).
    /// </summary>
    public string Type { get; set; } = "unload";
    /// <summary>Center in the POI's local space; width along x, depth along z.</summary>
    public float X { get; set; }
    public float Z { get; set; }
    public float W { get; set; } = 10f;
    public float D { get; set; } = 10f;
    /// <summary>Load: units per second poured into a trailer.</summary>
    public float Rate { get; set; } = 400f;
}

/// <summary>
/// Something a POI does, seen from the farmer's side. At an unload trigger: "sell" (the POI buys loads of
/// <see cref="FillTypes"/>, into its storage if it keeps them) or "store" (the owner's loads go into its storage).
/// At a fill trigger: "buy" (it sells <see cref="FillTypes"/>) or "refuel" (it fills fuel tanks with them). "repair"
/// and "wash" work at triggers of their own type. "process" needs no trigger: it turns stored inputs into outputs.
/// </summary>
public sealed class PoiActionDef
{
    public string Type { get; set; } = "sell";
    /// <summary>Id of the trigger machines use (none for process).</summary>
    public string Trigger { get; set; } = "";
    /// <summary>What it trades or stores (store: defaults to everything the storage keeps).</summary>
    public string[] FillTypes { get; set; } = [];
    /// <summary>Hours it is open, [from, to) in game hours (past midnight when from > to). Missing: always.</summary>
    public float[]? OpenHours { get; set; }
    /// <summary>Months it works in (1..12). Empty: all year.</summary>
    public int[] Months { get; set; } = [];
    /// <summary>Smallest load it takes (sell, store) or amount it sells (buy, refuel).</summary>
    public float MinAmount { get; set; }
    /// <summary>
    /// Multiplies the price: for sell, buy and refuel the market price (the fill type's monthly curve), for process
    /// the market price of the outputs it sells, for repair the standard price (1% of the machine's price for each
    /// 100% of wear), for configure the price of the options fitted.
    /// </summary>
    public float PriceFactor { get; set; } = 1f;
    /// <summary>Sell, buy, refuel, process: fill types whose factor differs from <see cref="PriceFactor"/>.</summary>
    public Dictionary<string, float> PriceFactors { get; set; } = new();
    /// <summary>Sell: how prices react to what farmers sell here.</summary>
    public DemandDef Demand { get; set; } = new();
    /// <summary>Wash: price of washing a fully dirty machine. Configure: price of the work for each option changed.</summary>
    public float Price { get; set; }
    /// <summary>Process: what one cycle takes from storage and puts into it.</summary>
    public FillAmountDef[] Inputs { get; set; } = [];
    public ProcessOutputDef[] Outputs { get; set; } = [];
    /// <summary>Process: game hours per cycle (below 1 for several cycles an hour).</summary>
    public float CycleHours { get; set; } = 1f;
    /// <summary>Process: what the owner pays for each hour it runs.</summary>
    public float RunningCost { get; set; }
}

/// <summary>One placeholder block of a POI: a building, a silo, a stack of pallets.</summary>
public sealed class PoiPartDef
{
    /// <summary>Placeholder shape: house, shed, silo, tank, elevator, store, pallets, canopy, pump or box. Silos and tanks are round.</summary>
    public string Shape { get; set; } = "box";
    /// <summary>Center in the POI's local space.</summary>
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>Width along local x (the diameter of round shapes), depth along z, height.</summary>
    public float W { get; set; } = 4f;
    public float D { get; set; } = 4f;
    public float H { get; set; } = 3f;
    public float RotDeg { get; set; }
    public string Color { get; set; } = "#8c8378";
    /// <summary>Blocks machines and the farmer (false for low props they can drive over).</summary>
    public bool Solid { get; set; } = true;

    public bool Round => Shape is "silo" or "tank";
}

/// <summary>A POI placed on a map.</summary>
public sealed class PoiPlacementDef
{
    /// <summary>Unique on the map; saves refer to it.</summary>
    public string Id { get; set; } = "";
    /// <summary>The POI type (pois/*.json id).</summary>
    public string Type { get; set; } = "";
    /// <summary>Replaces the type's name.</summary>
    public string? Name { get; set; }
    /// <summary>Where the POI's origin (the center of its footprint) goes.</summary>
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>Heading of the POI's front, as for machines: 0 faces +z (south), 90 faces +x (east).</summary>
    public float HeadingDeg { get; set; }
    /// <summary>Owning farm: 0 = an NPC's (default), 1 = the player's.</summary>
    public int Farm { get; set; }
}

public sealed class MachineSpawnDef
{
    public string Def { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float HeadingDeg { get; set; }
    /// <summary>Owning farm: 1 = the player's (default), 0 = an NPC's (can't be driven or hitched).</summary>
    public int Farm { get; set; } = Ownership.Farm.PlayerId;
    /// <summary>Its options (configuration id → option id); the defaults for the rest.</summary>
    public Dictionary<string, string>? Configuration { get; set; }
    /// <summary>Optional: attach to the machine spawned at this index, on this joint.</summary>
    public int? AttachToIndex { get; set; }
    public string? Joint { get; set; }
}

public sealed class ForestDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    /// <summary>Trees per 100 m².</summary>
    public float Density { get; set; } = 1f;
}

public sealed class MapDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Size { get; set; } = 512;
    public int Seed { get; set; } = 1337;
    public float HillAmplitude { get; set; } = 5f;
    /// <summary>Edge of one road/track/path/stream tile in meters (the art's band is 37–50% of a tile).</summary>
    public float TileSize { get; set; } = 16f;
    public TileNetworkDef[] Networks { get; set; } = [];
    public RectDef[] Yards { get; set; } = [];
    /// <summary>Parcels; where two overlap, the later one wins. Land outside every parcel can't be bought.</summary>
    public FarmlandDef[] Farmlands { get; set; } = [];
    /// <summary>What a hectare of farmland costs (and sells for), before each parcel's price factor.</summary>
    public float FarmlandPricePerHa { get; set; } = 20_000f;
    /// <summary>Each field lies inside a farmland (the one holding its centroid).</summary>
    public FieldDef[] Fields { get; set; } = [];
    public PoiPlacementDef[] Pois { get; set; } = [];
    public MachineSpawnDef[] Machines { get; set; } = [];
    public ForestDef[] Forests { get; set; } = [];
    /// <summary>Scattered trees per hectare on open grass.</summary>
    public float ScatteredTreesPerHa { get; set; } = 0.6f;
    public float PlayerX { get; set; }
    public float PlayerZ { get; set; }
}

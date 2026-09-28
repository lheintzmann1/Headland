using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Headland.Core.Components;
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
    /// <summary>The farmer's model (see docs/MODELING.md).</summary>
    public ModelDef Player { get; set; } = new();
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
    /// <summary>Nitrogen a unit gives the soil when spread (kg N per unit): what fertilizers are for.</summary>
    public float Nitrogen { get; set; }
}

/// <summary>A kind of attacher joint (jointtypes.json): an implement hitches to a joint of its attachable's type.</summary>
public sealed class JointTypeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>A three-point linkage: its lower links (the model node named after the joint and "Linkage") lift with the implement mounted on it.</summary>
    public bool Linkage { get; set; }
}

/// <summary>A kind of lamp (lamptypes.json): the driver switches a machine's lamps of a type together.</summary>
public sealed class LampTypeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The driver's lamps of the type also come on by themselves at night (headlights).</summary>
    public bool Night { get; set; }
    /// <summary>Its beam turns round (a beacon).</summary>
    public bool Rotating { get; set; }
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
    /// <summary>The ground its cells show once sown: seeded (bare, drilled soil), or grass for a meadow.</summary>
    public string Ground { get; set; } = "seeded";

    /// <summary><see cref="Ground"/>, read.</summary>
    [JsonIgnore]
    public World.GroundType SownGround => World.WorldGen.ParseGround(Ground);
    /// <summary>
    /// A crop that grows back once cut (grass): the stage it starts over from when mown. Null: cutting it clears the
    /// field.
    /// </summary>
    public int? RegrowStage { get; set; }
    /// <summary>Yield lost where weeds have grown among it (0.2 = 20%; half that where they're still small).</summary>
    public float WeedYieldLoss { get; set; } = 0.2f;
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

/// <summary>A glTF model: how a machine, a POI or the farmer looks.</summary>
public class ModelDef
{
    /// <summary>The glTF model (res:// path, e.g. a Blockbench .glb export).</summary>
    public string? Model { get; set; }
    /// <summary>Uniform scale applied to the model (1 = model units are meters).</summary>
    public float Scale { get; set; } = 1f;
    /// <summary>Turn the model around Y so its front faces +Z (180 for models built facing -Z).</summary>
    public float YawDeg { get; set; }
    /// <summary>Model offset [x, y, z] in meters, in local space (+Z forward, +X left).</summary>
    public float[] Offset { get; set; } = [0f, 0f, 0f];
}

/// <summary>How an entity (a machine, a POI) looks: its model, its paint, and the model nodes its components move.</summary>
public sealed class VisualDef : ModelDef
{
    /// <summary>The paint: the color of its model's paint materials (see docs/MODELING.md).</summary>
    public string Color { get; set; } = "#7a3326";
    /// <summary>
    /// Moving parts not named after their role in the model: role → node name. The entity's components give the roles
    /// (wheel0L, wheel0R… for each side of the running gear's axles, pipe, tipper, reel, load…). See docs/MODELING.md.
    /// </summary>
    public Dictionary<string, string>? Nodes { get; set; }
}

/// <summary>
/// A machine type: what it is, its size and looks, the components it's built from, and the options it can have. Each
/// set of options makes a def of its own (<see cref="Configure"/>); the content lists each machine with its default
/// options. Unknown properties are refused, so a machine written in the format from before components (motorized,
/// wheels, workArea… at the top) doesn't load as an empty shell.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MachineDef : EntityDef
{
    private Variants? _variants;

    public string Category { get; set; } = "";
    public string Brand { get; set; } = "";
    /// <summary>With the chosen options' prices and masses added.</summary>
    public float Price { get; set; }
    public float Mass { get; set; } = 3000f;
    public SizeDef Size { get; set; } = new();
    /// <summary>What it can be had with (FS configurations): wheels, engine, color… one option of each.</summary>
    public List<ConfigurationDef> Configurations { get; set; } = [];

    /// <summary>The option chosen in each of its configurations, by configuration id.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Choices { get; private set; } = new Dictionary<string, string>();

    /// <summary>Model nodes its options are made of: each is visible with the options it's in.</summary>
    [JsonIgnore]
    public IEnumerable<string> OptionNodes => Configurations.SelectMany(c => c.Options.SelectMany(o => o.Nodes(c))).Distinct();

    /// <summary>Model nodes the chosen options are made of.</summary>
    private IReadOnlySet<string> _shownNodes = new HashSet<string>();

    /// <summary>
    /// The model nodes that may move as <paramref name="role"/>, the first a model has winning: the chosen options' own
    /// versions of it (configuration_option_role, such as wheels_rowCrop_wheel0L for row-crop wheels), then
    /// <see cref="EntityDef.NodeOf"/>, which a version found hides.
    /// </summary>
    public override IEnumerable<string> NodesOf(string role) => Choices.Select(c => $"{c.Key}_{c.Value}_{role}").Append(NodeOf(role));

    /// <summary>
    /// Whether the model node <paramref name="node"/> is hidden on this machine (see
    /// docs/MODELING.md): it's part of options it doesn't have and of none it has, or a moving part only other options
    /// have (the rear wheels of a machine with rear tracks).
    /// </summary>
    public override bool Hides(string node) =>
        (OptionNodes.Any(n => IsPartOf(node, n)) && !_shownNodes.Any(n => IsPartOf(node, n)))
        || (_variants?.MovedNodes.Contains(node) == true && !Roles.Any(r => NodeOf(r) == node));

    /// <summary>
    /// The configuration a model node is named after (configuration_…) without being part of any of its options: a
    /// typo, most likely. Null for any other node.
    /// </summary>
    public ConfigurationDef? UnknownOption(string node) =>
        Configurations.FirstOrDefault(c => node.StartsWith(c.Id + "_", StringComparison.Ordinal))
            is { } configuration && !OptionNodes.Any(n => IsPartOf(node, n))
            ? configuration
            : null;

    /// <summary>Whether <paramref name="node"/> is <paramref name="name"/> or a piece of it: name_… (as Blender's name.001 imports).</summary>
    private static bool IsPartOf(string node, string name) =>
        node.StartsWith(name, StringComparison.Ordinal) && (node.Length == name.Length || node[name.Length] == '_');

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

    /// <summary>A machine type from its JSON, with its default options, linked to <paramref name="content"/> as each set of options is built.</summary>
    internal static MachineDef Read(JsonObject json, ContentDatabase content)
    {
        var source = (JsonObject)json.DeepClone();
        var configurations = source.Remove("configurations", out var node) && node != null
            ? node.Deserialize<List<ConfigurationDef>>(ContentDatabase.JsonOptions) ?? []
            : [];
        return new Variants(source, configurations, content).Get(new Dictionary<string, string>());
    }

    /// <summary>A machine type's JSON, and the defs built from it for each set of options chosen so far.</summary>
    private sealed class Variants(JsonObject json, List<ConfigurationDef> configurations, ContentDatabase content)
    {
        private readonly Dictionary<string, MachineDef> _built = new();
        private IReadOnlySet<string>? _moved;

        /// <summary>The model nodes it moves as it comes or with any one of its options.</summary>
        public IReadOnlySet<string> MovedNodes => _moved ??= configurations
            .SelectMany(c => c.Options.Select(o => Get(new Dictionary<string, string> { [c.Id] = o.Id })))
            .Prepend(Get(new Dictionary<string, string>()))
            .SelectMany(d => d.Roles.Select(d.NodeOf))
            .ToHashSet();

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
            def._shownNodes = options.SelectMany(x => x.option.Nodes(x.configuration)).ToHashSet();
            def.Price += options.Sum(x => x.option.Price);
            def.Mass += options.Sum(x => x.option.Mass);
            def._variants = this;
            def.Link(content);
            return def;
        }
    }

    /// <summary>Every joint implements hitch to.</summary>
    public IEnumerable<AttacherJointDef> Joints => Components.OfType<IJointSource>().SelectMany(s => s.Joints);
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
    /// Model nodes it shares with other options, such as the two beacons "both" shows, besides
    /// its own (<see cref="Nodes"/>).
    /// </summary>
    public string[] Show { get; set; } = [];

    /// <summary>
    /// The model nodes that make it up, hidden unless it's chosen so that one model holds every
    /// configuration: the one named after it, configuration_option (frontHitch_weight), and those it shows.
    /// </summary>
    public IEnumerable<string> Nodes(ConfigurationDef configuration) => Show.Prepend($"{configuration.Id}_{Id}");
}

// ---- Contracts (contracts.json)

/// <summary>
/// A kind of job the neighbors offer. A field job is done on one of their fields with a work area of type
/// <see cref="Work"/> (a <see cref="Machines.Work.WorkTypes">work type</see>); a delivery job (no work) asks for goods
/// at a POI that buys them.
/// </summary>
public sealed class ContractTypeDef
{
    public string Id { get; set; } = "";
    /// <summary>What the job is called on the board: "Cultivate", "Harvest".</summary>
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Work area type doing a field job (cultivator, plow, seeder…); empty for a delivery job.</summary>
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
/// What a field's cells look like: any of <see cref="Ground"/>, any of <see cref="Crop"/>, any of <see cref="Weeds"/>,
/// and fertilized or not; each ignored when left out.
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
    /// <summary>Weed states (<see cref="World.WeedState.Names"/>): none, small, grown, sprayed.</summary>
    public string[] Weeds { get; set; } = [];
    /// <summary>Fertilized since the last harvest (true) or not (false).</summary>
    public bool? Fertilized { get; set; }

    public bool IsEmpty => Ground.Length == 0 && Crop.Length == 0 && Weeds.Length == 0 && Fertilized == null;
}

/// <summary>Goods a contract wants brought to a buyer on the map (a POI whose selling station takes them).</summary>
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
/// A point of interest: a building or site maps place, from a farmhouse to a grain elevator, drawn by its model, doing
/// what its components do (selling, storing, repairing…). Local space as for machines: +Z forward (the front), +X left,
/// origin at the footprint's center. Unknown properties are refused, so a POI written in the format from before
/// components (triggers, storage, actions at the top) doesn't load as an empty shell.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PoiDef : EntityDef
{
    /// <summary>Ground the POI covers, centered on its origin: width along x, depth along z (meters).</summary>
    public float W { get; set; } = 10f;
    public float D { get; set; } = 10f;
    /// <summary>What machines and the farmer bump into.</summary>
    public PoiColliderDef[] Colliders { get; set; } = [];
}

/// <summary>What machines and the farmer bump into at a POI: a box, or a circle (a silo, a tank).</summary>
public sealed class PoiColliderDef
{
    /// <summary>Center in the POI's local space.</summary>
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>Width along local x (the diameter of a round one) and depth along z.</summary>
    public float W { get; set; } = 4f;
    public float D { get; set; } = 4f;
    public float RotDeg { get; set; }
    public bool Round { get; set; }
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

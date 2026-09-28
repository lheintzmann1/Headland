using System.Text.Json;
using System.Text.Json.Nodes;
using Headland.Core.Components;
using Headland.Core.Machines.Components;
using Headland.Core.Machines.Work;

namespace Headland.Core.Content;

/// <summary>Abstracts where data files come from (Godot res://, or the file system in tests/tools).</summary>
public interface IContentSource
{
    /// <summary>Relative paths of *.json files inside <paramref name="dir"/> (non-recursive), sorted.</summary>
    IReadOnlyList<string> ListJson(string dir);
    string ReadText(string relativePath);
    bool Exists(string relativePath);
}

public sealed class FileSystemContentSource : IContentSource
{
    private readonly string _root;

    public FileSystemContentSource(string root) => _root = root;

    public IReadOnlyList<string> ListJson(string dir)
    {
        var full = Path.Combine(_root, dir);
        if (!Directory.Exists(full)) return [];
        return Directory.GetFiles(full, "*.json")
            .Select(f => Path.Combine(dir, Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    public string ReadText(string relativePath) => File.ReadAllText(Path.Combine(_root, relativePath));

    public bool Exists(string relativePath) => File.Exists(Path.Combine(_root, relativePath));
}

public sealed class ContentException(string message) : Exception(message);

/// <summary>A mod the content was loaded with; saves record them.</summary>
public sealed record ModRef(string Id, string Version);

/// <summary>All loaded definitions. Soils and crops keep their list order: the index is what cells store.</summary>
public sealed class ContentDatabase
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public GameConfig Game { get; private set; } = new();
    public EconomyDef Economy { get; private set; } = new();
    public Dictionary<string, DifficultyDef> Difficulties { get; } = new();
    public List<FillTypeDef> FillTypeList { get; } = [];
    public Dictionary<string, FillTypeDef> FillTypes { get; } = new();
    public Dictionary<string, JointTypeDef> JointTypes { get; } = new();
    public Dictionary<string, LampTypeDef> LampTypes { get; } = new();
    public List<SoilDef> Soils { get; } = [];
    public Dictionary<string, NpcDef> Npcs { get; } = new();
    /// <summary>Crop index + 1 is stored in cells (0 = no crop).</summary>
    public List<CropDef> Crops { get; } = [];
    public Dictionary<string, MachineDef> Machines { get; } = new();
    public Dictionary<string, PoiDef> Pois { get; } = new();
    public Dictionary<string, ContractTypeDef> ContractTypes { get; } = new();
    public Dictionary<string, ClimateDef> Climates { get; } = new();
    public Dictionary<string, MapDef> Maps { get; } = new();
    /// <summary>Mods layered over the base game, in load order (none until the mod loader exists).</summary>
    public List<ModRef> Mods { get; } = [];

    public CropDef? CropById(string id) => Crops.Find(c => c.Id == id);

    /// <summary>The fill types in <paramref name="category"/>, in their order in filltypes.json.</summary>
    public IEnumerable<string> FillTypesIn(string category) => FillTypeList.Where(f => f.Categories.Contains(category)).Select(f => f.Id);

    /// <summary><paramref name="fillTypes"/> and those of <paramref name="categories"/>, each once: what a unit or a station takes.</summary>
    internal string[] WithCategories(string[] fillTypes, string[] categories) =>
        categories.Length == 0 ? fillTypes : fillTypes.Concat(categories.SelectMany(FillTypesIn)).Distinct().ToArray();

    /// <summary>What's wrong with <paramref name="categories"/>: those no fill type is in.</summary>
    internal IEnumerable<string> CategoryErrors(string[] categories) =>
        categories.Where(c => !FillTypesIn(c).Any()).Select(c => $"no fill type is in category '{c}'");
    public int CropIndex(string id) => Crops.FindIndex(c => c.Id == id);
    public int SoilIndex(string id) => Soils.FindIndex(s => s.Id == id);

    public ClimateDef Climate => Climates[Game.Climate];
    public MapDef Map => Maps[Game.Map];

    public static ContentDatabase Load(IContentSource src)
    {
        var db = new ContentDatabase();
        db.Game = Parse<GameConfig>(src, "game.json");
        db.Economy = Parse<EconomyDef>(src, "economy.json");
        foreach (var d in ReadMany<DifficultyDef>(src, "difficulties.json")) db.AddUnique(db.Difficulties, d.Id, d, "difficulty");

        foreach (var f in ReadMany<FillTypeDef>(src, "filltypes.json")) db.AddUnique(db.FillTypes, f.Id, f, "fill type");
        db.FillTypeList.AddRange(db.FillTypes.Values);
        foreach (var j in ReadMany<JointTypeDef>(src, "jointtypes.json")) db.AddUnique(db.JointTypes, j.Id, j, "joint type");
        foreach (var l in ReadMany<LampTypeDef>(src, "lamptypes.json")) db.AddUnique(db.LampTypes, l.Id, l, "lamp type");
        db.Soils.AddRange(ReadMany<SoilDef>(src, "soils.json"));
        foreach (var n in ReadMany<NpcDef>(src, "npcs.json")) db.AddUnique(db.Npcs, n.Id, n, "npc");
        foreach (var file in src.ListJson("crops")) db.Crops.AddRange(ReadMany<CropDef>(src, file));
        foreach (var file in src.ListJson("machines"))
        foreach (var m in ReadMachines(src, file, db))
            db.AddUnique(db.Machines, m.Id, m, "machine");
        foreach (var file in src.ListJson("pois"))
        foreach (var p in ReadMany<PoiDef>(src, file))
        {
            p.Link(db);
            db.AddUnique(db.Pois, p.Id, p, "poi");
        }
        foreach (var c in ReadMany<ContractTypeDef>(src, "contracts.json"))
        {
            if (c.Deliver is { } d) d.FillTypes = db.WithCategories(d.FillTypes, d.FillTypeCategories);
            db.AddUnique(db.ContractTypes, c.Id, c, "contract type");
        }
        foreach (var file in src.ListJson("climates"))
        foreach (var c in ReadMany<ClimateDef>(src, file))
            db.AddUnique(db.Climates, c.Id, c, "climate");
        foreach (var file in src.ListJson("maps"))
        foreach (var m in ReadMany<MapDef>(src, file))
            db.AddUnique(db.Maps, m.Id, m, "map");

        var errors = db.Validate();
        if (errors.Count > 0)
            throw new ContentException("Content validation failed:\n - " + string.Join("\n - ", errors));
        return db;
    }

    /// <summary>
    /// Leaves a machine out of the game (the game couldn't load its model): its def, its places on the maps and the
    /// lease sets it's in. What hung on it stays unhitched, where the map puts it or, placed only by its hitch, where
    /// the machine stood. Says what changed.
    /// </summary>
    public List<string> RemoveMachine(string id)
    {
        var gone = new List<string>();
        if (!Machines.Remove(id)) return gone;
        foreach (var map in Maps.Values)
        {
            // Old index → new one, for the machines that stay.
            var kept = new List<MachineSpawnDef>();
            var index = new Dictionary<int, int>();
            for (var i = 0; i < map.Machines.Length; i++)
            {
                if (map.Machines[i].Def == id) continue;
                index[i] = kept.Count;
                kept.Add(map.Machines[i]);
            }
            if (kept.Count == map.Machines.Length) continue;
            var placed = map.Machines.Length - kept.Count;
            gone.Add($"removed from map '{map.Id}' ({placed} {(placed == 1 ? "place" : "places")})");
            foreach (var sp in kept)
            {
                if (sp.AttachToIndex is not { } from) continue;
                if (index.TryGetValue(from, out var to))
                {
                    sp.AttachToIndex = to;
                    continue;
                }
                var puller = map.Machines[from];
                if (sp is { X: 0f, Z: 0f }) (sp.X, sp.Z, sp.HeadingDeg) = (puller.X, puller.Z, puller.HeadingDeg);
                sp.AttachToIndex = null;
                sp.Joint = null;
                gone.Add($"the {sp.Def} hitched to it on map '{map.Id}' stands unhitched");
            }
            map.Machines = kept.ToArray();
        }
        foreach (var t in ContractTypes.Values.Where(t => t.Leases.Any(l => l.Machines.Contains(id))))
        {
            t.Leases = t.Leases.Where(l => !l.Machines.Contains(id)).ToArray();
            gone.Add($"removed from the lease sets of '{t.Id}' contracts");
        }
        return gone;
    }

    private void AddUnique<T>(Dictionary<string, T> dict, string id, T value, string kind)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ContentException($"A {kind} has an empty id.");
        if (!dict.TryAdd(id, value)) throw new ContentException($"Duplicate {kind} id '{id}'.");
    }

    private static T Parse<T>(IContentSource src, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(src.ReadText(path), JsonOptions)
                   ?? throw new ContentException($"{path}: empty document.");
        }
        catch (JsonException e)
        {
            throw new ContentException($"{path}: {e.Message}");
        }
    }

    /// <summary>A file may hold a single object or an array of objects.</summary>
    private static List<T> ReadMany<T>(IContentSource src, string path)
    {
        var text = src.ReadText(path).TrimStart();
        try
        {
            if (text.StartsWith('['))
                return JsonSerializer.Deserialize<List<T>>(text, JsonOptions) ?? [];
            return [JsonSerializer.Deserialize<T>(text, JsonOptions)!];
        }
        catch (JsonException e)
        {
            throw new ContentException($"{path}: {e.Message}");
        }
    }

    /// <summary>Machines are kept as JSON too: their configuration options change it.</summary>
    private static List<MachineDef> ReadMachines(IContentSource src, string path, ContentDatabase content)
    {
        var id = "";
        try
        {
            var root = JsonNode.Parse(src.ReadText(path), new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var machines = new List<MachineDef>();
            IEnumerable<JsonNode?> nodes = root is JsonArray list ? list : [root];
            foreach (var node in nodes)
            {
                if (node is not JsonObject json) throw new JsonException("a machine must be an object");
                id = json["id"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
                machines.Add(MachineDef.Read(json, content));
            }
            return machines;
        }
        catch (JsonException e)
        {
            throw new ContentException($"{path}: {(id != "" ? $"machine '{id}': " : "")}{e.Message}");
        }
    }

    public List<string> Validate()
    {
        var e = new List<string>();
        string[] fieldGrounds = ["grass", "cultivated", "seeded", "stubble", "plowed"];

        if (Game.DaysPerMonth < 1) e.Add("game.daysPerMonth must be >= 1");
        if (!Climates.ContainsKey(Game.Climate)) e.Add($"game.climate '{Game.Climate}' not found");
        if (!Maps.ContainsKey(Game.Map)) e.Add($"game.map '{Game.Map}' not found");
        if (Soils.Count == 0) e.Add("no soils defined");
        if (Soils.Count > 16) e.Add("at most 16 soils are supported");
        if (Crops.Count is 0 or > 254) e.Add("need between 1 and 254 crops");
        if (Npcs.Count == 0) e.Add("no npcs defined");
        if (Economy.LoanStep <= 0) e.Add("economy.loanStep must be > 0");
        if (Economy.CreditLimit < 0) e.Add("economy.creditLimit must be >= 0");
        if (Economy.LoanInterest is < 0 or > 1) e.Add("economy.loanInterest must be 0..1");
        if (Economy.HelperWagePerHour < 0) e.Add("economy.helperWagePerHour must be >= 0");
        var rules = Economy.Contracts;
        if (rules.MaxOffers < 0 || rules.OffersPerDay < 0 || rules.OfferDays < 1 || rules.MaxActive < 1)
            e.Add("economy.contracts: maxOffers and offersPerDay must be >= 0, offerDays and maxActive >= 1");
        if (rules.Threshold is <= 0 or > 1) e.Add("economy.contracts.threshold must be in (0, 1]");
        if (rules.Penalty is < 0 or > 1) e.Add("economy.contracts.penalty must be 0..1");
        if (!Difficulties.ContainsKey(Game.Difficulty)) e.Add($"game.difficulty '{Game.Difficulty}' not found");
        foreach (var d in Difficulties.Values)
        {
            if (d.StartMoney < 0) e.Add($"difficulty '{d.Id}': startMoney must be >= 0");
            if (d.StartLoan < 0 || d.StartLoan > Economy.CreditLimit) e.Add($"difficulty '{d.Id}': startLoan must be 0..economy.creditLimit");
            if (d.PriceLevel <= 0) e.Add($"difficulty '{d.Id}': priceLevel must be > 0");
        }

        foreach (var (kind, names) in new[] { ("joint", JointTypes.Values.Select(j => (j.Id, j.Name))), ("lamp", LampTypes.Values.Select(l => (l.Id, l.Name))) })
            foreach (var (id, _) in names.Where(x => string.IsNullOrWhiteSpace(x.Name)))
                e.Add($"{kind} type '{id}': needs a name");

        foreach (var f in FillTypeList)
        {
            if (f.MonthlyPriceFactor is { Length: not 12 }) e.Add($"fill type '{f.Id}': monthlyPriceFactor needs 12 values");
            if (f.Nitrogen < 0f) e.Add($"fill type '{f.Id}': nitrogen must be >= 0");
            if (f.Categories.Any(string.IsNullOrWhiteSpace)) e.Add($"fill type '{f.Id}': categories need names");
        }

        var seenCrops = new HashSet<string>();
        foreach (var c in Crops)
        {
            if (!seenCrops.Add(c.Id)) e.Add($"duplicate crop id '{c.Id}'");
            if (!FillTypes.ContainsKey(c.FillType)) e.Add($"crop '{c.Id}': fill type '{c.FillType}' not found");
            if (c.Stages.Length is < 2 or > 16) e.Add($"crop '{c.Id}': needs 2..16 stages");
            else if (!c.Stages.Any(s => s.Harvestable)) e.Add($"crop '{c.Id}': no harvestable stage");
            for (var i = 0; i < c.Stages.Length - 1 && i < c.HarvestableStage; i++)
                if (c.Stages[i].Gdd <= 0) e.Add($"crop '{c.Id}': stage '{c.Stages[i].Name}' needs gdd > 0");
            if (c.Stages.Any(s => s.RequiresVernalization) != c.VernalizationDays > 0)
                e.Add($"crop '{c.Id}': vernalizationDays and a stage with requiresVernalization go together");
            if (c.VernalizationDays > 250) e.Add($"crop '{c.Id}': vernalizationDays must be <= 250");
            for (var i = 0; i < c.Stages.Length; i++)
                if (c.CardOf(i) is < 0 or > 6) e.Add($"crop '{c.Id}': stage '{c.Stages[i].Name}' card must be 0..6 (7 is the dead card)");
            if (c.OptimalMoistureMin >= c.OptimalMoistureMax) e.Add($"crop '{c.Id}': optimal moisture range is empty");
            if (c.SowingMonths.Any(m => m is < 1 or > 12)) e.Add($"crop '{c.Id}': sowing months must be 1..12");
            if (!fieldGrounds.Contains(c.Ground)) e.Add($"crop '{c.Id}': ground must be a field's ({string.Join(", ", fieldGrounds)})");
            if (c.RegrowStage is { } regrow && (regrow < 0 || regrow >= c.HarvestableStage))
                e.Add($"crop '{c.Id}': regrowStage must be a stage before the harvestable one");
            if (c.WeedYieldLoss is < 0f or > 1f) e.Add($"crop '{c.Id}': weedYieldLoss must be 0..1");
        }

        foreach (var cl in Climates.Values)
        {
            foreach (var (name, arr) in new[]
                     {
                         ("monthlyMeanTemp", cl.MonthlyMeanTemp), ("monthlyDailyRange", cl.MonthlyDailyRange),
                         ("monthlyPrecipMm", cl.MonthlyPrecipMm), ("monthlyPrecipChance", cl.MonthlyPrecipChance),
                         ("monthlyFogChance", cl.MonthlyFogChance), ("sunriseHour", cl.SunriseHour),
                         ("sunsetHour", cl.SunsetHour),
                     })
                if (arr.Length != 12) e.Add($"climate '{cl.Id}': {name} needs 12 values");
        }

        foreach (var m in Machines.Values)
        {
            foreach (var error in ConfigurationErrors(m)) e.Add($"machine '{m.Id}' {error}");
            // As it comes, then with each option in turn, naming only what that option gets wrong.
            var own = MachineErrors(m).ToList();
            e.AddRange(own.Select(error => $"machine '{m.Id}'{error}"));
            foreach (var c in m.Configurations)
            foreach (var option in c.Options.Where(o => o != c.Default && o.Id != ""))
            {
                var with = $"machine '{m.Id}' ({c.Id}: {option.Id})";
                try
                {
                    var variant = m.Configure(new Dictionary<string, string> { [c.Id] = option.Id });
                    e.AddRange(MachineErrors(variant).Except(own).Select(error => with + error));
                }
                catch (JsonException x)
                {
                    e.Add($"{with}: {x.Message}");
                }
            }
        }

        foreach (var p in Pois.Values)
        {
            e.AddRange(EntityErrors(p).Select(error => $"poi '{p.Id}'{error}"));
            if (p.W <= 0 || p.D <= 0) e.Add($"poi '{p.Id}': w and d must be > 0");
            if (p.Colliders.Any(q => q.W <= 0 || q.D <= 0)) e.Add($"poi '{p.Id}': colliders need w and d > 0");
        }

        foreach (var t in ContractTypes.Values)
        {
            var what = $"contract type '{t.Id}'";
            if (string.IsNullOrWhiteSpace(t.Name)) e.Add($"{what}: needs a name");
            if (t.Months.Any(m => m is < 1 or > 12)) e.Add($"{what}: months must be 1..12");
            if (t.Days is not [>= 1, var dMax] || dMax < t.Days[0]) e.Add($"{what}: days needs [min, max] >= 1");
            if (t.Weight <= 0 || t.RewardPerHa < 0) e.Add($"{what}: weight must be > 0 and rewardPerHa >= 0");
            foreach (var (name, state) in new[] { ("offer", t.Offer), ("done", t.Done) })
            {
                foreach (var g in state.Ground.Where(g => !fieldGrounds.Contains(g)))
                    e.Add($"{what}: {name} ground '{g}' is not a field's ({string.Join(", ", fieldGrounds)})");
                foreach (var c in state.Crop.Where(c => !FieldStateDef.CropStates.Contains(c)))
                    e.Add($"{what}: {name} crop state '{c}' is unknown ({string.Join(", ", FieldStateDef.CropStates)})");
                foreach (var w in state.Weeds.Where(w => !World.WeedState.Names.Contains(w)))
                    e.Add($"{what}: {name} weed state '{w}' is unknown ({string.Join(", ", World.WeedState.Names)})");
            }
            if (t.Work == "" && t.Leases.Length > 0) e.Add($"{what}: leases are for field jobs");
            for (var k = 0; k < t.Leases.Length && t.Work != ""; k++)
            {
                var lease = t.Leases[k];
                var set = $"{what} lease {k + 1}";
                if (lease.FeePerHa < 0) e.Add($"{set}: feePerHa must be >= 0");
                foreach (var id in lease.Machines.Where(id => !Machines.ContainsKey(id))) e.Add($"{set}: unknown machine '{id}'");
                var machines = lease.Machines.Where(Machines.ContainsKey).Select(id => Machines[id]).ToList();
                if (!machines.Any(m => m.Get<WorkAreasDef>()?.Areas.Any(a => a.Type == t.Work) == true)) e.Add($"{set}: no machine does the job's work ({t.Work})");
                if (!machines.Any(m => m.Get<DrivableDef>() != null)) e.Add($"{set}: needs a vehicle");
            }
            var d = t.Deliver;
            if (t.Work == "")
            {
                if (d is not { Amount: [> 0f, var aMax] } || aMax < d.Amount[0]) e.Add($"{what}: a delivery job (no work) needs deliver.amount [min, max] > 0");
                if (!t.Offer.IsEmpty || !t.Done.IsEmpty) e.Add($"{what}: offer and done are for field jobs");
                if (d?.Share > 0) e.Add($"{what}: deliver.share is for harvest jobs");
            }
            else
            {
                var work = WorkTypes.Find(t.Work);
                if (work == null) e.Add($"{what}: unknown work '{t.Work}' ({WorkTypes.Known})");
                if (t.Offer.IsEmpty || t.Done.IsEmpty) e.Add($"{what}: a field job needs offer and done states");
                if (d?.Amount.Length > 0) e.Add($"{what}: deliver.amount is for delivery jobs (no work)");
                if (d != null && (d.Share is <= 0 or > 1 || work is not { Harvests: true })) e.Add($"{what}: deliver.share must be in (0, 1], on jobs that harvest");
            }
            if (d == null) continue;
            if (d.PriceFactor <= 0) e.Add($"{what}: deliver.priceFactor must be > 0");
            foreach (var ft in d.FillTypes.Where(f => !FillTypes.ContainsKey(f))) e.Add($"{what}: unknown fill type '{ft}'");
            e.AddRange(CategoryErrors(d.FillTypeCategories).Select(error => $"{what}: deliver.fillTypeCategories: {error}"));
        }

        foreach (var map in Maps.Values)
        {
            if (map.Size % 32 != 0) e.Add($"map '{map.Id}': size must be a multiple of 32");
            if (map.TileSize <= 0 || map.Size % map.TileSize != 0) e.Add($"map '{map.Id}': tileSize must divide the map size");
            foreach (var net in map.Networks)
            {
                if (net.Ground is not ("road" or "dirt" or "water" or "yard"))
                    e.Add($"map '{map.Id}' network '{net.Id}': ground must be road, dirt, water or yard");
                if (net.Width <= 0 || net.Width >= map.TileSize)
                    e.Add($"map '{map.Id}' network '{net.Id}': width must be between 0 and tileSize");
                if (string.IsNullOrWhiteSpace(net.Style)) e.Add($"map '{map.Id}' network '{net.Id}': style is required");
            }
            if (map.FarmlandPricePerHa < 0) e.Add($"map '{map.Id}': farmlandPricePerHa must be >= 0");
            var farmlands = new List<World.Polygon>();
            foreach (var f in map.Farmlands)
            {
                if (f.PriceFactor <= 0) e.Add($"map '{map.Id}' farmland {f.Id}: priceFactor must be > 0");
                if (f.Id is < 1 or > ushort.MaxValue) e.Add($"map '{map.Id}': farmland ids must be 1..{ushort.MaxValue}");
                if (!Npcs.ContainsKey(f.Npc)) e.Add($"map '{map.Id}' farmland {f.Id}: unknown npc '{f.Npc}'");
                if (!ValidFarm(f.Farm)) e.Add($"map '{map.Id}' farmland {f.Id}: {FarmRule}");
                if (f.ShapeError() is { } err) e.Add($"map '{map.Id}' farmland {f.Id}: {err}");
                else farmlands.Add(f.Shape());
            }
            foreach (var id in map.Farmlands.GroupBy(f => f.Id).Where(g => g.Count() > 1).Select(g => g.Key))
                e.Add($"map '{map.Id}': farmland {id} is defined more than once");
            foreach (var f in map.Fields)
            {
                if (f.Crop != null && CropById(f.Crop) == null) e.Add($"map '{map.Id}' field {f.Id}: unknown crop '{f.Crop}'");
                if (f.Id is < 1 or > ushort.MaxValue) e.Add($"map '{map.Id}': field ids must be 1..{ushort.MaxValue}");
                if (f.ShapeError() is { } err) e.Add($"map '{map.Id}' field {f.Id}: {err}");
                else if (!farmlands.Any(l => l.Contains(f.Shape().Centroid))) e.Add($"map '{map.Id}' field {f.Id}: not inside a farmland");
            }
            foreach (var id in map.Fields.GroupBy(f => f.Id).Where(g => g.Count() > 1).Select(g => g.Key))
                e.Add($"map '{map.Id}': field {id} is defined more than once");
            foreach (var p in map.Pois)
            {
                if (string.IsNullOrWhiteSpace(p.Id)) e.Add($"map '{map.Id}': a poi has no id");
                if (!Pois.ContainsKey(p.Type)) e.Add($"map '{map.Id}' poi '{p.Id}': unknown type '{p.Type}'");
                if (!ValidFarm(p.Farm)) e.Add($"map '{map.Id}' poi '{p.Id}': {FarmRule}");
                if (p.X < 0 || p.Z < 0 || p.X > map.Size || p.Z > map.Size) e.Add($"map '{map.Id}' poi '{p.Id}': outside the map");
            }
            foreach (var id in map.Pois.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key))
                e.Add($"map '{map.Id}': poi '{id}' is defined more than once");
            for (var i = 0; i < map.Machines.Length; i++)
            {
                var sp = map.Machines[i];
                if (!Machines.TryGetValue(sp.Def, out var machine)) e.Add($"map '{map.Id}': unknown machine '{sp.Def}'");
                else
                    foreach (var (c, o) in sp.Configuration ?? [])
                        if (machine.Configurations.FirstOrDefault(x => x.Id == c)?.Option(o) == null)
                            e.Add($"map '{map.Id}' machine {i}: {sp.Def} has no option '{o}' of '{c}'");
                if (!ValidFarm(sp.Farm)) e.Add($"map '{map.Id}' machine {i}: {FarmRule}");
                if (sp.AttachToIndex is { } q && q >= 0 && q < i && map.Machines[q].Farm != sp.Farm)
                    e.Add($"map '{map.Id}': machine {i} must belong to the same farm as the machine it attaches to");
                if (sp.AttachToIndex is { } p && (p < 0 || p >= i)) e.Add($"map '{map.Id}': machine {i} attachToIndex must refer to an earlier machine");
            }
        }

        return e;
    }

    /// <summary>What's wrong with a machine (with its options), each starting as it follows the machine's name: " motor: …", ": …".</summary>
    private IEnumerable<string> MachineErrors(MachineDef m)
    {
        if (m.Price < 0f || m.Mass <= 0f) yield return ": price must be >= 0 and mass > 0";
        foreach (var error in EntityErrors(m)) yield return error;
        foreach (var id in m.Joints.GroupBy(j => j.Id).Where(g => g.Count() > 1).Select(g => g.Key))
            yield return $": joint '{id}' is defined more than once";
    }

    /// <summary>
    /// What's wrong with an entity type's components (each checked against it) and the roles its visual.nodes names,
    /// each starting as it follows the type's name: " motor: …", ": …".
    /// </summary>
    private IEnumerable<string> EntityErrors(EntityDef d)
    {
        foreach (var kind in d.Components.GroupBy(c => c.GetType()).Where(g => g.Count() > 1))
            yield return $": more than one {kind.First().Kind}";
        foreach (var c in d.Components)
        foreach (var error in c.Errors(d, this))
            yield return $" {c.Kind}: {error}";
        var roles = d.Roles.ToHashSet();
        foreach (var role in (d.Visual.Nodes?.Keys ?? Enumerable.Empty<string>()).Where(r => !roles.Contains(r)))
            yield return $": visual.nodes role '{role}' is not one of its components' ({string.Join(", ", roles)})";
    }

    /// <summary>What's wrong with a machine's configurations, each starting as it follows the machine's name.</summary>
    private static IEnumerable<string> ConfigurationErrors(MachineDef m)
    {
        // What identifies the machine, and what options add to rather than set.
        string[] fixedMembers = ["id", "configurations", "price", "mass"];
        foreach (var id in m.Configurations.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key))
            yield return $"configuration '{id}' is defined more than once";
        foreach (var c in m.Configurations)
        {
            var what = $"configuration '{c.Id}'";
            if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name)) yield return $"{what}: needs an id and a name";
            else if (!IsNodeName(c.Id)) yield return $"{what}: its id must be letters and digits, as model nodes are named after it";
            if (c.Options.Length == 0) yield return $"{what}: needs options";
            if (c.Options.Count(o => o.Default) > 1) yield return $"{what}: only one option can be the default";
            foreach (var id in c.Options.GroupBy(o => o.Id).Where(g => g.Count() > 1).Select(g => g.Key))
                yield return $"{what}: option '{id}' is defined more than once";
            foreach (var o in c.Options)
            {
                if (string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.Name)) yield return $"{what}: options need an id and a name";
                else if (!IsNodeName(o.Id)) yield return $"{what} option '{o.Id}': its id must be letters and digits, as model nodes are named after it";
                if (o.Show.Any(string.IsNullOrWhiteSpace)) yield return $"{what} option '{o.Id}': show needs node names";
                if (o.Changes?.Select(kv => kv.Key).FirstOrDefault(k => fixedMembers.Contains(k, StringComparer.OrdinalIgnoreCase)) is { } key)
                    yield return $"{what} option '{o.Id}': changes can't set '{key}' (an option's price and mass add to the machine's)";
            }
        }
    }

    /// <summary>Letters and digits only: an option's model nodes are configuration_option(_…), see docs/MODELING.md.</summary>
    private static bool IsNodeName(string id) => id.All(char.IsAsciiLetterOrDigit);

    private const string FarmRule = "farm must be 0 (an NPC's) or 1 (the player's farm)";

    private static bool ValidFarm(int farm) => farm is Ownership.Farm.None or Ownership.Farm.PlayerId;
}

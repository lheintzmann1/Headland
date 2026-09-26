using System.Text.Json;

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
        db.Soils.AddRange(ReadMany<SoilDef>(src, "soils.json"));
        foreach (var n in ReadMany<NpcDef>(src, "npcs.json")) db.AddUnique(db.Npcs, n.Id, n, "npc");
        foreach (var file in src.ListJson("crops")) db.Crops.AddRange(ReadMany<CropDef>(src, file));
        foreach (var file in src.ListJson("machines"))
        foreach (var m in ReadMany<MachineDef>(src, file))
            db.AddUnique(db.Machines, m.Id, m, "machine");
        foreach (var file in src.ListJson("pois"))
        foreach (var p in ReadMany<PoiDef>(src, file))
            db.AddUnique(db.Pois, p.Id, p, "poi");
        foreach (var c in ReadMany<ContractTypeDef>(src, "contracts.json")) db.AddUnique(db.ContractTypes, c.Id, c, "contract type");
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

    public List<string> Validate()
    {
        var e = new List<string>();
        string[] jointTypes = ["threePoint", "drawbar", "header"];
        string[] workTypes = ["cultivator", "seeder", "harvester"];
        string[] fieldGrounds = ["grass", "cultivated", "seeded", "stubble", "plowed"];
        string[] triggerTypes = ["unload", "load", "fill", "wash", "repair", "delivery"];
        // The trigger types each POI action works through (process works on its own).
        var actionTriggers = new Dictionary<string, string[]>
        {
            ["sell"] = ["unload"], ["store"] = ["unload"], ["buy"] = ["fill"], ["refuel"] = ["fill"],
            ["repair"] = ["repair"], ["wash"] = ["wash"], ["lease"] = ["delivery"], ["process"] = [],
        };

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

        foreach (var f in FillTypeList)
            if (f.MonthlyPriceFactor is { Length: not 12 })
                e.Add($"fill type '{f.Id}': monthlyPriceFactor needs 12 values");

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
            var units = m.FillUnits.Select(u => u.Id).ToHashSet();
            foreach (var u in m.FillUnits)
            foreach (var ft in u.FillTypes)
                if (!FillTypes.ContainsKey(ft)) e.Add($"machine '{m.Id}': fill unit '{u.Id}' accepts unknown fill type '{ft}'");
            foreach (var j in m.AttacherJoints)
                if (!jointTypes.Contains(j.Type)) e.Add($"machine '{m.Id}': joint '{j.Id}' has unknown type '{j.Type}'");
            if (m.Attacher != null)
            {
                if (!jointTypes.Contains(m.Attacher.Type)) e.Add($"machine '{m.Id}': attacher has unknown type '{m.Attacher.Type}'");
                if (m.Attacher.Mode is not ("mounted" or "trailed")) e.Add($"machine '{m.Id}': attacher mode must be mounted or trailed");
                if (m.Attacher.Mode == "trailed" && m.Attacher.Z <= 0.1f) e.Add($"machine '{m.Id}': trailed attacher needs z > 0 (drawbar length)");
            }
            if (m.WorkArea != null && !workTypes.Contains(m.WorkArea.Type))
                e.Add($"machine '{m.Id}': unknown work area type '{m.WorkArea.Type}'");
            if (m.Pipe != null && !units.Contains(m.Pipe.FillUnit)) e.Add($"machine '{m.Id}': pipe fill unit '{m.Pipe.FillUnit}' missing");
            if (m.Tipper != null && !units.Contains(m.Tipper.FillUnit)) e.Add($"machine '{m.Id}': tipper fill unit '{m.Tipper.FillUnit}' missing");
            if (m.HarvestTank != null && !units.Contains(m.HarvestTank)) e.Add($"machine '{m.Id}': harvest tank '{m.HarvestTank}' missing");
            if (m.SeedTank != null && !units.Contains(m.SeedTank)) e.Add($"machine '{m.Id}': seed tank '{m.SeedTank}' missing");
            if (m.WorkArea?.Type == "seeder" && m.SeedTank == null) e.Add($"machine '{m.Id}': seeder needs seedTank");
            if (m.Motorized is { Wheelbase: <= 0 }) e.Add($"machine '{m.Id}': wheelbase must be > 0");
            if (m.Motorized?.FuelTank is { } fuel && !units.Contains(fuel)) e.Add($"machine '{m.Id}': fuel tank '{fuel}' missing");
        }

        foreach (var p in Pois.Values)
        {
            if (p.W <= 0 || p.D <= 0) e.Add($"poi '{p.Id}': w and d must be > 0");
            if (p.Parts.Any(q => q.W <= 0 || q.D <= 0 || q.H <= 0)) e.Add($"poi '{p.Id}': parts need w, d and h > 0");
            var triggers = new Dictionary<string, PoiTriggerDef>();
            foreach (var t in p.Triggers)
            {
                if (!triggers.TryAdd(t.Id, t)) e.Add($"poi '{p.Id}': trigger '{t.Id}' is defined more than once");
                if (!triggerTypes.Contains(t.Type)) e.Add($"poi '{p.Id}' trigger '{t.Id}': unknown type '{t.Type}'");
                if (t.W <= 0 || t.D <= 0) e.Add($"poi '{p.Id}' trigger '{t.Id}': w and d must be > 0");
                if (t.Type == "load" && p.Storage == null) e.Add($"poi '{p.Id}' trigger '{t.Id}': load triggers need a storage");
                if (t.Rate <= 0) e.Add($"poi '{p.Id}' trigger '{t.Id}': rate must be > 0");
            }
            var stored = p.Storage?.FillTypes ?? [];
            foreach (var ft in stored)
                if (!FillTypes.ContainsKey(ft)) e.Add($"poi '{p.Id}' storage: unknown fill type '{ft}'");
            if (p.Storage != null && (p.Storage.Capacity <= 0 || p.Storage.Capacities.Values.Any(c => c <= 0)))
                e.Add($"poi '{p.Id}' storage: capacities must be > 0");
            foreach (var a in p.Actions)
            {
                var what = $"poi '{p.Id}' {a.Type} action";
                if (!actionTriggers.TryGetValue(a.Type, out var allowed))
                {
                    e.Add($"poi '{p.Id}': unknown action type '{a.Type}'");
                    continue;
                }
                if (allowed.Length == 0)
                {
                    if (a.Trigger != "") e.Add($"{what}: works without a trigger");
                }
                else if (!triggers.TryGetValue(a.Trigger, out var trigger)) e.Add($"{what}: trigger '{a.Trigger}' not found");
                else if (!allowed.Contains(trigger.Type)) e.Add($"{what}: works at {string.Join(" or ", allowed)} triggers, not {trigger.Type}");
                if (a.Type is "sell" or "buy" or "refuel" && a.FillTypes.Length == 0) e.Add($"{what}: needs fillTypes");
                foreach (var ft in a.FillTypes)
                    if (!FillTypes.ContainsKey(ft)) e.Add($"{what}: unknown fill type '{ft}'");
                if (a.Type is "store" or "process" && p.Storage == null) e.Add($"{what}: the poi has no storage");
                if (a.OpenHours is { } hours && (hours.Length != 2 || hours.Any(h => h is < 0 or > 24) || hours[0] == hours[1]))
                    e.Add($"{what}: openHours needs [from, to] hours, 0..24 and different");
                if (a.Months.Any(m => m is < 1 or > 12)) e.Add($"{what}: months must be 1..12");
                if (a.MinAmount < 0) e.Add($"{what}: minAmount must be >= 0");
                if (a.PriceFactor <= 0 || a.PriceFactors.Values.Any(f => f <= 0)) e.Add($"{what}: price factors must be > 0");
                foreach (var ft in a.PriceFactors.Keys.Where(f => !a.FillTypes.Contains(f) && a.Outputs.All(o => o.FillType != f)))
                    e.Add($"{what}: price factor for '{ft}', which it does not trade");
                var d = a.Demand;
                if (d.Drop < 0 || d.Floor is <= 0 or > 1 || d.Recovery < 0 || d.HighChance is < 0 or > 1)
                    e.Add($"{what}: demand needs drop >= 0, floor in (0, 1], recovery >= 0 and highChance in [0, 1]");
                if (d.HighFactor is not [>= 1f, var fMax] || fMax < d.HighFactor[0] || d.HighDays is not [>= 1, var dMax] || dMax < d.HighDays[0])
                    e.Add($"{what}: demand needs highFactor [min, max] >= 1 and highDays [min, max] >= 1");
                if (a.Type == "store")
                    foreach (var ft in a.FillTypes.Where(f => !stored.Contains(f)))
                        e.Add($"{what}: the storage does not keep '{ft}'");
                if (a.Type != "process") continue;
                if (a.Inputs.Length == 0 || a.Outputs.Length == 0) e.Add($"{what}: needs inputs and outputs");
                if (a.CycleHours <= 0 || a.RunningCost < 0) e.Add($"{what}: cycleHours must be > 0 and runningCost >= 0");
                foreach (var io in a.Inputs.Concat(a.Outputs))
                {
                    if (io.Amount <= 0) e.Add($"{what}: amounts must be > 0");
                    if (!stored.Contains(io.FillType)) e.Add($"{what}: the storage does not keep '{io.FillType}'");
                }
                foreach (var o in a.Outputs.Where(o => o.Mode is not ("store" or "sell")))
                    e.Add($"{what}: output '{o.FillType}' mode must be store or sell");
            }
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
            }
            if (t.Work == "" && t.Leases.Length > 0) e.Add($"{what}: leases are for field jobs");
            for (var k = 0; k < t.Leases.Length && t.Work != ""; k++)
            {
                var lease = t.Leases[k];
                var set = $"{what} lease {k + 1}";
                if (lease.FeePerHa < 0) e.Add($"{set}: feePerHa must be >= 0");
                foreach (var id in lease.Machines.Where(id => !Machines.ContainsKey(id))) e.Add($"{set}: unknown machine '{id}'");
                var machines = lease.Machines.Where(Machines.ContainsKey).Select(id => Machines[id]).ToList();
                if (!machines.Any(m => m.WorkArea?.Type == t.Work)) e.Add($"{set}: no machine does the job's work ({t.Work})");
                if (!machines.Any(m => m.Motorized != null)) e.Add($"{set}: needs a vehicle");
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
                if (!workTypes.Contains(t.Work)) e.Add($"{what}: unknown work '{t.Work}'");
                if (t.Offer.IsEmpty || t.Done.IsEmpty) e.Add($"{what}: a field job needs offer and done states");
                if (d?.Amount.Length > 0) e.Add($"{what}: deliver.amount is for delivery jobs (no work)");
                if (d != null && (d.Share is <= 0 or > 1 || t.Work != "harvester")) e.Add($"{what}: deliver.share must be in (0, 1], on harvester jobs");
            }
            if (d == null) continue;
            if (d.PriceFactor <= 0) e.Add($"{what}: deliver.priceFactor must be > 0");
            foreach (var ft in d.FillTypes.Where(f => !FillTypes.ContainsKey(f))) e.Add($"{what}: unknown fill type '{ft}'");
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
                if (!Machines.ContainsKey(sp.Def)) e.Add($"map '{map.Id}': unknown machine '{sp.Def}'");
                if (!ValidFarm(sp.Farm)) e.Add($"map '{map.Id}' machine {i}: {FarmRule}");
                if (sp.AttachToIndex is { } q && q >= 0 && q < i && map.Machines[q].Farm != sp.Farm)
                    e.Add($"map '{map.Id}': machine {i} must belong to the same farm as the machine it attaches to");
                if (sp.AttachToIndex is { } p && (p < 0 || p >= i)) e.Add($"map '{map.Id}': machine {i} attachToIndex must refer to an earlier machine");
            }
        }

        return e;
    }

    private const string FarmRule = "farm must be 0 (an NPC's) or 1 (the player's farm)";

    private static bool ValidFarm(int farm) => farm is Ownership.Farm.None or Ownership.Farm.PlayerId;
}

using System.Text.Json;

namespace FarmSim.Core.Content;

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
    public List<FillTypeDef> FillTypeList { get; } = [];
    public Dictionary<string, FillTypeDef> FillTypes { get; } = new();
    public List<SoilDef> Soils { get; } = [];
    /// <summary>Crop index + 1 is stored in cells (0 = no crop).</summary>
    public List<CropDef> Crops { get; } = [];
    public Dictionary<string, MachineDef> Machines { get; } = new();
    public Dictionary<string, ClimateDef> Climates { get; } = new();
    public Dictionary<string, MapDef> Maps { get; } = new();

    public CropDef? CropById(string id) => Crops.Find(c => c.Id == id);
    public int CropIndex(string id) => Crops.FindIndex(c => c.Id == id);
    public int SoilIndex(string id) => Soils.FindIndex(s => s.Id == id);

    public ClimateDef Climate => Climates[Game.Climate];
    public MapDef Map => Maps[Game.Map];

    public static ContentDatabase Load(IContentSource src)
    {
        var db = new ContentDatabase();
        db.Game = Parse<GameConfig>(src, "game.json");

        foreach (var f in ReadMany<FillTypeDef>(src, "filltypes.json")) db.AddUnique(db.FillTypes, f.Id, f, "fill type");
        db.FillTypeList.AddRange(db.FillTypes.Values);
        db.Soils.AddRange(ReadMany<SoilDef>(src, "soils.json"));
        foreach (var file in src.ListJson("crops")) db.Crops.AddRange(ReadMany<CropDef>(src, file));
        foreach (var file in src.ListJson("machines"))
        foreach (var m in ReadMany<MachineDef>(src, file))
            db.AddUnique(db.Machines, m.Id, m, "machine");
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

        if (Game.DaysPerMonth < 1) e.Add("game.daysPerMonth must be >= 1");
        if (!Climates.ContainsKey(Game.Climate)) e.Add($"game.climate '{Game.Climate}' not found");
        if (!Maps.ContainsKey(Game.Map)) e.Add($"game.map '{Game.Map}' not found");
        if (Soils.Count == 0) e.Add("no soils defined");
        if (Soils.Count > 16) e.Add("at most 16 soils are supported");
        if (Crops.Count is 0 or > 254) e.Add("need between 1 and 254 crops");

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
            foreach (var f in map.Fields)
            {
                if (f.Crop != null && CropById(f.Crop) == null) e.Add($"map '{map.Id}' field {f.Id}: unknown crop '{f.Crop}'");
                if (f.Id is < 1 or > 255) e.Add($"map '{map.Id}': field ids must be 1..255");
            }
            foreach (var s in map.SellPoints)
            foreach (var ft in s.Accepts)
                if (!FillTypes.ContainsKey(ft)) e.Add($"map '{map.Id}' sell point '{s.Id}': unknown fill type '{ft}'");
            foreach (var s in map.Shops)
            foreach (var ft in s.Sells)
                if (!FillTypes.ContainsKey(ft)) e.Add($"map '{map.Id}' shop '{s.Id}': unknown fill type '{ft}'");
            for (var i = 0; i < map.Machines.Length; i++)
            {
                var sp = map.Machines[i];
                if (!Machines.ContainsKey(sp.Def)) e.Add($"map '{map.Id}': unknown machine '{sp.Def}'");
                if (sp.AttachToIndex is { } p && (p < 0 || p >= i)) e.Add($"map '{map.Id}': machine {i} attachToIndex must refer to an earlier machine");
            }
        }

        return e;
    }
}

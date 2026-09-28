using System.Numerics;
using Headland.Core;
using Headland.Core.Content;
using Headland.Core.World;

namespace Headland.Core.Tests;

internal static class TestContent
{
    private static readonly Lazy<ContentDatabase> Db = new(() => ContentDatabase.Load(new FileSystemContentSource(DataDir)));

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Headland.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
        }
    }

    public static string DataDir => Path.Combine(RepoRoot, "game", "data");

    public static ContentDatabase Content => Db.Value;

    public static Simulation NewSim() => Simulation.Create(Content);

    /// <summary>The game's content with extra machines (a JSON array), written as a mod would.</summary>
    public static ContentDatabase WithMachines(string json) => With("machines", json);

    /// <summary>The game's content with extra POI types (a JSON array), written as a mod would.</summary>
    public static ContentDatabase WithPois(string json) => With("pois", json);

    /// <summary>The game's content with an extra file in <paramref name="dir"/> (machines, pois…), as a mod would add.</summary>
    public static ContentDatabase With(string dir, string json) => ContentDatabase.Load(new ExtraFile($"{dir}/test.json", json));

    /// <summary>The game's content with some of its files replaced or added (path → text), as a mod would.</summary>
    public static ContentDatabase Modded(Dictionary<string, string> files) => ContentDatabase.Load(new ModdedFiles(files));

    /// <summary>A data file's JSON array (<paramref name="file"/>, such as filltypes.json) with one more entry.</summary>
    public static string WithEntry(string file, string entry)
    {
        var text = File.ReadAllText(Path.Combine(DataDir, file)).TrimEnd();
        return text[..^1].TrimEnd() + ",\n" + entry + "\n]";
    }

    private sealed class ModdedFiles(Dictionary<string, string> files) : IContentSource
    {
        private readonly FileSystemContentSource _game = new(DataDir);

        public IReadOnlyList<string> ListJson(string dir) =>
            [.. _game.ListJson(dir), .. files.Keys.Where(f => f.StartsWith(dir + "/", StringComparison.Ordinal) && !_game.Exists(f))];

        public string ReadText(string path) => files.TryGetValue(path, out var text) ? text : _game.ReadText(path);
        public bool Exists(string path) => files.ContainsKey(path) || _game.Exists(path);
    }

    private sealed class ExtraFile(string path, string json) : IContentSource
    {
        private readonly FileSystemContentSource _game = new(DataDir);

        public IReadOnlyList<string> ListJson(string dir) => path.StartsWith(dir + "/", StringComparison.Ordinal) ? [.. _game.ListJson(dir), path] : _game.ListJson(dir);
        public string ReadText(string p) => p == path ? json : _game.ReadText(p);
        public bool Exists(string p) => p == path || _game.Exists(p);
    }

    /// <summary>
    /// A fresh 64 m world with one 48 x 48 m loam field (cultivated), for fast multi-year runs.
    /// Uses its own content instance so tests can run in parallel.
    /// </summary>
    public static Simulation SmallSim(ulong weatherSeed = 42)
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(DataDir));
        db.Maps["test"] = new MapDef
        {
            Id = "test", Name = "Test", Size = 64, Seed = 3, HillAmplitude = 0f, ScatteredTreesPerHa = 0f,
            Farmlands = [new FarmlandDef { Id = 1, Npc = "hendricks", Farm = 1, X = 4, Z = 4, W = 56, H = 56 }],
            Fields = [new FieldDef { Id = 1, X = 8, Z = 8, W = 48, H = 48, Ground = "cultivated" }],
            PlayerX = 2, PlayerZ = 2,
        };
        db.Game.Map = "test";
        db.Game.WeatherSeed = weatherSeed;
        var sim = Simulation.Create(db);
        var loam = (byte)db.SoilIndex("loam");
        var L = sim.World.Layers;
        for (var i = 0; i < L.Soil.Length; i++)
        {
            L.Soil[i] = loam;
            L.Moisture[i] = WorldGen.ToByte(0.6f);
        }
        return sim;
    }

    /// <summary>Hands farmland 5 (field 4, grass) to the farm: work only applies on the farm's own land.</summary>
    public static void OwnField4(Simulation sim) => sim.Farms.SetOwner(sim.World.FarmlandById(5)!, Ownership.Farm.PlayerId);

    /// <summary>Skips forward to the next occurrence of a date and hour.</summary>
    public static void SkipTo(Simulation sim, int month, int day, float hour)
    {
        var now = sim.Clock.Date;
        var year = month > now.Month || (month == now.Month && day >= now.Day) ? now.Year : now.Year + 1;
        var target = sim.Calendar.DayIndexOf(new Headland.Core.Time.GameDate(year, month, day)) * 24.0 + hour;
        var hours = (int)Math.Round(target - sim.Clock.TotalSeconds / 3600.0);
        if (hours > 0) sim.SkipHours(hours);
    }

    /// <summary>Resets a square of cells to a known state for isolated crop tests.</summary>
    public static void PrepareCells(WorldMap world, Vector2 center, int halfCells, Action<int> set)
    {
        var (cx, cz) = world.WorldToCell(center);
        for (var z = cz - halfCells; z < cz + halfCells; z++)
        for (var x = cx - halfCells; x < cx + halfCells; x++)
            set(world.CellIndex(x, z));
    }
}

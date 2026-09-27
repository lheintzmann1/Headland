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
    public static ContentDatabase WithMachines(string json) => ContentDatabase.Load(new ExtraMachines(json));

    private sealed class ExtraMachines(string json) : IContentSource
    {
        private readonly FileSystemContentSource _game = new(DataDir);

        public IReadOnlyList<string> ListJson(string dir) => dir == "machines" ? [.. _game.ListJson(dir), "machines/test.json"] : _game.ListJson(dir);
        public string ReadText(string path) => path == "machines/test.json" ? json : _game.ReadText(path);
        public bool Exists(string path) => path == "machines/test.json" || _game.Exists(path);
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

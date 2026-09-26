namespace Headland.Core.Content;

// Content definitions ("raws"), deserialized from game/data/**/*.json (camelCase, comments allowed).
// Local machine coordinates use model space: +z forward, +x left, origin at the non-steered axle.

public sealed class GameConfig
{
    public int DaysPerMonth { get; set; } = 3;
    public int StartYear { get; set; } = 1;
    public int StartMonth { get; set; } = 8;
    public int StartDay { get; set; } = 1;
    public float StartHour { get; set; } = 7f;
    public float StartMoney { get; set; } = 100_000f;
    public string FarmName { get; set; } = "My Farm";
    public string Map { get; set; } = "default";
    public string Climate { get; set; } = "temperate";
    public ulong WeatherSeed { get; set; } = 42;
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

/// <summary>A neighbor who owns the land no farm owns: sells it, and later offers contracts on it.</summary>
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

public sealed class MotorizedDef
{
    public float PowerHp { get; set; } = 100f;
    public float MaxSpeedKmh { get; set; } = 40f;
    public float MaxReverseKmh { get; set; } = 15f;
    public float Acceleration { get; set; } = 2.5f;
    public float Braking { get; set; } = 6f;
    public float Wheelbase { get; set; } = 2.6f;
    public float MaxSteerDeg { get; set; } = 38f;
    public float SteerRateDeg { get; set; } = 90f;
    /// <summary>"front" (tractor) or "rear" (combine).</summary>
    public string SteerAxle { get; set; } = "front";
}

public sealed class WheelDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 0.5f;
    public float Width { get; set; } = 0.4f;
    public bool Steer { get; set; }
}

public sealed class AttacherJointDef
{
    public string Id { get; set; } = "";
    /// <summary>"threePoint", "drawbar" or "header".</summary>
    public string Type { get; set; } = "threePoint";
    public float X { get; set; }
    public float Z { get; set; }
    public float Y { get; set; } = 0.6f;
}

public sealed class AttacherDef
{
    public string Type { get; set; } = "threePoint";
    /// <summary>"mounted" (rigid, liftable) or "trailed" (follows the hitch point).</summary>
    public string Mode { get; set; } = "mounted";
    public float X { get; set; }
    public float Z { get; set; }
    public float MaxArticulationDeg { get; set; } = 80f;
}

public sealed class WorkAreaDef
{
    /// <summary>"cultivator", "seeder" or "harvester".</summary>
    public string Type { get; set; } = "cultivator";
    public float Width { get; set; } = 3f;
    public float Length { get; set; } = 1f;
    public float X { get; set; }
    public float Z { get; set; }
    public bool RequiresLowered { get; set; } = true;
    public bool RequiresOn { get; set; }
    public float MaxWorkSpeedKmh { get; set; } = 12f;
    public float RequiredPowerHp { get; set; } = 60f;
    /// <summary>Header only: crop harvest groups this header can cut.</summary>
    public string[] HarvestGroups { get; set; } = [];
}

public sealed class FillUnitDef
{
    public string Id { get; set; } = "main";
    public float Capacity { get; set; } = 1000f;
    public string[] FillTypes { get; set; } = [];
    public string? StartFillType { get; set; }
    public float StartLevel { get; set; }
}

public sealed class PipeDef
{
    public string FillUnit { get; set; } = "tank";
    public float X { get; set; } = 4f;
    public float Z { get; set; } = 1f;
    public float RatePerSecond { get; set; } = 150f;
}

public sealed class TipperDef
{
    public string FillUnit { get; set; } = "main";
    public float RatePerSecond { get; set; } = 400f;
}

public sealed class VisualDef
{
    /// <summary>Procedural placeholder archetype: tractor, combine, trailer, cultivator, seeder, header.</summary>
    public string Placeholder { get; set; } = "tractor";
    public string Color { get; set; } = "#7a3326";
    /// <summary>Optional glTF model (res:// path, e.g. a Blockbench .glb export). Replaces the placeholder.</summary>
    public string? Model { get; set; }
    /// <summary>Uniform scale applied to the model (1 = model units are meters).</summary>
    public float Scale { get; set; } = 1f;
    /// <summary>Turn the model around Y so its front faces +Z (180 for models built facing -Z).</summary>
    public float YawDeg { get; set; }
    /// <summary>Model offset [x, y, z] in meters, in machine local space (+Z forward, +X left).</summary>
    public float[] Offset { get; set; } = [0f, 0f, 0f];
    /// <summary>
    /// Moving parts: role → node name in the model. Roles: wheel0..wheelN (same order as "wheels"),
    /// pipe, tipper, reel, load. See docs/MODELING.md.
    /// </summary>
    public Dictionary<string, string>? Nodes { get; set; }
}

public sealed class MachineDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Brand { get; set; } = "";
    public float Price { get; set; }
    public float Mass { get; set; } = 3000f;
    public SizeDef Size { get; set; } = new();
    public MotorizedDef? Motorized { get; set; }
    public WheelDef[] Wheels { get; set; } = [];
    public AttacherJointDef[] AttacherJoints { get; set; } = [];
    public AttacherDef? Attacher { get; set; }
    public WorkAreaDef? WorkArea { get; set; }
    public FillUnitDef[] FillUnits { get; set; } = [];
    public PipeDef? Pipe { get; set; }
    public TipperDef? Tipper { get; set; }
    /// <summary>Combine: fill unit receiving harvested crop from an attached header.</summary>
    public string? HarvestTank { get; set; }
    /// <summary>Seeder: fill unit holding seed.</summary>
    public string? SeedTank { get; set; }
    public VisualDef Visual { get; set; } = new();
    public string Description { get; set; } = "";
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

public sealed class BuildingDef
{
    public string Type { get; set; } = "shed";
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float W { get; set; } = 10f;
    public float D { get; set; } = 10f;
    public float H { get; set; } = 6f;
    public float RotDeg { get; set; }
    public string Color { get; set; } = "#8c8378";
}

public sealed class SellPointDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public string[] Accepts { get; set; } = [];
}

public sealed class ShopDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public string[] Sells { get; set; } = [];
}

public sealed class MachineSpawnDef
{
    public string Def { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float HeadingDeg { get; set; }
    /// <summary>Owning farm: 1 = the player's (default), 0 = an NPC's (can't be driven or hitched).</summary>
    public int Farm { get; set; } = Ownership.Farm.PlayerId;
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
    /// <summary>Each field lies inside a farmland (the one holding its centroid).</summary>
    public FieldDef[] Fields { get; set; } = [];
    public BuildingDef[] Buildings { get; set; } = [];
    public SellPointDef[] SellPoints { get; set; } = [];
    public ShopDef[] Shops { get; set; } = [];
    public MachineSpawnDef[] Machines { get; set; } = [];
    public ForestDef[] Forests { get; set; } = [];
    /// <summary>Scattered trees per hectare on open grass.</summary>
    public float ScatteredTreesPerHa { get; set; } = 0.6f;
    public float PlayerX { get; set; }
    public float PlayerZ { get; set; }
}

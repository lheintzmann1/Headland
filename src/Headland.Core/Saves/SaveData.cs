using Headland.Core.Content;
using Headland.Core.Weather;

namespace Headland.Core.Saves;

// The save format. A save is a zip holding meta.json (what a slot list shows), state.json (every entity) and
// layers.bin (the field layers, see LayerCodec). Content is referenced by id, never by list index, so saves
// survive content changes and mods. Adding a property is backward compatible (missing ones load as defaults);
// anything else bumps SaveGame.Format and adds a migration.

public sealed class SaveException(string message) : Exception(message);

/// <summary>Summary of a save, readable without loading it.</summary>
public sealed class SaveMeta
{
    public int Format { get; set; }
    /// <summary>The game version that wrote it (config/version).</summary>
    public string GameVersion { get; set; } = "";
    public List<ModRef> Mods { get; set; } = [];
    public DateTime SavedAtUtc { get; set; }
    public string Map { get; set; } = "";
    public string MapName { get; set; } = "";
    public string FarmName { get; set; } = "";
    /// <summary>In-game date and time, for display.</summary>
    public string Date { get; set; } = "";
    public float Money { get; set; }
    public double PlayTimeSeconds { get; set; }
}

public sealed class SaveState
{
    public GameConfig Setup { get; set; } = new();
    public double RealTime { get; set; }
    public ClockSave Clock { get; set; } = new();
    public long PendingHourFrom { get; set; }
    public int PendingHours { get; set; }
    public WeatherSave Weather { get; set; } = new();
    /// <summary>Crop ids by the index cells store (index + 1), to remap layers if the crop list changed.</summary>
    public List<string> Crops { get; set; } = [];
    /// <summary>Nitrogen mineralization carried over between hours, per soil id.</summary>
    public Dictionary<string, float> Mineralization { get; set; } = new();
    public EconomySave Economy { get; set; } = new();
    public StatisticsSave Statistics { get; set; } = new();
    public List<FarmSave> Farms { get; set; } = [];
    public List<FarmlandSave> Farmlands { get; set; } = [];
    public int NextMachineId { get; set; }
    public List<MachineSave> Machines { get; set; } = [];
    public PlayerSave Player { get; set; } = new();
}

public sealed class ClockSave
{
    public double TotalSeconds { get; set; }
    public long LastHour { get; set; }
    public float TimeScale { get; set; }
    public bool Paused { get; set; }
}

public sealed class WeatherSave
{
    public ulong RngState { get; set; }
    public float Anomaly { get; set; }
    /// <summary>Every day generated so far: the forecast must not change on load.</summary>
    public List<DayWeather> Days { get; set; } = [];
    public float GroundWetness { get; set; }
    public float SnowCover { get; set; }
    public float SnowpackMm { get; set; }
}

public sealed class EconomySave
{
    public float Money { get; set; }
    public float TotalIncome { get; set; }
    public float TotalExpenses { get; set; }
}

public sealed class StatisticsSave
{
    public Dictionary<string, float> HectaresWorked { get; set; } = new();
    public Dictionary<string, float> Harvested { get; set; } = new();
    public Dictionary<string, float> Sold { get; set; } = new();
    public Dictionary<string, float> Bought { get; set; } = new();
    public int HelpersHired { get; set; }
    public int DaysPlayed { get; set; }
}

public sealed class FarmSave
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class FarmlandSave
{
    public int Id { get; set; }
    public int Farm { get; set; }
}

public sealed class MachineSave
{
    public int Id { get; set; }
    public string Def { get; set; } = "";
    public int Farm { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float Heading { get; set; }
    public float Speed { get; set; }
    public float SteerAngle { get; set; }
    public float Distance { get; set; }
    public int? Parent { get; set; }
    public string? Joint { get; set; }
    public bool Lowered { get; set; }
    public bool TurnedOn { get; set; }
    public bool PipeOut { get; set; }
    public bool Tipping { get; set; }
    public float LowerAnim { get; set; }
    public float PipeAnim { get; set; }
    public float TipAnim { get; set; }
    /// <summary>Seeder: the selected crop id.</summary>
    public string? SeedCrop { get; set; }
    public float WorkedHa { get; set; }
    public List<FillUnitSave> FillUnits { get; set; } = [];
    /// <summary>Work area pose of the last tick [x, z, heading], so the next tick sweeps without a gap.</summary>
    public float[]? WorkPose { get; set; }
    /// <summary>A load being unloaded at a POI, totalled so far.</summary>
    public DeliverySave? Delivery { get; set; }
    public HelperSave? Helper { get; set; }
}

public sealed class FillUnitSave
{
    public string Id { get; set; } = "";
    public string? FillType { get; set; }
    public float Level { get; set; }
}

public sealed class DeliverySave
{
    /// <summary>Placement id of the POI.</summary>
    public string Poi { get; set; } = "";
    public string FillType { get; set; } = "";
    public float Amount { get; set; }
    public float Income { get; set; }
}

/// <summary>A field helper, with what it needs to plan the very same route again.</summary>
public sealed class HelperSave
{
    public int Field { get; set; }
    /// <summary>The area worked, as [x, z] points (a whole field, or a strip of one).</summary>
    public float[][] Shape { get; set; } = [];
    public float SpeedKmh { get; set; }
    public int? MaxLanes { get; set; }
    /// <summary>[x, z] the route was planned from, and the headland margin used.</summary>
    public float[] PlannedFrom { get; set; } = [];
    public float Margin { get; set; }
    /// <summary>Index of the waypoint being driven to, and [x, z] where driving started.</summary>
    public int Waypoint { get; set; }
    public float[]? DriveStart { get; set; }
}

public sealed class PlayerSave
{
    public float X { get; set; }
    public float Z { get; set; }
    public float Heading { get; set; }
    public int Farm { get; set; }
    public int? Vehicle { get; set; }
}

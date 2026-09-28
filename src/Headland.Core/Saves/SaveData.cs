using System.Text.Json;
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
    public List<PoiSave> Pois { get; set; } = [];
    /// <summary>State of the random stream rolling high demand at POIs.</summary>
    public ulong PoiRngState { get; set; }
    /// <summary>Contracts on the board and under way, the next contract's id and the stream rolling new ones.</summary>
    public List<ContractSave> Contracts { get; set; } = [];
    public int NextContractId { get; set; }
    public ulong ContractRngState { get; set; }
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
    public float Loan { get; set; }
    /// <summary>The books' pages, newest first.</summary>
    public List<PeriodSave> Days { get; set; } = [];
    public List<PeriodSave> Months { get; set; } = [];
}

/// <summary>A day or a month of the books.</summary>
public sealed class PeriodSave
{
    /// <summary>Day index, or for a month (year - 1) × 12 + month - 1.</summary>
    public int Index { get; set; }
    /// <summary>Money in (positive) and out (negative) by category ("sales", "loanInterest"...).</summary>
    public Dictionary<string, float> Amounts { get; set; } = new();
}

public sealed class StatisticsSave
{
    public Dictionary<string, float> HectaresWorked { get; set; } = new();
    public Dictionary<string, float> Harvested { get; set; } = new();
    public Dictionary<string, float> Sold { get; set; } = new();
    public Dictionary<string, float> Bought { get; set; } = new();
    public int HelpersHired { get; set; }
    public int ContractsCompleted { get; set; }
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

/// <summary>A POI's owner, and what its components keep: stored goods, production under way, demand.</summary>
public sealed class PoiSave
{
    /// <summary>Placement id on the map.</summary>
    public string Id { get; set; } = "";
    public int Farm { get; set; }
    /// <summary>What each of its components keeps, by component kind (fillUnits, sellingStation…).</summary>
    public Dictionary<string, JsonElement> Components { get; set; } = new();
}

/// <summary>A contract, with its parts by id: the job (contracts.json), field, crop, buyer and goods.</summary>
public sealed class ContractSave
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    /// <summary>The neighbor offering a field job (npcs.json id).</summary>
    public string? Npc { get; set; }
    public int? Field { get; set; }
    public string? Crop { get; set; }
    /// <summary>Placement id of the buyer, and the fill type of the goods.</summary>
    public string? Poi { get; set; }
    public string? Goods { get; set; }
    public float Amount { get; set; }
    public float Reward { get; set; }
    public int Days { get; set; }
    public int OfferedDay { get; set; }
    /// <summary>The farm doing it (0: on the board), and the day index it's due.</summary>
    public int Farm { get; set; }
    public int DueDay { get; set; }
    /// <summary>Crop threshed on the field (harvests), and goods tipped at the buyer.</summary>
    public float Harvested { get; set; }
    public float Delivered { get; set; }
    /// <summary>The job's lease set (index in its leases), its fee, and whether the contract was taken with it.</summary>
    public int? Lease { get; set; }
    public float LeaseFee { get; set; }
    public bool Leased { get; set; }
}

public sealed class MachineSave
{
    public int Id { get; set; }
    public string Def { get; set; } = "";
    /// <summary>The option chosen in each of its configurations (none when it has none; missing in older saves: the defaults).</summary>
    public Dictionary<string, string>? Configuration { get; set; }
    public int Farm { get; set; }
    /// <summary>The contract it's leased for.</summary>
    public int? Lease { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float Heading { get; set; }
    public float Speed { get; set; }
    public int? Parent { get; set; }
    public string? Joint { get; set; }
    public float WorkedHa { get; set; }
    public float Dirt { get; set; }
    /// <summary>What each of its components keeps, by component kind (runningGear, fillUnits, pipe…).</summary>
    public Dictionary<string, JsonElement> Components { get; set; } = new();
    /// <summary>A load being unloaded at a POI, totalled so far.</summary>
    public DeliverySave? Delivery { get; set; }
    /// <summary>A load being taken from a silo, totalled so far.</summary>
    public LoadingSave? Loading { get; set; }
    public HelperSave? Helper { get; set; }
}

public sealed class DeliverySave
{
    /// <summary>Placement id of the POI.</summary>
    public string Poi { get; set; } = "";
    public string FillType { get; set; } = "";
    public float Amount { get; set; }
    public float Income { get; set; }
    /// <summary>Put into the POI's storage by its owner rather than sold.</summary>
    public bool Stored { get; set; }
    /// <summary>Taken for this contract (id) rather than sold.</summary>
    public int? Contract { get; set; }
}

public sealed class LoadingSave
{
    /// <summary>Placement id of the POI whose silo loads it.</summary>
    public string Poi { get; set; } = "";
    public string FillType { get; set; } = "";
    public float Amount { get; set; }
}

/// <summary>
/// A field helper and its route. The route is kept rather than planned again: it was planned from the vehicle's pose
/// and from what was left to do on the field when the helper was hired.
/// </summary>
public sealed class HelperSave
{
    public int Field { get; set; }
    /// <summary>The area worked, as [x, z] points (a whole field, or a strip of one).</summary>
    public float[][] Shape { get; set; } = [];
    public float SpeedKmh { get; set; }
    public int? MaxLanes { get; set; }
    /// <summary>The headland margin used.</summary>
    public float Margin { get; set; }
    /// <summary>
    /// The route's waypoints as [x, z], and one letter per waypoint for how the segment arriving at it is driven:
    /// d(rive), w(ork) or r(everse). Missing in older saves: the route is then planned again from where the vehicle is.
    /// </summary>
    public float[][] Route { get; set; } = [];
    public string Segments { get; set; } = "";
    /// <summary>Index of the waypoint being driven to, and [x, z] where driving started.</summary>
    public int Waypoint { get; set; }
    public float[]? DriveStart { get; set; }
    /// <summary>Pay agreed when hired (missing in older saves: today's wage).</summary>
    public float? WagePerHour { get; set; }
    public double WorkedSeconds { get; set; }
    public float WagesPaid { get; set; }
}

public sealed class PlayerSave
{
    public float X { get; set; }
    public float Z { get; set; }
    public float Heading { get; set; }
    public int Farm { get; set; }
    public int? Vehicle { get; set; }
}

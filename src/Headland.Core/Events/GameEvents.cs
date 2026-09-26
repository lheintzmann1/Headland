using Headland.Core.Contracts;
using Headland.Core.Machines;
using Headland.Core.Pois;
using Headland.Core.Time;
using Headland.Core.Weather;
using Headland.Core.World;

namespace Headland.Core.Events;

// Everything the simulation announces on its EventBus. Notifications, statistics, and later sounds and Lua
// scripts subscribe to these instead of being called from the systems.

// ---- Time: published as each world hour is processed, so a skipped night publishes every hour it crossed.

/// <summary>A game hour began and its world tick (soil, crops, weather surface) has run.</summary>
public sealed record HourStarted(long Hour) : IGameEvent;

/// <summary>A new game day started at midnight (published after that hour's <see cref="HourStarted"/>).</summary>
public sealed record DayStarted(int DayIndex, GameDate Date) : IGameEvent;

/// <summary>A new month started (published after its first <see cref="DayStarted"/>).</summary>
public sealed record MonthStarted(int Year, int Month) : IGameEvent;

/// <summary>The current weather condition changed (rain started, fog lifted...).</summary>
public sealed record WeatherChanged(WeatherCondition From, WeatherCondition To) : IGameEvent;

// ---- Field work

/// <summary>
/// Cells changed under a work area during one tick. <paramref name="Work"/> is the work area type (cultivator,
/// seeder, harvester); <paramref name="FieldId"/> is the field under the work area's center (0 = none).
/// </summary>
public sealed record FieldWorked(Machine Machine, string Work, int FieldId, float Hectares) : IGameEvent;

/// <summary>Crop threshed into a combine's tank during one tick.</summary>
public sealed record CropHarvested(Machine Harvester, string Crop, string FillType, float Amount, int FieldId) : IGameEvent;

// ---- Trade

/// <summary>A whole load sold at a POI (published when the machine stops unloading).</summary>
public sealed record FillSold(Machine Machine, Poi Poi, string FillType, float Amount, float Income) : IGameEvent;

/// <summary>A whole load put into a POI's storage by its owner (published when the machine stops unloading).</summary>
public sealed record FillStored(Machine Machine, Poi Poi, string FillType, float Amount) : IGameEvent;

/// <summary>A whole load taken from a POI's storage by its owner (published when loading stops).</summary>
public sealed record FillLoaded(Machine Machine, Poi Poi, string FillType, float Amount) : IGameEvent;

/// <summary>Supplies or fuel bought at a POI into a machine's fill unit.</summary>
public sealed record FillBought(Machine Machine, Poi Poi, string FillType, float Amount, float Cost) : IGameEvent;

/// <summary>A POI pays <paramref name="Factor"/> times its price for a fill type, through the day <paramref name="Until"/>.</summary>
public sealed record HighDemandStarted(Poi Poi, string FillType, float Factor, GameDate Until) : IGameEvent;

public sealed record HighDemandEnded(Poi Poi, string FillType) : IGameEvent;

/// <summary>A POI's processing made this much of an output during one hour.</summary>
public sealed record PoiProduced(Poi Poi, string FillType, float Amount) : IGameEvent;

/// <summary>An hour's output sold for the POI's owner (outputs whose mode is "sell").</summary>
public sealed record ProductionSold(Poi Poi, string FillType, float Amount, float Income) : IGameEvent;

// ---- Money

/// <summary>The farm borrowed <paramref name="Amount"/>; <paramref name="Loan"/> is what it owes now.</summary>
public sealed record LoanTaken(float Amount, float Loan) : IGameEvent;

public sealed record LoanRepaid(float Amount, float Loan) : IGameEvent;

/// <summary>A cost that came due by itself (interest, wages, running costs) took the balance below zero.</summary>
public sealed record AccountOverdrawn(float Money) : IGameEvent;

// ---- Services

public sealed record MachineRepaired(Machine Machine, Poi Poi, float Cost) : IGameEvent;

public sealed record MachineWashed(Machine Machine, Poi Poi, float Cost) : IGameEvent;

/// <summary>A new machine put at a POI's delivery spot.</summary>
public sealed record MachineDelivered(Machine Machine, Poi Poi) : IGameEvent;

// ---- Ownership

/// <summary>A parcel changed hands (<see cref="Ownership.Farm.None"/> = its NPC).</summary>
public sealed record FarmlandOwnerChanged(Farmland Farmland, int FromFarm, int ToFarm) : IGameEvent;

/// <summary><paramref name="Farm"/> bought a parcel from its NPC.</summary>
public sealed record FarmlandBought(Farmland Farmland, int Farm, float Price) : IGameEvent;

/// <summary><paramref name="Farm"/> sold a parcel back to its NPC.</summary>
public sealed record FarmlandSold(Farmland Farmland, int Farm, float Price) : IGameEvent;

// ---- Contracts

/// <summary>A neighbor or a buyer put a contract on the board.</summary>
public sealed record ContractOffered(Contract Contract) : IGameEvent;

/// <summary>An offer came off the board untaken: too old, or its field was sold.</summary>
public sealed record ContractWithdrawn(Contract Contract) : IGameEvent;

/// <summary>A farm took a contract; it's due at the start of <see cref="Contract.DueDay"/>.</summary>
public sealed record ContractAccepted(Contract Contract) : IGameEvent;

/// <summary>A contract wasn't done by its due day.</summary>
public sealed record ContractFailed(Contract Contract) : IGameEvent;

// ---- Machines

public sealed record ImplementAttached(Machine Parent, string Joint, Machine Implement) : IGameEvent;

public sealed record ImplementDetached(Machine Parent, string Joint, Machine Implement) : IGameEvent;

public sealed record VehicleEntered(Machine Vehicle) : IGameEvent;

public sealed record VehicleExited(Machine Vehicle) : IGameEvent;

public enum HelperEnd { Finished, Stopped, Dismissed }

public sealed record HelperHired(Machine Vehicle, FieldInfo Field) : IGameEvent;

/// <summary>
/// A helper left its vehicle; <paramref name="Reason"/> says why it stopped early (out of seed, tank full), and
/// <paramref name="Wages"/> what it earned on the job.
/// </summary>
public sealed record HelperDismissed(Machine Vehicle, FieldInfo Field, HelperEnd End, string? Reason, float Wages) : IGameEvent;

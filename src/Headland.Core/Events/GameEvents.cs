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

/// <summary>Supplies bought at a POI into a machine's fill unit.</summary>
public sealed record FillBought(Machine Machine, Poi Poi, string FillType, float Amount, float Cost) : IGameEvent;

// ---- Ownership

/// <summary>A parcel changed hands (<see cref="Ownership.Farm.None"/> = its NPC).</summary>
public sealed record FarmlandOwnerChanged(Farmland Farmland, int FromFarm, int ToFarm) : IGameEvent;

// ---- Machines

public sealed record ImplementAttached(Machine Parent, string Joint, Machine Implement) : IGameEvent;

public sealed record ImplementDetached(Machine Parent, string Joint, Machine Implement) : IGameEvent;

public sealed record VehicleEntered(Machine Vehicle) : IGameEvent;

public sealed record VehicleExited(Machine Vehicle) : IGameEvent;

public enum HelperEnd { Finished, Stopped, Dismissed }

public sealed record HelperHired(Machine Vehicle, FieldInfo Field) : IGameEvent;

/// <summary>A helper left its vehicle; <paramref name="Reason"/> says why it stopped early (out of seed, tank full).</summary>
public sealed record HelperDismissed(Machine Vehicle, FieldInfo Field, HelperEnd End, string? Reason) : IGameEvent;

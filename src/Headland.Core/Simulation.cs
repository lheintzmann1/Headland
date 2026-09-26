using System.Diagnostics;
using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Crops;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Ownership;
using Headland.Core.Pois;
using Headland.Core.Time;
using Headland.Core.Weather;
using Headland.Core.World;

namespace Headland.Core;

/// <summary>
/// The whole game state and its update loop. No engine dependency: Godot drives <see cref="Tick"/> and renders.
/// </summary>
public sealed class Simulation
{
    /// <summary>Maximum world hours processed in a single frame (the rest carries over).</summary>
    public const int MaxHoursPerTick = 6;

    private long _pendingHourFrom;
    private int _pendingHours;
    private WeatherCondition _condition;

    private Simulation(ContentDatabase content, GameConfig setup)
    {
        Content = content;
        Setup = setup;
        Map = content.Maps[setup.Map];
        Climate = content.Climates[setup.Climate];
        Calendar = new Calendar(setup.DaysPerMonth);
        Clock = new GameClock(Calendar, new GameDate(setup.StartYear, setup.StartMonth, setup.StartDay), setup.StartHour);
        Weather = new WeatherSystem(Climate, Calendar, setup.WeatherSeed);
        World = WorldGen.Generate(Map, content);
        Difficulty = content.Difficulties[setup.Difficulty];
        Economy = new Economy(content, Calendar, Events, Difficulty, Clock.DayIndex);
        // Their own streams, so contracts and high demand don't change the weather.
        Contracts = new ContractSystem(this, setup.WeatherSeed * 0x9E3779B97F4A7C15UL + 0x434F4EUL);
        Farms = new Farms(World, Events, Economy, Contracts, setup.FarmName);
        Crops = new CropSystem(content, World, Calendar, Climate);
        Pois = new PoiSystem(this, setup.WeatherSeed * 0x9E3779B97F4A7C15UL + 0x504F49UL);
        Machines = new MachineSystem(this);
        Player = new PlayerCharacter(Events) { Position = new Vector2(Map.PlayerX, Map.PlayerZ) };
        Statistics = new Statistics(Events);
        Notifications.Follow(Events, content);
        Weather.Update(Clock.DayIndex, Clock.HourOfDay);
        _condition = Weather.Condition;
    }

    /// <summary>Everything that happens in the game is published here (see <c>GameEvents.cs</c>).</summary>
    public EventBus Events { get; } = new();
    public ContentDatabase Content { get; }
    /// <summary>What this game was started with: game.json for a new game, the save's own copy once loaded.</summary>
    public GameConfig Setup { get; }
    public MapDef Map { get; }
    public ClimateDef Climate { get; }
    public DifficultyDef Difficulty { get; }
    public Calendar Calendar { get; }
    public GameClock Clock { get; }
    public WeatherSystem Weather { get; }
    public WorldMap World { get; }
    public Farms Farms { get; }
    public CropSystem Crops { get; }
    public Economy Economy { get; }
    public PoiSystem Pois { get; }
    public ContractSystem Contracts { get; }
    public MachineSystem Machines { get; }
    public PlayerCharacter Player { get; }
    public Statistics Statistics { get; }
    public Notifications Notifications { get; } = new();

    /// <summary>Real seconds simulated since the game started (play time).</summary>
    public double RealTime { get; private set; }
    public double LastHourTickMs { get; private set; }

    /// <summary>A new game, set up from <paramref name="setup"/> (default: the content's game.json).</summary>
    public static Simulation Create(ContentDatabase content, GameConfig? setup = null)
    {
        var sim = new Simulation(content, setup ?? content.Game);
        sim.SpawnMapMachines();
        sim.Contracts.Post(sim.Clock.DayIndex, sim.Contracts.Rules.OffersPerDay);
        return sim;
    }

    /// <summary>The map's world without its starting machines, for a save to fill in.</summary>
    internal static Simulation CreateForLoad(ContentDatabase content, GameConfig setup) => new(content, setup);

    private void SpawnMapMachines()
    {
        var spawned = new List<Machine>();
        foreach (var sp in Map.Machines)
        {
            var m = Machines.Spawn(sp.Def, new Vector2(sp.X, sp.Z), sp.HeadingDeg * MathUtil.Deg2Rad, sp.Farm);
            spawned.Add(m);
            if (sp.AttachToIndex is not { } idx) continue;
            var parent = spawned[idx];
            var jointId = sp.Joint ?? parent.Def.AttacherJoints
                .FirstOrDefault(j => j.Type == m.Def.Attacher?.Type && !parent.Attached.ContainsKey(j.Id))?.Id;
            if (jointId == null || !Machines.Hitch(parent, jointId, m))
                throw new ContentException($"Map machine '{sp.Def}' cannot attach to '{parent.Def.Id}'");
        }
    }

    /// <summary>Advances everything by <paramref name="dt"/> real seconds (call at a fixed rate).</summary>
    public void Tick(float dt)
    {
        RealTime += dt;
        Notifications.Now = RealTime;

        var before = Clock.LastHour;
        var crossed = Clock.Advance(dt);
        QueueHours(before, crossed);
        RunPendingHours(MaxHoursPerTick);
        UpdateWeather();

        Machines.Update(dt);
        Pois.Update(dt);
        foreach (var m in Machines.All)
        {
            if (m.Controller is not FieldWorkController w) continue;
            PayHelper(w, dt);
            if (w.Finished) DismissHelper(m, w.Stopped ? HelperEnd.Stopped : HelperEnd.Finished);
        }
        Player.Update(this, dt);
        Notifications.Expire(8.0);
    }

    /// <summary>Skips game time instantly (sleep / debug), running every hourly world tick.</summary>
    public void SkipHours(int hours)
    {
        var before = Clock.LastHour;
        var crossed = Clock.Skip(hours * 3600.0);
        QueueHours(before, crossed);
        RunPendingHours(int.MaxValue);
        UpdateWeather();
    }

    /// <summary>Hours crossed but not yet run (a frame runs at most <see cref="MaxHoursPerTick"/>).</summary>
    internal (long from, int count) PendingHours => (_pendingHourFrom, _pendingHours);

    internal void RestoreTime(double realTime, long pendingFrom, int pendingCount)
    {
        RealTime = realTime;
        Notifications.Now = realTime;
        _pendingHourFrom = pendingFrom;
        _pendingHours = pendingCount;
        Weather.Update(Clock.DayIndex, Clock.HourOfDay);
        _condition = Weather.Condition;
    }

    private void UpdateWeather()
    {
        Weather.Update(Clock.DayIndex, Clock.HourOfDay);
        if (Weather.Condition == _condition) return;
        Events.Publish(new WeatherChanged(_condition, Weather.Condition));
        _condition = Weather.Condition;
    }

    private void QueueHours(long lastProcessed, int crossed)
    {
        if (crossed <= 0) return;
        if (_pendingHours == 0) _pendingHourFrom = lastProcessed + 1;
        _pendingHours += crossed;
    }

    private void RunPendingHours(int max)
    {
        var n = Math.Min(max, _pendingHours);
        if (n <= 0) return;
        var sw = Stopwatch.StartNew();
        var from = _pendingHourFrom;
        _pendingHourFrom += n;
        _pendingHours -= n;
        for (var k = 0; k < n; k++)
        {
            var hour = from + k;
            var day = (int)(hour / 24);
            Economy.StartHour(hour);
            Weather.Update(day, hour % 24 + 0.5f);
            Weather.TickHour();
            Crops.TickHour(Weather, day, hour);
            Pois.TickHour(hour);
            Contracts.TickHour(hour);
            PublishTime(hour);
        }
        LastHourTickMs = sw.Elapsed.TotalMilliseconds / n;
    }

    private void PublishTime(long hour)
    {
        Events.Publish(new HourStarted(hour));
        if (hour % 24 != 0) return;
        var day = (int)(hour / 24);
        var date = Calendar.DateOfDay(day);
        Events.Publish(new DayStarted(day, date));
        if (date.Day == 1) Events.Publish(new MonthStarted(date.Year, date.Month));
    }

    // ---------------------------------------------------------------- Player commands

    /// <summary>F: enter the nearest vehicle, or step out.</summary>
    public void ToggleEnterExit()
    {
        if (Player.Vehicle != null)
        {
            Player.Exit(this);
            return;
        }
        var m = Player.NearestEnterable(this);
        if (m != null) Player.Enter(m);
        else Notifications.Post("No vehicle nearby");
    }

    /// <summary>Tab / Shift+Tab: jump into the next or previous vehicle. A helper keeps driving the one left behind.</summary>
    public void SwitchVehicle(int step)
    {
        if (Player.NextVehicle(this, step) is not { } next)
        {
            Notifications.Post(Player.Vehicle != null ? "No other vehicle to switch to" : "No vehicle to switch to");
            return;
        }
        Player.Exit(this);
        Player.Enter(next);
    }

    public Machine? PlayerVehicle => Player.Vehicle;

    public void CommandAttach() => WithVehicle(Machines.ToggleAttach);
    public void CommandLower() => WithVehicle(Machines.ToggleLower);
    public void CommandTurnOn() => WithVehicle(Machines.ToggleOn);
    public void CommandUnload() => WithVehicle(Machines.ToggleUnload);
    public void CommandCycleSeed() => WithVehicle(Machines.CycleSeed);
    public void CommandUse() => WithVehicle(Pois.Use);

    /// <summary>H: hire a helper to work the field the vehicle is in (or the nearest one), or dismiss it.</summary>
    public void CommandHelper() => WithVehicle(v =>
    {
        if (v.Controller is FieldWorkController)
        {
            DismissHelper(v, HelperEnd.Dismissed);
            return;
        }
        var field = FieldNear(v);
        if (field == null)
        {
            Notifications.Post("Drive to a field first: helpers work the field you are in or next to", Severity.Warning);
            return;
        }
        if (!v.Chain().Any(m => m.Def.WorkArea != null))
        {
            Notifications.Post("Attach an implement first", Severity.Warning);
            return;
        }
        if (Economy.Money <= 0f)
        {
            Notifications.Post("Not enough money to pay a helper", Severity.Warning);
            return;
        }
        HireHelper(v, field);
    });

    /// <summary>What a helper hired now earns per hour of work.</summary>
    public float HelperWage => Content.Economy.HelperWagePerHour * Economy.PriceLevel;

    /// <summary>Puts a helper in the vehicle to work <paramref name="field"/> (optionally only its first lanes).</summary>
    public FieldWorkController HireHelper(Machine v, FieldInfo field, int? maxLanes = null)
    {
        var helper = new FieldWorkController(v, field, maxLanes: maxLanes) { WagePerHour = HelperWage };
        v.Controller = helper;
        Events.Publish(new HelperHired(v, field));
        return helper;
    }

    /// <summary>A helper earns its wage for every second it works, paid in whole dollars as they add up.</summary>
    private void PayHelper(FieldWorkController helper, float dt)
    {
        helper.WorkedSeconds += dt;
        if (MathF.Floor(helper.Wages - helper.WagesPaid) is var due and >= 1f) PayWages(helper, due);
    }

    private void PayWages(FieldWorkController helper, float amount)
    {
        helper.WagesPaid += amount;
        if (helper.Vehicle.FarmId == Farms.Player.Id) Economy.Spend(amount, MoneyCategory.Wages);
    }

    private void DismissHelper(Machine v, HelperEnd end)
    {
        var helper = (FieldWorkController)v.Controller!;
        if (helper.Wages - helper.WagesPaid is var rest and > 0f) PayWages(helper, rest);
        foreach (var m in v.Chain())
            if (m.Def.WorkArea != null) m.Lowered = false;
        v.Controller = Player.Vehicle == v ? Player.Controls : null;
        Events.Publish(new HelperDismissed(v, helper.Field, end, helper.StopReason, helper.Wages));
    }

    /// <summary>The field under the vehicle or its implements, else the nearest field within 25 m.</summary>
    public FieldInfo? FieldNear(Machine v)
    {
        foreach (var m in v.Chain())
        {
            var (cx, cz) = World.WorldToCell(m.Footprint.Center);
            if (World.InBounds(cx, cz) && World.Layers.FieldId[World.CellIndex(cx, cz)] is var id and > 0)
                return World.FieldById(id);
        }
        var p = v.Footprint.Center;
        return World.Fields
            .Select(f => (f, d: f.Shape.Distance(p)))
            .Where(x => x.d < 25f)
            .OrderBy(x => x.d)
            .Select(x => x.f)
            .FirstOrDefault();
    }

    private void WithVehicle(Action<Machine> action)
    {
        if (Player.Vehicle is { } v) action(v);
        else Notifications.Post("Get into a vehicle first (F)");
    }

    // ---------------------------------------------------------------- Queries

    /// <summary>Human-readable description of a cell for the inspect panel.</summary>
    public CellReport InspectCell(Vector2 p)
    {
        var (cx, cz) = World.WorldToCell(p);
        if (!World.InBounds(cx, cz)) return new CellReport(false);
        var i = World.CellIndex(cx, cz);
        var L = World.Layers;
        var cropId = L.Crop[i];
        CropDef? crop = cropId != 0 ? Content.Crops[cropId - 1] : null;
        var stage = L.Stage[i];
        var report = new CellReport(true)
        {
            Position = p,
            Height = World.HeightAt(p),
            Ground = (GroundType)L.Ground[i],
            Soil = Content.Soils[L.Soil[i]],
            FarmlandId = L.FarmlandId[i],
            FieldId = L.FieldId[i],
            Moisture = L.Moisture[i] / 255f,
            Nitrogen = L.Nitrogen[i],
            Crop = crop,
            Stage = stage,
            Health = L.Health[i] / 255f,
            Chill = L.Chill[i],
        };
        if (crop != null && stage != CropStage.Dead)
        {
            report.StageName = crop.Stages[stage].Name;
            report.DaysToHarvest = Crops.EstimateDaysToHarvest(i, Clock);
            report.WaterFactor = CropSystem.WaterFactor(report.Moisture, crop);
            var w = new List<string>();
            if (report.Moisture < crop.WiltingPoint) w.Add("Drought stress");
            else if (report.Moisture < crop.OptimalMoistureMin) w.Add("Soil is dry");
            else if (report.Moisture > 0.95f) w.Add("Waterlogged");
            if (Weather.Temperature < crop.FrostKillC + 3f) w.Add("Frost risk");
            if (L.Nitrogen[i] < crop.NitrogenDemandKgPerHa / Math.Max(1, crop.HarvestableStage)) w.Add("Low nitrogen");
            report.Warnings = w;
        }
        else if (crop != null) report.StageName = "Dead";
        return report;
    }
}

public sealed class CellReport(bool valid)
{
    public bool Valid { get; } = valid;
    public Vector2 Position { get; init; }
    public float Height { get; init; }
    public GroundType Ground { get; init; }
    public SoilDef? Soil { get; init; }
    public int FarmlandId { get; init; }
    public int FieldId { get; init; }
    public float Moisture { get; init; }
    public float Nitrogen { get; init; }
    public CropDef? Crop { get; init; }
    public byte Stage { get; init; }
    public string? StageName { get; set; }
    public float Health { get; init; }
    /// <summary>Vernalization chill accumulated so far (real days).</summary>
    public float Chill { get; init; }
    public float DaysToHarvest { get; set; } = float.NaN;
    public float WaterFactor { get; set; } = float.NaN;
    public List<string> Warnings { get; set; } = [];
}

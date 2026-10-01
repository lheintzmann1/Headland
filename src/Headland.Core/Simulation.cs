using System.Diagnostics;
using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Crops;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Objects;
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
        Objects = new ObjectSystem(this);
        Shop = new Shop(this);
        Garage = new Garage(this);
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
    /// <summary>Things lying about that machines carry: bales.</summary>
    public ObjectSystem Objects { get; }
    public Shop Shop { get; }
    public Garage Garage { get; }
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
            var m = Machines.Spawn(sp.Def, new Vector2(sp.X, sp.Z), sp.HeadingDeg * MathUtil.Deg2Rad, sp.Farm, sp.Configuration);
            spawned.Add(m);
            if (sp.AttachToIndex is not { } idx) continue;
            var parent = spawned[idx];
            var jointId = sp.Joint ?? parent.Def.Joints
                .FirstOrDefault(j => j.Type == m.Get<Attachable>()?.Def.Type && !parent.Attached.ContainsKey(j.Id))?.Id;
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
        Objects.Update(dt);
        Shop.Update();
        Pois.Update(dt);
        foreach (var m in Machines.All)
        {
            if (m.Get<Drivable>()?.Controller is not FieldWorkController w) continue;
            PayHelper(w, dt);
            if (w.Finished) DismissHelper(m, w.Stopped ? HelperEnd.Stopped : HelperEnd.Finished);
        }
        Contracts.Update(dt);
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

    // ---------------------------------------------------------------- Player actions

    /// <summary>
    /// The player's situation for their keys: walking about or driving, the mouse's tool mode with it (the game adds its
    /// menus, and says when the mouse is on the tool).
    /// </summary>
    public InputContext Situation =>
        InputContext.World | (Player.Vehicle != null ? InputContext.Vehicle | InputContext.MouseTool : InputContext.OnFoot);

    /// <summary>
    /// What each key does now for the player (FS: the action events registered for them): on foot, getting into the
    /// vehicle nearby; driving, getting out, hitching, selecting an implement, what the components of the selected
    /// implement and of the vehicle offer (lowering, folding, turning on, unloading…; the whole chain's with the vehicle
    /// itself selected) and the helper; and the use key where something is offered to it
    /// (<see cref="Activations"/>). The HUD hints them and the help lists them.
    /// </summary>
    public ActionList Offers()
    {
        var actions = new ActionList(Notifications);
        actions.Add(InputActions.NextVehicle, "Switch to the next vehicle", () => SwitchVehicle(1), hinted: false);
        actions.Add(InputActions.PrevVehicle, "Switch to the previous vehicle", () => SwitchVehicle(-1), hinted: false);
        if (Activations() is [var nearest, ..]) actions.Add(InputActions.Use, nearest.Label, () => Activate());
        if (Player.Vehicle is not { } v)
        {
            if (Player.NearestEnterable(this) is { } near) actions.Add(InputActions.Enter, $"Enter {near.Def.Name}", () => Player.Enter(near));
            return actions;
        }
        actions.Add(InputActions.Enter, "Exit", () => Player.Exit(this));
        if (Machines.FindAttachable(v) is var (parent, joint, child))
            actions.Add(InputActions.Attach, $"Attach {child.Def.Name}", () => Machines.Attach(parent, joint.Id, child));
        else if (v.Chain().Skip(1).LastOrDefault() is { } leaf)
            actions.Add(InputActions.Attach, $"Detach {leaf.Def.Name}", () => Machines.Detach(leaf));
        if (v.Get<Drivable>() is { CanSelect: true } seat)
        {
            // An implement, or a control group of a crane (FS: subselections): "Select Loader: arm".
            var (next, group) = seat.Next;
            var what = next ?? v;
            var name = Drivable.GroupsOf(what) > 1 ? $"{what.Def.Name}: {Drivable.GroupName(what, group)}" : what.Def.Name;
            actions.Add(InputActions.SelectImplement, next == null && group == 1 && v.Attached.Count > 0 ? "Select all" : $"Select {name}", seat.SelectNext);
        }
        foreach (var m in v.Get<Drivable>()?.ToolScope ?? v.Chain())
        foreach (var source in m.Components.OfType<IActionSource>())
            source.AddActions(actions, this);
        AddHelper(actions, v);
        return actions;
    }

    /// <summary>
    /// What the use key does where the player is, walking or in their vehicle's chain (or for <paramref name="vehicle"/>'s
    /// chain), as the components whose triggers they're in offer it: usable ones first, nearest first.
    /// </summary>
    public IReadOnlyList<Activation> Activations(Machine? vehicle = null)
    {
        var user = (vehicle?.Root ?? Player.Vehicle) is { } v
            ? new ActivationUser(v.FarmId, v.Footprint.Center, v)
            : new ActivationUser(Player.FarmId, Player.Position, null);
        return World.Pois
            .SelectMany(p => p.Components.OfType<IActivatable>())
            .SelectMany(a => a.Activations(user, this))
            .OrderBy(a => !a.Usable)
            .ThenBy(a => a.Distance)
            .ToList();
    }

    /// <summary>The use key: runs the nearest usable activation, else says why the nearest can't be used.</summary>
    public void Activate(Machine? vehicle = null)
    {
        switch (Activations(vehicle))
        {
            case [{ Usable: true } nearest, ..]: nearest.Run!(); break;
            case [var nearest, ..]: Notifications.Post(nearest.Blocked ?? "Nothing to do here"); break;
            default: Notifications.Post(InputActions.Def(InputActions.Use)!.Unavailable!); break;
        }
    }

    /// <summary>
    /// Does what <paramref name="action"/> does now for the player (see <see cref="Offers"/>), or says why nothing
    /// happens: it's for a vehicle and they walk, or there's nothing for it to do.
    /// </summary>
    public void Perform(string action)
    {
        var def = InputActions.Def(action);
        if (def != null && (def.Contexts & Situation) == 0)
            Notifications.Post(Player.Vehicle == null ? "Get into a vehicle first" : "Get out of the vehicle first");
        else if (Offers().Of(action) is { } offer) offer.Run();
        else if (action == InputActions.Attach && Player.Vehicle is { } v && Machines.AttachBlocker(v) is { } blocked) Notifications.Post(blocked);
        else if (def?.Unavailable is { } why) Notifications.Post(why);
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

    /// <summary>The helper key: dismiss the helper driving, or hire one for the field the vehicle works in or next to.</summary>
    private void AddHelper(ActionList actions, Machine v)
    {
        if (v.Get<Drivable>()?.Controller is FieldWorkController)
        {
            actions.Add(InputActions.Helper, "Dismiss helper", () => DismissHelper(v, HelperEnd.Dismissed));
            return;
        }
        var field = v.Chain().Any(m => m.Has<WorkAreas>()) ? FieldNear(v) : null;
        actions.Add(InputActions.Helper, field != null ? $"Hire helper for {field.Label} (${HelperWage:N0}/h)" : "Hire helper",
            () => HireHelper(v), hinted: field != null);
    }

    private void HireHelper(Machine v)
    {
        var field = FieldNear(v);
        if (field == null)
        {
            Notifications.Post("Drive to a field first: helpers work the field you are in or next to", Severity.Warning);
            return;
        }
        if (v.Chain().FirstOrDefault(m => m.Has<WorkAreas>()) is not { } tool)
        {
            Notifications.Post("Attach an implement first", Severity.Warning);
            return;
        }
        var areas = tool.Get<WorkAreas>()!;
        var area = areas.Def.Areas[0];
        if (Farms.FieldBlocker(v.FarmId, field, area.Type, area.Work.Crop(areas, Content)) is { } why)
        {
            Notifications.Post(why, Severity.Warning);
            return;
        }
        if (Economy.Money <= 0f)
        {
            Notifications.Post("Not enough money to pay a helper", Severity.Warning);
            return;
        }
        var helper = new FieldWorkController(this, v, field);
        if (helper.Path.LaneCount == 0)
        {
            Notifications.Post($"Nothing left for the {tool.Def.Name} to do on {field.Label}", Severity.Warning);
            return;
        }
        Hire(v, helper);
    }

    /// <summary>What a helper hired now earns per hour of work.</summary>
    public float HelperWage => Content.Economy.HelperWagePerHour * Economy.PriceLevel;

    /// <summary>
    /// Puts a helper in the vehicle to work what's left of <paramref name="field"/> (optionally only its first
    /// lanes), going on from where the vehicle stands.
    /// </summary>
    public FieldWorkController HireHelper(Machine v, FieldInfo field, int? maxLanes = null) =>
        Hire(v, new FieldWorkController(this, v, field, maxLanes: maxLanes));

    /// <summary>Dismisses a helper at work: its vehicle stops where it is.</summary>
    public void Dismiss(FieldWorkController helper)
    {
        if (helper.Vehicle.Get<Drivable>()?.Controller == helper) DismissHelper(helper.Vehicle, HelperEnd.Dismissed);
    }

    /// <summary>The farmer gets into <paramref name="v"/>, out of the vehicle they're in, a helper driving it keeps working.</summary>
    public bool TakeSeat(Machine v)
    {
        if (Player.Vehicle == v) return true;
        var from = Player.Vehicle;
        Player.Exit(this);
        if (Player.Enter(v)) return true;
        if (from != null) Player.Enter(from);
        return false;
    }

    /// <summary>
    /// What <paramref name="farmId"/> has of <paramref name="fillType"/> in stock (the prices page): in its POIs' storage
    /// (silos, a production's), and in the bales and on the pallets it owns.
    /// </summary>
    public float Stock(int farmId, string fillType) =>
        World.Pois.Where(p => p.FarmId == farmId).Sum(p => p.Get<FillUnits>()?.Level(fillType) ?? 0f)
        + Objects.All.Where(o => o.FarmId == farmId && o.Content?.FillType == fillType).Sum(o => o.Content!.Level);

    /// <summary>The helpers at work, in their vehicles.</summary>
    public IEnumerable<FieldWorkController> Helpers => Machines.All.Select(m => m.Get<Drivable>()?.Controller).OfType<FieldWorkController>();

    /// <summary>The lowest number no helper at work has (from 1).</summary>
    internal int FreeHelperNumber()
    {
        var taken = Helpers.Select(h => h.Number).ToHashSet();
        var n = 1;
        while (taken.Contains(n)) n++;
        return n;
    }

    private FieldWorkController Hire(Machine v, FieldWorkController helper)
    {
        helper.WagePerHour = HelperWage;
        helper.Number = FreeHelperNumber();
        helper.TakeOver();
        v.Get<Drivable>()!.Controller = helper;
        Events.Publish(new HelperHired(v, helper.Field, helper.Number));
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
        var drivable = v.Get<Drivable>()!;
        var helper = (FieldWorkController)drivable.Controller!;
        if (helper.Wages - helper.WagesPaid is var rest and > 0f) PayWages(helper, rest);
        foreach (var m in v.Chain())
        {
            if (m.Get<WorkAreas>() is not { } areas) continue;
            if (m.Get<Attachable>() is { } a) a.Lowered = false;
            if (FieldWorkController.SwitchedOnLanes(m, areas)) areas.On = false;
        }
        drivable.Controller = Player.Vehicle == v ? Player.Controls : null;
        Events.Publish(new HelperDismissed(v, helper.Field, helper.Number, end, helper.StopReason, helper.Wages));
    }

    /// <summary>
    /// Takes machines off the map (leased ones going back): helpers driving them leave, the player steps out, and the
    /// machines staying are unhitched from them.
    /// </summary>
    internal void RemoveMachines(IReadOnlyCollection<Machine> gone)
    {
        foreach (var root in gone.Select(m => m.Root).Distinct().ToList())
            if (root.Get<Drivable>()?.Controller is FieldWorkController) DismissHelper(root, HelperEnd.Dismissed);
        if (Player.Vehicle is { } v && gone.Contains(v)) Player.Exit(this);
        foreach (var m in gone)
        {
            if (m.Parent != null && !gone.Contains(m.Parent)) Machines.Detach(m);
            foreach (var child in m.Attached.Values.Where(c => !gone.Contains(c)).ToList()) Machines.Detach(child);
        }
        foreach (var m in gone)
        {
            Pois.Forget(m);
            Objects.DropFrom(m);
            Machines.All.Remove(m);
        }
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
            Weeds = L.Weeds[i],
            Fertilized = L.Fertilized[i],
            Windrow = Windrows.Has(L, i) ? L.Windrow[i] : 0f,
            WindrowFill = Windrows.Has(L, i) ? Windrows.FillTypeAt(Content, L, i) : null,
        };
        if (crop != null && stage != CropStage.Dead)
        {
            report.StageName = crop.Stages[stage].Name;
            report.DaysToHarvest = Crops.EstimateDaysToHarvest(i, Clock);
            report.WaterFactor = CropSystem.WaterFactor(report.Moisture, crop);
            report.ExpectedYieldPerHa = CropSystem.YieldPerHa(L, crop, i);
            var w = new List<string>();
            if (report.Moisture < crop.WiltingPoint) w.Add("Drought stress");
            else if (report.Moisture < crop.OptimalMoistureMin) w.Add("Soil is dry");
            else if (report.Moisture > 0.95f) w.Add("Waterlogged");
            if (Weather.Temperature < crop.FrostKillC + 3f) w.Add("Frost risk");
            if (L.Nitrogen[i] < crop.NitrogenDemandKgPerHa / Math.Max(1, crop.HarvestableStage)) w.Add("Low nitrogen");
            if (WeedState.Living(L.Weeds[i])) w.Add($"Weeds cost {(1f - CropSystem.WeedFactor(crop, L.Weeds[i])) * 100f:0}% of the yield: spray them");
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
    /// <summary>A <see cref="WeedState"/>.</summary>
    public byte Weeds { get; init; }
    /// <summary>Times fertilized since the last harvest.</summary>
    public int Fertilized { get; init; }
    /// <summary>What lies cut on the cell (grass, hay, straw), and how much (units).</summary>
    public float Windrow { get; init; }
    public FillTypeDef? WindrowFill { get; init; }
    /// <summary>What a hectare of the crop would yield now, by its health and the weeds.</summary>
    public float ExpectedYieldPerHa { get; set; }
    public float DaysToHarvest { get; set; } = float.NaN;
    public float WaterFactor { get; set; } = float.NaN;
    public List<string> Warnings { get; set; } = [];
}

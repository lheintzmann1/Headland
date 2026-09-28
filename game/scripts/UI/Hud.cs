using System.Text;
using Headland.Game.Common;
using Headland.Core.Components;
using Headland.Core;
using Headland.Core.Contracts;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;
using Headland.Core.Time;
using Headland.Core.Weather;
using Headland.Core.World;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.UI;

/// <summary>
/// Heads-up display: clock/weather/forecast, contracts under way, money, vehicle panel, DF-style cell inspector,
/// context key prompts, notifications and the debug overlay. Built from <see cref="Widgets"/>; styled by the theme.
/// </summary>
public partial class Hud : CanvasLayer
{
    private RichTextLabel _clock = null!;
    private RichTextLabel _contracts = null!;
    private PanelContainer _contractsPanel = null!;
    private Label _money = null!;
    private RichTextLabel _vehicle = null!;
    private PanelContainer _vehiclePanel = null!;
    private RichTextLabel _inspect = null!;
    private PanelContainer _inspectPanel = null!;
    private RichTextLabel _prompt = null!;
    private VBoxContainer _notes = null!;
    private Label _debug = null!;
    private double _inspectTimer;

    public Simulation Sim { get; init; } = null!;

    /// <summary>Ground point under the mouse (set by the game), or null.</summary>
    public NVec2? Hover { get; set; }

    public bool DebugVisible
    {
        get => _debug.Visible;
        set => _debug.Visible = value;
    }

    public override void _Ready()
    {
        Layer = 1;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // The clock, the contracts under way under it, and the debug overlay below them.
        var topLeft = Widgets.Anchor(new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }, Control.LayoutPreset.TopLeft, new Vector2(12, 12));
        root.AddChild(topLeft);
        _clock = Widgets.Rich(360);
        topLeft.AddChild(Widgets.Panel(_clock));
        _contracts = Widgets.Rich(360);
        _contractsPanel = Widgets.Panel(_contracts);
        topLeft.AddChild(_contractsPanel);

        _money = Widgets.Label(variation: "MoneyLabel");
        _money.HorizontalAlignment = HorizontalAlignment.Right;
        root.AddChild(Widgets.Anchor(Widgets.Panel(_money), Control.LayoutPreset.TopRight, new Vector2(-12, 12)));

        _vehicle = Widgets.Rich(380);
        _vehiclePanel = Widgets.Anchor(Widgets.Panel(_vehicle), Control.LayoutPreset.BottomRight, new Vector2(-12, -12));
        root.AddChild(_vehiclePanel);

        _inspect = Widgets.Rich(360);
        _inspectPanel = Widgets.Anchor(Widgets.Panel(_inspect), Control.LayoutPreset.TopRight, new Vector2(-12, 70));
        root.AddChild(_inspectPanel);

        _prompt = Widgets.Rich(420, "PromptText");
        var promptPanel = Widgets.Anchor(Widgets.Panel(_prompt), Control.LayoutPreset.CenterBottom, new Vector2(0, -12));
        promptPanel.Name = "Prompt";
        root.AddChild(promptPanel);

        _notes = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _notes.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _notes.GrowHorizontal = Control.GrowDirection.Both;
        _notes.Position = new Vector2(-260, 14);
        _notes.CustomMinimumSize = new Vector2(520, 0);
        root.AddChild(_notes);

        _debug = Widgets.Label(variation: "DebugLabel");
        _debug.Visible = false;
        topLeft.AddChild(_debug);
    }

    // ------------------------------------------------------------------ Update

    public override void _Process(double delta)
    {
        UpdateClock();
        UpdateContracts();
        Widgets.Balance(_money, Sim.Economy.Money);
        UpdateVehicle();
        UpdatePrompt();
        UpdateNotes();
        _inspectTimer -= delta;
        if (_inspectTimer <= 0)
        {
            _inspectTimer = 0.12;
            UpdateInspect();
        }
        if (_debug.Visible)
            _debug.Text = $"FPS {Engine.GetFramesPerSecond():0}  ·  draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}  ·  " +
                          $"objects {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0}\n" +
                          $"hour tick {Sim.LastHourTickMs:0.0} ms  ·  crop stage changes {Sim.Crops.LastStageChanges}  ·  " +
                          $"mem {OS.GetStaticMemoryUsage() / 1048576.0:0} MB";
    }

    private void UpdateClock()
    {
        var c = Sim.Clock;
        var w = Sim.Weather;
        var speed = c.Paused ? Widgets.Colored("paused", Palette.Paused) : $"×{c.TimeScale:0}";
        var sb = new StringBuilder();
        sb.Append($"[b]{c.Date}[/b]   {c.TimeString}   [color={Palette.Dim}]{speed} · {Calendar.SeasonOf(c.Month)}[/color]\n");
        sb.Append($"{Icon(w.Condition)} {w.Condition}  {w.Temperature:0}°C");
        if (w.Wind > 0.55f) sb.Append("  · windy");
        if (w.SnowCover > 0.05f) sb.Append($"  · snow {w.SnowCover * 100:0}%");
        sb.Append('\n');
        sb.Append($"[color={Palette.Dim}]");
        foreach (var d in w.Forecast(c.DayIndex + 1, 3))
        {
            var date = Sim.Calendar.DateOfDay(d.DayIndex);
            sb.Append($"{Calendar.MonthNames[date.Month - 1][..3]} {date.Day}: {Icon(d.Summary)} {d.TempMin:0}–{d.TempMax:0}°");
            // A compressed game day stands for ~10 real days, so show when it rains, not millimeters.
            if (d.PrecipMm > 0.5f) sb.Append($" {d.PrecipStartHour:00}–{(d.PrecipStartHour + d.PrecipHours) % 24:00}h");
            else if (d.FogUntilHour > 0) sb.Append(" fog a.m.");
            sb.Append("   ");
        }
        sb.Append("[/color]");
        _clock.Text = sb.ToString();
    }

    /// <summary>The farm's contracts under way: what, how far, and how long is left.</summary>
    private void UpdateContracts()
    {
        var mine = Sim.Contracts.ActiveOf(Sim.Player.FarmId).ToList();
        _contractsPanel.Visible = mine.Count > 0;
        if (mine.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var c in mine)
        {
            var left = c.DueDay - Sim.Clock.DayIndex;
            var due = left <= 1 ? Widgets.Colored("last day", Palette.Warning) : Widgets.Colored($"{left} days left", Palette.Dim);
            sb.Append($"{Widgets.Colored(c.Label, Palette.Contract)}  {ContractsScreen.Progress(Sim, c)}  {due}\n");
        }
        _contracts.Text = sb.ToString().TrimEnd('\n');
    }

    /// <summary>The condition's icon (assets/icons is named after <see cref="WeatherCondition"/>).</summary>
    private static string Icon(WeatherCondition c) => Widgets.Icon(c.ToString().ToLowerInvariant(), Palette.Weather(c));

    private static string Warning(string text) => $"{Widgets.Icon("warning", Palette.Warning)} {Widgets.Colored(text, Palette.Warning)}";

    private void UpdateVehicle()
    {
        var v = Sim.Player.Vehicle;
        _vehiclePanel.Visible = v != null;
        if (v == null) return;
        var sb = new StringBuilder();
        var leased = v.LeaseContract != 0 ? Widgets.Colored(" leased", Palette.Contract) : "";
        sb.Append($"[b]{v.Def.Name}[/b]{leased}   {Mathf.Abs(v.Speed) * 3.6f:0} km/h{(v.Speed < -0.05f ? " (R)" : "")}");
        if (v.Get<Motor>() is { Running: true } motor) sb.Append(Widgets.Colored($"   {motor.FuelPerHour:0.0} L/h", Palette.Dim));
        if (v.Get<RunningGear>() is { Slip: > 0.05f } gear) sb.Append("   " + Widgets.Colored($"slip {gear.Slip * 100f:0}%", gear.Slip > 0.15f ? Palette.Warning : Palette.Dim));
        if (v.Get<Thresher>() is { } thresher) sb.Append("   " + (thresher.On ? Widgets.Colored("threshing", Palette.Good) : Widgets.Colored("off", Palette.Dim)));
        sb.Append('\n');
        if (v.Get<Drivable>()?.Controller is FieldWorkController w)
            sb.Append($"  {Widgets.Colored($"Helper working {w.Field.Label}: lane {Math.Min(w.LanesDone + 1, w.Path.LaneCount)}/{w.Path.LaneCount} · ${w.Wages:N0} in wages", Palette.Info)}\n");
        foreach (var m in v.Chain())
        {
            if (m != v) sb.Append($"  {m.Def.Name}");
            var bits = new List<string>();
            if (m.Get<AnimatedParts>() is { CanFold: true } parts && !parts.Unfolded) bits.Add(parts.Folded ? "folded" : "unfolding");
            if (m != v && m.Get<Attachable>() is { Def.Lowerable: true } hitch) bits.Add(hitch.Lowered ? Widgets.Colored("lowered", Palette.Good) : "raised");
            foreach (var s in m.Components.OfType<ISwitchable>().Where(s => s.CanTurnOn && s is not Thresher))
                bits.Add(s.On ? Widgets.Colored("on", Palette.Good) : "off");
            if (m.Get<WorkAreas>() is { Sows: true } seeder) bits.Add(Sim.Content.Crops[seeder.Crop].Name);
            foreach (var u in m.FillUnits)
            {
                var ft = u.FillType != null ? Sim.Content.FillTypes[u.FillType] : null;
                var unit = ft?.Unit ?? Sim.Content.FillTypes[u.Def.FillTypes[0]].Unit;
                bits.Add($"{ft?.Name ?? "empty"} {u.Level:N0}/{u.Capacity:N0} {unit}");
            }
            if (m.WorkedHa > 0.001f) bits.Add($"{m.WorkedHa:0.00} ha");
            if (m.Get<Tipper>() is { Tipping: true }) bits.Add(Widgets.Colored("tipping", Palette.Busy));
            if (Sim.Pois.LoadingFillType(m) is { } loading) bits.Add(Widgets.Colored($"loading {Sim.Content.FillTypes[loading].Name.ToLowerInvariant()}", Palette.Busy));
            if (m.Get<Pipe>() is { Out: true }) bits.Add(Widgets.Colored("pipe out", Palette.Busy));
            if (bits.Count > 0) sb.Append((m == v ? "  " : " — ") + string.Join(" · ", bits));
            if (m != v || bits.Count > 0) sb.Append('\n');
            foreach (var c in m.Conditions) sb.Append($"  {Warning(c.Text)}\n");
        }
        _vehicle.Text = sb.ToString().TrimEnd('\n');
    }

    private void UpdatePrompt()
    {
        var lines = new List<string>();
        var K = Widgets.Key;
        var v = Sim.Player.Vehicle;
        if (v == null)
        {
            var near = Sim.Player.NearestEnterable(Sim);
            if (near != null) lines.Add($"{K("enter")} Enter {near.Def.Name}");
        }
        else
        {
            if (Sim.Machines.FindAttachable(v) is var (_, _, child)) lines.Add($"{K("attach")} Attach {child.Def.Name}");
            else if (v.Chain().Skip(1).LastOrDefault() is { } leaf) lines.Add($"{K("attach")} Detach {leaf.Def.Name}");
            var tools = MachineSystem.Lowerable(v);
            if (tools.Count > 0) lines.Add($"{K("lower")} {(tools.Any(t => t.Lowered) ? "Raise" : "Lower")}");
            var switches = MachineSystem.Switchable(v);
            if (switches.Count > 0) lines.Add($"{K("turn_on")} Turn {(switches.Any(s => s.On) ? "off" : "on")}");
            if (v.Get<Pipe>() is { } pipe) lines.Add($"{K("unload")} {(pipe.Out ? "Fold" : "Unfold")} pipe");
            foreach (var t in v.Chain().Where(m => m.Has<Tipper>()))
            {
                var pit = Sim.Pois.TriggerAt(t.Footprint.Center, "unload");
                if (pit == null) continue;
                var tipper = t.Get<Tipper>()!;
                var load = tipper.Load;
                var price = load.FillType != null ? Sim.Pois.SalePrice(t, pit, load.FillType) : null;
                var at = price is { } p ? $" at ${p:0.00}/{Sim.Content.FillTypes[load.FillType!].Unit}" : "";
                if (load.FillType != null && Sim.Contracts.Taking(t.FarmId, pit.Poi, load.FillType) is { } job)
                    at = $" for the contract ({job.Owed:N0} {job.Goods!.Unit} to go)";
                lines.Add($"{K("unload")} {(tipper.Tipping ? "Stop tipping" : $"Tip into {pit.Poi.Name}{at}")}");
            }
            if (v.Chain().Any(m => m.Get<WorkAreas>() is { Sows: true })) lines.Add($"{K("cycle_seed")} Change seed");
            if (v.Get<RunningGear>() is { Def.Modes.Length: > 1 } gear) lines.Add($"{K("steering")} Steering: {MachineSystem.SteeringName(gear.Mode)}");
            if (Sim.Pois.UseOptions(v) is { Count: > 0 } uses) lines.Add($"{K("use")} {string.Join(", ", uses)}");
            if (v.Get<Drivable>()?.Controller is FieldWorkController) lines.Add($"{K("helper")} Dismiss helper");
            else if (v.Chain().Any(m => m.Has<WorkAreas>()) && Sim.FieldNear(v) is { } f) lines.Add($"{K("helper")} Hire helper for {f.Label} (${Sim.HelperWage:N0}/h)");
            lines.Add($"{K("enter")} Exit");
        }
        _prompt.Text = string.Join("    ", lines);
        _prompt.GetParent<Control>().Visible = lines.Count > 0;
    }

    private void UpdateNotes()
    {
        var items = Sim.Notifications.Items;
        while (_notes.GetChildCount() > items.Count) _notes.GetChild(0).Free();
        while (_notes.GetChildCount() < items.Count)
        {
            var l = Widgets.Label();
            l.HorizontalAlignment = HorizontalAlignment.Center;
            _notes.AddChild(l);
        }
        for (var i = 0; i < items.Count; i++)
        {
            var n = items[i];
            var l = (Label)_notes.GetChild(i);
            l.Text = n.Text;
            l.ThemeTypeVariation = n.Severity switch
            {
                Severity.Good => "NoteGoodLabel",
                Severity.Warning => "NoteWarningLabel",
                _ => "NoteLabel",
            };
            var age = Sim.RealTime - n.RealTime;
            l.Modulate = new Color(1, 1, 1, Mathf.Clamp(8f - (float)age, 0f, 1f));
        }
    }

    private void UpdateInspect()
    {
        if (Hover is not { } p)
        {
            _inspectPanel.Visible = false;
            return;
        }
        var r = Sim.InspectCell(p);
        if (!r.Valid)
        {
            _inspectPanel.Visible = false;
            return;
        }
        _inspectPanel.Visible = true;
        var sb = new StringBuilder();
        var field = r.FieldId != 0 ? Sim.World.FieldById(r.FieldId) : null;
        var farmland = r.FarmlandId != 0 ? Sim.World.FarmlandById(r.FarmlandId) : null;
        sb.Append(field != null ? $"[b]{field.Label}[/b] ({field.AreaHa:0.00} ha)" : "[b]Open ground[/b]");
        if (farmland != null)
        {
            var owner = farmland.FarmId == Sim.Player.FarmId ? "yours" : $"{Sim.Farms.OwnerName(farmland)}'s";
            if (farmland.FarmId == Farm.None) owner += $", for sale at ${Sim.Farms.Price(farmland):N0}";
            sb.Append($" · {farmland.Label}, {owner}");
        }
        sb.Append($"   {Widgets.Colored($"{r.Position.X:0}, {r.Position.Y:0} · {r.Height:0.0} m", Palette.Dim)}\n");
        if (field != null && Sim.Contracts.On(field) is { } contract)
            sb.Append((contract.State == ContractState.Active
                ? Widgets.Colored($"Contract: {contract.Job}, by {Sim.Contracts.DueDate(contract).Short}", Palette.Contract)
                : Widgets.Colored($"Offer: {contract.Job} for {contract.Client}, ${contract.Reward:N0}", Palette.Dim)) + "\n");
        sb.Append($"{GroundName(r.Ground)} on [b]{r.Soil?.Name}[/b]");
        if (r.Crop != null)
        {
            var ripe = r.Crop.Stages.ElementAtOrDefault(r.Stage)?.Harvestable == true;
            sb.Append($" · [b]{r.Crop.Name}[/b] — {r.StageName}{(ripe ? " " + Widgets.Colored("(ready to harvest)", Palette.Good) : "")}");
        }
        sb.Append('\n');
        if (!WorldMap.IsSealed(r.Ground))
        {
            sb.Append($"Moisture [b]{r.Moisture * 100:0}%[/b]   Nitrogen [b]{r.Nitrogen:0}[/b] kg/ha");
            if (r.Crop != null && r.StageName != "Dead") sb.Append($"   Health [b]{r.Health * 100:0}%[/b]");
            sb.Append('\n');
            var treated = new List<string>();
            if (r.Weeds != WeedState.None) treated.Add(WeedName(r.Weeds));
            if (r.Fertilized > 0) treated.Add(r.Fertilized == 1 ? "fertilized" : $"fertilized {r.Fertilized}×");
            if (treated.Count > 0) sb.Append(Widgets.Colored(string.Join(" · ", treated), Palette.Dim) + "\n");
        }
        if (r.Crop is { VernalizationDays: > 0 } vc && r.StageName != "Dead" && r.Stage < Array.FindIndex(vc.Stages, s => s.RequiresVernalization))
            sb.Append(r.Chill >= vc.VernalizationDays
                ? Widgets.Colored("Vernalized: ready to shoot in spring", Palette.Info) + "\n"
                : Widgets.Colored($"Vernalization {r.Chill:0}/{vc.VernalizationDays:0} cold days", Palette.Info) + "\n");
        if (r.Crop != null && !float.IsNaN(r.DaysToHarvest) && r.DaysToHarvest > 0)
        {
            var days = float.IsInfinity(r.DaysToHarvest) ? "over 2 years" : $"~{r.DaysToHarvest:0} days";
            sb.Append(Widgets.Colored($"Harvest in {days} · water factor {r.WaterFactor * 100:0}% · expected {r.ExpectedYieldPerHa:N0} L/ha", Palette.Dim) + "\n");
        }
        if (r.Crop == null && r.Soil != null && !WorldMap.IsSealed(r.Ground))
            sb.Append(Widgets.Colored(r.Soil.Description, Palette.Dim) + "\n");
        foreach (var warn in r.Warnings) sb.Append(Warning(warn) + "\n");
        _inspect.Text = sb.ToString().TrimEnd('\n');
    }

    private static string WeedName(byte weeds) => weeds switch
    {
        WeedState.Small => "Small weeds",
        WeedState.Grown => "Weeds",
        WeedState.Sprayed => "Sprayed against weeds",
        _ => "",
    };

    private static string GroundName(GroundType g) => g switch
    {
        GroundType.Grass => "Grass",
        GroundType.Cultivated => "Cultivated seedbed",
        GroundType.Seeded => "Sown",
        GroundType.Stubble => "Stubble",
        GroundType.Plowed => "Plowed",
        GroundType.Road => "Road",
        GroundType.Yard => "Gravel yard",
        GroundType.Dirt => "Dirt track",
        GroundType.Forest => "Forest floor",
        GroundType.Water => "Water",
        _ => g.ToString(),
    };
}

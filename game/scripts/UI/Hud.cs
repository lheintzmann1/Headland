using System.Text;
using Headland.Game.Common;
using Headland.Core.Components;
using Headland.Core;
using Headland.Core.Contracts;
using Headland.Core.Input;
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
    private MapView _minimap = null!;
    private PanelContainer _minimapPanel = null!;
    private string _minimapSize = "small";
    private RichTextLabel _prompt = null!;
    private VBoxContainer _notes = null!;
    private Label _debug = null!;
    private double _inspectTimer;

    public Simulation Sim { get; init; } = null!;

    /// <summary>Ground point under the mouse (set by the game), or null.</summary>
    public NVec2? Hover { get; set; }

    /// <summary>How far the camera is turned (set by the game): the minimap turns with it.</summary>
    public float CameraYaw { get; set; }

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

        // The minimap (FS: the in-game map), bottom left: the farmer in the middle, up the way the camera looks.
        _minimap = new MapView { Sim = Sim, Minimap = true, Off = [MapFilter.Farmland], Name = "Minimap" };
        _minimapPanel = Widgets.Anchor(Widgets.Panel(_minimap), Control.LayoutPreset.BottomLeft, new Vector2(12, -12));
        root.AddChild(_minimapPanel);
        MinimapSize = MinimapSize;

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

    /// <summary>The minimap's size: small, large or off (<see cref="Game.Common.UserSettings.MinimapSizes"/>).</summary>
    public string MinimapSize
    {
        get => _minimapSize;
        set
        {
            _minimapSize = value;
            if (_minimap == null) return;
            _minimapPanel.Visible = value != "off";
            _minimap.CustomMinimumSize = Vector2.One * (value == "large" ? 340 : 210);
            // Back in its corner at its new size.
            _minimapPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.Minsize, 12);
        }
    }

    // ------------------------------------------------------------------ Update

    public override void _Process(double delta)
    {
        UpdateClock();
        UpdateContracts();
        _minimap.Angle = CameraYaw;
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
            sb.Append($"{Widgets.Colored(c.Label, Palette.Contract)}  {ContractsPage.Progress(Sim, c)}  {due}\n");
        }
        _contracts.Text = sb.ToString().TrimEnd('\n');
    }

    /// <summary>The condition's icon (assets/icons is named after <see cref="WeatherCondition"/>).</summary>
    private static string Icon(WeatherCondition c) => Widgets.Icon(c.ToString().ToLowerInvariant(), Palette.Weather(c));

    private void UpdateVehicle()
    {
        var v = Sim.Player.Vehicle;
        _vehiclePanel.Visible = v != null;
        if (v == null) return;
        var sb = new StringBuilder();
        var leased = v.LeaseContract != 0 ? Widgets.Colored(" leased", Palette.Contract)
            : v.Lease is { } lease ? Widgets.Colored($" leased, ${lease.PerHour:N0}/h", Palette.Contract) : "";
        // What the tool keys act on is in the key color: the selected implement, or the vehicle for the whole chain.
        var selected = v.Get<Drivable>()?.Selected;
        string Named(Machine m) => m == selected || m == v && selected == null && v.Attached.Count > 0 ? Widgets.Colored(m.Def.Name, Palette.Key) : m.Def.Name;
        sb.Append($"[b]{Named(v)}[/b]{leased}   {Mathf.Abs(v.Speed) * 3.6f:0} km/h{(v.Speed < -0.05f ? " (R)" : "")}");
        if (v.Get<Motor>() is { Running: true } motor) sb.Append(Widgets.Colored($"   {motor.FuelPerHour:0.0} L/h", Palette.Dim));
        if (v.Get<RunningGear>() is { Slip: > 0.05f } gear) sb.Append("   " + Widgets.Colored($"slip {gear.Slip * 100f:0}%", gear.Slip > 0.15f ? Palette.Warning : Palette.Dim));
        if (v.Get<Thresher>() is { } thresher) sb.Append("   " + (thresher.On ? Widgets.Colored("threshing", Palette.Good) : Widgets.Colored("off", Palette.Dim)));
        if (v.Get<Lights>() is { } lights)
        {
            var lit = new List<string>();
            if (lights.On.Count > 0) lit.Add("lights");
            if (lights.Beacons) lit.Add("beacons");
            lit.Add(lights.Signal switch
            {
                TurnSignal.Left => "signal left",
                TurnSignal.Right => "signal right",
                TurnSignal.Hazards => "hazards",
                _ => "",
            });
            if (lit.Any(l => l.Length > 0)) sb.Append("   " + Widgets.Colored(string.Join(" · ", lit.Where(l => l.Length > 0)), Palette.Info));
        }
        sb.Append('\n');
        if (v.Get<Drivable>()?.Controller is FieldWorkController w)
            sb.Append($"  {Widgets.Colored($"Helper {w.Number} working {w.Field.Label}: lane {Math.Min(w.LanesDone + 1, w.Path.LaneCount)}/{w.Path.LaneCount} · ${w.Wages:N0} in wages", Palette.Info)}\n");
        foreach (var m in v.Chain())
        {
            if (m != v) sb.Append(m == selected ? $"  [b]{Named(m)}[/b]" : $"  {Named(m)}");
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
            if (m.Get<Wearable>() is { } wear)
                bits.Add(Widgets.Colored($"condition {wear.Condition * 100f:0}%", wear.Condition < Wearable.WornBelow ? Palette.Warning : Palette.Dim));
            if (m.Dirt >= 0.2f) bits.Add(Widgets.Colored($"dirt {m.Dirt * 100f:0}%", Palette.Dim));
            if (m.Get<Cover>() is { State: > 0 }) bits.Add("cover open");
            if (m.Get<RidgeMarker>()?.Down is { } marker) bits.Add($"marker {marker.Name}");
            if (m.Get<Tipper>() is { Tipping: true }) bits.Add(Widgets.Colored("tipping", Palette.Busy));
            if (Sim.Pois.LoadingFillType(m) is { } loading) bits.Add(Widgets.Colored($"loading {Sim.Content.FillTypes[loading].Name.ToLowerInvariant()}", Palette.Busy));
            if (m.Get<Pipe>() is { Out: true }) bits.Add(Widgets.Colored("pipe out", Palette.Busy));
            if (bits.Count > 0) sb.Append((m == v ? "  " : " — ") + string.Join(" · ", bits));
            if (m != v || bits.Count > 0) sb.Append('\n');
            foreach (var c in m.Conditions) sb.Append($"  {Widgets.Warning(c.Text)}\n");
        }
        _vehicle.Text = sb.ToString().TrimEnd('\n');
    }

    /// <summary>The keys that do something now, with what they do (<see cref="Simulation.Offers"/>).</summary>
    private void UpdatePrompt()
    {
        var hints = Sim.Offers().Offers.Where(o => o.Hinted).Select(o => o.Action == InputActions.Use ? UseHint() : $"{Widgets.Key(o.Action)} {o.Label}").ToList();
        _prompt.Text = string.Join("    ", hints);
        _prompt.GetParent<Control>().Visible = hints.Count > 0;
    }

    /// <summary>
    /// What the use key does: the nearest activation (or why it can't be used), then dimmed the others offered here,
    /// which it runs once nearer.
    /// </summary>
    private string UseHint()
    {
        var all = Sim.Activations();
        var first = all[0];
        var sb = new StringBuilder($"{Widgets.Key(InputActions.Use)} ");
        sb.Append(first.Usable ? first.Label : Widgets.Colored($"{first.Label}: {first.Blocked}", Palette.Dim));
        foreach (var other in all.Skip(1).Where(a => a.Usable)) sb.Append(Widgets.Colored($" · {other.Label}", Palette.Dim));
        return sb.ToString();
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
        var text = Hover is { } p ? CellText.Describe(Sim, p) : null;
        _inspectPanel.Visible = text != null;
        if (text != null) _inspect.Text = text;
    }
}

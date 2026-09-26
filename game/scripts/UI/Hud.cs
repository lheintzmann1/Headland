using System.Text;
using Headland.Game.Common;
using Headland.Core;
using Headland.Core.Machines;
using Headland.Core.Time;
using Headland.Core.Weather;
using Headland.Core.World;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.UI;

/// <summary>
/// Heads-up display: clock/weather/forecast, money, vehicle panel, DF-style cell inspector,
/// context key prompts, notifications, help and debug overlays. Built in code, styled muted.
/// </summary>
public partial class Hud : CanvasLayer
{
    private static readonly Color Text = new(0.9f, 0.9f, 0.86f);
    private static readonly Color Dim = new(0.66f, 0.68f, 0.64f);

    private RichTextLabel _clock = null!;
    private Label _money = null!;
    private RichTextLabel _vehicle = null!;
    private PanelContainer _vehiclePanel = null!;
    private RichTextLabel _inspect = null!;
    private PanelContainer _inspectPanel = null!;
    private RichTextLabel _prompt = null!;
    private VBoxContainer _notes = null!;
    private PanelContainer _help = null!;
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

        _clock = Rich(360);
        root.AddChild(Panel(_clock, Control.LayoutPreset.TopLeft, new Vector2(12, 12)));

        _money = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _money.AddThemeFontSizeOverride("font_size", 22);
        _money.AddThemeColorOverride("font_color", new Color(0.86f, 0.8f, 0.55f));
        root.AddChild(Panel(_money, Control.LayoutPreset.TopRight, new Vector2(-12, 12)));

        _vehicle = Rich(380);
        _vehiclePanel = Panel(_vehicle, Control.LayoutPreset.BottomRight, new Vector2(-12, -12));
        root.AddChild(_vehiclePanel);

        _inspect = Rich(360);
        _inspectPanel = Panel(_inspect, Control.LayoutPreset.TopRight, new Vector2(-12, 70));
        root.AddChild(_inspectPanel);

        _prompt = Rich(420);
        _prompt.AddThemeFontSizeOverride("normal_font_size", 16);
        var promptPanel = Panel(_prompt, Control.LayoutPreset.CenterBottom, new Vector2(0, -12));
        promptPanel.Name = "Prompt";
        root.AddChild(promptPanel);

        _notes = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _notes.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _notes.GrowHorizontal = Control.GrowDirection.Both;
        _notes.Position = new Vector2(-260, 14);
        _notes.CustomMinimumSize = new Vector2(520, 0);
        root.AddChild(_notes);

        var helpText = Rich(560);
        helpText.Text = HelpText();
        _help = Panel(helpText, Control.LayoutPreset.Center, Vector2.Zero);
        _help.Visible = false;
        root.AddChild(_help);

        _debug = new Label { Visible = false, Position = new Vector2(14, 150) };
        _debug.AddThemeColorOverride("font_color", new Color(0.7f, 0.95f, 0.7f));
        _debug.AddThemeFontSizeOverride("font_size", 13);
        root.AddChild(_debug);
    }

    public void ToggleHelp() => _help.Visible = !_help.Visible;

    // ------------------------------------------------------------------ Construction helpers

    private static RichTextLabel Rich(float width)
    {
        var r = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(width, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        r.AddThemeColorOverride("default_color", Text);
        r.AddThemeFontSizeOverride("normal_font_size", 15);
        r.AddThemeFontSizeOverride("bold_font_size", 15);
        return r;
    }

    private static PanelContainer Panel(Control content, Control.LayoutPreset anchor, Vector2 offset)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.06f, 0.78f),
            BorderColor = new Color(1f, 1f, 1f, 0.07f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", style);
        p.AddChild(content);
        p.SetAnchorsPreset(anchor);
        // Grow away from the anchored edge so the panel stays on screen as its content changes.
        p.GrowHorizontal = anchor is Control.LayoutPreset.TopRight or Control.LayoutPreset.BottomRight
            ? Control.GrowDirection.Begin
            : anchor is Control.LayoutPreset.CenterBottom or Control.LayoutPreset.Center or Control.LayoutPreset.CenterTop
                ? Control.GrowDirection.Both
                : Control.GrowDirection.End;
        p.GrowVertical = anchor is Control.LayoutPreset.BottomLeft or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterBottom
            ? Control.GrowDirection.Begin
            : anchor == Control.LayoutPreset.Center ? Control.GrowDirection.Both : Control.GrowDirection.End;
        p.Position += offset;
        return p;
    }

    // ------------------------------------------------------------------ Update

    public override void _Process(double delta)
    {
        UpdateClock();
        _money.Text = $"$ {Sim.Economy.Money:N0}";
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
        var speed = c.Paused ? "[color=#e0a060]paused[/color]" : $"×{c.TimeScale:0}";
        var sb = new StringBuilder();
        sb.Append($"[b]{c.Date}[/b]   {c.TimeString}   [color=#a8aba4]{speed} · {Calendar.SeasonOf(c.Month)}[/color]\n");
        sb.Append($"{Icon(w.Condition)} {w.Condition}  {w.Temperature:0}°C");
        if (w.Wind > 0.55f) sb.Append("  · windy");
        if (w.SnowCover > 0.05f) sb.Append($"  · snow {w.SnowCover * 100:0}%");
        sb.Append('\n');
        sb.Append("[color=#a8aba4]");
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

    private static string Icon(WeatherCondition c) => c switch
    {
        WeatherCondition.Clear => "[color=#e8cf6a]☀[/color]",
        WeatherCondition.Cloudy => "[color=#b8bcc0]☁[/color]",
        WeatherCondition.Rain => "[color=#7fa6d8]☂[/color]",
        WeatherCondition.Storm => "[color=#9aa8e8]⚡[/color]",
        WeatherCondition.Snow => "[color=#e8eef4]❄[/color]",
        _ => "[color=#b0b4b0]≋[/color]",
    };

    private void UpdateVehicle()
    {
        var v = Sim.Player.Vehicle;
        _vehiclePanel.Visible = v != null;
        if (v == null) return;
        var sb = new StringBuilder();
        sb.Append($"[b]{v.Def.Name}[/b]   {Mathf.Abs(v.Speed) * 3.6f:0} km/h{(v.Speed < -0.05f ? " (R)" : "")}");
        if (v.Def.HarvestTank != null) sb.Append(v.TurnedOn ? "   [color=#9fd67f]threshing[/color]" : "   [color=#a8aba4]off[/color]");
        sb.Append('\n');
        if (v.Controller is FieldWorkController w)
            sb.Append($"  [color=#8fc0e8]Helper working {w.Field.Label}: lane {Math.Min(w.LanesDone + 1, w.Path.LaneCount)}/{w.Path.LaneCount}[/color]\n");
        foreach (var m in v.Chain())
        {
            if (m != v) sb.Append($"  {m.Def.Name}");
            var bits = new List<string>();
            if (m != v && m.Def.WorkArea is { RequiresLowered: true }) bits.Add(m.Lowered ? "[color=#9fd67f]lowered[/color]" : "raised");
            if (m.Def.WorkArea is { RequiresOn: true }) bits.Add(m.TurnedOn ? "[color=#9fd67f]on[/color]" : "off");
            if (m.Def.SeedTank != null) bits.Add(Sim.Content.Crops[m.SelectedCrop].Name);
            foreach (var u in m.FillUnits)
            {
                var ft = u.FillType != null ? Sim.Content.FillTypes[u.FillType] : null;
                var unit = ft?.Unit ?? Sim.Content.FillTypes[u.Def.FillTypes[0]].Unit;
                bits.Add($"{ft?.Name ?? "empty"} {u.Level:N0}/{u.Capacity:N0} {unit}");
            }
            if (m.WorkedHa > 0.001f) bits.Add($"{m.WorkedHa:0.00} ha");
            if (m.Tipping) bits.Add("[color=#e8c060]tipping[/color]");
            if (m.PipeOut) bits.Add("[color=#e8c060]pipe out[/color]");
            if (bits.Count > 0) sb.Append((m == v ? "  " : " — ") + string.Join(" · ", bits));
            if (m != v || bits.Count > 0) sb.Append('\n');
            if (m.Status != null) sb.Append($"  [color=#e89a60]⚠ {m.Status}[/color]\n");
        }
        _vehicle.Text = sb.ToString().TrimEnd('\n');
    }

    private void UpdatePrompt()
    {
        var lines = new List<string>();
        string K(string action) => $"[color=#e8cf6a][{InputSetup.Label(action)}][/color]";
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
            var tools = v.Chain().Where(m => m != v && m.Def.WorkArea is { RequiresLowered: true }).ToList();
            if (tools.Count > 0) lines.Add($"{K("lower")} {(tools.Any(t => t.Lowered) ? "Raise" : "Lower")}");
            if (v.Chain().Any(m => m.Def.HarvestTank != null || m.Def.WorkArea is { RequiresOn: true }))
                lines.Add($"{K("turn_on")} Turn {(v.Chain().Any(m => m.TurnedOn) ? "off" : "on")}");
            if (v.Def.Pipe != null) lines.Add($"{K("unload")} {(v.PipeOut ? "Fold" : "Unfold")} pipe");
            foreach (var t in v.Chain().Where(m => m.Def.Tipper != null))
            {
                var sell = Sim.World.SellPointAt(t.Footprint.Center);
                if (sell != null) lines.Add($"{K("unload")} {(t.Tipping ? "Stop tipping" : $"Tip into {sell.Name}")}");
            }
            if (v.Chain().Any(m => m.Def.SeedTank != null)) lines.Add($"{K("cycle_seed")} Change seed");
            if (v.Chain().Any(m => Sim.World.ShopAt(m.Footprint.Center) != null && m.FillUnits.Length > 0))
                lines.Add($"{K("buy")} Buy supplies");
            if (v.Controller is FieldWorkController) lines.Add($"{K("helper")} Dismiss helper");
            else if (v.Chain().Any(m => m.Def.WorkArea != null) && Sim.FieldNear(v) is { } f) lines.Add($"{K("helper")} Hire helper for {f.Label}");
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
            var l = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            l.AddThemeFontSizeOverride("font_size", 16);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            l.AddThemeConstantOverride("outline_size", 6);
            _notes.AddChild(l);
        }
        for (var i = 0; i < items.Count; i++)
        {
            var n = items[i];
            var l = (Label)_notes.GetChild(i);
            l.Text = n.Text;
            l.AddThemeColorOverride("font_color", n.Severity switch
            {
                Severity.Good => new Color(0.7f, 0.9f, 0.6f),
                Severity.Warning => new Color(0.95f, 0.7f, 0.45f),
                _ => Text,
            });
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
        sb.Append(field != null ? $"[b]{field.Label}[/b] ({field.AreaHa:0.00} ha)" : "[b]Open ground[/b]");
        sb.Append($"   [color=#a8aba4]{r.Position.X:0}, {r.Position.Y:0} · {r.Height:0.0} m[/color]\n");
        sb.Append($"{GroundName(r.Ground)} on [b]{r.Soil?.Name}[/b]");
        if (r.Crop != null)
        {
            var ripe = r.Crop.Stages.ElementAtOrDefault(r.Stage)?.Harvestable == true;
            sb.Append($" · [b]{r.Crop.Name}[/b] — {r.StageName}{(ripe ? " [color=#9fd67f](ready to harvest)[/color]" : "")}");
        }
        sb.Append('\n');
        if (!WorldMap.IsSealed(r.Ground))
        {
            sb.Append($"Moisture [b]{r.Moisture * 100:0}%[/b]   Nitrogen [b]{r.Nitrogen:0}[/b] kg/ha");
            if (r.Crop != null && r.StageName != "Dead") sb.Append($"   Health [b]{r.Health * 100:0}%[/b]");
            sb.Append('\n');
        }
        if (r.Crop is { VernalizationDays: > 0 } vc && r.StageName != "Dead" && r.Stage < Array.FindIndex(vc.Stages, s => s.RequiresVernalization))
            sb.Append(r.Chill >= vc.VernalizationDays
                ? "[color=#8fc0e8]Vernalized: ready to shoot in spring[/color]\n"
                : $"[color=#8fc0e8]Vernalization {r.Chill:0}/{vc.VernalizationDays:0} cold days[/color]\n");
        if (r.Crop != null && !float.IsNaN(r.DaysToHarvest) && r.DaysToHarvest > 0)
        {
            var days = float.IsInfinity(r.DaysToHarvest) ? "over 2 years" : $"~{r.DaysToHarvest:0} days";
            sb.Append($"[color=#a8aba4]Harvest in {days} · water factor {r.WaterFactor * 100:0}% · expected {r.Crop.YieldPerHa * r.Health:N0} L/ha[/color]\n");
        }
        if (r.Crop == null && r.Soil != null && !WorldMap.IsSealed(r.Ground))
            sb.Append($"[color=#a8aba4]{r.Soil.Description}[/color]\n");
        foreach (var warn in r.Warnings) sb.Append($"[color=#e89a60]⚠ {warn}[/color]\n");
        _inspect.Text = sb.ToString().TrimEnd('\n');
    }

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

    private static string HelpText()
    {
        var sb = new StringBuilder("[b]Controls[/b]   [color=#a8aba4](keys follow your keyboard layout)[/color]\n\n");
        foreach (var (action, _, help) in InputSetup.Bindings)
            if (!action.StartsWith("time_") || action == "time_1")
                sb.Append($"[color=#e8cf6a]{(action == "time_1" ? "1–6" : InputSetup.Label(action)),-8}[/color] {(action == "time_1" ? "Time speed ×1 … ×240" : help)}\n");
        sb.Append("[color=#e8cf6a]Wheel[/color]    Zoom        [color=#e8cf6a]Middle drag[/color]  Pan\n\n");
        sb.Append("[b]Loop[/b]: cultivate stubble → sow in season → let it grow → harvest with the right header →\n");
        sb.Append("unload into a trailer → tip at the Grain Elevator. Hover the ground to inspect soil and crops.");
        return sb.ToString();
    }
}

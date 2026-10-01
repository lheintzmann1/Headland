using System.Text;
using Headland.Core;
using Headland.Core.Input;
using Headland.Core.Time;
using Headland.Core.Weather;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.UI;

/// <summary>
/// Heads-up display, laid out as Farming Simulator's: the keys that do something now down the top left (with the debug
/// overlay under them), the date, weather and money top right with the forecast, the contracts under way and the cell
/// inspector under them, notifications top center, the minimap bottom left and the vehicle panel bottom right (its
/// gauges and states from the vehicle's components). Built from <see cref="Widgets"/>; styled by the theme.
/// </summary>
public partial class Hud : CanvasLayer
{
    private RichTextLabel _clock = null!;
    private RichTextLabel _forecast = null!;
    private RichTextLabel _contracts = null!;
    private PanelContainer _contractsPanel = null!;
    private Label _money = null!;
    private VehiclePanel _vehicle = null!;
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

        // Top left: the keys that do something now (FS: the input help), the debug overlay under them.
        var left = Widgets.Anchor(new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }, Control.LayoutPreset.TopLeft, new Vector2(12, 12));
        root.AddChild(left);
        _prompt = Widgets.Rich(300, "PromptText");
        var promptPanel = Widgets.Panel(_prompt);
        promptPanel.Name = "Prompt";
        left.AddChild(promptPanel);
        _debug = Widgets.Label(variation: "DebugLabel");
        _debug.Visible = false;
        left.AddChild(_debug);

        // Top right: the date, the weather and the money (FS: the game info), the forecast; the contracts under way and
        // the inspector under them.
        var right = Widgets.Anchor(new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }, Control.LayoutPreset.TopRight, new Vector2(-12, 12));
        right.CustomMinimumSize = new Vector2(380, 0);
        root.AddChild(right);
        var info = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _clock = Widgets.Rich(0);
        _clock.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(_clock);
        _money = Widgets.Label(variation: "MoneyLabel");
        _money.HorizontalAlignment = HorizontalAlignment.Right;
        _money.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(_money);
        info.AddChild(top);
        _forecast = Widgets.Rich(0);
        info.AddChild(_forecast);
        right.AddChild(Widgets.Panel(info));
        _contracts = Widgets.Rich(0);
        _contractsPanel = Widgets.Panel(_contracts);
        right.AddChild(_contractsPanel);
        _inspect = Widgets.Rich(0);
        _inspectPanel = Widgets.Panel(_inspect);
        right.AddChild(_inspectPanel);

        // Bottom right: the vehicle's gauges and states (FS: the speed meter and fill levels).
        _vehicle = new VehiclePanel { Sim = Sim, Name = "Vehicle" };
        _vehiclePanel = Widgets.Anchor(Widgets.Panel(_vehicle), Control.LayoutPreset.BottomRight, new Vector2(-12, -12));
        root.AddChild(_vehiclePanel);

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
        _clock.Text = sb.ToString();
        sb.Clear();
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
        _forecast.Text = sb.ToString().TrimEnd();
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
        if (v != null) _vehicle.Refresh(v);
    }

    /// <summary>The keys that do something now, with what they do (<see cref="Simulation.Offers"/>).</summary>
    private void UpdatePrompt()
    {
        var hints = Sim.Offers().Offers.Where(o => o.Hinted).Select(o => o.Action == InputActions.Use ? UseHint() : $"{Widgets.Key(o.Action)} {o.Label}").ToList();
        _prompt.Text = string.Join("\n", hints);
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

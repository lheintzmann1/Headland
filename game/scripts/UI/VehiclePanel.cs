using System.Text;
using Headland.Core;
using Headland.Core.Components;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The HUD's vehicle panel (FS: the speed meter and fill levels): for the vehicle and each machine of its chain, its
/// name and what its components report (<see cref="Machine.Readouts"/>): gauges as bars (speed, engine load, fuel, fill
/// levels, condition, dirt) and states in a line (lowered, threshing, pipe out…), then what keeps it from working. The
/// rows are built anew when the chain or its gauges change, and updated in place otherwise.
/// </summary>
public partial class VehiclePanel : VBoxContainer
{
    /// <summary>The icon of each kind of gauge (assets/icons).</summary>
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["speed"] = "speed", ["load"] = "manufacturing", ["fuel"] = "local_gas_station", ["fill"] = "inventory_2",
        ["bales"] = "grass", ["condition"] = "build", ["dirt"] = "water_drop",
    };

    private readonly List<Row> _rows = [];
    private string _built = "";

    public Simulation Sim { get; init; } = null!;

    /// <summary>A machine's block: its header (name, speed, states), gauges and conditions.</summary>
    private sealed record Row(Machine Machine, RichTextLabel Header, Label? Speed, List<(Label label, ProgressBar bar, Label value)> Gauges, RichTextLabel Notes);

    public override void _Ready()
    {
        ThemeTypeVariation = "DialogBox";
        CustomMinimumSize = new Vector2(380, 0);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Shows <paramref name="v"/>'s chain, building the rows again when what they show changed.</summary>
    public void Refresh(Machine v)
    {
        var chain = v.Chain().Select(m => (m, readouts: m.Readouts(Sim).ToList())).ToList();
        var shape = string.Join("|", chain.Select(c => $"{c.m.Id}:{string.Join(",", Gauges(c.readouts).Select(g => g.Kind))}"));
        if (shape != _built) Build(chain, shape);
        var selected = v.Get<Drivable>()?.Selected;
        for (var k = 0; k < chain.Count; k++) Update(_rows[k], chain[k].readouts, v, selected);
    }

    /// <summary>The gauges, in the order the panel shows them (the speed first).</summary>
    private static IEnumerable<Gauge> Gauges(IEnumerable<Readout> readouts) =>
        readouts.OfType<Gauge>().OrderBy(g => Array.IndexOf(Gauge.Kinds, g.Kind) is var i and >= 0 ? i : Gauge.Kinds.Length);

    private void Build(List<(Machine m, List<Readout> readouts)> chain, string shape)
    {
        _built = shape;
        _rows.Clear();
        foreach (var child in GetChildren()) child.Free();
        foreach (var (m, readouts) in chain)
        {
            if (_rows.Count > 0) AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Ignore });
            var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            var header = Widgets.Rich(0);
            header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            header.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            top.AddChild(header);
            Label? speed = null;
            if (readouts.OfType<Gauge>().Any(g => g.Kind == "speed"))
            {
                speed = Widgets.Label(variation: "SpeedLabel");
                speed.SizeFlagsVertical = SizeFlags.ShrinkBegin;
                top.AddChild(speed);
            }
            AddChild(top);
            var table = new GridContainer { Columns = 4, ThemeTypeVariation = "GaugeTable", MouseFilter = MouseFilterEnum.Ignore };
            var gauges = new List<(Label, ProgressBar, Label)>();
            foreach (var g in Gauges(readouts))
            {
                var icon = Widgets.Rich(0);
                icon.Text = Widgets.Icon(Icons.GetValueOrDefault(g.Kind, "inventory_2"), Palette.Dim, 16);
                icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                table.AddChild(icon);
                var label = Widgets.Label(variation: "DimLabel");
                label.CustomMinimumSize = new Vector2(72, 0);
                table.AddChild(label);
                var bar = new ProgressBar
                {
                    ShowPercentage = false, MaxValue = 1, Step = 0, CustomMinimumSize = new Vector2(150, 0), MouseFilter = MouseFilterEnum.Ignore,
                };
                bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                table.AddChild(bar);
                var value = Widgets.Label();
                value.HorizontalAlignment = HorizontalAlignment.Right;
                value.CustomMinimumSize = new Vector2(110, 0);
                table.AddChild(value);
                gauges.Add((label, bar, value));
            }
            if (gauges.Count > 0) AddChild(table);
            var notes = Widgets.Rich(0);
            notes.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            AddChild(notes);
            _rows.Add(new Row(m, header, speed, gauges, notes));
        }
    }

    private void Update(Row row, List<Readout> readouts, Machine v, Machine? selected)
    {
        var m = row.Machine;
        // What the tool keys act on is in the key color: the selected implement, or the vehicle for the whole chain.
        var acting = m == selected || m == v && selected == null && v.Attached.Count > 0;
        var name = acting ? Widgets.Colored(m.Def.Name, Palette.Key) : m.Def.Name;
        var states = readouts.OfType<Status>().Select(s => Palette.Of(s.Tone) is { } color ? Widgets.Colored(s.Text, color) : s.Text);
        var header = new StringBuilder($"[b]{name}[/b]");
        foreach (var s in states) header.Append($"   {s}");
        row.Header.Text = header.ToString();
        var gauges = Gauges(readouts).ToList();
        for (var k = 0; k < row.Gauges.Count && k < gauges.Count; k++)
        {
            var (label, bar, value) = row.Gauges[k];
            var g = gauges[k];
            label.Text = g.Label;
            bar.Value = Math.Clamp(g.Fraction, 0f, 1f);
            StringName variation = g.Tone switch { Tone.Warning => "GaugeWarning", Tone.Dim => "GaugeDim", _ => "Gauge" };
            if (bar.ThemeTypeVariation != variation) bar.ThemeTypeVariation = variation;
            value.Text = g.Value;
            if (g.Kind == "speed" && row.Speed != null) row.Speed.Text = g.Value;
        }
        var notes = string.Join("\n", m.Conditions.Select(c => Widgets.Warning(c.Text)));
        row.Notes.Text = notes;
        row.Notes.Visible = notes.Length > 0;
    }
}

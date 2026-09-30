using Headland.Core;
using Headland.Core.World;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's map (FS: the map page): the <see cref="MapView"/>, and beside it the layer it shows with its legend, what's
/// switched on, and what's under the mouse. The layer and the switches stay as they were left.
/// </summary>
public partial class MapPage : MenuPage
{
    private static MapLayer _layer;
    private static readonly HashSet<MapFilter> SwitchedOff = [];

    private static readonly (MapLayer layer, string name)[] Layers =
    [
        (MapLayer.Terrain, "Overview"), (MapLayer.Crops, "Crops"), (MapLayer.Growth, "Growth"), (MapLayer.Soil, "Soil"),
        (MapLayer.Moisture, "Moisture"),
    ];

    private static readonly (MapFilter filter, string name)[] Filters =
    [
        (MapFilter.Fields, "Fields"), (MapFilter.Farmland, "Farmland"), (MapFilter.Contracts, "Contracts"),
        (MapFilter.Farm, "Your farm"), (MapFilter.Selling, "Selling points"), (MapFilter.Services, "Shops and services"),
        (MapFilter.Vehicles, "Vehicles"),
    ];

    private MapView _map = null!;
    private VBoxContainer _legend = null!;
    private RichTextLabel _here = null!;
    private double _hereTimer;

    public Simulation Sim { get; init; } = null!;

    public override string Subtitle => "Click to set a waypoint, right click to take it away. The wheel zooms, dragging moves the map.";

    protected override void Build()
    {
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _map = new MapView { Sim = Sim, Off = SwitchedOff, Layer = _layer, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddChild(_map);

        var side = new VBoxContainer { ThemeTypeVariation = "DialogBox", CustomMinimumSize = new Vector2(300, 0) };
        side.AddChild(Widgets.Label("Show", "DimLabel"));
        var layers = new HFlowContainer();
        var group = new ButtonGroup();
        foreach (var (layer, name) in Layers)
            layers.AddChild(Widgets.Tab(name, group, layer == _layer, () => ShowLayer(layer)));
        side.AddChild(layers);
        _legend = new VBoxContainer();
        side.AddChild(_legend);

        side.AddChild(Widgets.Label("On the map", "DimLabel"));
        foreach (var (filter, name) in Filters)
        {
            var box = new CheckBox { Text = name, ButtonPressed = !SwitchedOff.Contains(filter) };
            box.Toggled += on =>
            {
                if (on) SwitchedOff.Remove(filter);
                else SwitchedOff.Add(filter);
            };
            side.AddChild(box);
        }

        _here = Widgets.Rich(300);
        _here.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        side.AddChild(_here);
        row.AddChild(side);
        AddChild(row);
        ShowLegend();
    }

    /// <summary>Shows <paramref name="layer"/> on the map, and its legend.</summary>
    public void ShowLayer(MapLayer layer)
    {
        _layer = layer;
        _map.Layer = layer;
        ShowLegend();
    }

    /// <summary>Each color of the layer and what it stands for.</summary>
    private void ShowLegend()
    {
        foreach (var child in _legend.GetChildren()) child.QueueFree();
        foreach (var entry in MapLayers.Legend(Sim.Content, _layer))
        {
            var line = new HBoxContainer();
            var swatch = new ColorRect { Color = new Color(entry.Rgb << 8 | 0xff), CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            line.AddChild(swatch);
            line.AddChild(Widgets.Label(entry.Name));
            _legend.AddChild(line);
        }
    }

    public override void _Process(double delta)
    {
        _hereTimer -= delta;
        if (_hereTimer > 0) return;
        _hereTimer = 0.12;
        _here.Text = _map.Hover is { } p ? CellText.Describe(Sim, p) ?? "" : "";
    }
}

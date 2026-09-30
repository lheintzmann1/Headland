using Headland.Core;
using Headland.Core.World;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's map (FS: the map page): the <see cref="MapView"/>, and beside it the layer it shows with its legend, what's
/// switched on, and what's under the mouse. On the farmland layer, a click picks a parcel, which the side shows with its
/// area, fields and price, to buy from its neighbor or sell back (FS). The layer and the switches stay as they were left.
/// </summary>
public partial class MapPage : MenuPage
{
    private static MapLayer _layer;
    private static readonly HashSet<MapFilter> SwitchedOff = [];

    private static readonly (MapLayer layer, string name)[] Layers =
    [
        (MapLayer.Terrain, "Overview"), (MapLayer.Crops, "Crops"), (MapLayer.Growth, "Growth"), (MapLayer.Soil, "Soil"),
        (MapLayer.Moisture, "Moisture"), (MapLayer.Farmland, "Farmland"),
    ];

    private static readonly (MapFilter filter, string name)[] Filters =
    [
        (MapFilter.Fields, "Fields"), (MapFilter.Farmland, "Farmland"), (MapFilter.Contracts, "Contracts"),
        (MapFilter.Farm, "Your farm"), (MapFilter.Selling, "Selling points"), (MapFilter.Services, "Shops and services"),
        (MapFilter.Vehicles, "Vehicles"),
    ];

    private MapView _map = null!;
    private VBoxContainer _legend = null!;
    private Label _hint = null!;
    private VBoxContainer _parcel = null!;
    private RichTextLabel _parcelText = null!;
    private Button _deal = null!;
    private Label _why = null!;
    private RichTextLabel _here = null!;
    private double _hereTimer;
    private Farmland? _picked;

    public Simulation Sim { get; init; } = null!;

    public override string Subtitle => "The wheel zooms, dragging moves the map.";

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
        _hint = Widgets.Label(variation: "DimLabel");
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        side.AddChild(_hint);

        // The parcel picked on the farmland layer, and its deal.
        _parcel = new VBoxContainer();
        _parcelText = Widgets.Rich(300);
        _parcelText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _parcel.AddChild(_parcelText);
        _deal = Widgets.Button("", Deal);
        _deal.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _parcel.AddChild(_deal);
        _why = Widgets.Label(variation: "DimLabel");
        _why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _parcel.AddChild(_why);
        side.AddChild(_parcel);

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
        ShowLayer(_layer);
    }

    /// <summary>Shows <paramref name="layer"/> on the map, and its legend.</summary>
    public void ShowLayer(MapLayer layer)
    {
        _layer = layer;
        _map.Layer = layer;
        var farmland = layer == MapLayer.Farmland;
        _map.Picked = farmland ? Pick : null;
        _parcel.Visible = farmland;
        _hint.Text = farmland
            ? "Click a parcel to see what it holds and what it costs, and buy it or sell it back."
            : "Click to set a waypoint, right click to take it away.";
        ShowLegend();
        ShowParcel();
    }

    /// <summary>Picks <paramref name="land"/> (none off the parcels), to see it and deal for it.</summary>
    public void Pick(Farmland? land)
    {
        _picked = land;
        _map.Selected = land;
        ShowParcel();
    }

    /// <summary>Buys the parcel picked from its neighbor, or sells the farm's back.</summary>
    public void Deal()
    {
        if (_picked is not { } land) return;
        if (land.FarmId == Sim.Farms.Player.Id) Sim.Farms.Sell(land);
        else Sim.Farms.Buy(land);
        _map.Repaint();
        ShowParcel();
    }

    /// <summary>The parcel picked: its area, fields, owner and price, and whether the farm can buy or sell it now.</summary>
    private void ShowParcel()
    {
        if (_picked is not { } land)
        {
            _parcelText.Text = "";
            _deal.Visible = false;
            _why.Text = "";
            return;
        }
        var yours = land.FarmId == Sim.Farms.Player.Id;
        var fields = land.Fields.Count > 0
            ? string.Join(", ", land.Fields.OrderBy(f => f.Id).Select(f => $"{f.Id} ({f.AreaHa:0.00} ha)"))
            : "none";
        _parcelText.Text = $"[b]{land.Label}[/b]\n" +
                           $"Area {land.AreaHa:0.00} ha · fields {fields}\n" +
                           $"{(yours ? "Yours" : $"{Sim.Farms.OwnerName(land)}'s")} · $ {Sim.Farms.Price(land):N0}";
        var blocker = yours ? Sim.Farms.SellBlocker(land) : Sim.Farms.BuyBlocker(land);
        _deal.Visible = true;
        _deal.Text = yours ? $"Sell for $ {Sim.Farms.Price(land):N0}" : $"Buy for $ {Sim.Farms.Price(land):N0}";
        _deal.Disabled = blocker != null;
        _why.Text = blocker ?? "";
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
        // The money changes as the game runs on: the deal follows.
        if (_picked != null) ShowParcel();
    }
}

using Headland.Game.Common;
using Headland.Core;
using Headland.Core.Components;
using Headland.Core.Contracts;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Pois;
using Headland.Core.Pois.Components;
using Headland.Core.World;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.UI;

/// <summary>What the map can show over its picture, each switched on and off in the map's list.</summary>
public enum MapFilter { Fields, Farmland, Contracts, Farm, Selling, Services, Vehicles }

/// <summary>
/// The map (FS: the in-game map): the world from above in a layer's colors (<see cref="MapLayers"/>), with the fields and
/// their numbers, the farmland (the farm's own shaded), the contracts' fields, the POIs' icons, the machines, the farmer
/// and the waypoint. The wheel zooms at the mouse, dragging moves the map, a click sets the waypoint and a right click
/// takes it away.
/// </summary>
public partial class MapView : Control
{
    /// <summary>Seconds between redrawing the picture: crops grow and machines work the fields while it's open.</summary>
    private const double RefreshSeconds = 2.0;
    /// <summary>Most pixels per meter, zoomed in.</summary>
    private const float MaxZoom = 12f;
    /// <summary>Pixels the mouse moves, a button down, before it drags the map rather than clicks.</summary>
    private const float DragPixels = 5f;

    private static readonly Color Backdrop = new("#1c1f1a");
    private static readonly Color Outline = new(0f, 0f, 0f, 0.8f);
    private static readonly Color Parcel = new(1f, 1f, 1f, 0.28f);
    private static readonly Color FieldEdge = new(1f, 1f, 1f, 0.55f);
    private static readonly Color Own = new(Palette.Key) { A = 0.18f };
    private static readonly Color OwnEdge = new(Palette.Key) { A = 0.7f };
    private static readonly Color Machines = new("#dcdcd2");
    private static readonly Color Others = new("#98a0a8");

    private readonly Dictionary<string, Texture2D?> _icons = new();
    private Image? _image;
    private ImageTexture? _picture;
    private byte[] _buffer = [];
    private int _step = 1;
    private double _refresh;
    private MapLayer _layer;
    /// <summary>Pixels per meter; 0 until it's first drawn, when the whole map fits.</summary>
    private float _zoom;
    private NVec2 _center;
    private Vector2? _pressedAt;
    private bool _dragging;

    public Simulation Sim { get; init; } = null!;

    /// <summary>What's switched off.</summary>
    public HashSet<MapFilter> Off { get; init; } = [];

    /// <summary>The ground point under the mouse, if it's over the map.</summary>
    public NVec2? Hover { get; private set; }

    public MapLayer Layer
    {
        get => _layer;
        set
        {
            _layer = value;
            _refresh = 0;
        }
    }

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        var w = Sim.World;
        _step = Math.Max(1, w.CellsX / 1024);
        _buffer = new byte[w.CellsX / _step * (w.CellsZ / _step) * 4];
        _center = new NVec2(w.Size * 0.5f);
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = RefreshSeconds;
            Paint();
        }
        QueueRedraw();
    }

    /// <summary>The picture, in the layer's colors as the world is now.</summary>
    private void Paint()
    {
        var w = Sim.World;
        MapLayers.Fill(w, Sim.Content, _layer, _step, _buffer);
        int width = w.CellsX / _step, height = w.CellsZ / _step;
        if (_image == null)
        {
            _image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, _buffer);
            _picture = ImageTexture.CreateFromImage(_image);
        }
        else
        {
            _image.SetData(width, height, false, Image.Format.Rgba8, _buffer);
            _picture!.Update(_image);
        }
    }

    // ------------------------------------------------------------------ View

    private Vector2 ToScreen(NVec2 p) => new Vector2(p.X - _center.X, p.Y - _center.Y) * _zoom + Size * 0.5f;

    private NVec2 ToWorld(Vector2 s)
    {
        var d = (s - Size * 0.5f) / _zoom;
        return new NVec2(_center.X + d.X, _center.Y + d.Y);
    }

    private float FitZoom => MathF.Min(Size.X, Size.Y) / Sim.World.Size;

    private void ZoomAt(Vector2 at, float factor)
    {
        var before = ToWorld(at);
        _zoom = Math.Clamp(_zoom * factor, FitZoom * 0.8f, MaxZoom);
        var after = ToWorld(at);
        _center += before - after;
        ClampCenter();
    }

    private void ClampCenter() => _center = NVec2.Clamp(_center, NVec2.Zero, new NVec2(Sim.World.Size));

    public override void _GuiInput(InputEvent e)
    {
        if (_zoom <= 0f) return;
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } wheel:
                ZoomAt(wheel.Position, 1.15f);
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } wheel:
                ZoomAt(wheel.Position, 1f / 1.15f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } button:
                if (button.Pressed)
                {
                    _pressedAt = button.Position;
                    _dragging = false;
                }
                else
                {
                    // A click, not a drag: the waypoint goes there.
                    if (!_dragging && button.ButtonIndex == MouseButton.Left && OnMap(ToWorld(button.Position)) is { } spot)
                        Sim.Player.Waypoint = spot;
                    _pressedAt = null;
                }
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                Sim.Player.Waypoint = null;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                if (_pressedAt is { } from && (_dragging || motion.Position.DistanceTo(from) > DragPixels))
                {
                    _dragging = true;
                    _center -= new NVec2(motion.Relative.X, motion.Relative.Y) / _zoom;
                    ClampCenter();
                }
                Hover = OnMap(ToWorld(motion.Position));
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) Hover = null;
    }

    private NVec2? OnMap(NVec2 p) => p.X >= 0f && p.Y >= 0f && p.X < Sim.World.Size && p.Y < Sim.World.Size ? p : null;

    // ------------------------------------------------------------------ Drawing

    public override void _Draw()
    {
        if (_zoom <= 0f && Size.X > 0f) _zoom = FitZoom;
        DrawRect(new Rect2(Vector2.Zero, Size), Backdrop);
        if (_picture == null || _zoom <= 0f) return;
        // Cells stay sharp zoomed in, the picture smooth zoomed out.
        TextureFilter = _zoom * WorldMap.CellSize * _step > 1.5f ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
        DrawTextureRect(_picture, new Rect2(ToScreen(NVec2.Zero), Vector2.One * (Sim.World.Size * _zoom)), false);

        if (!Off.Contains(MapFilter.Farmland)) DrawFarmland();
        if (!Off.Contains(MapFilter.Fields)) DrawFields();
        if (!Off.Contains(MapFilter.Contracts)) DrawContracts();
        DrawPois();
        if (!Off.Contains(MapFilter.Vehicles)) DrawMachines();
        DrawWaypoint();
        DrawFarmer();
        DrawScale();
    }

    private Vector2[] Screen(IReadOnlyList<NVec2> points) => points.Select(ToScreen).ToArray();

    private void DrawClosed(Vector2[] points, Color color, float width)
    {
        DrawPolyline([.. points, points[0]], color, width, true);
    }

    private void DrawFarmland()
    {
        foreach (var land in Sim.World.Farmlands)
        {
            var points = Screen(land.Shape.Points);
            var own = land.FarmId == Sim.Player.FarmId;
            if (own) DrawColoredPolygon(points, Own);
            DrawClosed(points, own ? OwnEdge : Parcel, own ? 1.5f : 1f);
        }
    }

    private void DrawFields()
    {
        var font = GetThemeDefaultFont();
        var size = GetThemeDefaultFontSize();
        foreach (var field in Sim.World.Fields)
        {
            DrawClosed(Screen(field.Shape.Points), FieldEdge, 1f);
            Text(font, ToScreen(field.Center), field.Id.ToString(), size, Colors.White);
        }
    }

    /// <summary>The farm's contracts under way outlined in the contract color, the offers dashed.</summary>
    private void DrawContracts()
    {
        var color = new Color(Palette.Contract);
        foreach (var c in Sim.Contracts.All)
        {
            if (c.Field is not { } field) continue;
            var points = Screen(field.Shape.Points);
            if (c.State == ContractState.Active && c.FarmId == Sim.Player.FarmId) DrawClosed(points, color, 2.5f);
            else if (c.State == ContractState.Offered)
                for (var i = 0; i < points.Length; i++)
                    DrawDashedLine(points[i], points[(i + 1) % points.Length], color with { A = 0.7f }, 1.5f, 6f);
        }
    }

    private void DrawPois()
    {
        var font = GetThemeDefaultFont();
        var size = GetThemeDefaultFontSize() - 3;
        foreach (var poi in Sim.World.Pois)
        {
            if (poi.Get<Hotspots>() is not { } hotspots || Off.Contains(Category(poi))) continue;
            foreach (var spot in hotspots.Spots)
            {
                var at = ToScreen(spot.Position);
                DrawCircle(at, 13f, Outline);
                if (Icon(spot.Icon) is { } icon) DrawTextureRect(icon, new Rect2(at - new Vector2(9f, 9f), new Vector2(18f, 18f)), false);
                Text(font, at + new Vector2(0f, 26f), spot.Name, size, new Color(Palette.Dim).Lightened(0.3f));
            }
        }
    }

    /// <summary>Which filter a POI is under: the farm's own, a place that buys (a production point too), or a service.</summary>
    private MapFilter Category(Poi poi) =>
        poi.FarmId == Sim.Player.FarmId ? MapFilter.Farm
        : poi.Has<SellingStation>() || poi.Has<ProductionPoint>() ? MapFilter.Selling
        : MapFilter.Services;

    /// <summary>Every machine to scale, the one driven and what it pulls in the key color; a dot where it's too small.</summary>
    private void DrawMachines()
    {
        var driven = Sim.Player.Vehicle?.Chain().ToHashSet() ?? [];
        foreach (var m in Sim.Machines.All)
        {
            var color = driven.Contains(m) ? new Color(Palette.Key) : m.FarmId == Sim.Player.FarmId ? Machines : Others;
            var box = m.Footprint;
            if (box.HalfExtents.Y * 2f * _zoom < 5f)
            {
                if (m.Has<Drivable>() || driven.Contains(m)) DrawCircle(ToScreen(box.Center), 3f, color);
                continue;
            }
            var (x, y) = (box.AxisX * box.HalfExtents.X, box.AxisY * box.HalfExtents.Y);
            Vector2[] corners = [ToScreen(box.Center + x + y), ToScreen(box.Center - x + y), ToScreen(box.Center - x - y), ToScreen(box.Center + x - y)];
            DrawColoredPolygon(corners, color);
            DrawClosed(corners, Outline, 1f);
        }
    }

    /// <summary>The farmer (their vehicle's heading when driving), as an arrow in the key color.</summary>
    private void DrawFarmer()
    {
        var p = Sim.Player;
        var heading = p.Vehicle?.Heading ?? p.Heading;
        // The arrow points up (north); heading θ faces (sin θ, cos θ), south being down.
        Glyph("navigation", ToScreen(p.Position), 22f, new Color(Palette.Key), MathF.PI - heading);
    }

    private void DrawWaypoint()
    {
        if (Sim.Player.Waypoint is not { } w) return;
        var color = new Color(Palette.Waypoint);
        DrawDashedLine(ToScreen(Sim.Player.Position), ToScreen(w), color with { A = 0.6f }, 1.5f, 8f);
        Glyph("flag", ToScreen(w) - new Vector2(0f, 10f), 24f, color, 0f);
    }

    /// <summary>A bar a round number of meters long, bottom left.</summary>
    private void DrawScale()
    {
        var meters = new[] { 10f, 20f, 50f, 100f, 200f, 500f, 1000f, 2000f }.FirstOrDefault(m => m * _zoom >= 70f, 5000f);
        var from = new Vector2(16f, Size.Y - 18f);
        var to = from + new Vector2(meters * _zoom, 0f);
        DrawLine(from, to, Outline, 5f);
        DrawLine(from, to, Colors.White, 2f);
        Text(GetThemeDefaultFont(), (from + to) * 0.5f - new Vector2(0f, 10f), meters >= 1000f ? $"{meters / 1000f:0.#} km" : $"{meters:0} m",
            GetThemeDefaultFontSize() - 3, Colors.White);
    }

    /// <summary>An icon centered on <paramref name="at"/>, turned by <paramref name="angle"/>, with a dark outline.</summary>
    private void Glyph(string name, Vector2 at, float size, Color color, float angle)
    {
        if (Icon(name) is not { } icon) return;
        DrawSetTransform(at, angle);
        var outline = new Rect2(-Vector2.One * (size * 0.5f + 1.5f), Vector2.One * (size + 3f));
        DrawTextureRect(icon, outline, false, Outline);
        DrawTextureRect(icon, new Rect2(-Vector2.One * (size * 0.5f), Vector2.One * size), false, color);
        DrawSetTransform(Vector2.Zero);
    }

    /// <summary>Text centered on <paramref name="at"/>, outlined dark to read over any ground.</summary>
    private void Text(Font font, Vector2 at, string text, int size, Color color)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var pos = at + new Vector2(-width * 0.5f, size * 0.35f);
        DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, size, 4, Outline);
        DrawString(font, pos, text, HorizontalAlignment.Left, -1, size, color);
    }

    private Texture2D? Icon(string name)
    {
        if (_icons.TryGetValue(name, out var icon)) return icon;
        var path = $"res://assets/icons/{name}.svg";
        return _icons[name] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}

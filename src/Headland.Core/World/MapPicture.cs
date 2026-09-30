using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Ownership;

namespace Headland.Core.World;

/// <summary>
/// The map's picture of the world, a pixel per cell or per few cells (<see cref="Rgba"/>, rows from north to south): the
/// ground in its colors, lit from the north-west by its slopes, and a layer's colors over the fields
/// (<see cref="MapLayers"/>). It paints a band of rows at a time as well, so the HUD's minimap never stalls a frame.
/// </summary>
public sealed class MapPicture
{
    private readonly WorldMap _world;
    private readonly ContentDatabase _content;
    /// <summary>Light on each pixel's ground, 128 for flat ground: the slopes never change.</summary>
    private readonly byte[] _shade;

    /// <param name="farmId">The farm it's drawn for: its parcels are its own on the farmland.</param>
    public MapPicture(WorldMap world, ContentDatabase content, int step, int farmId = Farm.PlayerId)
    {
        _world = world;
        _content = content;
        FarmId = farmId;
        Step = Math.Max(1, step);
        Width = world.CellsX / Step;
        Height = world.CellsZ / Step;
        Rgba = new byte[Width * Height * 4];
        _shade = new byte[Width * Height];
        // The slope of the height sample (a meter apart) nearest each pixel.
        var heights = world.Height;
        for (var z = 0; z < Height; z++)
        for (var x = 0; x < Width; x++)
        {
            var p = world.CellCenter(x * Step, z * Step);
            int hx = (int)MathF.Round(p.X), hz = (int)MathF.Round(p.Y);
            var dx = (heights.At(hx + 1, hz) - heights.At(hx - 1, hz)) * 0.5f;
            var dz = (heights.At(hx, hz + 1) - heights.At(hx, hz - 1)) * 0.5f;
            var n = Vector3.Normalize(new Vector3(-dx, 1f, -dz));
            _shade[z * Width + x] = (byte)(Math.Clamp(1f + (-n.X - n.Z) * 1.6f, 0.75f, 1.25f) * 128f);
        }
    }

    public int FarmId { get; }

    /// <summary>Cells a pixel stands for, along each side; each pixel shows the cell at its north-west corner.</summary>
    public int Step { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>The picture, RGBA8.</summary>
    public byte[] Rgba { get; }

    /// <summary>Paints the whole picture in <paramref name="layer"/>'s colors.</summary>
    public void Paint(MapLayer layer) => Paint(layer, 0, Height);

    /// <summary>Paints rows <paramref name="fromRow"/> to <paramref name="toRow"/> (not included) in <paramref name="layer"/>'s colors.</summary>
    public void Paint(MapLayer layer, int fromRow, int toRow)
    {
        var legend = MapLayers.Legend(_content, layer);
        var parcels = layer == MapLayer.Farmland ? MapLayers.ParcelEntries(_world, FarmId) : [];
        for (var z = Math.Max(0, fromRow); z < Math.Min(Height, toRow); z++)
        {
            var i = _world.CellIndex(0, z * Step);
            for (var x = 0; x < Width; x++, i += Step)
            {
                var rgb = MapLayers.ColorAt(_world, _content, layer, legend, i, parcels);
                var shade = _shade[z * Width + x];
                var o = (z * Width + x) * 4;
                Rgba[o] = Lit(rgb >> 16, shade);
                Rgba[o + 1] = Lit(rgb >> 8, shade);
                Rgba[o + 2] = Lit(rgb, shade);
                Rgba[o + 3] = 255;
            }
        }
    }

    private static byte Lit(uint channel, byte shade) => (byte)Math.Min((int)(channel & 0xff) * shade >> 7, 255);
}

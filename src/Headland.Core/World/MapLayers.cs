using System.Globalization;
using Headland.Core.Content;
using Headland.Core.Ownership;

namespace Headland.Core.World;

/// <summary>What the map shows on the fields over the ground (FS: the map's overlays).</summary>
public enum MapLayer
{
    /// <summary>The ground alone.</summary>
    Terrain,
    /// <summary>What grows on each field, in its crop's map color.</summary>
    Crops,
    /// <summary>How far each field is: plowed, cultivated, sown, growing, ready, withered, harvested.</summary>
    Growth,
    /// <summary>The fields' soils, in their map colors.</summary>
    Soil,
    /// <summary>How wet the fields' soil is.</summary>
    Moisture,
    /// <summary>The parcels of land, by who owns them: the farm, a neighbor selling it, another farm (FS: farmlands).</summary>
    Farmland,
}

/// <summary>An entry of a map layer's legend: what a color (0xRRGGBB) stands for.</summary>
public readonly record struct MapLegendEntry(string Name, uint Rgb);

/// <summary>The colors of the map's layers (<see cref="MapPicture"/>), and what they stand for.</summary>
public static class MapLayers
{
    /// <summary>Each <see cref="GroundType"/>'s color, by value.</summary>
    private static readonly uint[] GroundColors =
    [
        0x6f8a4e, // grass
        0x7d6249, // cultivated
        0x8a7052, // seeded
        0xb3a372, // stubble
        0x5e4a39, // plowed
        0x6e6e6a, // road
        0x8d877a, // yard
        0x8a7a5e, // dirt
        0x3f5a36, // forest
        0x4a6e8c, // water
    ];

    private static readonly MapLegendEntry[] GrowthLegend =
    [
        new("Plowed", 0x5a4636),
        new("Cultivated", 0x8c6d4f),
        new("Sown", 0xb9a579),
        new("Growing", 0x74a052),
        new("Ready to harvest", 0xe0c24a),
        new("Withered", 0xa3503c),
        new("Harvested", 0xd8cfa6),
    ];

    private static readonly MapLegendEntry[] MoistureLegend =
    [
        new("Under 20%", 0xc9a46c),
        new("20–40%", 0xb8b27a),
        new("40–60%", 0x7fa77a),
        new("60–80%", 0x5a8fa6),
        new("80% and over", 0x3f5f9e),
    ];

    private static readonly MapLegendEntry[] FarmlandLegend =
    [
        new("Yours", 0xe0b84a),
        new("For sale", 0xc8c4b4),
        new("Another farm's", 0x7a9ac8),
    ];

    /// <summary>What <paramref name="layer"/>'s colors stand for; the terrain has none.</summary>
    public static IReadOnlyList<MapLegendEntry> Legend(ContentDatabase content, MapLayer layer) => layer switch
    {
        MapLayer.Crops => content.Crops.Select(c => new MapLegendEntry(c.Name, ParseRgb(c.MapColor) ?? 0)).ToList(),
        MapLayer.Growth => GrowthLegend,
        MapLayer.Soil => content.Soils.Select(s => new MapLegendEntry(s.Name, ParseRgb(s.MapColor) ?? 0)).ToList(),
        MapLayer.Moisture => MoistureLegend,
        MapLayer.Farmland => FarmlandLegend,
        _ => [],
    };

    /// <summary>
    /// The legend entry <paramref name="layer"/> shows at cell <paramref name="i"/>, or -1 where the ground shows (off the
    /// fields, or off the parcels for the farmland), for farm <paramref name="farmId"/>.
    /// </summary>
    public static int EntryAt(WorldMap world, ContentDatabase content, MapLayer layer, int i, int farmId = Farm.PlayerId) =>
        EntryAt(world, content, layer, i, layer == MapLayer.Farmland ? ParcelEntries(world, farmId) : []);

    /// <summary>The farmland legend's entry of each parcel for farm <paramref name="farmId"/>, by parcel number.</summary>
    internal static int[] ParcelEntries(WorldMap world, int farmId)
    {
        var entries = new int[world.Farmlands.Select(f => f.Id).DefaultIfEmpty(0).Max() + 1];
        foreach (var land in world.Farmlands)
            entries[land.Id] = land.FarmId == farmId ? 0 : land.FarmId == Farm.None ? 1 : 2;
        return entries;
    }

    private static int EntryAt(WorldMap world, ContentDatabase content, MapLayer layer, int i, int[] parcels)
    {
        var l = world.Layers;
        if (layer == MapLayer.Farmland) return l.FarmlandId[i] is var id and > 0 && id < parcels.Length ? parcels[id] : -1;
        if (layer == MapLayer.Terrain || l.FieldId[i] == 0) return -1;
        return layer switch
        {
            MapLayer.Crops => l.Crop[i] - 1,
            MapLayer.Growth => GrowthAt(content, l, i),
            MapLayer.Soil => l.Soil[i],
            MapLayer.Moisture => Math.Min(l.Moisture[i] * 5 / 256, MoistureLegend.Length - 1),
            _ => -1,
        };
    }

    /// <summary>
    /// The growth legend's entry for a field cell: its crop's stage (sown at the first, ready at the harvestable one),
    /// else what was last done to the ground.
    /// </summary>
    private static int GrowthAt(ContentDatabase content, FieldLayers l, int i)
    {
        if (l.Crop[i] != 0)
        {
            var stage = l.Stage[i];
            return stage == CropStage.Dead ? 5 : stage == 0 ? 2 : content.Crops[l.Crop[i] - 1].Stages[stage].Harvestable ? 4 : 3;
        }
        return (GroundType)l.Ground[i] switch
        {
            GroundType.Plowed => 0,
            GroundType.Cultivated => 1,
            GroundType.Stubble => 6,
            _ => -1,
        };
    }

    /// <summary>The color <paramref name="layer"/> gives cell <paramref name="i"/> (0xRRGGBB): its legend's, or the ground's.</summary>
    /// <paramref name="parcels"/>: <see cref="ParcelEntries"/>, for the farmland.
    internal static uint ColorAt(WorldMap world, ContentDatabase content, MapLayer layer, IReadOnlyList<MapLegendEntry> legend, int i, int[] parcels)
    {
        var entry = EntryAt(world, content, layer, i, parcels);
        var ground = GroundColors[Math.Min(world.Layers.Ground[i], (byte)(GroundColors.Length - 1))];
        if (entry < 0 || entry >= legend.Count) return Dim(ground, layer != MapLayer.Terrain);
        // The parcels are tinted, their ground showing through.
        return layer == MapLayer.Farmland ? Mix(ground, legend[entry].Rgb, 0.5f) : legend[entry].Rgb;
    }

    private static uint Mix(uint a, uint b, float t)
    {
        uint Channel(int shift) => (uint)MathF.Round(((a >> shift) & 0xff) * (1f - t) + ((b >> shift) & 0xff) * t) << shift;
        return Channel(16) | Channel(8) | Channel(0);
    }

    /// <summary>The ground under a layer shows darker, so the fields' colors stand out.</summary>
    private static uint Dim(uint rgb, bool dim)
    {
        if (!dim) return rgb;
        uint Channel(int shift) => (uint)(((rgb >> shift) & 0xff) * 0.55f) << shift;
        return Channel(16) | Channel(8) | Channel(0);
    }

    /// <summary>A "#rrggbb" color as 0xRRGGBB, or null when it isn't one.</summary>
    public static uint? ParseRgb(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? rgb
            : null;
}

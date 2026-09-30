using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class MapTests
{
    [Fact]
    public void TheLayersColorTheFieldsByWhatIsOnThem()
    {
        var sim = TestContent.NewSim();
        var w = sim.World;
        var content = sim.Content;
        var field = w.Fields[0];
        var (cx, cz) = w.WorldToCell(field.Center);
        var i = w.CellIndex(cx, cz);
        var l = w.Layers;
        int Entry(MapLayer layer) => MapLayers.EntryAt(w, content, layer, i);
        string Name(MapLayer layer) => MapLayers.Legend(content, layer)[Entry(layer)].Name;

        // The ground alone on the terrain; the soil and how wet it is on their layers.
        Assert.Equal(-1, Entry(MapLayer.Terrain));
        Assert.Equal(content.Soils[l.Soil[i]].Name, Name(MapLayer.Soil));
        l.Moisture[i] = 128;
        Assert.Equal("40–60%", Name(MapLayer.Moisture));
        l.Moisture[i] = 255;
        Assert.Equal("80% and over", Name(MapLayer.Moisture));

        // Growth: what was last done to the ground, then the crop's stage.
        l.Crop[i] = 0;
        l.Ground[i] = (byte)GroundType.Plowed;
        Assert.Equal((-1, "Plowed"), (Entry(MapLayer.Crops), Name(MapLayer.Growth)));
        l.Ground[i] = (byte)GroundType.Stubble;
        Assert.Equal("Harvested", Name(MapLayer.Growth));
        var wheat = content.CropIndex("wheat");
        l.Crop[i] = (byte)(wheat + 1);
        l.Stage[i] = 0;
        Assert.Equal(("Wheat", "Sown"), (Name(MapLayer.Crops), Name(MapLayer.Growth)));
        l.Stage[i] = 2;
        Assert.Equal("Growing", Name(MapLayer.Growth));
        l.Stage[i] = (byte)content.Crops[wheat].HarvestableStage;
        Assert.Equal("Ready to harvest", Name(MapLayer.Growth));
        l.Stage[i] = CropStage.Dead;
        Assert.Equal("Withered", Name(MapLayer.Growth));

        // Off the fields, every layer shows the ground.
        var (ox, oz) = w.WorldToCell(new Vector2(2f, 2f));
        Assert.Equal(0, l.FieldId[w.CellIndex(ox, oz)]);
        Assert.All(Enum.GetValues<MapLayer>(), layer => Assert.Equal(-1, MapLayers.EntryAt(w, content, layer, w.CellIndex(ox, oz))));

        // The picture: the field's cell in its crop's map color, the ground elsewhere.
        var picture = new MapPicture(w, content, 2);
        Assert.Equal((w.CellsX / 2, w.CellsZ / 2), (picture.Width, picture.Height));
        var (px, pz) = (cx / 2, cz / 2);
        var at = w.CellIndex(px * 2, pz * 2);
        l.Crop[at] = (byte)(wheat + 1);
        l.Stage[at] = 3;
        // A band of rows at a time: those outside it stay as they were.
        picture.Paint(MapLayer.Crops, pz + 1, picture.Height);
        var rgba = picture.Rgba;
        var o = (pz * picture.Width + px) * 4;
        Assert.Equal(0, rgba[o + 3]);
        picture.Paint(MapLayer.Crops, pz, pz + 1);
        var color = MapLayers.ParseRgb(content.Crops[wheat].MapColor)!.Value;
        // Lit by the slope, at most a quarter lighter or darker.
        Assert.InRange(rgba[o] / (float)(color >> 16), 0.74f, 1.26f);
        Assert.InRange(rgba[o + 1] / (float)((color >> 8) & 0xff), 0.74f, 1.26f);
        Assert.Equal(255, rgba[o + 3]);
    }

    [Fact]
    public void CropsAndSoilsNeedAMapColor()
    {
        Assert.Equal((0xcfa44fu, (uint?)null, (uint?)null), (MapLayers.ParseRgb("#cfa44f")!.Value, MapLayers.ParseRgb("cfa44f"), MapLayers.ParseRgb("#cfa44")));
        // Its own content: the others' stays valid.
        var content = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        content.Crops[0].MapColor = "yellow";
        content.Soils[0].MapColor = "";
        var errors = content.Validate();
        Assert.Contains($"crop '{content.Crops[0].Id}': mapColor must be a color (#rrggbb)", errors);
        Assert.Contains($"soil '{content.Soils[0].Id}': mapColor must be a color (#rrggbb)", errors);
    }

    [Fact]
    public void TheWaypointStaysUntilTheFarmerGetsThereAndIsSaved()
    {
        var sim = TestContent.NewSim();
        var reached = new List<WaypointReached>();
        sim.Events.Subscribe<WaypointReached>(reached.Add);
        var spot = sim.Player.Position + new Vector2(100f, 0f);
        sim.Player.Waypoint = spot;
        sim.Tick(1f / 60f);
        Assert.Equal(spot, sim.Player.Waypoint);

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        Assert.Equal(spot, loaded.Player.Waypoint);

        sim.Player.Position = spot + new Vector2(0f, PlayerCharacter.WaypointReach - 1f);
        sim.Tick(1f / 60f);
        Assert.Null(sim.Player.Waypoint);
        Assert.Equal(spot, Assert.Single(reached).Position);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Waypoint reached");
        Assert.Null(SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim.Player.Waypoint);
    }
}

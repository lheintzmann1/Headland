using System.Text;
using Headland.Core.World;

namespace Headland.Core.Saves;

/// <summary>
/// layers.bin: the field layers gameplay changes, as raw little-endian arrays (the zip compresses them).
/// "HLYR", version, cells x and z, then per layer its name, element size and bytes. Unknown layers are skipped
/// and missing ones keep the map's values, so layers can be added without breaking saves. Map structure
/// (soil, farmland and field ids) is not saved: it comes from the map.
/// </summary>
internal static class LayerCodec
{
    private const uint Magic = 0x5259_4C48; // "HLYR"
    private const ushort Version = 1;

    private static readonly (string name, Func<FieldLayers, Array> array)[] Layers =
    [
        ("ground", l => l.Ground), ("crop", l => l.Crop), ("stage", l => l.Stage), ("progress", l => l.Progress),
        ("moisture", l => l.Moisture), ("nitrogen", l => l.Nitrogen), ("health", l => l.Health),
        ("workAngle", l => l.WorkAngle), ("chill", l => l.Chill), ("weeds", l => l.Weeds), ("fertilized", l => l.Fertilized),
    ];

    public static byte[] Write(WorldMap world)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(world.CellsX);
            w.Write(world.CellsZ);
            w.Write((ushort)Layers.Length);
            foreach (var (name, get) in Layers)
            {
                var array = get(world.Layers);
                var bytes = new byte[Buffer.ByteLength(array)];
                Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
                w.Write(name);
                w.Write((byte)(bytes.Length / array.Length));
                w.Write(bytes.Length);
                w.Write(bytes);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Reads the layers into the world; <paramref name="cropRemap"/> maps saved crop values to current ones.</summary>
    public static void Read(byte[] data, WorldMap world, byte[] cropRemap, List<string> warnings)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        if (r.ReadUInt32() != Magic) throw new SaveException("layers.bin is not a Headland layer file");
        if (r.ReadUInt16() > Version) throw new SaveException("layers.bin comes from a newer version of the game");
        var (cx, cz) = (r.ReadInt32(), r.ReadInt32());
        if (cx != world.CellsX || cz != world.CellsZ)
            throw new SaveException($"The save's map is {cx / 2} m wide but this map is {world.CellsX / 2} m: it has changed since");
        int count = r.ReadUInt16();
        for (var k = 0; k < count; k++)
        {
            var name = r.ReadString();
            var size = r.ReadByte();
            var bytes = r.ReadBytes(r.ReadInt32());
            var layer = Array.Find(Layers, l => l.name == name);
            if (layer.array == null)
            {
                warnings.Add($"Skipped unknown field layer '{name}'");
                continue;
            }
            var array = layer.array(world.Layers);
            if (size != Buffer.ByteLength(array) / array.Length || bytes.Length != Buffer.ByteLength(array))
                throw new SaveException($"Field layer '{name}' has the wrong size");
            Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
        }

        var L = world.Layers;
        for (var i = 0; i < L.Crop.Length; i++)
        {
            var crop = cropRemap[L.Crop[i]];
            if (crop == L.Crop[i]) continue;
            L.Crop[i] = crop;
            if (crop != 0) continue;
            // The crop no longer exists: leave a bare seedbed.
            L.Stage[i] = 0;
            L.Progress[i] = 0f;
            L.Health[i] = 0;
            L.Chill[i] = 0;
            if (L.Ground[i] == (byte)GroundType.Seeded) L.Ground[i] = (byte)GroundType.Cultivated;
        }
        world.MarkAllDirty(crop: true);
    }
}

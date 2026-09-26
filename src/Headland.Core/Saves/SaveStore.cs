using System.IO.Compression;
using System.Text.Json;

namespace Headland.Core.Saves;

/// <summary>A save slot on disk, with its summary.</summary>
public sealed record SaveSlot(string Name, SaveMeta Meta);

/// <summary>
/// Save slots in a directory: one zip per slot (<c>slot.zip</c>) holding meta.json, state.json and layers.bin.
/// Writes go to a temporary file first, so a crash mid-save never damages the previous save.
/// </summary>
public sealed class SaveStore(string directory)
{
    private const string Extension = ".zip";

    public string Directory { get; } = directory;

    /// <summary>Slot names are file names: 1 to 40 letters, digits, '-' or '_'.</summary>
    public static bool IsValidSlot(string slot) =>
        slot.Length is > 0 and <= 40 && slot.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public string PathOf(string slot) => IsValidSlot(slot)
        ? Path.Combine(Directory, slot + Extension)
        : throw new ArgumentException($"Invalid save slot name '{slot}'", nameof(slot));

    public bool Exists(string slot) => File.Exists(PathOf(slot));

    /// <summary>Every readable slot, most recently saved first.</summary>
    public IReadOnlyList<SaveSlot> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var slots = new List<SaveSlot>();
        foreach (var path in System.IO.Directory.GetFiles(Directory, "*" + Extension))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!IsValidSlot(name)) continue;
            try
            {
                using var zip = ZipFile.OpenRead(path);
                slots.Add(new SaveSlot(name, ReadJson<SaveMeta>(zip, "meta.json")));
            }
            catch (Exception e) when (e is SaveException or InvalidDataException or IOException)
            {
                // Damaged or foreign file: not a slot.
            }
        }
        return slots.OrderByDescending(s => s.Meta.SavedAtUtc).ToList();
    }

    public void Write(string slot, SaveFile file)
    {
        var path = PathOf(slot);
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = path + ".tmp";
        File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            Add(zip, "meta.json", JsonSerializer.SerializeToUtf8Bytes(file.Meta, SaveGame.Json), CompressionLevel.Optimal);
            Add(zip, "state.json", file.State, CompressionLevel.Optimal);
            Add(zip, "layers.bin", file.Layers, CompressionLevel.Fastest);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public SaveFile Read(string slot)
    {
        var path = PathOf(slot);
        if (!File.Exists(path)) throw new SaveException($"There is no save named '{slot}'");
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return new SaveFile(ReadJson<SaveMeta>(zip, "meta.json"), ReadBytes(zip, "state.json"), ReadBytes(zip, "layers.bin"));
        }
        catch (InvalidDataException e)
        {
            throw new SaveException($"Save '{slot}' is damaged: {e.Message}");
        }
    }

    public void Delete(string slot) => File.Delete(PathOf(slot));

    private static void Add(ZipArchive zip, string name, byte[] data, CompressionLevel level)
    {
        using var stream = zip.CreateEntry(name, level).Open();
        stream.Write(data);
    }

    private static byte[] ReadBytes(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new SaveException($"The save has no {name}");
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static T ReadJson<T>(ZipArchive zip, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(ReadBytes(zip, name), SaveGame.Json) ?? throw new SaveException($"{name} is empty");
        }
        catch (JsonException e)
        {
            throw new SaveException($"{name} is damaged: {e.Message}");
        }
    }
}

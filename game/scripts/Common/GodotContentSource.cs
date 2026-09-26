using Headland.Core.Content;
using Godot;

namespace Headland.Game.Common;

/// <summary>Reads JSON content from res://data through Godot's virtual file system (works in exports too).</summary>
public sealed class GodotContentSource(string root) : IContentSource
{
    public IReadOnlyList<string> ListJson(string dir)
    {
        var files = DirAccess.GetFilesAt($"{root}/{dir}");
        return files.Where(f => f.EndsWith(".json"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"{dir}/{f}")
            .ToList();
    }

    public string ReadText(string relativePath)
    {
        var path = $"{root}/{relativePath}";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read)
                         ?? throw new ContentException($"Cannot open {path}: {FileAccess.GetOpenError()}");
        return file.GetAsText();
    }

    public bool Exists(string relativePath) => FileAccess.FileExists($"{root}/{relativePath}");
}

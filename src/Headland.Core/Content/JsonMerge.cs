using System.Text.Json.Nodes;

namespace Headland.Core.Content;

/// <summary>
/// Merges a JSON patch into a document, the way a machine's configuration options change its JSON. Objects merge member
/// by member, and a member set to null is removed. Arrays of objects merge item by item: an item with an <c>id</c>
/// merges into the item with that id, or is added at the end when there is none; an item without one merges into the
/// item at its own index (<c>{}</c> leaves it as it is), or is added past the end. Anything else replaces what was there.
/// Member names match whatever their case, as content is read.
/// </summary>
public static class JsonMerge
{
    public static void Into(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            var name = Find(target, key) ?? key;
            if (value == null)
            {
                target.Remove(name);
                continue;
            }
            switch (value, target[name])
            {
                case (JsonObject p, JsonObject t):
                    Into(t, p);
                    break;
                case (JsonArray p, JsonArray t) when Objects(p) && Objects(t):
                    Into(t, p);
                    break;
                default:
                    target[name] = value.DeepClone();
                    break;
            }
        }
    }

    private static void Into(JsonArray target, JsonArray patch)
    {
        for (var i = 0; i < patch.Count; i++)
        {
            var item = (JsonObject)patch[i]!;
            var match = Id(item) is { } id
                ? target.OfType<JsonObject>().FirstOrDefault(t => Id(t) == id)
                : i < target.Count ? (JsonObject)target[i]! : null;
            if (match != null) Into(match, item);
            else target.Add(item.DeepClone());
        }
    }

    private static bool Objects(JsonArray array) => array.All(n => n is JsonObject);

    /// <summary>The name <paramref name="obj"/> has a member under, whatever its case.</summary>
    private static string? Find(JsonObject obj, string name) =>
        obj.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static string? Id(JsonObject obj) =>
        Find(obj, "id") is { } key && obj[key] is JsonValue v && v.TryGetValue<string>(out var id) ? id : null;
}

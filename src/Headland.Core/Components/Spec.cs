using Headland.Core.Content;

namespace Headland.Core.Components;

/// <summary>A line of what an entity type is, as the shop lists it (FS: store specs): "Power", "125 hp".</summary>
public sealed record Spec(string Name, string Value)
{
    /// <summary>What <paramref name="def"/>'s components bring, in their order.</summary>
    public static IEnumerable<Spec> Of(EntityDef def, ContentDatabase content) =>
        def.Components.OfType<ISpecSource>().SelectMany(c => c.Specs(def, content));

    /// <summary>The unit a fill unit's amounts are in: its first fill type's (L, kg).</summary>
    internal static string UnitOf(FillUnitDef? unit, ContentDatabase content) =>
        unit?.FillTypes.FirstOrDefault() is { } ft && content.FillTypes.TryGetValue(ft, out var def) ? def.Unit : "L";
}

/// <summary>A component def that tells the shop what it brings: an engine its power, a tank its capacity.</summary>
public interface ISpecSource
{
    IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content);
}

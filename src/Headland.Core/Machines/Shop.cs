using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Machines;

/// <summary>
/// The machine shop (FS: the store): the machines for sale in the shop's categories (shopcategories.json) and by brand
/// (brands.json), what they are (<see cref="Spec"/>s from their components) and what they cost with the options picked,
/// at the difficulty's price level.
/// </summary>
public sealed class Shop(Simulation sim)
{
    private ContentDatabase Content => sim.Content;

    /// <summary>The categories with machines for sale (of <paramref name="brand"/>, when given), in the shop's order.</summary>
    public IEnumerable<ShopCategoryDef> Categories(BrandDef? brand = null) =>
        Content.ShopCategories.Values.Where(c => Machines(c, brand).Any());

    /// <summary>The machines for sale in <paramref name="category"/> (of <paramref name="brand"/>, when given), cheapest first.</summary>
    public IEnumerable<MachineDef> Machines(ShopCategoryDef category, BrandDef? brand = null) => Content.Machines.Values
        .Where(m => m.Category == category.Id && (brand == null || m.Brand == brand.Id))
        .OrderBy(m => m.Price)
        .ThenBy(m => m.Name, StringComparer.Ordinal);

    /// <summary>The brands of the machines for sale, by name.</summary>
    public IEnumerable<BrandDef> Brands => Content.Brands.Values
        .Where(b => Content.Machines.Values.Any(m => m.Brand == b.Id && Content.ShopCategories.ContainsKey(m.Category)))
        .OrderBy(b => b.Name, StringComparer.Ordinal);

    public BrandDef? BrandOf(MachineDef def) => Content.Brands.GetValueOrDefault(def.Brand);

    /// <summary>What <paramref name="def"/> costs the farm with its options, at the difficulty's price level.</summary>
    public float Price(MachineDef def) => MathF.Round(def.Price * sim.Economy.PriceLevel);

    /// <summary>What <paramref name="def"/> is with its options: what its components bring, then its mass.</summary>
    public IReadOnlyList<Spec> Specs(MachineDef def) => Spec.Of(def, Content).Append(new Spec("Mass", $"{def.Mass:N0} kg")).ToList();
}

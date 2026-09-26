using Headland.Core.Content;

namespace Headland.Core.Economics;

public sealed class Economy(ContentDatabase content, float startMoney)
{
    public float Money { get; private set; } = startMoney;
    public float TotalIncome { get; private set; }
    public float TotalExpenses { get; private set; }

    /// <summary>Units sold per fill type (statistics).</summary>
    public Dictionary<string, float> Sold { get; } = new();

    public float Price(string fillType, int month)
    {
        var def = content.FillTypes[fillType];
        var factor = def.MonthlyPriceFactor is { Length: 12 } f ? f[Math.Clamp(month, 1, 12) - 1] : 1f;
        return def.PricePerUnit * factor;
    }

    public float Sell(string fillType, float amount, int month)
    {
        var income = amount * Price(fillType, month);
        Money += income;
        TotalIncome += income;
        Sold[fillType] = Sold.GetValueOrDefault(fillType) + amount;
        return income;
    }

    /// <summary>Buys up to <paramref name="amount"/> units; returns how many were affordable.</summary>
    public float Buy(string fillType, float amount, int month)
    {
        var price = Price(fillType, month);
        if (price <= 0f) return amount;
        var affordable = MathF.Min(amount, MathF.Max(0f, Money) / price);
        var cost = affordable * price;
        Money -= cost;
        TotalExpenses += cost;
        return affordable;
    }
}

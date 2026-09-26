using Headland.Core.Content;
using Headland.Core.Time;

namespace Headland.Core.Economics;

/// <summary>The player's farm money and its books, and market prices.</summary>
public sealed class Economy(ContentDatabase content, Calendar calendar, float startMoney, int startDay)
{
    public float Money { get; private set; } = startMoney;
    public float TotalIncome { get; private set; }
    public float TotalExpenses { get; private set; }
    /// <summary>Money in and out by category, day by day and month by month.</summary>
    public Ledger Ledger { get; } = new(calendar, startDay);

    internal void Restore(float money, float totalIncome, float totalExpenses)
    {
        Money = money;
        TotalIncome = totalIncome;
        TotalExpenses = totalExpenses;
    }

    /// <summary>Market price of a unit of <paramref name="fillType"/> in <paramref name="month"/> (the monthly curve).</summary>
    public float Price(string fillType, int month)
    {
        var def = content.FillTypes[fillType];
        var factor = def.MonthlyPriceFactor is { Length: 12 } f ? f[Math.Clamp(month, 1, 12) - 1] : 1f;
        return def.PricePerUnit * factor;
    }

    public void Earn(float amount, MoneyCategory category)
    {
        Money += amount;
        TotalIncome += amount;
        Ledger.Book(category, amount);
    }

    public void Spend(float amount, MoneyCategory category)
    {
        Money -= amount;
        TotalExpenses += amount;
        Ledger.Book(category, -amount);
    }

    /// <summary>How many of <paramref name="amount"/> units at <paramref name="unitPrice"/> the money pays for.</summary>
    public float Affordable(float amount, float unitPrice) =>
        unitPrice <= 0f ? amount : MathF.Min(amount, MathF.Max(0f, Money) / unitPrice);

    /// <summary>Runs before each world hour's tick: its day opens a new page of the books.</summary>
    internal void StartHour(long hour) => Ledger.StartDay((int)(hour / 24));
}

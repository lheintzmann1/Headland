using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Time;

namespace Headland.Core.Economics;

/// <summary>
/// The player's farm money, its books and its bank loan, and market prices. Money goes below zero only through
/// costs that come due by themselves (interest, running costs); the farm can't buy anything until it's back up.
/// </summary>
public sealed class Economy(ContentDatabase content, Calendar calendar, EventBus events, float startMoney, int startDay)
{
    public float Money { get; private set; } = startMoney;
    public float TotalIncome { get; private set; }
    public float TotalExpenses { get; private set; }
    /// <summary>Money in and out by category, day by day and month by month.</summary>
    public Ledger Ledger { get; } = new(calendar, startDay);

    /// <summary>What the farm owes the bank.</summary>
    public float Loan { get; private set; }
    public EconomyDef Terms => content.Economy;
    /// <summary>What the next <see cref="Borrow"/> lends: a step, or what's left under the credit limit.</summary>
    public float NextLoan => MathF.Max(0f, MathF.Min(Terms.LoanStep, Terms.CreditLimit - Loan));
    /// <summary>What the next <see cref="Repay"/> pays back: a step, or the rest of the loan.</summary>
    public float NextRepayment => MathF.Min(Terms.LoanStep, Loan);
    /// <summary>Interest charged at the start of each day, for the loan as it is now.</summary>
    public float DailyInterest => Loan * Terms.LoanInterest / calendar.DaysPerYear;

    internal void Restore(float money, float totalIncome, float totalExpenses, float loan)
    {
        Money = money;
        TotalIncome = totalIncome;
        TotalExpenses = totalExpenses;
        Loan = MathF.Max(0f, loan);
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
        var before = Money;
        Money -= amount;
        TotalExpenses += amount;
        Ledger.Book(category, -amount);
        if (before >= 0f && Money < 0f) events.Publish(new AccountOverdrawn(Money));
    }

    /// <summary>How many of <paramref name="amount"/> units at <paramref name="unitPrice"/> the money pays for.</summary>
    public float Affordable(float amount, float unitPrice) =>
        unitPrice <= 0f ? amount : MathF.Min(amount, MathF.Max(0f, Money) / unitPrice);

    /// <summary>Borrows <see cref="NextLoan"/>; false at the credit limit.</summary>
    public bool Borrow()
    {
        var amount = NextLoan;
        if (amount < 1f) return false;
        Loan += amount;
        Money += amount;
        events.Publish(new LoanTaken(amount, Loan));
        return true;
    }

    /// <summary>Pays back <see cref="NextRepayment"/>, or the whole loan; false without a loan or the money for it.</summary>
    public bool Repay(bool all = false)
    {
        var amount = all ? Loan : NextRepayment;
        if (amount < 1f || amount > Money) return false;
        Loan -= amount;
        Money -= amount;
        if (Loan < 1f) Loan = 0f;
        events.Publish(new LoanRepaid(amount, Loan));
        return true;
    }

    /// <summary>Runs before each world hour's tick: a new day opens a new page of the books and pays the interest.</summary>
    internal void StartHour(long hour)
    {
        if (Ledger.StartDay((int)(hour / 24)) && Loan > 0f) Spend(DailyInterest, MoneyCategory.LoanInterest);
    }
}

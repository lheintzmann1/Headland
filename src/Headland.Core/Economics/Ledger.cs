using Headland.Core.Time;

namespace Headland.Core.Economics;

/// <summary>What money came in for or went out on (FS money types), the rows of the finances screen.</summary>
public enum MoneyCategory
{
    /// <summary>Goods sold at POIs, and production sold for the farm.</summary>
    Sales,
    /// <summary>Seed and other supplies.</summary>
    Purchases,
    Fuel,
    /// <summary>Repairs and washing.</summary>
    Maintenance,
    /// <summary>Machines, and the options fitted to them.</summary>
    Machines,
    /// <summary>Running costs of the farm's processing.</summary>
    Production,
    Wages,
    Leasing,
    Land,
    LoanInterest,
    Contracts,
    Other,
}

/// <summary>Money in and out during one day or month, by category: income positive, expenses negative.</summary>
public sealed class FinancePeriod(int index)
{
    /// <summary>The day index, or for a month (year - 1) × 12 + month - 1.</summary>
    public int Index { get; } = index;

    internal float[] Amounts { get; } = new float[Ledger.Categories.Length];

    public float this[MoneyCategory category] => Amounts[(int)category];

    public float Income => Amounts.Where(a => a > 0f).Sum();
    public float Expenses => -Amounts.Where(a => a < 0f).Sum();
    public float Net => Amounts.Sum();
}

/// <summary>
/// The farm's books: what came in and went out, by category, for today and the <see cref="KeptDays"/> - 1 days before,
/// and for this month and the <see cref="KeptMonths"/> - 1 months before.
/// </summary>
public sealed class Ledger
{
    public const int KeptDays = 5;
    public const int KeptMonths = 12;

    public static readonly MoneyCategory[] Categories = Enum.GetValues<MoneyCategory>();

    private readonly Calendar _calendar;
    private readonly List<FinancePeriod> _days = [];
    private readonly List<FinancePeriod> _months = [];

    public Ledger(Calendar calendar, int day)
    {
        _calendar = calendar;
        Open(day);
    }

    /// <summary>Newest first: today, then the days before.</summary>
    public IReadOnlyList<FinancePeriod> Days => _days;
    /// <summary>Newest first: this month, then the months before.</summary>
    public IReadOnlyList<FinancePeriod> Months => _months;
    public FinancePeriod Today => _days[0];
    public FinancePeriod ThisMonth => _months[0];

    public static string Name(MoneyCategory category) => category switch
    {
        MoneyCategory.Production => "Production costs",
        MoneyCategory.LoanInterest => "Loan interest",
        _ => category.ToString(),
    };

    public GameDate DateOf(FinancePeriod day) => _calendar.DateOfDay(day.Index);

    public static (int year, int month) MonthOf(FinancePeriod month) => (month.Index / 12 + 1, month.Index % 12 + 1);

    internal void Book(MoneyCategory category, float amount)
    {
        Today.Amounts[(int)category] += amount;
        ThisMonth.Amounts[(int)category] += amount;
    }

    /// <summary>Turns to a new page on a later day (true), and a new month's page when the month changed too.</summary>
    internal bool StartDay(int day)
    {
        if (day <= Today.Index) return false;
        Push(_days, new FinancePeriod(day), KeptDays);
        if (MonthIndex(day) is var month && month != ThisMonth.Index) Push(_months, new FinancePeriod(month), KeptMonths);
        return true;
    }

    /// <summary>Puts back saved pages (any order); starts afresh on <paramref name="today"/> when there are none.</summary>
    internal void Restore(IEnumerable<FinancePeriod> days, IEnumerable<FinancePeriod> months, int today)
    {
        _days.Clear();
        _months.Clear();
        _days.AddRange(days.OrderByDescending(p => p.Index).Take(KeptDays));
        _months.AddRange(months.OrderByDescending(p => p.Index).Take(KeptMonths));
        if (_days.Count == 0 || _months.Count == 0) Open(today);
    }

    private void Open(int day)
    {
        _days.Clear();
        _months.Clear();
        _days.Add(new FinancePeriod(day));
        _months.Add(new FinancePeriod(MonthIndex(day)));
    }

    private int MonthIndex(int day)
    {
        var date = _calendar.DateOfDay(day);
        return (date.Year - 1) * 12 + date.Month - 1;
    }

    private static void Push(List<FinancePeriod> pages, FinancePeriod page, int keep)
    {
        pages.Insert(0, page);
        if (pages.Count > keep) pages.RemoveRange(keep, pages.Count - keep);
    }
}

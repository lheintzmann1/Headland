namespace Headland.Core.Time;

public enum Season { Spring, Summer, Autumn, Winter }

public readonly record struct GameDate(int Year, int Month, int Day)
{
    public override string ToString() => $"{Calendar.MonthNames[Month - 1]} {Day}, Year {Year}";
}

/// <summary>
/// Compressed calendar: 12 months of <see cref="DaysPerMonth"/> days. Agronomic processes are scaled by
/// <see cref="RealDaysPerGameDay"/> so crops need the same calendar months whatever the month length.
/// </summary>
public sealed class Calendar(int daysPerMonth)
{
    public static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public const float RealDaysPerMonth = 30.4f;

    public int DaysPerMonth { get; } = Math.Max(1, daysPerMonth);
    public int DaysPerYear => DaysPerMonth * 12;
    public float RealDaysPerGameDay => RealDaysPerMonth / DaysPerMonth;

    public static Season SeasonOf(int month) => month switch
    {
        3 or 4 or 5 => Season.Spring,
        6 or 7 or 8 => Season.Summer,
        9 or 10 or 11 => Season.Autumn,
        _ => Season.Winter,
    };

    public GameDate DateOfDay(int dayIndex)
    {
        var year = dayIndex / DaysPerYear;
        var dayOfYear = dayIndex % DaysPerYear;
        return new GameDate(year + 1, dayOfYear / DaysPerMonth + 1, dayOfYear % DaysPerMonth + 1);
    }

    public int DayIndexOf(GameDate d) => (d.Year - 1) * DaysPerYear + (d.Month - 1) * DaysPerMonth + (d.Day - 1);
}

/// <summary>World clock. Only world processes use the time scale; driving is always real time.</summary>
public sealed class GameClock
{
    public static readonly float[] Speeds = [1f, 5f, 15f, 60f, 120f, 240f];

    public GameClock(Calendar calendar, GameDate start, float startHour)
    {
        Calendar = calendar;
        TotalSeconds = calendar.DayIndexOf(start) * 86400.0 + startHour * 3600.0;
        LastHour = (long)(TotalSeconds / 3600.0);
    }

    public Calendar Calendar { get; }

    /// <summary>Game seconds since Year 1, January 1st, 00:00.</summary>
    public double TotalSeconds { get; private set; }

    public float TimeScale { get; set; } = 5f;
    public bool Paused { get; set; }

    /// <summary>Index of the last hour whose world tick has been run.</summary>
    public long LastHour { get; private set; }

    public long TotalHours => (long)(TotalSeconds / 3600.0);
    public int DayIndex => (int)(TotalSeconds / 86400.0);
    public GameDate Date => Calendar.DateOfDay(DayIndex);
    public int Month => Date.Month;
    public float HourOfDay => (float)(TotalSeconds % 86400.0 / 3600.0);

    /// <summary>Fractional month in [1, 13): 1.0 is the start of January.</summary>
    public float MonthFloat
    {
        get
        {
            var dayOfYear = DayIndex % Calendar.DaysPerYear + HourOfDay / 24f;
            return 1f + dayOfYear / Calendar.DaysPerMonth;
        }
    }

    public Season Season => Calendar.SeasonOf(Month);

    /// <summary>Advances by real seconds; returns how many whole game hours were crossed.</summary>
    public int Advance(float realDt)
    {
        if (!Paused) TotalSeconds += realDt * TimeScale;
        return CollectHours();
    }

    /// <summary>Jumps forward without running machines (sleep, debug skip).</summary>
    public int Skip(double gameSeconds)
    {
        TotalSeconds += gameSeconds;
        return CollectHours();
    }

    internal void Restore(double totalSeconds, long lastHour)
    {
        TotalSeconds = totalSeconds;
        LastHour = lastHour;
    }

    private int CollectHours()
    {
        var now = TotalHours;
        var crossed = (int)(now - LastHour);
        LastHour = now;
        return crossed;
    }

    public string TimeString
    {
        get
        {
            var h = HourOfDay;
            var hh = (int)h;
            var mm = (int)((h - hh) * 60f);
            return $"{hh:00}:{mm:00}";
        }
    }
}

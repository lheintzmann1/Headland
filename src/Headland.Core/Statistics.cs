using Headland.Core.Events;

namespace Headland.Core;

/// <summary>Running farm totals for the statistics screen, kept up to date from the event bus.</summary>
public sealed class Statistics
{
    public Statistics(EventBus events)
    {
        events.Subscribe<FieldWorked>(e => Add(HectaresWorked, e.Work, e.Hectares));
        events.Subscribe<CropHarvested>(e => Add(Harvested, e.FillType, e.Amount));
        events.Subscribe<FillSold>(e => Add(Sold, e.FillType, e.Amount));
        events.Subscribe<FillBought>(e => Add(Bought, e.FillType, e.Amount));
        events.Subscribe<HelperHired>(_ => HelpersHired++);
        events.Subscribe<DayStarted>(_ => DaysPlayed++);
    }

    /// <summary>Hectares worked per work type (cultivator, seeder, harvester).</summary>
    public Dictionary<string, float> HectaresWorked { get; } = new();
    /// <summary>Units harvested per fill type.</summary>
    public Dictionary<string, float> Harvested { get; } = new();
    /// <summary>Units sold per fill type.</summary>
    public Dictionary<string, float> Sold { get; } = new();
    /// <summary>Units bought per fill type.</summary>
    public Dictionary<string, float> Bought { get; } = new();
    public int HelpersHired { get; set; }
    public int DaysPlayed { get; set; }

    private static void Add(Dictionary<string, float> totals, string key, float amount) =>
        totals[key] = totals.GetValueOrDefault(key) + amount;
}

using Headland.Core.Content;

namespace Headland.Core.Components;

public sealed class FillUnitDef
{
    public string Id { get; set; } = "main";
    public float Capacity { get; set; } = 1000f;
    public string[] FillTypes { get; set; } = [];
    public string? StartFillType { get; set; }
    public float StartLevel { get; set; }
}

/// <summary>Tanks and bins: fuel, seed, a grain tank, a trailer's bed, a silo's bins.</summary>
public sealed class FillUnitsDef : ComponentDef
{
    public FillUnitDef[] Units { get; set; } = [];

    public override IEnumerable<string> Roles => ["load"];

    internal override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content)
    {
        foreach (var id in Units.GroupBy(u => u.Id).Where(g => g.Count() > 1).Select(g => g.Key)) yield return $"unit '{id}' is defined more than once";
        foreach (var u in Units)
        {
            if (u.Capacity <= 0f) yield return $"unit '{u.Id}': capacity must be > 0";
            if (u.FillTypes.Length == 0) yield return $"unit '{u.Id}': needs fillTypes";
            foreach (var ft in u.FillTypes.Where(ft => !content.FillTypes.ContainsKey(ft))) yield return $"unit '{u.Id}' accepts unknown fill type '{ft}'";
            if (u.StartLevel is var level and > 0f && (level > u.Capacity || u.StartFillType == null || !u.FillTypes.Contains(u.StartFillType)))
                yield return $"unit '{u.Id}': startLevel needs a startFillType it accepts, and room for it";
        }
    }

    internal override Component Create(Entity owner) => new FillUnits(owner, this);
}

public sealed class FillUnit(FillUnitDef def)
{
    public FillUnitDef Def { get; } = def;
    public string? FillType { get; set; } = def.StartLevel > 0 ? def.StartFillType : null;
    public float Level { get; set; } = def.StartLevel;
    public float Capacity => Def.Capacity;
    public float Free => Capacity - Level;
    public bool IsEmpty => Level <= 0.001f;
    public float Fraction => Capacity > 0 ? Level / Capacity : 0f;

    /// <summary>True if this unit can take the fill type right now (accepted, and not mixed with another type).</summary>
    public bool CanAccept(string fillType) =>
        Def.FillTypes.Contains(fillType) && (IsEmpty || FillType == fillType) && Free > 0.001f;

    public bool Accepts(string fillType) => Def.FillTypes.Contains(fillType);

    public float Add(string fillType, float amount)
    {
        if (!CanAccept(fillType)) return 0f;
        var added = MathF.Min(amount, Free);
        Level += added;
        FillType = fillType;
        return added;
    }

    public float Remove(float amount)
    {
        var removed = MathF.Min(amount, Level);
        Level -= removed;
        if (IsEmpty)
        {
            Level = 0f;
            FillType = null;
        }
        return removed;
    }
}

public sealed class FillUnitSave
{
    public string Id { get; set; } = "";
    public string? FillType { get; set; }
    public float Level { get; set; }
}

public sealed class FillUnitsSave
{
    public List<FillUnitSave> Units { get; set; } = [];
}

public sealed class FillUnits(Entity owner, FillUnitsDef def) : Component<FillUnitsDef, FillUnitsSave>(owner, def)
{
    public IReadOnlyList<FillUnit> Units { get; } = def.Units.Select(u => new FillUnit(u)).ToArray();

    public FillUnit? Unit(string? id) => id == null ? null : Units.FirstOrDefault(u => u.Def.Id == id);

    // By fill type, across the units: a silo's bins, a production's stock.

    /// <summary>Whether some unit takes <paramref name="fillType"/>.</summary>
    public bool Keeps(string fillType) => Units.Any(u => u.Accepts(fillType));

    /// <summary>What the units hold of <paramref name="fillType"/>.</summary>
    public float Level(string fillType) => Units.Where(u => u.FillType == fillType).Sum(u => u.Level);

    /// <summary>What they have room for: in the units holding it, and the empty ones that take it.</summary>
    public float Free(string fillType) => Units.Where(u => u.CanAccept(fillType)).Sum(u => u.Free);

    /// <summary>Puts up to <paramref name="amount"/> into the units taking it, those already holding it first; returns what fitted.</summary>
    public float Add(string fillType, float amount)
    {
        var added = 0f;
        foreach (var u in Units.Where(u => u.CanAccept(fillType)).OrderByDescending(u => u.FillType == fillType))
        {
            if (amount - added <= 0f) break;
            added += u.Add(fillType, amount - added);
        }
        return added;
    }

    /// <summary>Takes up to <paramref name="amount"/> out of the units holding it; returns what there was.</summary>
    public float Remove(string fillType, float amount)
    {
        var removed = 0f;
        foreach (var u in Units.Where(u => u.FillType == fillType))
        {
            if (amount - removed <= 0f) break;
            removed += u.Remove(amount - removed);
        }
        return removed;
    }

    protected override FillUnitsSave Capture(ContentDatabase content) =>
        new() { Units = Units.Select(u => new FillUnitSave { Id = u.Def.Id, FillType = u.FillType, Level = u.Level }).ToList() };

    protected override void Restore(FillUnitsSave save, SaveContext context)
    {
        foreach (var u in save.Units)
        {
            var held = u.FillType != null && u.Level >= 1f;
            if (Unit(u.Id) is not { } unit)
            {
                if (held) context.Warnings.Add($"{Owner.Name} no longer keeps '{u.Id}': {u.Level:N0} of {u.FillType} was lost");
                continue;
            }
            if (u.FillType != null && !context.Content.FillTypes.ContainsKey(u.FillType))
            {
                context.Warnings.Add($"Fill type '{u.FillType}' no longer exists: {Owner.Name} was emptied");
                unit.Remove(unit.Level);
                continue;
            }
            if (held && !unit.Accepts(u.FillType!))
            {
                context.Warnings.Add($"{Owner.Name} no longer keeps {u.FillType} in '{u.Id}': {u.Level:N0} was lost");
                continue;
            }
            unit.Level = Math.Clamp(u.Level, 0f, unit.Capacity);
            unit.FillType = unit.Level > 0f ? u.FillType : null;
        }
    }
}

using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Machines;

namespace Headland.Core.World;

/// <summary>
/// Heaps on the ground (FS: the density map heights): grain tipped off a trailer or dumped from a bucket lies where it
/// fell, one fill type to a cell (<see cref="FieldLayers.Heap"/>, its height, and <see cref="FieldLayers.HeapFill"/>),
/// its sides no steeper than its fill type's angle: what stands steeper slides down, a little every tick. A bucket
/// scoops it back up. Only a fill type with a heap (filltypes.json) lies on the ground, and only on its farm's own land
/// (FS: tipping needs access to the land).
/// </summary>
public sealed class Heaps
{
    /// <summary>Less than this on a cell (meters) counts as nothing.</summary>
    public const float Trace = 0.0005f;
    /// <summary>Cells a tick looks at to let what stands too steep slide down.</summary>
    private const int SettlePerTick = 4000;
    /// <summary>A step steeper than the heap's angle by less than this (meters) is left as it is.</summary>
    private const float Tolerance = 0.002f;
    private static readonly float Diagonal = WorldMap.CellSize * MathF.Sqrt(2f);

    private readonly Simulation _sim;
    private readonly Queue<int> _pending = new();
    private readonly bool[] _queued;
    /// <summary>By fill byte (index + 1): the slope its sides stand at, and units on a cell per meter of height.</summary>
    private readonly float[] _slope;
    private readonly float[] _perMeter;
    private readonly Dictionary<Machine, (string fillType, float amount)> _tipped = new();
    private readonly Dictionary<Machine, (string fillType, float amount)> _scooped = new();
    private readonly HashSet<Machine> _active = [];

    public Heaps(Simulation sim)
    {
        _sim = sim;
        _queued = new bool[sim.World.Layers.Heap.Length];
        var types = sim.Content.FillTypeList;
        _slope = new float[types.Count + 1];
        _perMeter = new float[types.Count + 1];
        for (var k = 0; k < types.Count; k++)
        {
            if (types[k].Heap is not { } h) continue;
            _slope[k + 1] = MathF.Tan(h.AngleDeg * MathUtil.Deg2Rad);
            _perMeter[k + 1] = h.PerCubicMeter * WorldMap.CellArea;
        }
    }

    private WorldMap World => _sim.World;
    private FieldLayers L => _sim.World.Layers;

    /// <summary>Whether <paramref name="fillType"/> lies in heaps on the ground.</summary>
    public bool Lies(string fillType) => _sim.Content.FillTypes.TryGetValue(fillType, out var ft) && ft.Heap != null;

    /// <summary>Whether a heap lies on cell <paramref name="i"/>.</summary>
    public bool Has(int i) => L.HeapFill[i] != 0 && L.Heap[i] >= Trace && _perMeter[L.HeapFill[i]] > 0f;

    /// <summary>What the heap on cell <paramref name="i"/> is of, if any.</summary>
    public FillTypeDef? FillTypeAt(int i) => Has(i) ? _sim.Content.FillTypeList[L.HeapFill[i] - 1] : null;

    /// <summary>Units of the heap on cell <paramref name="i"/>.</summary>
    public float AmountAt(int i) => Has(i) ? L.Heap[i] * _perMeter[L.HeapFill[i]] : 0f;

    /// <summary>The top of the heap on cell <paramref name="i"/>, or the ground there.</summary>
    private float Top(int i) => World.CellHeight(i) + L.Heap[i];

    /// <summary>
    /// Why <paramref name="farmId"/> can't tip <paramref name="fillType"/> on the ground along <paramref name="a"/> →
    /// <paramref name="b"/>, or null if it can: it doesn't lie in heaps, or the land isn't the farm's.
    /// </summary>
    public string? DropBlocker(int farmId, Vector2 a, Vector2 b, string fillType)
    {
        if (!Lies(fillType)) return $"{_sim.Content.FillTypes[fillType].Name} can't be tipped on the ground";
        if (!_sim.Farms.Owns(farmId, a) || !_sim.Farms.Owns(farmId, b)) return "You can only tip on your own land";
        return null;
    }

    /// <summary>
    /// Puts up to <paramref name="amount"/> of <paramref name="fillType"/> on the ground along <paramref name="a"/> →
    /// <paramref name="b"/>, tipped by <paramref name="by"/>; returns what it put there. A cell where the heap is up to
    /// <paramref name="top"/> (the outlet it falls from) takes no more, nor one holding another fill type. Check
    /// <see cref="DropBlocker"/> first.
    /// </summary>
    public float Drop(Machine by, Vector2 a, Vector2 b, string fillType, float amount, float top = float.PositiveInfinity)
    {
        if (amount <= 0f || !Lies(fillType)) return 0f;
        var fill = Windrows.FillOf(_sim.Content, fillType);
        var cells = new List<int>();
        foreach (var i in CellsAlong(a, b))
            if ((L.HeapFill[i] == fill || !Has(i)) && Top(i) < top) cells.Add(i);
        if (cells.Count == 0) return 0f;
        var per = _perMeter[fill];
        var share = amount / cells.Count / per;
        var placed = 0f;
        foreach (var i in cells)
        {
            if (L.HeapFill[i] != fill) L.Heap[i] = 0f;
            var h = MathF.Min(share, top - Top(i));
            L.HeapFill[i] = fill;
            L.Heap[i] += h;
            placed += h * per;
            Changed(i);
        }
        Record(_tipped, by, fillType, placed);
        return placed;
    }

    /// <summary>
    /// Takes what lies above <paramref name="level"/> (a bucket's edge) on <paramref name="cells"/>, up to
    /// <paramref name="max"/> units, for <paramref name="by"/>: of <paramref name="fillType"/>, or with none, of the
    /// first heap it meets that <paramref name="accepts"/> takes. Returns how much, and of what.
    /// </summary>
    public (string? fillType, float amount) TakeAbove(Machine by, IEnumerable<int> cells, string? fillType, float level, float max,
        Func<string, bool> accepts)
    {
        var taken = 0f;
        foreach (var i in cells)
        {
            if (taken >= max) break;
            if (FillTypeAt(i) is not { } ft) continue;
            if (fillType == null)
            {
                if (!accepts(ft.Id)) continue;
                fillType = ft.Id;
            }
            else if (ft.Id != fillType) continue;
            var above = Top(i) - level;
            if (above <= 0f) continue;
            var per = _perMeter[L.HeapFill[i]];
            var h = MathF.Min(MathF.Min(above, L.Heap[i]), (max - taken) / per);
            L.Heap[i] -= h;
            taken += h * per;
            if (L.Heap[i] < Trace) Clear(i);
            Changed(i);
            // What stood around it slides into the cut.
            foreach (var n in Neighbors(i))
                if (L.Heap[n] > 0f) Queue(n);
        }
        if (fillType != null) Record(_scooped, by, fillType, taken);
        return (fillType, taken);
    }

    /// <summary>The cells under a line from <paramref name="a"/> to <paramref name="b"/>, each once.</summary>
    private List<int> CellsAlong(Vector2 a, Vector2 b)
    {
        var cells = new List<int>();
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / (WorldMap.CellSize * 0.5f)));
        for (var k = 0; k <= steps; k++)
        {
            var (cx, cz) = World.WorldToCell(Vector2.Lerp(a, b, (float)k / steps));
            if (!World.InBounds(cx, cz)) continue;
            var i = World.CellIndex(cx, cz);
            if (!cells.Contains(i)) cells.Add(i);
        }
        return cells;
    }

    private IEnumerable<int> Neighbors(int i)
    {
        var (cx, cz) = (i % World.CellsX, i / World.CellsX);
        for (var dz = -1; dz <= 1; dz++)
        for (var dx = -1; dx <= 1; dx++)
            if ((dx != 0 || dz != 0) && World.InBounds(cx + dx, cz + dz))
                yield return World.CellIndex(cx + dx, cz + dz);
    }

    private void Changed(int i)
    {
        World.MarkHeapDirty(i);
        Queue(i);
    }

    private void Queue(int i)
    {
        if (_queued[i]) return;
        _queued[i] = true;
        _pending.Enqueue(i);
    }

    private void Clear(int i)
    {
        L.Heap[i] = 0f;
        L.HeapFill[i] = 0;
    }

    /// <summary>
    /// Lets what stands steeper than its angle on cell <paramref name="i"/> slide onto the cells around it that are
    /// lower by more than the angle allows (a cell holding another fill type stops it, as a wall would).
    /// </summary>
    private void Settle(int i)
    {
        var h = L.Heap[i];
        var fill = L.HeapFill[i];
        if (h <= 0f || fill == 0 || _perMeter[fill] <= 0f) return;
        var slope = _slope[fill];
        var top = Top(i);
        Span<(int n, float excess)> lower = stackalloc (int, float)[8];
        var count = 0;
        var (total, most) = (0f, 0f);
        var (cx, cz) = (i % World.CellsX, i / World.CellsX);
        for (var dz = -1; dz <= 1; dz++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0 || !World.InBounds(cx + dx, cz + dz)) continue;
            var n = World.CellIndex(cx + dx, cz + dz);
            if (L.HeapFill[n] != fill && Has(n)) continue;
            var excess = top - Top(n) - slope * (dx != 0 && dz != 0 ? Diagonal : WorldMap.CellSize);
            if (excess <= Tolerance) continue;
            lower[count++] = (n, excess);
            total += excess;
            most = MathF.Max(most, excess);
        }
        if (count == 0) return;
        // Half the steepest step at most, so no neighbor ends up above where this cell comes down to.
        var move = MathF.Min(h, most * 0.5f);
        foreach (var (n, excess) in lower[..count])
        {
            if (L.HeapFill[n] != fill) L.Heap[n] = 0f;
            L.HeapFill[n] = fill;
            L.Heap[n] += move * excess / total;
            Changed(n);
        }
        L.Heap[i] -= move;
        if (L.Heap[i] < 1e-6f) Clear(i);
        Changed(i);
    }

    /// <summary>Lets heaps slide down until every one stands at its angle (a few ticks' work at most, in play).</summary>
    public void SettleAll()
    {
        while (_pending.Count > 0) SettleNext();
    }

    private void SettleNext()
    {
        var i = _pending.Dequeue();
        _queued[i] = false;
        Settle(i);
    }

    private void Record(Dictionary<Machine, (string fillType, float amount)> runs, Machine by, string fillType, float amount)
    {
        if (amount <= 0f) return;
        if (runs.TryGetValue(by, out var run) && run.fillType != fillType) Flush(by);
        runs[by] = runs.TryGetValue(by, out run) ? (fillType, run.amount + amount) : (fillType, amount);
        _active.Add(by);
    }

    /// <summary>
    /// After the machines moved: heaps slide down a little, and the machines that stopped tipping or scooping this tick
    /// say how much they did.
    /// </summary>
    internal void Update()
    {
        for (var k = 0; k < SettlePerTick && _pending.Count > 0; k++) SettleNext();
        foreach (var m in _tipped.Keys.Concat(_scooped.Keys).Where(m => !_active.Contains(m)).Distinct().ToList()) Flush(m);
        _active.Clear();
    }

    /// <summary>A machine leaving the map: what it tipped or scooped is told.</summary>
    internal void Forget(Machine m) => Flush(m);

    private void Flush(Machine m)
    {
        if (_tipped.Remove(m, out var tipped) && tipped.amount >= 1f) _sim.Events.Publish(new FillTippedOnGround(m, tipped.fillType, tipped.amount));
        if (_scooped.Remove(m, out var scooped) && scooped.amount >= 1f) _sim.Events.Publish(new HeapScooped(m, scooped.fillType, scooped.amount));
    }
}

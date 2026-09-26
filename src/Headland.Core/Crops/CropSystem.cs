using Headland.Core.Content;
using Headland.Core.Time;
using Headland.Core.Weather;
using Headland.Core.World;

namespace Headland.Core.Crops;

/// <summary>
/// Hourly soil and crop model:
/// water balance (rain/melt in, evapotranspiration and drainage out), growing degree-days scaled by a water factor,
/// stage advance with nitrogen uptake, and health stress (drought, waterlogging, frost, nitrogen shortfall).
/// </summary>
public sealed class CropSystem
{
    private readonly ContentDatabase _content;
    private readonly WorldMap _world;
    private readonly Calendar _calendar;
    private readonly ClimateDef _climate;
    private readonly float[] _mineralAcc;
    private readonly bool[] _growingChunk;

    public CropSystem(ContentDatabase content, WorldMap world, Calendar calendar, ClimateDef climate)
    {
        _content = content;
        _world = world;
        _calendar = calendar;
        _climate = climate;
        _mineralAcc = new float[content.Soils.Count];
        _growingChunk = new bool[world.ChunksX * world.ChunksZ];
        CoverLut = BuildCoverLut(content);
    }

    /// <summary>Nitrogen mineralization carried over between hours, per soil index (for saves).</summary>
    internal float[] MineralAccumulators => _mineralAcc;

    /// <summary>Crop cover for the terrain shader, indexed by (crop &lt;&lt; 8) | stage.</summary>
    public byte[] CoverLut { get; }

    public int LastStageChanges { get; private set; }
    public int LastDeaths { get; private set; }

    public static byte[] BuildCoverLut(ContentDatabase content)
    {
        var lut = new byte[256 * 256];
        for (var c = 0; c < content.Crops.Count; c++)
        {
            var def = content.Crops[c];
            var maxH = def.Stages.Max(s => s.Height);
            for (var s = 0; s < def.Stages.Length; s++)
                lut[((c + 1) << 8) | s] = (byte)(255f * MathUtil.Saturate(def.Stages[s].Height / MathF.Max(0.01f, maxH)));
            lut[((c + 1) << 8) | CropStage.Dead] = 90;
        }
        return lut;
    }

    /// <summary>Health lost per real day below the wilting point (fraction of full health).</summary>
    public const float DroughtLossPerDay = 0.01f;
    /// <summary>Health lost per real day in saturated soil.</summary>
    public const float WaterlogLossPerDay = 0.005f;
    /// <summary>Health lost at a stage change when no nitrogen at all is available (scaled by the shortfall).</summary>
    public const float NitrogenShortfallLoss = 0.1f;

    /// <summary>Temperatures that count toward vernalization.</summary>
    public static bool IsChilling(float tempC) => tempC is > -4f and < 8f;

    public static float WaterFactor(float m, CropDef def)
    {
        if (m < def.WiltingPoint) return 0.1f;
        if (m < def.OptimalMoistureMin)
            return MathUtil.Lerp(0.3f, 1f, MathUtil.InverseLerp(def.WiltingPoint, def.OptimalMoistureMin, m));
        if (m <= def.OptimalMoistureMax) return 1f;
        return MathUtil.Lerp(1f, 0.6f, MathUtil.InverseLerp(def.OptimalMoistureMax, 1f, m));
    }

    /// <summary>Crop water use coefficient by stage progress (0..1 through the growing stages).</summary>
    private static float CropKc(float stageFraction) =>
        stageFraction < 0.6f ? MathUtil.Lerp(0.4f, 1.1f, stageFraction / 0.6f) : MathUtil.Lerp(1.1f, 0.6f, (stageFraction - 0.6f) / 0.4f);

    /// <summary>Runs one game hour. <paramref name="weather"/> must already hold that hour's snapshot.</summary>
    public void TickHour(WeatherSystem weather, int dayIndex, long hourIndex)
    {
        var L = _world.Layers;
        var soils = _content.Soils;
        var crops = _content.Crops;
        var realDaysPerHour = _calendar.RealDaysPerGameDay / 24f;

        var temp = weather.Temperature;
        var waterIn = weather.RainMm + weather.MeltMm;
        var dayMean = weather.GetDay(dayIndex).TempMean;
        var etRealDay = MathF.Max(0f, 0.16f * (dayMean + 2f));
        var etHour = etRealDay * realDaysPerHour * (1f - 0.4f * weather.Cloudiness);

        // Nitrogen mineralization: integer steps from a per-soil accumulator.
        var mineralSteps = new int[soils.Count];
        for (var s = 0; s < soils.Count; s++)
        {
            _mineralAcc[s] += soils[s].MineralizationPerMonth / (_calendar.DaysPerMonth * 24f);
            mineralSteps[s] = (int)_mineralAcc[s];
            _mineralAcc[s] -= mineralSteps[s];
        }

        var stageChanges = 0;
        var deaths = 0;
        var hourSeed = (int)(hourIndex & 0x7FFFFFFF);
        Array.Clear(_growingChunk);

        Parallel.For(0, _world.CellsZ, () => (changes: 0, deaths: 0), (cz, _, acc) =>
        {
            var row = cz * _world.CellsX;
            for (var cx = 0; cx < _world.CellsX; cx++)
            {
                var i = row + cx;
                var g = (GroundType)L.Ground[i];
                if (WorldMap.IsSealed(g)) continue;

                var soil = soils[L.Soil[i]];
                var m = L.Moisture[i] / 255f;
                var cropId = L.Crop[i];
                var stage = L.Stage[i];
                var alive = cropId != 0 && stage != CropStage.Dead;
                CropDef? def = alive ? crops[cropId - 1] : null;

                // --- Water balance ---
                m += waterIn * soil.Infiltration / soil.WaterCapacityMm;
                float kc;
                if (def != null)
                    kc = CropKc(stage / (float)Math.Max(1, def.HarvestableStage));
                else
                    kc = g is GroundType.Grass or GroundType.Forest ? 0.9f : 0.45f;
                // Plants transpire less as the soil dries out.
                var dryLimit = m < 0.45f ? m / 0.45f : 1f;
                m -= etHour * kc * dryLimit / soil.WaterCapacityMm;
                if (m > soil.FieldCapacity) m -= (m - soil.FieldCapacity) * soil.DrainageRate;
                m = MathUtil.Saturate(m);
                L.Moisture[i] = (byte)(m * 255f + 0.5f);

                // --- Nitrogen mineralization ---
                var ms = mineralSteps[L.Soil[i]];
                if (ms > 0 && L.Nitrogen[i] < soil.InitialNitrogen)
                    L.Nitrogen[i] = (byte)Math.Min(255, L.Nitrogen[i] + ms);

                if (def == null) continue;

                // --- Stress ---
                var dither = Rng.Hash01(i, hourSeed, 77);
                float loss = 0f;
                if (temp < def.FrostKillC) loss += 70f * MathUtil.Saturate((def.FrostKillC - temp) / 3f + 0.3f);
                if (m < def.WiltingPoint) loss += 255f * DroughtLossPerDay * realDaysPerHour;
                else if (m > 0.95f) loss += 255f * WaterlogLossPerDay * realDaysPerHour;
                var health = (int)L.Health[i];

                // --- Vernalization: winter crops count cold days until they are ready to bolt ---
                var vernalized = def.VernalizationDays <= 0f || L.Chill[i] >= def.VernalizationDays;
                if (!vernalized && IsChilling(temp))
                {
                    var chill = L.Chill[i] + (int)(realDaysPerHour + Rng.Hash01(i, hourSeed, 31));
                    L.Chill[i] = (byte)Math.Min(255, chill);
                }

                // --- Growth: degree-days scaled by water ---
                var stageDef = def.Stages[stage];
                if (!stageDef.Harvestable)
                {
                    var gdd = MathF.Max(0f, temp - def.BaseTempC) * realDaysPerHour;
                    var need = stageDef.Gdd;
                    // Development follows temperature; water stress only slows it a little (it mostly costs yield).
                    var progress = L.Progress[i] + gdd * MathUtil.Lerp(0.75f, 1f, WaterFactor(m, def));
                    // A winter crop waits at the end of its stage until vernalized.
                    if (!vernalized && def.Stages[stage + 1].RequiresVernalization) progress = MathF.Min(progress, need);
                    else if (progress >= need)
                    {
                        progress -= need;
                        stage++;
                        acc.changes++;
                        // Nitrogen uptake for the stage just completed.
                        var nNeed = def.NitrogenDemandKgPerHa / Math.Max(1, def.HarvestableStage);
                        var n = L.Nitrogen[i];
                        if (n >= nNeed) L.Nitrogen[i] = (byte)(n - nNeed);
                        else
                        {
                            var shortfall = (nNeed - n) / nNeed;
                            L.Nitrogen[i] = 0;
                            loss += shortfall * NitrogenShortfallLoss * 255f;
                        }
                        L.Stage[i] = stage;
                        _world.MarkCellDirty(cx, cz, crop: true);
                    }
                    L.Progress[i] = progress;
                    _growingChunk[(cz / WorldMap.ChunkCells) * _world.ChunksX + cx / WorldMap.ChunkCells] = true;
                }

                health -= (int)(loss + dither);
                if (health <= 0)
                {
                    L.Health[i] = 0;
                    L.Stage[i] = CropStage.Dead;
                    acc.deaths++;
                    _world.MarkCellDirty(cx, cz, crop: true);
                }
                else L.Health[i] = (byte)health;
            }
            return acc;
        }, acc =>
        {
            Interlocked.Add(ref stageChanges, acc.changes);
            Interlocked.Add(ref deaths, acc.deaths);
        });

        LastStageChanges = stageChanges;
        LastDeaths = deaths;
        // Moisture changed everywhere: every ground texture needs a refresh.
        _world.MarkAllDirty(crop: false);
        // Growing crops get taller within their stage: refresh those chunks' instances.
        for (var c = 0; c < _growingChunk.Length; c++)
            if (_growingChunk[c]) _world.CropDirty[c] = true;
    }

    /// <summary>Rough estimate of game days until the crop in a cell becomes harvestable, from climate means.</summary>
    public float EstimateDaysToHarvest(int cellIndex, GameClock clock)
    {
        var L = _world.Layers;
        var cropId = L.Crop[cellIndex];
        var stage = L.Stage[cellIndex];
        if (cropId == 0 || stage == CropStage.Dead) return float.NaN;
        var def = _content.Crops[cropId - 1];
        if (def.Stages[stage].Harvestable) return 0f;

        // Walk the remaining stages day by day with climate-average temperatures.
        var climate = _climate;
        var day = clock.DayIndex;
        var s = (int)stage;
        var progress = L.Progress[cellIndex];
        var chill = (float)L.Chill[cellIndex];
        for (var d = 0; d < _calendar.DaysPerYear * 2; d++)
        {
            var date = _calendar.DateOfDay(day + d);
            var mf = date.Month + (date.Day - 0.5f) / _calendar.DaysPerMonth;
            var t = WeatherSystem.MonthlyLerp(climate.MonthlyMeanTemp, mf);
            // Share of a day's hours inside the chilling range, around the daily mean.
            chill += _calendar.RealDaysPerGameDay * MathUtil.Saturate((10f - t) / 6f) * (t > -6f ? 1f : 0f);
            progress += MathF.Max(0f, t - def.BaseTempC) * _calendar.RealDaysPerGameDay;
            while (s < def.HarvestableStage && progress >= def.Stages[s].Gdd)
            {
                if (def.Stages[s + 1].RequiresVernalization && chill < def.VernalizationDays)
                {
                    progress = def.Stages[s].Gdd;
                    break;
                }
                progress -= def.Stages[s].Gdd;
                s++;
            }
            if (s >= def.HarvestableStage) return d + 1;
        }
        return float.PositiveInfinity;
    }

    public bool InSowingWindow(CropDef def, int month) => def.SowingMonths.Length == 0 || def.SowingMonths.Contains(month);
}

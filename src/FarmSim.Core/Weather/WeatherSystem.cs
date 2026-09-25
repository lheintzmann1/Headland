using FarmSim.Core.Content;
using FarmSim.Core.Time;

namespace FarmSim.Core.Weather;

public enum WeatherCondition { Clear, Cloudy, Rain, Storm, Snow, Fog }

/// <summary>One game day of weather, generated ahead of time so it can be forecast.</summary>
public sealed class DayWeather
{
    public int DayIndex { get; init; }
    public float TempMean { get; init; }
    public float TempRange { get; init; }
    public float PrecipMm { get; init; }
    public int PrecipStartHour { get; init; }
    public int PrecipHours { get; init; }
    public float Cloudiness { get; init; }
    /// <summary>Morning fog lasts until this hour (0 = no fog).</summary>
    public int FogUntilHour { get; init; }
    public float Wind { get; init; }
    public bool Stormy { get; init; }

    public float TempMin => TempMean - TempRange * 0.5f;
    public float TempMax => TempMean + TempRange * 0.5f;

    public bool IsPrecipitating(float hour) =>
        PrecipHours > 0 && hour >= PrecipStartHour && hour < PrecipStartHour + PrecipHours;

    public float PrecipRate => PrecipHours > 0 ? PrecipMm / PrecipHours : 0f;

    /// <summary>Dominant condition for icons/forecast.</summary>
    public WeatherCondition Summary
    {
        get
        {
            if (PrecipMm > 0.5f) return TempMean < 1f ? WeatherCondition.Snow : Stormy ? WeatherCondition.Storm : WeatherCondition.Rain;
            if (FogUntilHour > 0) return WeatherCondition.Fog;
            return Cloudiness > 0.55f ? WeatherCondition.Cloudy : WeatherCondition.Clear;
        }
    }
}

/// <summary>
/// Single-cell weather for the whole map, generated per day from a climate definition.
/// Also tracks surface state: visible wetness (puddles), snow cover and the snowpack water.
/// </summary>
public sealed class WeatherSystem
{
    private readonly ClimateDef _climate;
    private readonly Calendar _calendar;
    private readonly Rng _rng;
    private readonly List<DayWeather> _days = [];
    private float _anomaly;

    public WeatherSystem(ClimateDef climate, Calendar calendar, ulong seed)
    {
        _climate = climate;
        _calendar = calendar;
        _rng = new Rng(seed);
    }

    // ---- Current hourly snapshot (refreshed by Update) ----
    public WeatherCondition Condition { get; private set; }
    public float Temperature { get; private set; }
    /// <summary>Liquid precipitation this game hour (mm).</summary>
    public float RainMm { get; private set; }
    /// <summary>Snow (water equivalent) this game hour (mm).</summary>
    public float SnowMm { get; private set; }
    public float Cloudiness { get; private set; }
    public float Fog { get; private set; }
    public float Wind { get; private set; }

    // ---- Surface state ----
    /// <summary>Visible surface wetness 0..1 (dark soil, wet asphalt, puddles).</summary>
    public float GroundWetness { get; set; }
    /// <summary>Visible snow cover 0..1.</summary>
    public float SnowCover { get; set; }
    /// <summary>Water stored in snow (mm), released into the soil when it melts.</summary>
    public float SnowpackMm { get; set; }
    /// <summary>Snow melt released during the last hourly tick (mm).</summary>
    public float MeltMm { get; private set; }

    /// <summary>0..1 precipitation intensity for effects.</summary>
    public float PrecipIntensity => MathUtil.Saturate((RainMm + SnowMm) / 6f);

    public DayWeather GetDay(int dayIndex)
    {
        EnsureDays(Math.Max(0, dayIndex) + 3);
        return _days[Math.Max(0, dayIndex)];
    }

    public IEnumerable<DayWeather> Forecast(int fromDay, int count)
    {
        for (var i = 0; i < count; i++) yield return GetDay(fromDay + i);
    }

    /// <summary>Monthly climate value interpolated between month centers.</summary>
    public static float MonthlyLerp(float[] values, float monthFloat)
    {
        var m = monthFloat - 1.5f; // month centers at x.5
        var i0 = (int)MathF.Floor(m);
        var t = m - i0;
        var a = values[((i0 % 12) + 12) % 12];
        var b = values[(((i0 + 1) % 12) + 12) % 12];
        return MathUtil.Lerp(a, b, t);
    }

    public float TemperatureAt(int dayIndex, float hourOfDay)
    {
        var d = GetDay(dayIndex);
        return d.TempMean + d.TempRange * 0.5f * MathF.Cos(MathF.Tau * (hourOfDay - 15f) / 24f);
    }

    public (float sunrise, float sunset) SunTimes(float monthFloat) =>
        (MonthlyLerp(_climate.SunriseHour, monthFloat), MonthlyLerp(_climate.SunsetHour, monthFloat));

    /// <summary>Refreshes the snapshot for a moment in time. Cheap: call every frame for smooth values.</summary>
    public void Update(int dayIndex, float hour)
    {
        var day = GetDay(dayIndex);
        Temperature = TemperatureAt(dayIndex, hour);
        Wind = day.Wind;

        var precip = day.IsPrecipitating(hour);
        var rate = precip ? day.PrecipRate : 0f;
        var snowing = precip && Temperature < 0.5f;
        RainMm = snowing ? 0f : rate;
        SnowMm = snowing ? rate : 0f;

        var fog = !precip && day.FogUntilHour > 0 && hour >= 3f && hour < day.FogUntilHour
            ? MathUtil.SmoothStep(day.FogUntilHour, day.FogUntilHour - 1.5f, hour)
            : 0f;
        Fog = fog;

        Cloudiness = precip ? MathF.Max(day.Cloudiness, 0.85f) : day.Cloudiness;
        Condition = precip
            ? snowing ? WeatherCondition.Snow : day.Stormy ? WeatherCondition.Storm : WeatherCondition.Rain
            : fog > 0.3f ? WeatherCondition.Fog
            : Cloudiness > 0.55f ? WeatherCondition.Cloudy
            : WeatherCondition.Clear;
    }

    /// <summary>Overrides the current snapshot (tests and scripted scenarios).</summary>
    public void ForceSnapshot(float temperature, float rainMm = 0f, float snowMm = 0f, float cloudiness = 0.3f)
    {
        Temperature = temperature;
        RainMm = rainMm;
        SnowMm = snowMm;
        Cloudiness = cloudiness;
        MeltMm = 0f;
        Fog = 0f;
        Condition = snowMm > 0 ? WeatherCondition.Snow : rainMm > 0 ? WeatherCondition.Rain : WeatherCondition.Clear;
    }

    /// <summary>Hourly surface update (wetness, snow). Call once per crossed game hour, after Update.</summary>
    public void TickHour()
    {
        var dry = (0.015f + 0.004f * MathF.Max(Temperature, 0f)) * (1f - 0.5f * Cloudiness);
        GroundWetness = MathUtil.Saturate(GroundWetness + RainMm * 0.12f - dry);

        SnowpackMm += SnowMm;
        MeltMm = 0f;
        if (Temperature > 0.5f && SnowpackMm > 0f)
        {
            MeltMm = MathF.Min(SnowpackMm, 0.4f * (Temperature - 0.5f) + RainMm * 0.1f);
            SnowpackMm -= MeltMm;
            GroundWetness = MathUtil.Saturate(GroundWetness + MeltMm * 0.1f);
        }
        SnowCover = MathUtil.Saturate(SnowpackMm / 8f);
    }

    /// <summary>Days are generated in order from day 0 so the sequence (and the forecast) depends only on the seed.</summary>
    private void EnsureDays(int lastDay)
    {
        if (_days.Count == 0) _days.Add(Generate(0));
        while (_days[^1].DayIndex < lastDay) _days.Add(Generate(_days[^1].DayIndex + 1));
    }

    private DayWeather Generate(int dayIndex)
    {
        var date = _calendar.DateOfDay(dayIndex);
        var monthFloat = date.Month + (date.Day - 0.5f) / _calendar.DaysPerMonth;
        var mi = date.Month - 1;

        var p = _climate.AnomalyPersistence;
        _anomaly = p * _anomaly + MathF.Sqrt(1f - p * p) * _climate.AnomalyStdDev * _rng.NextGaussian();

        var mean = MonthlyLerp(_climate.MonthlyMeanTemp, monthFloat) + _anomaly;
        var range = MonthlyLerp(_climate.MonthlyDailyRange, monthFloat) * _rng.Range(0.7f, 1.2f);

        var chance = _climate.MonthlyPrecipChance[mi];
        float precip = 0f;
        int start = 0, hours = 0;
        var stormy = false;
        if (_rng.Chance(chance))
        {
            var meanAmount = _climate.MonthlyPrecipMm[mi] / MathF.Max(0.2f, _calendar.DaysPerMonth * chance);
            precip = MathF.Max(1f, _rng.NextExponential(meanAmount));
            hours = Math.Clamp((int)(precip / _rng.Range(2.5f, 6f)) + 2, 2, 16);
            start = _rng.Range(0, 24 - hours + 1);
            stormy = mean > 14f && precip > meanAmount * 1.3f && _rng.Chance(0.6f);
            range *= 0.6f; // cloudy days vary less
        }

        var cloud = precip > 0f ? _rng.Range(0.8f, 1f) : MathF.Pow(_rng.NextFloat(), 1.3f) * 0.85f;
        var fogUntil = precip <= 0f && _rng.Chance(_climate.MonthlyFogChance[mi]) ? _rng.Range(7, 11) : 0;
        var wind = stormy ? _rng.Range(0.75f, 1f) : precip > 0 ? _rng.Range(0.3f, 0.6f) : _rng.Range(0.05f, 0.4f);

        return new DayWeather
        {
            DayIndex = dayIndex,
            TempMean = mean,
            TempRange = range,
            PrecipMm = precip,
            PrecipStartHour = start,
            PrecipHours = hours,
            Cloudiness = cloud,
            FogUntilHour = fogUntil,
            Wind = wind,
            Stormy = stormy,
        };
    }
}

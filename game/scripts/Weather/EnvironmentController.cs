using Headland.Core;
using Headland.Core.Weather;
using Godot;
using GEnvironment = Godot.Environment;

namespace Headland.Game.Weather;

/// <summary>
/// Sun/moon from time of day and season, ambient light, haze and fog, muted color grading,
/// global shader parameters (wetness, snow, clouds, wind) and a screen-space precipitation overlay.
/// </summary>
public partial class EnvironmentController : Node3D
{
    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _moon = null!;
    private GEnvironment _env = null!;
    private ShaderMaterial _overlay = null!;
    private float _wetness;
    private float _snow;
    private float _cloud;
    private float _rain;
    private float _snowFall;
    private float _fog;
    private Vector2 _cloudOffset;
    private double _lastClockSeconds = double.NaN;

    public Simulation Sim { get; init; } = null!;
    /// <summary>Sun shadows (the settings' shadow quality sets the atlas; "off" clears this).</summary>
    public bool Shadows { get; init; } = true;

    /// <summary>0 at night, 1 in full daylight (for UI and effects).</summary>
    public float Daylight { get; private set; } = 1f;

    /// <summary>Latest daylight value, for visuals such as headlights.</summary>
    public static float CurrentDaylight { get; private set; } = 1f;

    public override void _Ready()
    {
        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = Shadows,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 380f,
            ShadowBlur = 1.6f,
            LightAngularDistance = 0.8f,
        };
        _moon = new DirectionalLight3D { Name = "Moon", ShadowEnabled = false, LightColor = new Color(0.55f, 0.62f, 0.82f) };
        AddChild(_sun);
        AddChild(_moon);

        _env = new GEnvironment
        {
            BackgroundMode = GEnvironment.BGMode.Color,
            AmbientLightSource = GEnvironment.AmbientSource.Color,
            ReflectedLightSource = GEnvironment.ReflectionSource.Disabled,
            TonemapMode = GEnvironment.ToneMapper.Agx,
            TonemapExposure = 1.05f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 0.85f,
            AdjustmentContrast = 1.06f,
            FogEnabled = true,
            FogDensity = 0.0004f,
            FogSkyAffect = 0f,
        };
        AddChild(new WorldEnvironment { Name = "Environment", Environment = _env });

        var layer = new CanvasLayer { Name = "WeatherOverlay", Layer = 0 };
        _overlay = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/weather_overlay.gdshader") };
        var rect = new ColorRect { Material = _overlay, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(rect);
        AddChild(layer);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var clock = Sim.Clock;
        var w = Sim.Weather;
        var monthF = clock.MonthFloat;
        // After a time skip (sleeping), jump straight to the new weather instead of easing into it.
        var jumped = !double.IsNaN(_lastClockSeconds) && clock.TotalSeconds - _lastClockSeconds > 3600.0;
        _lastClockSeconds = clock.TotalSeconds;

        // --- Sun path: rises in the east (+x), peaks in the south (+z), sets in the west.
        var (sunrise, sunset) = w.SunTimes(monthF);
        var declination = 23.4f * Mathf.Sin(Mathf.Tau * (monthF - 3.7f) / 12f);
        var noon = Mathf.DegToRad(40f + declination);
        var t = (clock.HourOfDay - sunrise) / (sunset - sunrise);
        var elevation = t is > 0f and < 1f ? noon * Mathf.Sin(Mathf.Pi * t) : -0.2f;
        var azimuth = Mathf.Pi * Mathf.Clamp(t, 0f, 1f);
        var shadowElev = Mathf.Max(elevation, Mathf.DegToRad(6f));
        var toSun = new Vector3(Mathf.Cos(shadowElev) * Mathf.Cos(azimuth), Mathf.Sin(shadowElev), Mathf.Cos(shadowElev) * Mathf.Sin(azimuth));
        _sun.GlobalTransform = new Transform3D(Basis.LookingAt(-toSun, Vector3.Up), Vector3.Zero);

        var target = w.Cloudiness;
        _cloud = Mathf.Lerp(_cloud, target, 1f - Mathf.Exp(-0.8f * dt));
        Daylight = Mathf.SmoothStep(Mathf.DegToRad(-4f), Mathf.DegToRad(6f), elevation);
        var warmth = 1f - Mathf.SmoothStep(Mathf.DegToRad(2f), Mathf.DegToRad(22f), elevation);
        _sun.LightColor = new Color(1f, 0.95f, 0.88f).Lerp(new Color(1f, 0.64f, 0.4f), warmth);
        _sun.LightEnergy = 1.3f * Daylight * (1f - 0.65f * _cloud) * (1f - 0.5f * _fog);
        _sun.ShadowOpacity = Mathf.Lerp(0.9f, 0.35f, _cloud);
        _moon.LightEnergy = 0.32f * (1f - Daylight);
        _moon.GlobalTransform = new Transform3D(Basis.LookingAt(new Vector3(0.4f, -0.8f, -0.45f), Vector3.Up), Vector3.Zero);

        // --- Ambient: overcast skies give flatter, brighter ambient light.
        var dayAmbient = new Color(0.6f, 0.65f, 0.72f).Lerp(new Color(0.66f, 0.68f, 0.7f), _cloud);
        var nightAmbient = new Color(0.3f, 0.36f, 0.55f);
        _env.AmbientLightColor = nightAmbient.Lerp(dayAmbient, Daylight);
        _env.AmbientLightEnergy = Mathf.Lerp(0.42f, 0.5f + 0.4f * _cloud, Daylight);
        CurrentDaylight = Daylight;
        _env.BackgroundColor = new Color(0.03f, 0.04f, 0.07f).Lerp(new Color(0.5f, 0.56f, 0.62f), Daylight);

        // --- Precipitation and fog, eased so they build up and fade out.
        var rainTarget = w.Condition is WeatherCondition.Rain or WeatherCondition.Storm ? Mathf.Max(0.25f, w.PrecipIntensity) : 0f;
        var snowTarget = w.Condition == WeatherCondition.Snow ? Mathf.Max(0.3f, w.PrecipIntensity) : 0f;
        _rain = Mathf.MoveToward(_rain, rainTarget, dt * 0.25f);
        _snowFall = Mathf.MoveToward(_snowFall, snowTarget, dt * 0.25f);
        _fog = Mathf.MoveToward(_fog, w.Fog, dt * 0.1f);
        if (jumped)
        {
            _rain = rainTarget;
            _snowFall = snowTarget;
            _fog = w.Fog;
            _cloud = target;
        }
        _overlay.SetShaderParameter("rain", _rain);
        _overlay.SetShaderParameter("snow", _snowFall);
        _overlay.SetShaderParameter("fog", _fog * 0.8f);
        _overlay.SetShaderParameter("wind", w.Wind);

        _env.FogDensity = 0.0004f + _fog * 0.006f + _rain * 0.0012f;
        _env.FogLightColor = _env.AmbientLightColor * 0.9f;
        _env.AdjustmentSaturation = 0.85f - 0.12f * _rain - 0.1f * _fog;

        // --- Global shader parameters for terrain, crops and decals.
        _wetness = Mathf.MoveToward(_wetness, w.GroundWetness, dt * 0.2f);
        _snow = Mathf.MoveToward(_snow, w.SnowCover, dt * 0.2f);
        if (jumped)
        {
            _wetness = w.GroundWetness;
            _snow = w.SnowCover;
        }
        _cloudOffset += new Vector2(1f, 0.35f) * (3f + 9f * w.Wind) * dt;
        var monthlyMean = WeatherSystem.MonthlyLerp(Sim.Climate.MonthlyMeanTemp, monthF);
        RenderingServer.GlobalShaderParameterSet("g_wetness", _wetness);
        RenderingServer.GlobalShaderParameterSet("g_snow", _snow);
        RenderingServer.GlobalShaderParameterSet("g_cloud_cover", _cloud);
        RenderingServer.GlobalShaderParameterSet("g_cloud_offset", _cloudOffset);
        RenderingServer.GlobalShaderParameterSet("g_season_green", Mathf.SmoothStep(3f, 11f, monthlyMean));
        RenderingServer.GlobalShaderParameterSet("g_wind", w.Wind);
    }
}

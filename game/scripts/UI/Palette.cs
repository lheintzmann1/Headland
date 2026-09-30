using Headland.Core.Weather;

namespace Headland.Game.UI;

/// <summary>Colors used in BBCode text. Control colors live in the theme (ui/theme.tres).</summary>
public static class Palette
{
    public const string Key = "#e8cf6a";
    public const string Dim = "#a8aba4";
    public const string Good = "#9fd67f";
    public const string Warning = "#e89a60";
    /// <summary>What's worth knowing, and the helpers: their lines in the HUD, their vehicles on the map.</summary>
    public const string Info = "#8fc0e8";
    /// <summary>Something running for a while: tipping, pipe out.</summary>
    public const string Busy = "#e8c060";
    public const string Paused = "#e0a060";
    /// <summary>Contracts under way: their fields on the map, and their lines in the HUD.</summary>
    public const string Contract = "#b9a2dc";
    /// <summary>The waypoint set on the map: its flag.</summary>
    public const string Waypoint = "#e8707a";

    public static string Weather(WeatherCondition c) => c switch
    {
        WeatherCondition.Clear => "#e8cf6a",
        WeatherCondition.Cloudy => "#b8bcc0",
        WeatherCondition.Rain => "#7fa6d8",
        WeatherCondition.Storm => "#9aa8e8",
        WeatherCondition.Snow => "#e8eef4",
        _ => "#b0b4b0",
    };
}

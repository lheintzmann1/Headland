namespace Headland.Core.Components;

/// <summary>How a readout is shown: its color.</summary>
public enum Tone
{
    Normal,
    /// <summary>Nothing going on: off, raised, empty.</summary>
    Dim,
    /// <summary>At work: lowered, turned on, threshing.</summary>
    Good,
    /// <summary>Something running for a while: tipping, pipe out, loading.</summary>
    Busy,
    /// <summary>Worth knowing: lights, the helper.</summary>
    Info,
    /// <summary>Needs seeing to: low fuel, worn, slipping.</summary>
    Warning,
}

/// <summary>
/// What a component shows of its state on the HUD's vehicle panel (FS: the speed meter's gauges, the fill level bars, a
/// specialization's status): a <see cref="Gauge"/> or a <see cref="Status"/>.
/// </summary>
public abstract record Readout(Tone Tone);

/// <summary>
/// A level on a bar: the speed, the fuel, a tank's fill level, the condition. <paramref name="Kind"/> says what it is (the
/// HUD's icon for it): speed, load, fuel, fill, bales, condition, dirt.
/// </summary>
public sealed record Gauge(string Kind, string Label, float Fraction, string Value, Tone Tone = Tone.Normal) : Readout(Tone)
{
    /// <summary>The kinds, in the order the panel shows them.</summary>
    public static readonly string[] Kinds = ["speed", "load", "fuel", "fill", "bales", "condition", "dirt"];
}

/// <summary>A state in a word or two: lowered, threshing, pipe out, the seed it sows.</summary>
public sealed record Status(string Text, Tone Tone = Tone.Normal) : Readout(Tone);

/// <summary>A component showing its state on the HUD's vehicle panel (FS: a specialization's HUD extension).</summary>
public interface IReadoutSource
{
    IEnumerable<Readout> Readouts(Simulation sim);
}

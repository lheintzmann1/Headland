using System.Text;
using Headland.Game.Controls;

namespace Headland.Game.UI;

/// <summary>F1: every control, in two columns, and the farming loop. An overlay: the game keeps running under it.</summary>
public partial class HelpScreen : Screen
{
    public override bool Modal => false;

    protected override void Build()
    {
        var speeds = GameActions.TimeSpeeds;
        var keys = InputLayer.Current.Actions
            .Where(a => !speeds.Contains(a.Id) || a.Id == speeds[0])
            .Select(a => a.Id == speeds[0]
                ? (Widgets.Colored($"{InputLayer.Label(speeds[0])}–{InputLayer.Label(speeds[^1])}", Palette.Key), "Time speed ×1 … ×240")
                : (Widgets.Key(a.Id), a.Help))
            .ToList();
        var half = (keys.Count + 1) / 2;

        var sb = new StringBuilder("[table=4]");
        for (var row = 0; row < half; row++)
        foreach (var (key, help) in new[] { keys[row], row + half < keys.Count ? keys[row + half] : ("", "") })
            sb.Append($"[cell]{key}   [/cell][cell]{help}        [/cell]");
        sb.Append("[/table]\n\n");
        sb.Append("[b]Loop[/b]: cultivate or plow the stubble, sow in season, fertilize and spray the weeds as it grows. Harvest\n");
        sb.Append("with the right header, unload into a trailer and tip at the Grain Elevator, or store it in the farm silo. Mow\n");
        sb.Append("meadows when the grass is ready. Hover the ground to inspect soil, weeds and crops.");

        var text = Widgets.Rich(0);
        text.Text = sb.ToString();
        AddChild(Widgets.Dialog("Controls", text, $"Keys follow your keyboard layout. {InputLayer.Label(GameActions.ToggleHelp)} or Esc closes."));
    }
}

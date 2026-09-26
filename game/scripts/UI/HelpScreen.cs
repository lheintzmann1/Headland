using System.Text;
using Headland.Game.Common;

namespace Headland.Game.UI;

/// <summary>F1: every control, in two columns, and the farming loop. An overlay: the game keeps running under it.</summary>
public partial class HelpScreen : Screen
{
    public override bool Modal => false;

    protected override void Build()
    {
        var keys = InputSetup.Bindings
            .Where(b => !b.action.StartsWith("time_") || b.action == "time_1")
            .Select(b => b.action == "time_1"
                ? (Widgets.Colored("1–6", Palette.Key), "Time speed ×1 … ×240")
                : (Widgets.Key(b.action), b.help))
            .Append((Widgets.Colored("Wheel", Palette.Key), "Zoom"))
            .Append((Widgets.Colored("Middle drag", Palette.Key), "Pan"))
            .ToList();
        var half = (keys.Count + 1) / 2;

        var sb = new StringBuilder("[table=4]");
        for (var row = 0; row < half; row++)
        foreach (var (key, help) in new[] { keys[row], row + half < keys.Count ? keys[row + half] : ("", "") })
            sb.Append($"[cell]{key}   [/cell][cell]{help}        [/cell]");
        sb.Append("[/table]\n\n");
        sb.Append("[b]Loop[/b]: cultivate stubble, sow in season and let it grow. Harvest with the right header, unload into\n");
        sb.Append("a trailer and tip at the Grain Elevator, or store it in the farm silo. Hover the ground to inspect soil and crops.");

        var text = Widgets.Rich(0);
        text.Text = sb.ToString();
        AddChild(Widgets.Dialog("Controls", text, $"Keys follow your keyboard layout. {InputSetup.Label("toggle_help")} or Esc closes."));
    }
}

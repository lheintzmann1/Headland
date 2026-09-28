using System.Text;
using Headland.Core;
using Headland.Core.Input;
using Headland.Game.Controls;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// F1: the keys of the player's situation (walking, or driving what they drive), in two columns: what each does now,
/// dimmed when it does nothing here; then the farming loop. An overlay: the game keeps running under it, and the list
/// follows as the player gets in and out or hitches.
/// </summary>
public partial class HelpScreen : Screen
{
    private RichTextLabel _text = null!;
    private double _refresh;

    public Simulation Sim { get; init; } = null!;

    public override bool Modal => false;

    protected override void Build()
    {
        _text = Widgets.Rich(0);
        AddChild(Widgets.Dialog("Controls", _text, $"Keys follow your keyboard layout. {InputLayer.Label(GameActions.ToggleHelp)} or Esc closes."));
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        Refresh();
    }

    private void Refresh()
    {
        var situation = Sim.Situation;
        var offers = Sim.Offers();
        var speeds = GameActions.TimeSpeeds;
        var keys = InputLayer.Current.Actions
            .Where(a => (a.Contexts & situation) != 0 && (!speeds.Contains(a.Id) || a.Id == speeds[0]))
            .Select(a => a.Id == speeds[0]
                ? (Widgets.Colored($"{InputLayer.Label(speeds[0])}–{InputLayer.Label(speeds[^1])}", Palette.Key), "Time speed ×1 … ×240")
                : (Widgets.Key(a.Id), What(a, offers)))
            .ToList();
        var half = (keys.Count + 1) / 2;

        var v = Sim.Player.Vehicle;
        var sb = new StringBuilder($"[b]{(v != null ? $"Driving the {v.Def.Name}" : "On foot")}[/b]\n[table=4]");
        for (var row = 0; row < half; row++)
        foreach (var (key, help) in new[] { keys[row], row + half < keys.Count ? keys[row + half] : ("", "") })
            sb.Append($"[cell]{key}   [/cell][cell]{help}        [/cell]");
        sb.Append("[/table]\n\n");
        sb.Append("[b]Loop[/b]: cultivate or plow the stubble, sow in season, fertilize and spray the weeds as it grows. Harvest\n");
        sb.Append("with the right header, unload into a trailer and tip at the Grain Elevator, or store it in the farm silo. Mow\n");
        sb.Append("meadows when the grass is ready. Hover the ground to inspect soil, weeds and crops.");
        _text.Text = sb.ToString();
    }

    /// <summary>What the simulation's action does here, or its help dimmed when it does nothing; the game's own help.</summary>
    private static string What(InputActionDef action, ActionList offers)
    {
        if (action.Analog || InputActions.Def(action.Id) == null) return action.Help;
        return offers.Of(action.Id) is { } offer ? offer.Label : Widgets.Colored(action.Help, Palette.Dim);
    }
}

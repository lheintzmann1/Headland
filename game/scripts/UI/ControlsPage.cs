using System.Text;
using Headland.Core.Input;
using Headland.Game.Controls;
using Godot;

namespace Headland.Game.UI;

/// <summary>The menu's controls: every action with all its bindings, by where it works.</summary>
public partial class ControlsPage : MenuPage
{
    private const InputContext Moving = InputContext.OnFoot | InputContext.Vehicle;

    /// <summary>The sections, each with the actions whose contexts are exactly these, in two columns.</summary>
    private static readonly (string title, Func<InputContext, bool> holds, int column)[] Sections =
    [
        ("Walking and driving", c => c == Moving, 0),
        ("On foot", c => c == InputContext.OnFoot, 0),
        ("Driving", c => c == InputContext.Vehicle, 0),
        ("Camera, time and game", c => c.HasFlag(InputContext.World), 1),
        ("In the menu", c => c == InputContext.Menu, 1),
    ];

    public override string Subtitle =>
        "Every key and where it works. Keys follow your keyboard layout; change them in settings.cfg (see the README).";

    protected override void Build()
    {
        var columns = new HBoxContainer();
        var text = new[] { new StringBuilder(), new StringBuilder() };
        foreach (var (title, holds, column) in Sections)
        {
            var actions = InputLayer.Current.Actions.Where(a => holds(a.Contexts)).ToList();
            if (actions.Count == 0) continue;
            var sb = text[column];
            sb.Append($"[b]{title}[/b]\n[table=2]");
            foreach (var a in actions)
            {
                var keys = InputLayer.Current.Of(a.Id).Select(b => Widgets.Colored(InputLayer.Label(b), Palette.Key));
                sb.Append($"[cell]{string.Join(" / ", keys)}      [/cell][cell]{a.Help}      [/cell]");
            }
            sb.Append("[/table]\n\n");
        }
        foreach (var sb in text)
        {
            var label = Widgets.Rich(0);
            label.Text = sb.ToString().TrimEnd('\n');
            label.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            columns.AddChild(label);
        }
        AddChild(columns);
    }
}

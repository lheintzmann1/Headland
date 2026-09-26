using Headland.Game.Common;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// Builders for the controls the HUD and screens share. Their looks come from the project theme (ui/theme.tres)
/// through type variations (ScreenPanel, TitleLabel, NoteLabel...), never from per-control overrides.
/// </summary>
public static class Widgets
{
    /// <summary>Icon size in text, in pixels (the icons are 24 px Material Symbols).</summary>
    public const int IconSize = 18;

    public static RichTextLabel Rich(float width, string variation = "") => new()
    {
        BbcodeEnabled = true,
        FitContent = true,
        ScrollActive = false,
        AutowrapMode = TextServer.AutowrapMode.Off,
        CustomMinimumSize = new Vector2(width, 0),
        MouseFilter = Control.MouseFilterEnum.Ignore,
        ThemeTypeVariation = variation,
    };

    public static Label Label(string text = "", string variation = "") => new()
    {
        Text = text,
        ThemeTypeVariation = variation,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>A panel around <paramref name="content"/>: the translucent HUD panel, or a variation such as ScreenPanel.</summary>
    public static PanelContainer Panel(Control content, string variation = "")
    {
        var panel = new PanelContainer { ThemeTypeVariation = variation, MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddChild(content);
        return panel;
    }

    /// <summary>
    /// Pins a control to a screen edge, corner or the center, offset in pixels. It grows away from that edge, so it
    /// stays on screen as its content changes.
    /// </summary>
    public static T Anchor<T>(T control, Control.LayoutPreset anchor, Vector2 offset) where T : Control
    {
        control.SetAnchorsPreset(anchor);
        control.GrowHorizontal = anchor is Control.LayoutPreset.TopRight or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterRight
            ? Control.GrowDirection.Begin
            : anchor is Control.LayoutPreset.CenterBottom or Control.LayoutPreset.Center or Control.LayoutPreset.CenterTop
                ? Control.GrowDirection.Both
                : Control.GrowDirection.End;
        control.GrowVertical = anchor is Control.LayoutPreset.BottomLeft or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterBottom
            ? Control.GrowDirection.Begin
            : anchor is Control.LayoutPreset.Center or Control.LayoutPreset.CenterLeft or Control.LayoutPreset.CenterRight
                ? Control.GrowDirection.Both
                : Control.GrowDirection.End;
        control.Position += offset;
        return control;
    }

    /// <summary>A centered screen panel with a title above its content.</summary>
    public static Control Dialog(string title, Control content, string? subtitle = null)
    {
        var box = new VBoxContainer { ThemeTypeVariation = "DialogBox" };
        box.AddChild(Label(title, "TitleLabel"));
        if (subtitle != null) box.AddChild(Label(subtitle, "DimLabel"));
        box.AddChild(content);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.AddChild(Panel(box, "ScreenPanel"));
        return center;
    }

    public static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        return button;
    }

    /// <summary>One of a row of buttons that stay down while selected (the others in <paramref name="group"/> pop up).</summary>
    public static Button Tab(string text, ButtonGroup group, bool selected, Action select)
    {
        var tab = new Button { Text = text, ToggleMode = true, ButtonGroup = group, ButtonPressed = selected };
        tab.Pressed += select;
        return tab;
    }

    /// <summary>A right-aligned table cell for an amount of money: green in, orange out, a dash for none.</summary>
    public static Label Money(float amount, float width = 84f)
    {
        var cell = MathF.Abs(amount) < 0.5f
            ? Label("–", "DimLabel")
            : Label($"{amount:N0}", amount > 0f ? "IncomeLabel" : "ExpenseLabel");
        cell.HorizontalAlignment = HorizontalAlignment.Right;
        cell.CustomMinimumSize = new Vector2(width, 0);
        return cell;
    }

    /// <summary>BBCode for a key prompt: the action's key on the player's keyboard layout, highlighted.</summary>
    public static string Key(string action) => Colored($"[lb]{InputSetup.Label(action)}[rb]", Palette.Key);

    /// <summary>BBCode for a tinted icon from assets/icons.</summary>
    public static string Icon(string name, string color, int size = IconSize) =>
        $"[img width={size} height={size} color={color}]res://assets/icons/{name}.svg[/img]";

    public static string Colored(string text, string color) => $"[color={color}]{text}[/color]";
}

using Headland.Core;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// A screen covering the whole view (FS: the in-game menu, the shop): its title or tabs across the top beside the
/// farm's balance and the date, a line on what it's for, its content filling the rest, and along the bottom the keys it
/// answers and the latest notification (the HUD's are under it). The world only shows faintly through. Without a game
/// (the main menu's settings), the balance, date and notifications are left out.
/// </summary>
public partial class ScreenFrame : PanelContainer
{
    /// <summary>Seconds a notification stays in the footer.</summary>
    private const double NoteSeconds = 6.0;

    private Label _balance = null!;
    private Label _date = null!;
    private RichTextLabel _note = null!;
    private double _refresh;

    /// <summary>The game whose balance, date and notifications it shows, if there's one.</summary>
    public Simulation? Sim { get; init; }
    /// <summary>The keys shown along the bottom: the action, and what it does here.</summary>
    public (string action, string label)[] Hints { get; init; } = [];

    /// <summary>Where the title or the tabs go.</summary>
    public HBoxContainer Header { get; } = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    /// <summary>What the screen is for, under the header.</summary>
    public Label Subtitle { get; } = Widgets.Label(variation: "DimLabel");
    /// <summary>The content, filling the frame.</summary>
    public VBoxContainer Body { get; } = new() { ThemeTypeVariation = "DialogBox", SizeFlagsVertical = SizeFlags.ExpandFill };

    public override void _Ready()
    {
        ThemeTypeVariation = "FullScreenPanel";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var column = new VBoxContainer { ThemeTypeVariation = "DialogBox" };
        var top = new HBoxContainer { ThemeTypeVariation = "DialogBox" };
        top.AddChild(Header);
        _date = Widgets.Label(variation: "DimLabel");
        _date.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(_date);
        _balance = Widgets.Label();
        _balance.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(_balance);
        column.AddChild(top);
        column.AddChild(Subtitle);
        column.AddChild(new HSeparator());
        column.AddChild(Body);
        column.AddChild(new HSeparator());
        var bottom = new HBoxContainer();
        var hints = Widgets.Rich(0);
        hints.Text = string.Join("      ", Hints.Select(h => $"{Widgets.Key(h.action)} {h.label}"));
        hints.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottom.AddChild(hints);
        _note = Widgets.Rich(0);
        bottom.AddChild(_note);
        column.AddChild(bottom);
        AddChild(column);
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
        if (Sim == null) return;
        Widgets.Balance(_balance, Sim.Economy.Money);
        _date.Text = $"{Sim.Clock.Date}   {Sim.Clock.TimeString}";
        var note = Sim.Notifications.Items.LastOrDefault(n => Sim.RealTime - n.RealTime < NoteSeconds);
        _note.Text = note == null ? "" : note.Severity switch
        {
            Severity.Good => Widgets.Colored(note.Text, Palette.Good),
            Severity.Warning => $"{Widgets.Icon("warning", Palette.Warning)} {Widgets.Colored(note.Text, Palette.Warning)}",
            _ => note.Text,
        };
    }
}

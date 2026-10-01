using Headland.Core.Saves;
using Headland.Game.Saves;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's game page (FS: the pause menu): back to the game, the settings, save to a slot, load one, or quit to the
/// main menu or the desktop (a second press, as what isn't saved is lost).
/// </summary>
public partial class GamePage : MenuPage
{
    private GridContainer _slots = null!;
    private string _listed = "";
    private double _refresh;
    /// <summary>The quit button waiting for its second press, and how long it waits.</summary>
    private Button? _confirm;
    private double _confirmLeft;

    public GameRoot Game { get; init; } = null!;

    public override string Subtitle => "Saves go to your user data folder: the slots you name, the quicksave and the autosave.";

    protected override void Build()
    {
        var game = new HBoxContainer();
        var resume = Widgets.Button("Resume", () => Game.Screens.Top?.Close());
        game.AddChild(resume);
        game.AddChild(Widgets.Button("Settings…", () => Game.Screens.Push(new SettingsScreen { Sim = Game.Sim, Applied = Game.ApplySettings })));
        AddChild(game);
        resume.CallDeferred(Control.MethodName.GrabFocus);

        var save = new HBoxContainer();
        save.AddChild(Widgets.Label("Save as"));
        var name = new LineEdit { Text = "save", MaxLength = 40, CustomMinimumSize = new Vector2(240, 0) };
        save.AddChild(name);
        var button = Widgets.Button("Save", () => Game.Saves.Save(name.Text));
        save.AddChild(button);
        var why = Widgets.Label(variation: "DimLabel");
        save.AddChild(why);
        name.TextChanged += text =>
        {
            button.Disabled = !SaveStore.IsValidSlot(text);
            why.Text = button.Disabled ? "Letters, digits, - and _ only" : "";
        };
        AddChild(save);

        AddChild(Widgets.Label("Load", "StrongLabel"));
        _slots = new GridContainer { Columns = 5, ThemeTypeVariation = "TableGrid" };
        AddChild(_slots);
        ListSlots();

        var quit = new HBoxContainer();
        Button? toMenu = null, toDesktop = null;
        toMenu = Widgets.Button("Quit to menu", () => Confirm(toMenu!, Game.QuitToMenu));
        toDesktop = Widgets.Button("Quit to desktop", () => Confirm(toDesktop!, () => GetTree().Quit()));
        quit.AddChild(toMenu);
        quit.AddChild(toDesktop);
        AddChild(quit);
    }

    /// <summary>A quit button does it on its second press, within a few seconds: what isn't saved is lost.</summary>
    private void Confirm(Button button, Action quit)
    {
        if (_confirm == button)
        {
            quit();
            return;
        }
        if (_confirm != null) _confirm.Text = _confirm.Text.Replace(": unsaved progress is lost", "");
        _confirm = button;
        _confirmLeft = 4;
        button.Text += ": unsaved progress is lost";
    }

    public override void _Process(double delta)
    {
        if (_confirm != null && (_confirmLeft -= delta) <= 0)
        {
            _confirm.Text = _confirm.Text.Replace(": unsaved progress is lost", "");
            _confirm = null;
        }
        // A save written in the background shows up once it's on disk.
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 1;
        ListSlots();
    }

    private void ListSlots()
    {
        var slots = SaveManager.Store.List();
        var listed = string.Join("|", slots.Select(s => $"{s.Name}@{s.Meta.SavedAtUtc.Ticks}"));
        if (listed == _listed) return;
        _listed = listed;
        foreach (var cell in _slots.GetChildren()) cell.Free();
        if (slots.Count == 0)
        {
            _slots.AddChild(Widgets.Label("No saves yet.", "DimLabel"));
            return;
        }
        foreach (var slot in slots)
        {
            var meta = slot.Meta;
            _slots.AddChild(Widgets.Label(slot.Name, "StrongLabel"));
            _slots.AddChild(Widgets.Label($"{meta.FarmName}, {meta.MapName}"));
            _slots.AddChild(Widgets.Label(meta.Date));
            _slots.AddChild(Widgets.Label(meta.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), "DimLabel"));
            _slots.AddChild(Widgets.Button("Load", () => Game.Saves.Load(slot.Name)));
        }
    }
}

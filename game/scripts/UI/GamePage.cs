using Headland.Core.Saves;
using Headland.Game.Saves;
using Godot;

namespace Headland.Game.UI;

/// <summary>The menu's game page: save to a slot, load one, or quit.</summary>
public partial class GamePage : MenuPage
{
    private GridContainer _slots = null!;
    private string _listed = "";
    private double _refresh;

    public GameRoot Game { get; init; } = null!;

    public override string Subtitle => "Saves go to your user data folder: the slots you name, the quicksave and the autosave.";

    protected override void Build()
    {
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

        AddChild(Widgets.Button("Quit to desktop", () => GetTree().Quit()));
    }

    public override void _Process(double delta)
    {
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

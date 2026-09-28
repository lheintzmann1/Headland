using Headland.Game.Controls;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// Esc: the in-game menu, one screen holding every page as a tab (the contracts, the finances, the farmland, the
/// controls, the game), switched with the mouse or the tab keys (Q and E by default: the camera's keys outside it). It
/// opens on the tab last shown.
/// </summary>
public partial class MenuScreen : Screen
{
    private static int _last;
    private readonly List<Button> _tabs = [];
    private (string title, Func<MenuPage> create)[] _pages = [];
    private VBoxContainer _box = null!;
    private Label _subtitle = null!;
    private MenuPage? _page;
    private int _current;

    public GameRoot Game { get; init; } = null!;

    protected override void Build()
    {
        _pages =
        [
            ("Contracts", () => new ContractsPage { Sim = Game.Sim }),
            ("Finances", () => new FinancesPage { Sim = Game.Sim }),
            ("Farmland", () => new FarmlandPage { Sim = Game.Sim }),
            ("Controls", () => new ControlsPage()),
            ("Game", () => new GamePage { Game = Game }),
        ];
        _box = new VBoxContainer { ThemeTypeVariation = "DialogBox" };
        var row = new HBoxContainer();
        row.AddChild(KeyHint(GameActions.MenuPrevTab));
        var group = new ButtonGroup();
        for (var i = 0; i < _pages.Length; i++)
        {
            var index = i;
            var tab = Widgets.Tab(_pages[i].title, group, false, () => Show(index));
            _tabs.Add(tab);
            row.AddChild(tab);
        }
        row.AddChild(KeyHint(GameActions.MenuNextTab));
        _box.AddChild(row);
        _subtitle = Widgets.Label(variation: "DimLabel");
        _box.AddChild(_subtitle);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.AddChild(Widgets.Panel(_box, "ScreenPanel"));
        AddChild(center);
        Show(Math.Clamp(_last, 0, _pages.Length - 1));
    }

    /// <summary>The tab <paramref name="step"/> tabs over, wrapping around.</summary>
    public void Step(int step) => Show(((_current + step) % _pages.Length + _pages.Length) % _pages.Length);

    private void Show(int index)
    {
        _current = _last = index;
        _tabs[index].ButtonPressed = true;
        if (_page != null)
        {
            _box.RemoveChild(_page);
            _page.QueueFree();
        }
        _page = _pages[index].create();
        _subtitle.Text = $"{_page.Subtitle} Esc closes.".TrimStart();
        _box.AddChild(_page);
    }

    private static RichTextLabel KeyHint(string action)
    {
        var hint = Widgets.Rich(0);
        hint.Text = Widgets.Key(action);
        hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return hint;
    }
}

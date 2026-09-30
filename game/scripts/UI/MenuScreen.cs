using Headland.Game.Controls;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// Esc: the in-game menu, covering the whole view (FS), one screen holding every page as a tab (the map, the contracts,
/// the finances, the farmland, the shop, the garage, the controls, the game), switched with the mouse or the tab keys (Q
/// and E by default: the camera's keys outside it). It opens on the tab last shown, or the one a shortcut asks for (M
/// for the map).
/// </summary>
public partial class MenuScreen : Screen
{
    private static int _last;
    private readonly List<Button> _tabs = [];
    private (string title, Func<MenuPage> create)[] _pages = [];
    private ScreenFrame _frame = null!;
    private MenuPage? _page;
    private int _current;

    public GameRoot Game { get; init; } = null!;

    /// <summary>The tab it opens on, by title, instead of the last one shown.</summary>
    public string? Tab { get; init; }

    public override bool CoversView => true;

    protected override void Build()
    {
        _pages =
        [
            ("Map", () => new MapPage { Sim = Game.Sim }),
            ("Contracts", () => new ContractsPage { Sim = Game.Sim }),
            ("Finances", () => new FinancesPage { Sim = Game.Sim }),
            ("Farmland", () => new FarmlandPage { Sim = Game.Sim }),
            ("Shop", () => new ShopPage { Sim = Game.Sim }),
            ("Garage", () => new GaragePage { Game = Game }),
            ("Controls", () => new ControlsPage()),
            ("Game", () => new GamePage { Game = Game }),
        ];
        _frame = new ScreenFrame
        {
            Sim = Game.Sim,
            Hints = [(GameActions.Menu, "Back to the game"), (GameActions.MenuPrevTab, "Previous tab"), (GameActions.MenuNextTab, "Next tab")],
        };
        var row = _frame.Header;
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
        AddChild(_frame);
        Show(Tab != null && IndexOf(Tab) is var asked and >= 0 ? asked : Math.Clamp(_last, 0, _pages.Length - 1));
    }

    public int TabCount => _pages.Length;
    /// <summary>The tab shown, and its title.</summary>
    public MenuPage? Page => _page;
    public string TabTitle => _pages[_current].title;

    /// <summary>Shows the tab titled <paramref name="title"/>, if there's one.</summary>
    public void ShowTab(string title)
    {
        if (IndexOf(title) is var index and >= 0) Show(index);
    }

    private int IndexOf(string title) => Array.FindIndex(_pages, p => p.title == title);

    /// <summary>The tab <paramref name="step"/> tabs over, wrapping around.</summary>
    public void Step(int step) => Show(((_current + step) % _pages.Length + _pages.Length) % _pages.Length);

    private void Show(int index)
    {
        _current = _last = index;
        _tabs[index].ButtonPressed = true;
        if (_page != null)
        {
            _frame.Body.RemoveChild(_page);
            _page.QueueFree();
        }
        _page = _pages[index].create();
        _page.SizeFlagsVertical = SizeFlags.ExpandFill;
        _frame.Subtitle.Text = _page.Subtitle;
        _frame.Body.AddChild(_page);
    }

    private static RichTextLabel KeyHint(string action)
    {
        var hint = Widgets.Rich(0);
        hint.Text = Widgets.Key(action);
        hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return hint;
    }
}

using System.Globalization;
using Headland.Core.Content;
using Headland.Game.Common;
using Headland.Game.Saves;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The main menu (FS: the main menu), the game's first scene: continue the latest save, start a new game (the farm's
/// name, the map, the difficulty), load a save (or delete one), the settings, the credits, or quit. The game's command
/// line (--scenario, --load, --difficulty, --new) goes straight to the game.
/// </summary>
public partial class MainMenu : Control
{
    public const string ScenePath = "res://scenes/Menu.tscn";
    private static readonly string[] Skips = ["--scenario=", "--load=", "--difficulty=", "--new"];

    private ContentDatabase _content = null!;
    private ScreenStack _screens = null!;
    private VBoxContainer _side = null!;
    private Label _error = null!;
    /// <summary>The save whose deletion waits for a second press.</summary>
    private string? _confirm;

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (GameRoot.CommandLineWaiting && OS.GetCmdlineUserArgs().Any(a => Skips.Any(a.StartsWith)))
        {
            CallDeferred(MethodName.Play);
            return;
        }
        UserSettings.Current.ApplyAtStart(GetViewport());
        _content = ContentDatabase.Load(new GodotContentSource("res://data"));
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var back = new PanelContainer { ThemeTypeVariation = "FullScreenPanel" };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(back);
        var columns = new HBoxContainer { ThemeTypeVariation = "DialogBox" };
        back.AddChild(columns);

        var menu = new VBoxContainer { ThemeTypeVariation = "DialogBox", CustomMinimumSize = new Vector2(320, 0) };
        menu.AddChild(Widgets.Label("Headland", "TitleLabel"));
        menu.AddChild(Widgets.Label($"Farming, soils and weather · {SaveManager.GameVersion}", "DimLabel"));
        menu.AddChild(new HSeparator());
        var latest = SaveManager.Store.List().OrderByDescending(s => s.Meta.SavedAtUtc).FirstOrDefault();
        var resume = Widgets.Button(latest != null ? $"Continue: {latest.Meta.FarmName}, {latest.Meta.Date}" : "Continue", () => Load(latest!.Name));
        resume.Disabled = latest == null;
        menu.AddChild(resume);
        menu.AddChild(Widgets.Button("New game", NewGame));
        menu.AddChild(Widgets.Button("Load", ListSaves));
        menu.AddChild(Widgets.Button("Settings", () => _screens.Push(new SettingsScreen())));
        menu.AddChild(Widgets.Button("Credits", Credits));
        menu.AddChild(Widgets.Button("Quit", () => GetTree().Quit()));
        _error = Widgets.Label(variation: "NoteWarningLabel");
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        menu.AddChild(_error);
        columns.AddChild(menu);
        columns.AddChild(new VSeparator());
        _side = new VBoxContainer { ThemeTypeVariation = "DialogBox", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(_side);

        _screens = new ScreenStack { Name = "Screens" };
        AddChild(_screens);
        if (latest != null) resume.CallDeferred(Control.MethodName.GrabFocus);
        else menu.GetChild<Button>(4).CallDeferred(Control.MethodName.GrabFocus);
        NewGame();
    }

    /// <summary>Esc closes the settings over the menu.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape } || _screens.Top is not { } top) return;
        top.Close();
        GetViewport().SetInputAsHandled();
    }

    private void Play() => GetTree().ChangeSceneToFile(GameRoot.ScenePath);

    private void Side(string title)
    {
        foreach (var child in _side.GetChildren()) child.Free();
        _side.AddChild(Widgets.Label(title, "TitleLabel"));
        _error.Text = "";
    }

    /// <summary>The farm's name, the map and the difficulty, then off.</summary>
    private void NewGame()
    {
        Side("New game");
        var form = new GridContainer { Columns = 2, ThemeTypeVariation = "TableGrid" };
        form.AddChild(Widgets.Label("Farm"));
        var name = new LineEdit { Text = _content.Game.FarmName, MaxLength = 40, CustomMinimumSize = new Vector2(300, 0) };
        form.AddChild(name);
        var maps = _content.Maps.Values.ToList();
        var map = maps.FindIndex(m => m.Id == _content.Game.Map);
        form.AddChild(Widgets.Label("Map"));
        form.AddChild(Widgets.Dropdown(maps.Select(m => $"{m.Name} ({m.Size} m)"), map, i => map = i));
        var levels = _content.Difficulties.Values.ToList();
        var level = levels.FindIndex(d => d.Id == _content.Game.Difficulty);
        var about = Widgets.Label(levels[level].Description, "DimLabel");
        form.AddChild(Widgets.Label("Difficulty"));
        form.AddChild(Widgets.Dropdown(levels.Select(d => d.Name), level, i =>
        {
            level = i;
            about.Text = levels[i].Description;
        }));
        _side.AddChild(form);
        _side.AddChild(about);
        _side.AddChild(Widgets.Button("Start", () =>
        {
            GameRoot.NextGame = new GameSetup(maps[map].Id, levels[level].Id, name.Text.Trim() is { Length: > 0 } farm ? farm : _content.Game.FarmName);
            Play();
        }));
    }

    /// <summary>The saves, the latest first, to load or delete.</summary>
    private void ListSaves()
    {
        Side("Load");
        var slots = SaveManager.Store.List().OrderByDescending(s => s.Meta.SavedAtUtc).ToList();
        if (slots.Count == 0)
        {
            _side.AddChild(Widgets.Label("No saves yet.", "DimLabel"));
            return;
        }
        var table = new GridContainer { Columns = 6, ThemeTypeVariation = "TableGrid" };
        foreach (var slot in slots)
        {
            var meta = slot.Meta;
            table.AddChild(Widgets.Label(slot.Name, "StrongLabel"));
            table.AddChild(Widgets.Label($"{meta.FarmName}, {meta.MapName}"));
            table.AddChild(Widgets.Label(meta.Date));
            table.AddChild(Widgets.Label(meta.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), "DimLabel"));
            table.AddChild(Widgets.Button("Load", () => Load(slot.Name)));
            table.AddChild(Widgets.Button(_confirm == slot.Name ? "Delete it?" : "Delete", () =>
            {
                if (_confirm != slot.Name)
                {
                    _confirm = slot.Name;
                    ListSaves();
                    return;
                }
                SaveManager.Store.Delete(slot.Name);
                _confirm = null;
                ListSaves();
            }));
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(table);
        _side.AddChild(scroll);
    }

    private void Load(string slot)
    {
        if (SaveManager.Open(_content, slot) is { } error)
        {
            _error.Text = $"Could not load {slot}: {error}";
            return;
        }
        Play();
    }

    private void Credits()
    {
        Side("Credits");
        var text = Widgets.Rich(0);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.Text = string.Join("\n\n",
            "[b]Headland[/b]: the machines and field work of Farming Simulator, with the depth of Dwarf Fortress and Cataclysm: DDA. " +
            "Code under the GNU GPL 3.0 or later, original art under CC BY-SA 4.0.",
            "[b]Road, track and stream tiles[/b]: Screaming Brain Studios, CC0.",
            "[b]Ground textures[/b]: ambientCG, CC0.",
            "[b]Font[/b]: Barlow by Jeremy Tribby, SIL Open Font License.",
            "[b]Icons[/b]: Material Symbols by Google, Apache License 2.0.",
            Widgets.Colored("The full list is in game/assets/CREDITS.md.", Palette.Dim));
        _side.AddChild(text);
    }
}

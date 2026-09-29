using Godot;

namespace Headland.Game.UI;

/// <summary>
/// Screens above the HUD, last opened on top. The menu key (Esc) closes the top screen, or opens the in-game menu when
/// none is open. A backdrop dims the world under the topmost modal screen, and the screens under one covering the whole
/// view are hidden.
/// </summary>
public partial class ScreenStack : CanvasLayer
{
    private readonly List<Screen> _screens = [];
    private Panel _backdrop = null!;

    public Screen? Top => _screens.Count > 0 ? _screens[^1] : null;

    /// <summary>True while a modal screen is open: the game ignores its own input.</summary>
    public bool BlocksInput => _screens.Any(s => s.Modal);

    /// <summary>True while a screen covers the whole view: the HUD is hidden under it.</summary>
    public bool CoversView => _screens.Any(s => s.CoversView);

    public override void _Ready()
    {
        Layer = 10;
        _backdrop = new Panel { Name = "Backdrop", ThemeTypeVariation = "ScreenBackdrop", Visible = false };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);
    }

    public T Push<T>(T screen) where T : Screen
    {
        screen.Stack = this;
        _screens.Add(screen);
        AddChild(screen);
        Update();
        return screen;
    }

    public void Close(Screen screen)
    {
        if (!_screens.Remove(screen)) return;
        screen.QueueFree();
        Update();
    }

    /// <summary>Opens a screen, or closes it if one of that type is on top (a key that toggles a screen).</summary>
    public void Toggle<T>(Func<T> create) where T : Screen
    {
        if (Top is T open) Close(open);
        else Push(create());
    }

    private void Update()
    {
        var cover = _screens.FindLastIndex(s => s.CoversView);
        for (var i = 0; i < _screens.Count; i++) _screens[i].Visible = i >= cover;
        UpdateBackdrop();
    }

    /// <summary>Puts the backdrop right under the topmost modal screen, so it dims what's below and not the screen.</summary>
    private void UpdateBackdrop()
    {
        var modal = _screens.FindLast(s => s.Modal);
        _backdrop.Visible = modal != null;
        if (modal == null) return;
        // Moving the backdrop from below the screen shifts the screen down one index first.
        var index = modal.GetIndex();
        MoveChild(_backdrop, _backdrop.GetIndex() < index ? index - 1 : index);
    }
}

using Godot;

namespace Headland.Game.UI;

/// <summary>
/// Screens above the HUD, last opened on top. Esc (ui_cancel) closes the top screen. A backdrop dims the world
/// under the topmost modal screen.
/// </summary>
public partial class ScreenStack : CanvasLayer
{
    private readonly List<Screen> _screens = [];
    private Panel _backdrop = null!;

    public Screen? Top => _screens.Count > 0 ? _screens[^1] : null;

    /// <summary>True while a modal screen is open: the game ignores its own input.</summary>
    public bool BlocksInput => _screens.Any(s => s.Modal);

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
        UpdateBackdrop();
        return screen;
    }

    public void Close(Screen screen)
    {
        if (!_screens.Remove(screen)) return;
        screen.QueueFree();
        UpdateBackdrop();
    }

    /// <summary>Opens a screen, or closes it if one of that type is on top (a key that toggles a screen).</summary>
    public void Toggle<T>(Func<T> create) where T : Screen
    {
        if (Top is T open) Close(open);
        else Push(create());
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Top == null || !e.IsActionPressed("ui_cancel")) return;
        Close(Top);
        GetViewport().SetInputAsHandled();
    }

    private void UpdateBackdrop()
    {
        var modal = _screens.FindLast(s => s.Modal);
        _backdrop.Visible = modal != null;
        if (modal != null) MoveChild(_backdrop, modal.GetIndex());
    }
}

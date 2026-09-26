using Godot;

namespace Headland.Game.UI;

/// <summary>
/// A page on the <see cref="ScreenStack"/> (help, and later menus, shop, map...). It fills the viewport; subclasses
/// build their content in <see cref="Build"/>, usually with <see cref="Widgets.Dialog"/>.
/// </summary>
public partial class Screen : Control
{
    public ScreenStack Stack { get; internal set; } = null!;

    /// <summary>
    /// Modal screens dim the world, take the mouse and stop game input (moving, driving, action keys) while open.
    /// Others are overlays the player can keep playing under.
    /// </summary>
    public virtual bool Modal => true;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = Modal ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        Build();
    }

    protected virtual void Build()
    {
    }

    public void Close() => Stack.Close(this);
}

using Godot;

namespace Headland.Game.UI;

/// <summary>
/// A page on the <see cref="ScreenStack"/>: the in-game menu, a workshop, the help. It fills the viewport; subclasses
/// build their content in <see cref="Build"/>: a whole screen (FS) in a <see cref="ScreenFrame"/>, a small choice or an
/// overlay in a <see cref="Widgets.Dialog"/>.
/// </summary>
public partial class Screen : Control
{
    public ScreenStack Stack { get; internal set; } = null!;

    /// <summary>
    /// Modal screens dim the world, take the mouse and stop game input (moving, driving, action keys) while open.
    /// Others are overlays the player can keep playing under.
    /// </summary>
    public virtual bool Modal => true;

    /// <summary>It covers the whole view (a <see cref="ScreenFrame"/>): the screens under it are hidden while it's open.</summary>
    public virtual bool CoversView => false;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = Modal ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        Build();
    }

    protected virtual void Build()
    {
    }

    public void Close() => Stack.Close(this);
}

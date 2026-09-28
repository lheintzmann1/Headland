using Godot;

namespace Headland.Game.UI;

/// <summary>
/// A tab of the in-game menu (<see cref="MenuScreen"/>): the contracts, the finances… It builds its rows in
/// <see cref="Build"/> under the menu's tabs, and keeps them up to date while it's shown.
/// </summary>
public partial class MenuPage : VBoxContainer
{
    /// <summary>What the page is about, shown under the tabs.</summary>
    public virtual string Subtitle => "";

    public override void _Ready()
    {
        ThemeTypeVariation = "DialogBox";
        Build();
    }

    protected virtual void Build()
    {
    }
}

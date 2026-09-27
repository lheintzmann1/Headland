using Headland.Core;
using Headland.Core.Machines;
using Godot;

namespace Headland.Game.UI;

/// <summary>The use key at a load trigger holding several goods: pick what to load into the vehicle's trailers.</summary>
public partial class LoadScreen : Screen
{
    public Simulation Sim { get; init; } = null!;
    public Machine Vehicle { get; init; } = null!;
    public IReadOnlyList<string> Choices { get; init; } = [];

    protected override void Build()
    {
        var list = new VBoxContainer();
        var silo = Sim.Pois.LoadingSilo(Vehicle);
        foreach (var ft in Choices)
        {
            var def = Sim.Content.FillTypes[ft];
            var stock = silo?.Storage.Level(ft) ?? 0f;
            list.AddChild(Widgets.Button($"{def.Name}   {stock:N0} {def.Unit}", () =>
            {
                Sim.Pois.StartLoading(Vehicle, ft);
                Close();
            }));
        }
        AddChild(Widgets.Dialog($"Load from {silo?.Poi.Name}", list, "Esc cancels."));
        if (list.GetChildCount() > 0) list.GetChild<Button>(0).CallDeferred(Control.MethodName.GrabFocus);
    }
}

using FarmSim.Core;
using Godot;

namespace FarmSim.Game.Vehicles;

/// <summary>Keeps one view per Core machine and draws the farmer.</summary>
public partial class EntityRenderer : Node3D
{
    private readonly Dictionary<int, MachineView> _views = new();

    public Simulation Sim { get; init; } = null!;

    public override void _Ready() => AddChild(new PlayerView { Sim = Sim, Name = "Player" });

    public override void _Process(double delta)
    {
        foreach (var m in Sim.Machines.All)
        {
            if (_views.ContainsKey(m.Id)) continue;
            var view = new MachineView { Sim = Sim, Machine = m };
            _views[m.Id] = view;
            AddChild(view);
        }
    }
}

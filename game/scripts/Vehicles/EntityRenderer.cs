using Headland.Core;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>Keeps one view per Core machine (new ones delivered, leased ones gone back) and draws the farmer.</summary>
public partial class EntityRenderer : Node3D
{
    private readonly Dictionary<int, MachineView> _views = new();
    private readonly HashSet<int> _present = [];

    public Simulation Sim { get; init; } = null!;

    public override void _Ready() => AddChild(new PlayerView { Sim = Sim, Name = "Player" });

    public override void _Process(double delta)
    {
        _present.Clear();
        foreach (var m in Sim.Machines.All)
        {
            _present.Add(m.Id);
            if (_views.ContainsKey(m.Id)) continue;
            var view = new MachineView { Sim = Sim, Machine = m };
            _views[m.Id] = view;
            AddChild(view);
        }
        if (_views.Count == _present.Count) return;
        foreach (var id in _views.Keys.Where(id => !_present.Contains(id)).ToList())
        {
            _views[id].QueueFree();
            _views.Remove(id);
        }
    }
}

using Headland.Game.Objects;
using Headland.Core;
using Headland.Core.Content;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>
/// Keeps one view per Core machine (new ones delivered, leased ones gone back, refitted ones built again) and per object
/// (bales made, sold), and draws the farmer.
/// </summary>
public partial class EntityRenderer : Node3D
{
    private readonly Dictionary<int, (MachineView view, MachineDef def)> _views = new();
    private readonly HashSet<int> _present = [];
    private readonly Dictionary<int, ObjectView> _objects = new();

    public Simulation Sim { get; init; } = null!;

    public override void _Ready() => AddChild(new PlayerView { Sim = Sim, Name = "Player" });

    /// <summary>The view of machine <paramref name="id"/>, if it's drawn.</summary>
    public Node3D? ViewOf(int id) => _views.TryGetValue(id, out var shown) ? shown.view : null;

    public override void _Process(double delta)
    {
        UpdateMachines();
        UpdateObjects();
    }

    private void UpdateMachines()
    {
        _present.Clear();
        foreach (var m in Sim.Machines.All)
        {
            _present.Add(m.Id);
            if (_views.TryGetValue(m.Id, out var shown))
            {
                // A workshop gave it other options: its parts and components are new.
                if (shown.def == m.Def) continue;
                shown.view.QueueFree();
            }
            var view = new MachineView { Sim = Sim, Machine = m };
            _views[m.Id] = (view, m.Def);
            AddChild(view);
        }
        if (_views.Count == _present.Count) return;
        foreach (var id in _views.Keys.Where(id => !_present.Contains(id)).ToList())
        {
            _views[id].view.QueueFree();
            _views.Remove(id);
        }
    }

    private void UpdateObjects()
    {
        _present.Clear();
        foreach (var o in Sim.Objects.All)
        {
            _present.Add(o.Id);
            if (_objects.ContainsKey(o.Id)) continue;
            var view = new ObjectView { Sim = Sim, Object = o, MachineView = ViewOf };
            _objects[o.Id] = view;
            AddChild(view);
        }
        if (_objects.Count == _present.Count) return;
        foreach (var id in _objects.Keys.Where(id => !_present.Contains(id)).ToList())
        {
            _objects[id].QueueFree();
            _objects.Remove(id);
        }
    }
}

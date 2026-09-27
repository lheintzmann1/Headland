using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>A header's reel (reel), turning while the combine it hangs on threshes.</summary>
public partial class WorkAreasView : MachineComponentView
{
    private float _reel;

    public WorkAreas Areas { get; init; } = null!;

    public override void _Process(double delta)
    {
        if (Machine.Parent?.Get<Thresher>() is not { On: true }) return;
        _reel += (float)delta * 3.5f;
        Turn("reel", new Vector3(_reel, 0f, 0f));
    }
}

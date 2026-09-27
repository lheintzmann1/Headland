using Headland.Game.Components;
using Headland.Core.Machines;

namespace Headland.Game.Vehicles.Components;

/// <summary>Draws a component of a machine: running gear, a hitch, a pipe…</summary>
public partial class MachineComponentView : ComponentView
{
    public Machine Machine => (Machine)Owner;
}

using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>Parts moving between their rest and moved poses (roles: the parts' ids), eased at both ends.</summary>
public partial class AnimatedPartsView : MachineComponentView
{
    public AnimatedParts Parts { get; init; } = null!;

    public override void _Process(double delta)
    {
        foreach (var p in Parts.Parts)
        {
            if (Rig.Part(p.Def.Id) is not { } part) continue;
            var t = Ease(p.Position);
            part.Node.Rotation = part.Rotation + Vec(p.Def.RotationDeg) * (Mathf.DegToRad(1f) * t);
            part.Node.Position = part.Position + Vec(p.Def.Offset) * (t / Rig.Scale);
        }
    }
}

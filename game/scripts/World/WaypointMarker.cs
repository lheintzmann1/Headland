using Headland.Game.Common;
using Headland.Game.UI;
using Headland.Core;
using Godot;

namespace Headland.Game.World;

/// <summary>The waypoint set on the map, in the world: a faint beam standing on the spot, seen from afar.</summary>
public partial class WaypointMarker : MeshInstance3D
{
    private const float Height = 40f;

    public Simulation Sim { get; init; } = null!;

    public override void _Ready()
    {
        Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.35f, Height = Height, RadialSegments = 12, Rings = 1 };
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = Conv.Hex(Palette.Waypoint) with { A = 0.45f },
        };
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        Visible = Sim.Player.Waypoint != null;
        if (Sim.Player.Waypoint is { } w) Position = Sim.World.OnGround(w, Height * 0.5f);
    }
}

using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>
/// A placeholder track: a belt of links around a wheel at each end, with road wheels and a frame between them. The
/// links lie still on the ground as the machine drives, running back along the bottom and forward over the top.
/// </summary>
public sealed class TrackRig
{
    private const float LinkPitch = 0.2f;
    private readonly List<(Node3D node, float radius)> _wheels = [];
    private readonly List<Node3D> _links = [];
    private readonly float _length;
    /// <summary>Radius of the belt's middle around the end wheels.</summary>
    private readonly float _bend;
    private readonly float _hub;
    private readonly float _perimeter;

    /// <param name="center">The middle of the track, on the ground.</param>
    public TrackRig(WheelSetDef set, Vector3 center, Color body)
    {
        Root = new Node3D { Name = "Track", Position = center };
        var t = WheelSetDef.TrackThickness;
        _length = set.Length;
        _hub = set.Radius + t;
        _bend = set.Radius + t * 0.5f;
        _perimeter = 2f * _length + Mathf.Pi * 2f * _bend;

        foreach (var z in new[] { _length * 0.5f, -_length * 0.5f })
            AddWheel(set, set.Radius, new Vector3(0f, _hub, z), Materials.DarkSteel);
        // Road wheels on the bottom run, and the frame carrying them.
        var road = set.Radius * 0.42f;
        var count = Math.Max(1, (int)(_length / (road * 2.6f)));
        for (var k = 1; k <= count; k++)
            AddWheel(set, road, new Vector3(0f, t + road, -_length * 0.5f + _length * k / (count + 1)), Materials.Steel);
        PlaceholderBuilder.Box(Root, new Vector3(set.Width * 0.5f, set.Radius * 0.7f, _length), new Vector3(0f, _hub, 0f), body * 0.8f);

        var links = Math.Max(8, (int)Mathf.Round(_perimeter / LinkPitch));
        var material = Materials.Get(Materials.Tire * 1.2f, 0.95f);
        for (var k = 0; k < links; k++)
        {
            var link = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(set.Width, t, _perimeter / links * 0.8f) }, MaterialOverride = material };
            Root.AddChild(link);
            _links.Add(link);
        }
        Roll(0f);
    }

    public Node3D Root { get; }

    /// <summary>Moves the belt and turns the wheels for a track that has rolled <paramref name="distance"/> meters.</summary>
    public void Roll(float distance)
    {
        foreach (var (node, radius) in _wheels) node.Rotation = new Vector3(distance / radius, 0f, 0f);
        for (var k = 0; k < _links.Count; k++)
        {
            var s = Mathf.PosMod(k * _perimeter / _links.Count + distance, _perimeter);
            var (z, y, nz, ny) = OnBelt(s);
            _links[k].Position = new Vector3(0f, y, z);
            // Its thin side along the belt's outward normal.
            _links[k].Rotation = new Vector3(Mathf.Atan2(nz, ny), 0f, 0f);
        }
    }

    /// <summary>
    /// The point <paramref name="s"/> meters along the belt (z, y) and its outward normal: from the front of the bottom
    /// run back, round the rear wheel, forward along the top and round the front wheel.
    /// </summary>
    private (float z, float y, float nz, float ny) OnBelt(float s)
    {
        var half = _length * 0.5f;
        var arc = Mathf.Pi * _bend;
        if (s < _length) return (half - s, _hub - _bend, 0f, -1f);
        s -= _length;
        if (s < arc)
        {
            var (sin, cos) = Mathf.SinCos(s / _bend);
            return (-half - _bend * sin, _hub - _bend * cos, -sin, -cos);
        }
        s -= arc;
        if (s < _length) return (-half + s, _hub + _bend, 0f, 1f);
        s -= _length;
        var (sn, cs) = Mathf.SinCos(Mathf.Min(s, arc) / _bend);
        return (half + _bend * sn, _hub + _bend * cs, sn, cs);
    }

    private void AddWheel(WheelSetDef set, float radius, Vector3 position, Color rim)
    {
        var wheel = new Node3D { Name = "TrackWheel", Position = position };
        wheel.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = set.Width * 0.9f, RadialSegments = 14 },
            Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
            MaterialOverride = Materials.Get(rim, 0.6f, 0.3f),
        });
        // A spoke shows it turning.
        wheel.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(set.Width * 0.95f, radius * 1.6f, radius * 0.25f) },
            MaterialOverride = Materials.Get(Materials.DarkSteel, 0.7f),
        });
        Root.AddChild(wheel);
        _wheels.Add((wheel, radius));
    }
}

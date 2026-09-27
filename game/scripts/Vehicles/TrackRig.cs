using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>
/// A placeholder track: a belt of links (under name_belt, run by the running gear's view) around a wheel at each end,
/// with road wheels and a frame between them.
/// </summary>
public sealed class TrackRig
{
    private const float LinkPitch = 0.2f;
    private readonly List<(Node3D node, float radius)> _wheels = [];

    /// <summary>A track named <paramref name="name"/>, whose root goes at the middle of the track, on the ground.</summary>
    public TrackRig(WheelSetDef set, Color body, string name)
    {
        Root = new Node3D { Name = name };
        var t = WheelSetDef.TrackThickness;
        var belt = new TrackBelt(set);
        foreach (var z in new[] { set.Length * 0.5f, -set.Length * 0.5f })
            AddWheel(set, set.Radius, new Vector3(0f, belt.Hub, z), Materials.DarkSteel);
        // Road wheels on the bottom run, and the frame carrying them.
        var road = set.Radius * 0.42f;
        var count = Math.Max(1, (int)(set.Length / (road * 2.6f)));
        for (var k = 1; k <= count; k++)
            AddWheel(set, road, new Vector3(0f, t + road, -set.Length * 0.5f + set.Length * k / (count + 1)), Materials.Steel);
        PlaceholderBuilder.Box(Root, new Vector3(set.Width * 0.5f, set.Radius * 0.7f, set.Length), new Vector3(0f, belt.Hub, 0f), body * 0.8f);
        var links = new Node3D { Name = $"{name}_belt" };
        Root.AddChild(links);
        var n = Math.Max(8, (int)Mathf.Round(belt.Perimeter / LinkPitch));
        var material = Materials.Get(Materials.Tire * 1.2f, 0.95f);
        for (var k = 0; k < n; k++)
            links.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(set.Width, t, belt.Perimeter / n * 0.8f) }, MaterialOverride = material });
        belt.Place(links.GetChildren().OfType<Node3D>().ToList(), 0f);
    }

    public Node3D Root { get; }

    /// <summary>Turns the wheels of a track that has rolled <paramref name="distance"/> meters.</summary>
    public void Roll(float distance)
    {
        foreach (var (node, radius) in _wheels) node.Rotation = new Vector3(distance / radius, 0f, 0f);
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

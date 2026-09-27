using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>
/// The loop a track's belt runs round, from its wheel set: along the bottom, round the rear wheel, forward over the top
/// and round the front one, in the space of the track (its middle, on the ground). The links lie still on the ground as
/// the machine drives.
/// </summary>
public readonly struct TrackBelt
{
    private readonly float _length;
    /// <summary>Radius of the belt's middle around the end wheels.</summary>
    private readonly float _bend;

    public TrackBelt(WheelSetDef set)
    {
        var t = WheelSetDef.TrackThickness;
        _length = set.Length;
        Hub = set.Radius + t;
        _bend = set.Radius + t * 0.5f;
        Perimeter = 2f * _length + Mathf.Pi * 2f * _bend;
    }

    /// <summary>Height of the end wheels' axles.</summary>
    public float Hub { get; }
    public float Perimeter { get; }

    /// <summary>Spaces <paramref name="links"/> evenly round the belt of a track that has rolled <paramref name="distance"/> meters.</summary>
    public void Place(IReadOnlyList<Node3D> links, float distance)
    {
        for (var k = 0; k < links.Count; k++)
        {
            var (z, y, nz, ny) = At(Mathf.PosMod(k * Perimeter / links.Count + distance, Perimeter));
            links[k].Position = new Vector3(links[k].Position.X, y, z);
            // Its thin side along the belt's outward normal.
            links[k].Rotation = new Vector3(Mathf.Atan2(nz, ny), 0f, 0f);
        }
    }

    /// <summary>The point <paramref name="s"/> meters along the belt (z, y) and its outward normal, from the front of the bottom run back.</summary>
    private (float z, float y, float nz, float ny) At(float s)
    {
        var half = _length * 0.5f;
        var arc = Mathf.Pi * _bend;
        if (s < _length) return (half - s, Hub - _bend, 0f, -1f);
        s -= _length;
        if (s < arc)
        {
            var (sin, cos) = Mathf.SinCos(s / _bend);
            return (-half - _bend * sin, Hub - _bend * cos, -sin, -cos);
        }
        s -= arc;
        if (s < _length) return (-half + s, Hub + _bend, 0f, 1f);
        s -= _length;
        var (sn, cs) = Mathf.SinCos(Mathf.Min(s, arc) / _bend);
        return (half + _bend * sn, Hub + _bend * cs, sn, cs);
    }
}

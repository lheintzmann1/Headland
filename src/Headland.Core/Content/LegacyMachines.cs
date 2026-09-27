using Headland.Core.Machines.Components;

namespace Headland.Core.Content;

public sealed class LegacyMotorizedDef
{
    public float PowerHp { get; set; } = 100f;
    public float MaxSpeedKmh { get; set; } = 40f;
    public float MaxReverseKmh { get; set; } = 15f;
    public float Acceleration { get; set; } = 2.5f;
    public float Braking { get; set; } = 6f;
    public float Wheelbase { get; set; } = 2.6f;
    public float MaxSteerDeg { get; set; } = 38f;
    public float SteerRateDeg { get; set; } = 90f;
    public string SteerAxle { get; set; } = "front";
    public string? FuelTank { get; set; }
}

public sealed class LegacyAttacherDef
{
    public string Type { get; set; } = "threePoint";
    public string Mode { get; set; } = "mounted";
    public float X { get; set; }
    public float Z { get; set; }
    public float MaxArticulationDeg { get; set; } = 80f;
}

/// <summary>Turns machines written with the fixed blocks from before components (motorized, wheels, workArea…) into components.</summary>
internal static class LegacyMachines
{
    public static void Upgrade(MachineDef m)
    {
        if (m.Components.Count > 0) return;
        var c = new List<ComponentDef>();
        var mot = m.Motorized;
        if (m.Wheels is { Length: > 0 } || mot != null)
            c.Add(new RunningGearDef { Wheels = m.Wheels ?? [], MaxSteerDeg = mot?.MaxSteerDeg ?? 38f, SteerRateDeg = mot?.SteerRateDeg ?? 90f });
        if (mot != null)
        {
            c.Add(new MotorDef
            {
                PowerHp = mot.PowerHp, MaxSpeedKmh = mot.MaxSpeedKmh, MaxReverseKmh = mot.MaxReverseKmh,
                Acceleration = mot.Acceleration, Braking = mot.Braking, FuelUnit = mot.FuelTank,
            });
            c.Add(new DrivableDef());
            var s = m.Size;
            var front = s.CenterZ + s.Length * 0.5f;
            c.Add(new LightsDef
            {
                Lamps = [.. new[] { s.Width * 0.3f, -s.Width * 0.3f }.Select(x => new LampDef { X = x, Y = MathF.Min(s.Height - 0.3f, 2.2f), Z = front })],
            });
        }
        if (m.AttacherJoints is { Length: > 0 }) c.Add(new AttacherJointsDef { Joints = m.AttacherJoints });
        if (m.Attacher is { } a)
            c.Add(new AttachableDef
            {
                Type = a.Type, Mode = a.Mode, X = a.X, Z = a.Z, MaxArticulationDeg = a.MaxArticulationDeg,
                Lowerable = m.WorkArea != null, Lift = a.Type == "header" ? 0.6f : 0.45f,
            });
        if (m.FillUnits is { Length: > 0 }) c.Add(new FillUnitsDef { Units = m.FillUnits });
        if (m.WorkArea is { } wa)
        {
            wa.FillUnit ??= m.SeedTank;
            c.Add(new WorkAreasDef { Areas = [wa] });
        }
        if (m.HarvestTank != null) c.Add(new ThresherDef { FillUnit = m.HarvestTank });
        if (m.Pipe != null) c.Add(m.Pipe);
        if (m.Tipper != null) c.Add(m.Tipper);
        m.Components = c.OrderBy(d => ComponentKinds.Order(d.GetType())).ToList();
    }
}

using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>The paint (wearable): duller and paler as it wears, by tenths, and as it came once repainted.</summary>
public partial class WearableView : MachineComponentView
{
    /// <summary>How far paint worn through has gone toward a pale grey.</summary>
    private const float Faded = 0.45f;

    private int _step;

    public Wearable Wearable { get; init; } = null!;

    public override void _Process(double delta)
    {
        var step = (int)MathF.Round(Wearable.PaintWear * 10f);
        if (step == _step) return;
        _step = step;
        var paint = Conv.Hex(Entity.Def.Visual.Color);
        var grey = new Color(paint.Luminance, paint.Luminance, paint.Luminance).Lightened(0.25f);
        Rig.SetPaint(paint.Lerp(grey, step / 10f * Faded));
    }
}

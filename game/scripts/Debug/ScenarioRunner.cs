using System.Diagnostics;
using Headland.Core;
using Headland.Core.Machines;
using Headland.Core.Time;
using Headland.Core.World;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.Debug;

/// <summary>
/// Scripted end-to-end run used for verification (godot --path game -- --scenario=loop [--shots=DIR]).
/// Drives the whole loop with the autopilot, saves a screenshot per phase and prints a summary, then quits.
/// </summary>
public partial class ScenarioRunner : Node
{
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private int _shot;

    public GameRoot Game { get; init; } = null!;
    public string Scenario { get; init; } = "loop";
    public string ShotsDir { get; init; } = "user://shots";

    private Simulation Sim => Game.Sim;
    private MachineSystem Ms => Sim.Machines;

    public override async void _Ready()
    {
        var ok = false;
        try
        {
            Game.PlayerInputEnabled = false;
            switch (Scenario)
            {
                case "loop": await Loop(); break;
                case "tour": await Tour(); break;
                default: Log($"unknown scenario '{Scenario}'"); break;
            }
            ok = true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[scenario] FAILED: {e}");
        }
        finally
        {
            Log(ok ? "done" : "aborted");
            GetTree().Quit(ok ? 0 : 1);
        }
    }

    // ------------------------------------------------------------------ Scenarios

    /// <summary>The M1 loop: harvest → unload → sell → cultivate → sow → grow through the seasons.</summary>
    private async Task Loop()
    {
        Game.Camera.Zoom = 40f;
        await Frames(30);
        Log($"start {Sim.Clock.Date} {Sim.Clock.TimeString}, money ${Sim.Economy.Money:N0}");
        await Shot("yard");
        Log($"fps in yard: {await MeasureFps(3f):0.0}");

        var combine = Find("combine_7");
        var header = combine.Attached.Values.First();
        var tank = combine.Unit(combine.Def.HarvestTank)!;
        var f2 = Sim.World.FieldById(2)!;

        // --- Harvest three lanes of ripe wheat.
        Sim.Player.Enter(combine);
        Ms.Teleport(combine, new NVec2(f2.X + 3f, f2.Z - 8f), 0f);
        var helper = Hire(combine, new FieldInfo { Id = 2, Name = f2.Name, X = f2.X, Z = f2.Z, W = 18f, H = f2.H }, maxLanes: 3);
        Game.FocusOverride = () => combine.Footprint.Center;
        Game.Camera.Zoom = 34f;
        Game.SimSubsteps = 4;
        await Until(() => helper.Finished || helper.LanesDone >= 2 && combine.Speed > 2f && header.Lowered, 150f);
        Log($"harvested {tank.Level:N0} L wheat ({header.WorkedHa:0.00} ha), fps {Engine.GetFramesPerSecond():0}");
        await Shot("harvest");
        await Until(() => helper.Finished, 120f);
        Game.SimSubsteps = 1;
        combine.Controller = null;
        await Until(() => combine.Speed == 0f, 5f);

        // --- Unload into the trailer, parked under the pipe.
        var t95 = Find("tractor_95");
        var trailer = t95.Attached.Values.First();
        var pipe = combine.Def.Pipe!;
        var outlet = combine.LocalToWorld(pipe.X, pipe.Z);
        var h = combine.Heading;
        var trailerPos = outlet - MathUtil.Forward(h) * trailer.Def.Size.CenterZ;
        var eye = trailerPos + MathUtil.Forward(h) * trailer.Def.Attacher!.Z;
        Ms.Teleport(t95, eye - MathUtil.Forward(h) * t95.Joint("drawbar")!.Z, h);
        combine.PipeOut = true;
        Game.Camera.Zoom = 26f;
        var start = tank.Level;
        await Until(() => tank.Level < start * 0.55f, 60f);
        await Shot("unload");
        await Until(() => tank.IsEmpty, 60f);
        combine.PipeOut = false;
        Log($"trailer holds {trailer.FillUnits[0].Level:N0} L");

        // --- Drive (teleport) the trailer to the elevator and tip it.
        Sim.Player.Exit(Sim);
        var sell = Sim.World.SellPoints[0];
        Ms.Teleport(t95, new NVec2(sell.X + sell.W - 2.5f, sell.Z + sell.H * 0.5f), MathF.PI / 2f);
        Sim.Player.Enter(t95);
        Game.FocusOverride = null;
        var money = Sim.Economy.Money;
        Sim.CommandUnload();
        await Until(() => trailer.TipAnim > 0.9f && trailer.FillUnits[0].Fraction < 0.6f, 60f);
        await Shot("sell");
        await Until(() => !trailer.Tipping, 60f);
        Log($"sold for ${Sim.Economy.Money - money:N0}; money ${Sim.Economy.Money:N0}");

        // --- Cultivate stubble on the North Field.
        Sim.Player.Exit(Sim);
        var t125 = Find("tractor_125");
        var cultivator = t125.Attached.Values.First();
        var f1 = Sim.World.FieldById(1)!;
        var strip = new FieldInfo { X = f1.X, Z = f1.Z, W = 15f, H = f1.H };
        Sim.Player.Enter(t125);
        Ms.Teleport(t125, new NVec2(f1.X + 1.5f, f1.Z - 8f), 0f);
        helper = Hire(t125, strip, maxLanes: 5);
        Game.FocusOverride = () => t125.Footprint.Center;
        Game.Camera.Zoom = 40f;
        Game.SimSubsteps = 6;
        await Until(() => helper.Finished || helper.LanesDone >= 3 && t125.Speed < 2.5f, 120f);
        await Shot("cultivate_turn");
        await Until(() => helper.Finished, 120f);
        Game.SimSubsteps = 1;
        Log($"cultivated {cultivator.WorkedHa:0.00} ha, headland margin {helper.Margin:0.0} m, worked outside field: {OutsideCells(strip)} cells");
        Game.FocusOverride = () => new NVec2(strip.X + strip.W * 0.5f, strip.Z + 12f);
        Game.Camera.Zoom = 60f;
        await Frames(20);
        await Shot("cultivate_edges");

        // --- Swap to the seed drill and sow canola (in season in August).
        Ms.Detach(cultivator);
        Ms.Teleport(cultivator, new NVec2(f1.X - 10f, f1.Z - 12f), 0f);
        var seeder = Find("seeder_3");
        Ms.Teleport(t125, new NVec2(f1.X + 1.5f, f1.Z - 16f), 0f);
        Ms.Teleport(seeder, t125.LocalToWorld(0f, t125.Joint("drawbar")!.Z) - MathUtil.Forward(0f) * seeder.Def.Attacher!.Z, 0f);
        if (!Ms.Attach(t125, "drawbar", seeder)) throw new InvalidOperationException("could not attach the seeder");
        seeder.SelectedCrop = Sim.Content.CropIndex("canola");
        var seed = seeder.Unit("seed")!.Level;
        helper = Hire(t125, strip, maxLanes: 5);
        Game.FocusOverride = () => t125.Footprint.Center;
        Game.Camera.Zoom = 40f;
        Game.SimSubsteps = 6;
        await Until(() => helper.Finished, 150f);
        Game.SimSubsteps = 1;
        Log($"sowed {seeder.WorkedHa:0.00} ha canola using {seed - seeder.Unit("seed")!.Level:0.0} kg seed");
        await Shot("sow");

        // --- Let the seasons run.
        Sim.Player.Exit(Sim);
        var sown = new NVec2(f1.X + 6f, f1.Z + 30f);
        Sim.Player.Position = sown - new NVec2(10f, 0f);
        Game.FocusOverride = () => sown;
        Game.Camera.Zoom = 30f;
        foreach (var (month, day, hour, name) in new[]
                 {
                     (10, 2, 13f, "october"), (10, 2, 21.5f, "night"), (1, 2, 12f, "january"),
                     (4, 2, 12f, "april"), (5, 2, 12f, "may"), (7, 2, 11f, "july"),
                 })
        {
            SkipTo(month, day, hour);
            await Frames(40);
            Log($"{Sim.Clock.Date} {Sim.Clock.TimeString}: {Sim.Weather.Condition} {Sim.Weather.Temperature:0}°C, " +
                $"snow {Sim.Weather.SnowCover:0.00}, {CropSummary(1)}");
            await Shot(name);
        }

        Log($"summary: money ${Sim.Economy.Money:N0}, income ${Sim.Economy.TotalIncome:N0}, " +
            $"expenses ${Sim.Economy.TotalExpenses:N0}, hour tick {Sim.LastHourTickMs:0.0} ms, wall {_wall.Elapsed.TotalSeconds:0}s");
    }

    /// <summary>Quick visual pass without the gameplay loop (camera angles, zoom levels).</summary>
    private async Task Tour()
    {
        await Frames(30);
        foreach (var zoom in new[] { 20f, 45f, 110f })
        {
            Game.Camera.Zoom = zoom;
            await Frames(60);
            await Shot($"zoom{zoom:0}");
        }
        Game.Camera.RotateStep(1);
        await Frames(60);
        await Shot("rotated");
    }

    // ------------------------------------------------------------------ Helpers

    private Machine Find(string defId) => Ms.All.First(m => m.Def.Id == defId);

    private static FieldWorkController Hire(Machine v, FieldInfo area, int maxLanes)
    {
        var helper = new FieldWorkController(v, area, maxLanes: maxLanes);
        v.Controller = helper;
        return helper;
    }

    /// <summary>Cultivated cells within 20 m of a field rectangle but outside it.</summary>
    private int OutsideCells(FieldInfo f)
    {
        var w = Sim.World;
        var n = 0;
        for (var cz = 0; cz < w.CellsZ; cz++)
        for (var cx = 0; cx < w.CellsX; cx++)
        {
            var p = w.CellCenter(cx, cz);
            var inside = p.X > f.X && p.X < f.X + f.W && p.Y > f.Z && p.Y < f.Z + f.H;
            var near = p.X > f.X - 20 && p.X < f.X + f.W + 20 && p.Y > f.Z - 20 && p.Y < f.Z + f.H + 20;
            if (!inside && near && w.Layers.Ground[w.CellIndex(cx, cz)] == (byte)GroundType.Cultivated) n++;
        }
        return n;
    }

    private void SkipTo(int month, int day, float hour)
    {
        var cal = Sim.Calendar;
        var now = Sim.Clock.Date;
        var year = month > now.Month || (month == now.Month && day >= now.Day) ? now.Year : now.Year + 1;
        var targetHours = cal.DayIndexOf(new GameDate(year, month, day)) * 24.0 + hour;
        var currentHours = Sim.Clock.TotalSeconds / 3600.0;
        if (targetHours > currentHours) Sim.SkipHours((int)Math.Round(targetHours - currentHours));
    }

    private string CropSummary(int fieldId)
    {
        var L = Sim.World.Layers;
        var counts = new Dictionary<string, int>();
        double health = 0;
        var n = 0;
        for (var i = 0; i < L.Crop.Length; i++)
        {
            if (L.FieldId[i] != fieldId || L.Crop[i] == 0) continue;
            var def = Sim.Content.Crops[L.Crop[i] - 1];
            var stage = L.Stage[i] == CropStage.Dead ? "dead" : def.Stages[L.Stage[i]].Name;
            var key = $"{def.Name} {stage}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
            health += L.Health[i] / 255.0;
            n++;
        }
        if (n == 0) return "no crop";
        var top = string.Join(", ", counts.OrderByDescending(kv => kv.Value).Take(2).Select(kv => $"{kv.Key} {kv.Value * 100 / n}%"));
        return $"{top}, health {health / n * 100:0}%";
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Until(Func<bool> done, float timeoutSeconds)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.Elapsed.TotalSeconds < timeoutSeconds) await Frames(1);
        if (!done()) Log($"(timeout after {timeoutSeconds:0}s)");
    }

    private async Task<float> MeasureFps(float seconds)
    {
        var sw = Stopwatch.StartNew();
        var frames = 0;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            await Frames(1);
            frames++;
        }
        return (float)(frames / sw.Elapsed.TotalSeconds);
    }

    private async Task Shot(string name)
    {
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Game.SaveScreenshot($"{ShotsDir}/{++_shot:00}_{name}.png");
    }

    private void Log(string message) => GD.Print($"[scenario {_wall.Elapsed.TotalSeconds,5:0.0}s] {message}");
}

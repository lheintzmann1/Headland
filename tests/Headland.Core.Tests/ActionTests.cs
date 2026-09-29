using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Tests;

public class ActionTests
{
    /// <summary>What each hinted key does now, by action.</summary>
    private static Dictionary<string, string> Hints(Simulation sim) =>
        sim.Offers().Offers.Where(o => o.Hinted).ToDictionary(o => o.Action, o => o.Label);

    private static (Simulation sim, Machine tractor) Tractor()
    {
        var sim = TestContent.NewSim();
        return (sim, sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f));
    }

    [Fact]
    public void TogglesOfOneActionSwitchTogether()
    {
        var notes = new Notifications();
        var list = new ActionList(notes);
        var down = new[] { false, true, false };
        for (var i = 0; i < down.Length; i++)
        {
            var part = i;
            list.Toggle(InputActions.Lower, down[part], "Lower", "Raise", lower =>
            {
                if (lower && part == 2) return "The third is stuck";
                down[part] = lower;
                return null;
            });
        }
        list.Add(InputActions.CycleSeed, "Change seed", () => down[0] = true);
        list.Add(InputActions.Enter, "Exit", () => { });

        // One offer per action, in the order the help lists them.
        Assert.Equal([InputActions.Lower, InputActions.CycleSeed, InputActions.Enter], list.Offers.Select(o => o.Action));
        var lower = list.Of(InputActions.Lower)!;
        Assert.Equal("Raise", lower.Label);
        lower.Run();
        Assert.Equal([false, false, false], down);
        Assert.Empty(notes.Items);

        list = new ActionList(notes);
        for (var i = 0; i < down.Length; i++)
        {
            var part = i;
            list.Toggle(InputActions.Lower, down[part], "Lower", "Raise", lower =>
            {
                if (lower && part == 2) return "The third is stuck";
                down[part] = lower;
                return null;
            });
        }
        Assert.Equal("Lower", list.Of(InputActions.Lower)!.Label);
        list.Of(InputActions.Lower)!.Run();
        Assert.Equal([true, true, false], down);
        Assert.Equal("The third is stuck", Assert.Single(notes.Items).Text);
    }

    [Fact]
    public void TheKeysOfferWhatTheChainCanDo()
    {
        var (sim, t) = Tractor();
        var cultivator = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 298f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", cultivator));
        sim.Player.Enter(t);

        var hints = Hints(sim);
        Assert.Equal("Lower cultivator", hints[InputActions.Lower]);
        Assert.Equal($"Detach {cultivator.Def.Name}", hints[InputActions.Attach]);
        Assert.Equal("Exit", hints[InputActions.Enter]);
        Assert.False(hints.ContainsKey(InputActions.Fold));
        Assert.False(hints.ContainsKey(InputActions.Use));

        sim.Perform(InputActions.Lower);
        Assert.True(cultivator.Get<Attachable>()!.Lowered);
        Assert.Equal("Lift cultivator", Hints(sim)[InputActions.Lower]);
        sim.Perform(InputActions.Fold);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Nothing to fold");

        // A mower in front: one key lifts both while one is down, and lowers both once none is, saying so for all.
        var mower = sim.Machines.Spawn("mower_3", new Vector2(269f, 304f), 0f);
        Assert.True(sim.Machines.Attach(t, "front", mower));
        Assert.Equal("Lift all", Hints(sim)[InputActions.Lower]);
        Assert.Equal("Turn on mower", Hints(sim)[InputActions.TurnOn]);
        sim.Perform(InputActions.Lower);
        Assert.False(cultivator.Get<Attachable>()!.Lowered || mower.Get<Attachable>()!.Lowered);
        Assert.Equal("Lower all", Hints(sim)[InputActions.Lower]);
        sim.Perform(InputActions.Lower);
        Assert.True(cultivator.Get<Attachable>()!.Lowered && mower.Get<Attachable>()!.Lowered);
        sim.Perform(InputActions.TurnOn);
        Assert.True(mower.Get<WorkAreas>()!.On);
        Assert.Equal("Turn off mower", Hints(sim)[InputActions.TurnOn]);
    }

    [Fact]
    public void OnFootTheKeysOfferTheVehicleNearby()
    {
        var (sim, t) = Tractor();
        sim.Player.Position = new Vector2(250f, 300f);
        Assert.False(Hints(sim).ContainsKey(InputActions.Enter));
        sim.Perform(InputActions.Enter);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "No vehicle nearby");

        sim.Player.Position = t.LocalToWorld(2.2f, 1f);
        Assert.Equal($"Enter {t.Def.Name}", Hints(sim)[InputActions.Enter]);
        sim.Perform(InputActions.Lower);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Get into a vehicle first");
        sim.Perform(InputActions.Enter);
        Assert.Same(t, sim.Player.Vehicle);
        sim.Perform(InputActions.Lower);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "No implement to lower");
    }

    [Fact]
    public void TheToolKeysActOnTheSelectedImplement()
    {
        var (sim, t) = Tractor();
        var cultivator = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 298f), 0f);
        var mower = sim.Machines.Spawn("mower_3", new Vector2(269f, 304f), 0f);
        var trailer = sim.Machines.Spawn("trailer_16", new Vector2(269f, 290f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", cultivator));
        Assert.True(sim.Machines.Attach(t, "front", mower));
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
        trailer.Unit("main")!.Add("wheat", 1000f);
        sim.Player.Enter(t);
        var seat = t.Get<Drivable>()!;
        bool Lowered(Machine m) => m.Get<Attachable>()!.Lowered;

        Assert.Null(seat.Selected);
        Assert.Equal($"Select {cultivator.Def.Name}", Hints(sim)[InputActions.SelectImplement]);
        sim.Perform(InputActions.SelectImplement);
        Assert.Same(cultivator, seat.Selected);
        sim.Perform(InputActions.Lower);
        Assert.Equal((true, false), (Lowered(cultivator), Lowered(mower)));
        // The mower's switch and the trailer's tipping are not the cultivator's.
        Assert.Null(sim.Offers().Of(InputActions.TurnOn));
        Assert.Null(sim.Offers().Of(InputActions.Unload));

        sim.Perform(InputActions.SelectImplement);
        Assert.Same(mower, seat.Selected);
        sim.Perform(InputActions.Lower);
        sim.Perform(InputActions.TurnOn);
        Assert.Equal((true, true, true), (Lowered(cultivator), Lowered(mower), mower.Get<WorkAreas>()!.On));

        sim.Perform(InputActions.SelectImplement);
        Assert.Same(trailer, seat.Selected);
        Assert.NotNull(sim.Offers().Of(InputActions.Unload));
        Assert.Equal("Select all", Hints(sim)[InputActions.SelectImplement]);

        // The vehicle itself: the keys act on the whole chain again.
        sim.Perform(InputActions.SelectImplement);
        Assert.Null(seat.Selected);
        sim.Perform(InputActions.Lower);
        Assert.Equal((false, false), (Lowered(cultivator), Lowered(mower)));

        // An implement leaving the chain is no longer selected.
        seat.Selected = mower;
        sim.Machines.Detach(mower);
        Assert.Null(seat.Selected);
        Assert.Equal("Lower cultivator", Hints(sim)[InputActions.Lower]);
    }

    [Fact]
    public void EachToolSaysWhatItsKeysDoInItsOwnWords()
    {
        var (sim, t) = Tractor();
        var sprayer = sim.Machines.Spawn("sprayer_12", new Vector2(269f, 294f), 0f);
        Assert.True(sim.Machines.Attach(t, "drawbar", sprayer));
        sim.Player.Enter(t);
        var boom = sprayer.Get<AnimatedParts>()!;
        var lift = boom.Part("boom")!;

        // The sprayer names its boom: its wings unfold on the fold key, and it goes down on the lower key.
        Assert.Equal(("Unfold boom", "Lower boom", "Turn on sprayer"),
            (Hints(sim)[InputActions.Fold], Hints(sim)[InputActions.Lower], Hints(sim)[InputActions.TurnOn]));
        sim.Perform(InputActions.Lower);
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Unfold the {sprayer.Def.Name} first");
        sim.Perform(InputActions.Fold);
        sim.Perform(InputActions.Lower);
        Assert.Equal(("Fold boom", "Lift boom"), (Hints(sim)[InputActions.Fold], Hints(sim)[InputActions.Lower]));
        for (var s = 0f; s < 6.5f; s += 1f / 60f) sim.Tick(1f / 60f);
        // Unfolded, then down on its mast.
        Assert.Equal((0f, true), (lift.Position, boom.InWorkingPose));

        // Folding lifts it first, and the boom goes back up.
        sim.Perform(InputActions.Fold);
        for (var s = 0f; s < 1.5f; s += 1f / 60f) sim.Tick(1f / 60f);
        Assert.Equal((false, 1f), (sprayer.Get<Attachable>()!.Lowered, lift.Position));

        // A combine's pipe and its header: the words FS uses.
        var combine = sim.Machines.Spawn("combine_7", new Vector2(240f, 300f), 0f);
        var header = sim.Machines.Spawn("header_grain_6", new Vector2(240f, 301.6f), 0f);
        Assert.True(sim.Machines.Attach(combine, "header", header));
        sim.Player.Exit(sim);
        sim.Player.Enter(combine);
        Assert.Equal(("Lower header", "Turn on combine", "Pipe out"),
            (Hints(sim)[InputActions.Lower], Hints(sim)[InputActions.TurnOn], Hints(sim)[InputActions.Unload]));
    }
}

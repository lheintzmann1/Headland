using System.Numerics;
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
        Assert.Equal("Lower", hints[InputActions.Lower]);
        Assert.Equal($"Detach {cultivator.Def.Name}", hints[InputActions.Attach]);
        Assert.Equal("Exit", hints[InputActions.Enter]);
        Assert.False(hints.ContainsKey(InputActions.Fold));
        Assert.False(hints.ContainsKey(InputActions.Use));

        sim.Perform(InputActions.Lower);
        Assert.True(cultivator.Get<Attachable>()!.Lowered);
        Assert.Equal("Raise", Hints(sim)[InputActions.Lower]);
        sim.Perform(InputActions.Fold);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Nothing to fold");

        // A mower in front: one key raises both while one is down, and lowers both once none is.
        var mower = sim.Machines.Spawn("mower_3", new Vector2(269f, 304f), 0f);
        Assert.True(sim.Machines.Attach(t, "front", mower));
        Assert.Equal("Raise", Hints(sim)[InputActions.Lower]);
        Assert.Equal("Turn on", Hints(sim)[InputActions.TurnOn]);
        sim.Perform(InputActions.Lower);
        Assert.False(cultivator.Get<Attachable>()!.Lowered || mower.Get<Attachable>()!.Lowered);
        sim.Perform(InputActions.Lower);
        Assert.True(cultivator.Get<Attachable>()!.Lowered && mower.Get<Attachable>()!.Lowered);
        sim.Perform(InputActions.TurnOn);
        Assert.True(mower.Get<WorkAreas>()!.On);
        Assert.Equal("Turn off", Hints(sim)[InputActions.TurnOn]);
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
}

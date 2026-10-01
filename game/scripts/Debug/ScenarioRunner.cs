using System.Diagnostics;
using Headland.Core.Components;
using Headland.Core;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
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
                case "menus": await Menus(); break;
                case "bales": await Bales(); break;
                case "loader": await Loader(); break;
                case "heaps": await Heaps(); break;
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
        var tank = combine.Get<Thresher>()!.Tank;
        var f2 = Sim.World.FieldById(2)!.Shape;

        // --- Harvest three lanes of ripe wheat.
        Sim.Player.Enter(combine);
        Ms.Teleport(combine, f2.Min + new NVec2(3f, -8f), 0f);
        var helper = Sim.HireHelper(combine, FieldInfo.Rect(2, f2.Min.X, f2.Min.Y, 18f, f2.Size.Y), maxLanes: 3);
        Game.FocusOverride = () => combine.Footprint.Center;
        Game.Camera.Zoom = 34f;
        Game.SimSubsteps = 4;
        await Until(() => helper.Finished || helper.LanesDone >= 2 && combine.Speed > 2f && header.Get<Attachable>()!.Lowered, 150f);
        Log($"harvested {tank.Level:N0} L wheat ({header.WorkedHa:0.00} ha), fps {Engine.GetFramesPerSecond():0}");
        await Shot("harvest");
        await Until(() => helper.Finished, 120f);
        Game.SimSubsteps = 1;
        combine.Get<Drivable>()!.Controller = null;
        await Until(() => combine.Speed == 0f, 5f);

        // --- Unload into the trailer, parked under the pipe.
        var t95 = Find("tractor_95");
        var trailer = t95.Attached.Values.First();
        var pipe = combine.Get<Pipe>()!;
        var outlet = pipe.Outlet;
        var h = combine.Heading;
        var trailerPos = outlet - MathUtil.Forward(h) * trailer.Def.Size.CenterZ;
        var eye = trailerPos + MathUtil.Forward(h) * trailer.Get<Attachable>()!.Def.Z;
        Ms.Teleport(t95, eye - MathUtil.Forward(h) * t95.Joint("drawbar")!.Z, h);
        pipe.Out = true;
        Game.Camera.Zoom = 26f;
        var start = tank.Level;
        await Until(() => tank.Level < start * 0.55f, 60f);
        await Shot("unload");
        await Until(() => tank.IsEmpty, 60f);
        pipe.Out = false;
        Log($"trailer holds {trailer.FillUnits[0].Level:N0} L");

        // --- Drive (teleport) the trailer to the elevator and tip it.
        Sim.Player.Exit(Sim);
        // Along the pit's x axis, with the trailer inside it.
        var pit = Sim.World.PoiById("elevator")!.Trigger("unload")!.Area;
        Ms.Teleport(t95, pit.Center + pit.AxisX * (pit.HalfExtents.X - 2.5f), MathUtil.HeadingOf(pit.AxisX));
        Sim.Player.Enter(t95);
        Game.FocusOverride = null;
        var money = Sim.Economy.Money;
        Sim.Perform(InputActions.Unload);
        var tipper = trailer.Get<Tipper>()!;
        await Until(() => tipper.Anim > 0.9f && tipper.Load.Fraction < 0.6f, 60f);
        await Shot("sell");
        await Until(() => !tipper.Tipping, 60f);
        Log($"sold for ${Sim.Economy.Money - money:N0}; money ${Sim.Economy.Money:N0}");

        // --- Cultivate stubble on field 1.
        Sim.Player.Exit(Sim);
        var t125 = Find("tractor_125");
        var cultivator = t125.Attached.Values.First();
        var f1 = Sim.World.FieldById(1)!.Shape;
        var strip = FieldInfo.Rect(1, f1.Min.X, f1.Min.Y, 15f, f1.Size.Y);
        Sim.Player.Enter(t125);
        Ms.Teleport(t125, f1.Min + new NVec2(1.5f, -8f), 0f);
        helper = Sim.HireHelper(t125, strip, maxLanes: 5);
        Game.FocusOverride = () => t125.Footprint.Center;
        Game.Camera.Zoom = 40f;
        Game.SimSubsteps = 6;
        await Until(() => helper.Finished || helper.LanesDone >= 3 && t125.Speed < 2.5f, 120f);
        await Shot("cultivate_turn");
        await Until(() => helper.Finished, 120f);
        Game.SimSubsteps = 1;
        Log($"cultivated {cultivator.WorkedHa:0.00} ha, headland margin {helper.Margin:0.0} m, worked outside field: {OutsideCells(strip)} cells");
        Game.FocusOverride = () => new NVec2(strip.Center.X, strip.Shape.Min.Y + 12f);
        Game.Camera.Zoom = 60f;
        await Frames(20);
        await Shot("cultivate_edges");

        // --- Swap to the seed drill and sow canola (in season in August).
        Ms.Detach(cultivator);
        Ms.Teleport(cultivator, f1.Min + new NVec2(-10f, -12f), 0f);
        var seeder = Find("seeder_3");
        Ms.Teleport(t125, f1.Min + new NVec2(1.5f, -16f), 0f);
        Ms.Teleport(seeder, t125.LocalToWorld(0f, t125.Joint("drawbar")!.Z) - MathUtil.Forward(0f) * seeder.Get<Attachable>()!.Def.Z, 0f);
        if (!Ms.Attach(t125, "drawbar", seeder)) throw new InvalidOperationException("could not attach the seeder");
        seeder.Get<WorkAreas>()!.Crop = Sim.Content.CropIndex("canola");
        var seed = seeder.Unit("seed")!.Level;
        helper = Sim.HireHelper(t125, strip, maxLanes: 5);
        Game.FocusOverride = () => t125.Footprint.Center;
        Game.Camera.Zoom = 40f;
        Game.SimSubsteps = 6;
        await Until(() => helper.Finished, 150f);
        Game.SimSubsteps = 1;
        Log($"sowed {seeder.WorkedHa:0.00} ha canola using {seed - seeder.Unit("seed")!.Level:0.0} kg seed");
        await Shot("sow");

        // --- Let the seasons run.
        Sim.Player.Exit(Sim);
        var sown = f1.Min + new NVec2(6f, 30f);
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

    /// <summary>Every tab of the in-game menu, in turn (with --headless, only built and shown: nothing is drawn).</summary>
    private async Task Menus()
    {
        await Frames(10);
        var menu = Game.Screens.Push(new UI.MenuScreen { Game = Game });
        for (var i = 0; i < menu.TabCount; i++)
        {
            if (i > 0) menu.Step(1);
            await Frames(20);
            Log($"tab {menu.TabTitle}");
            if (menu.Page is UI.ShopPage shop) await ShopMachines(shop);
            else if (menu.Page is UI.MapPage map) await EachMapLayer(map);
            else if (DisplayServer.GetName() != "headless") await Shot($"menu_{menu.TabTitle.ToLowerInvariant()}");
            if (menu.Page is UI.GaragePage garage) await GarageMachine(garage);
        }
        menu.Close();
        await Settings();
    }

    /// <summary>
    /// The settings screen's tabs, and a rebinding: a key pressed for an action, then removed. On settings of its own,
    /// in the shots' folder, so the player's aren't touched.
    /// </summary>
    private async Task Settings()
    {
        var path = $"{ShotsDir.TrimEnd('/')}/settings-test.cfg";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(ShotsDir));
        var settings = new Common.UserSettings { Path = path };
        var screen = Game.Screens.Push(new UI.SettingsScreen { Settings = settings, Sim = Sim });
        foreach (var tab in new[] { "Graphics", "Audio", "Gameplay", "Controls" })
        {
            screen.ShowTab(tab);
            await Frames(10);
            Log($"settings: {tab}");
            if (DisplayServer.GetName() != "headless") await Shot($"settings_{tab.ToLowerInvariant()}");
        }
        var action = InputActions.Helper;
        var before = settings.Controls.Of(action).Count;
        screen.Capture(action, -1);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F7, Keycode = Key.F7, Pressed = true });
        await Frames(5);
        var bound = settings.Controls.Of(action).Select(b => b.ToString()).ToList();
        screen.Capture(action, bound.IndexOf("F7"));
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Delete, Keycode = Key.Delete, Pressed = true });
        await Frames(5);
        Log($"settings: {action} bound to {string.Join(", ", bound)}, then back to {string.Join(", ", settings.Controls.Of(action))} ({(settings.Controls.Of(action).Count == before ? "ok" : "wrong")})");
        screen.Close();
    }

    /// <summary>The map in each of its layers.</summary>
    private async Task EachMapLayer(UI.MapPage map)
    {
        foreach (var layer in Enum.GetValues<MapLayer>())
        {
            map.ShowLayer(layer);
            await Frames(10);
            Log($"map: {layer}");
            if (layer == MapLayer.Farmland && Sim.World.Farmlands.FirstOrDefault(l => Sim.Farms.BuyBlocker(l) == null) is { } land)
            {
                // Bought through the map, as the farmer would.
                var money = Sim.Economy.Money;
                map.Pick(land);
                map.Deal();
                await Frames(5);
                Log($"map: bought {land.Label} for ${money - Sim.Economy.Money:N0} ({Sim.Farms.OwnerName(land)}'s now)");
            }
            if (DisplayServer.GetName() != "headless") await Shot($"map_{layer.ToString().ToLowerInvariant()}");
        }
        map.ShowLayer(MapLayer.Terrain);
    }

    /// <summary>The first machine of the garage: its options where it stands, then sold.</summary>
    private async Task GarageMachine(UI.GaragePage garage)
    {
        Button Named(string start) => garage.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text.StartsWith(start));
        Named("Options").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(10);
        Log($"garage: options of {Sim.Garage.Machines.First()} ({Game.Screens.Top?.GetType().Name})");
        if (DisplayServer.GetName() != "headless") await Shot("garage_options");
        Game.Screens.Top!.Close();
        var count = Sim.Machines.All.Count;
        var money = Sim.Economy.Money;
        for (var press = 0; press < 2; press++)
        {
            Named("Sell").EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(5);
        }
        Log($"garage: sold {count - Sim.Machines.All.Count} for ${Sim.Economy.Money - money:N0}");
    }

    /// <summary>Each machine for sale in the shop, with each of its options picked in turn.</summary>
    private async Task ShopMachines(UI.ShopPage shop)
    {
        foreach (var def in Sim.Shop.Categories().SelectMany(c => Sim.Shop.Machines(c)).ToList())
        {
            shop.Show(def);
            await Frames(5);
            if (DisplayServer.GetName() != "headless") await Shot($"shop_{def.Id}");
            var options = shop.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>().Where(o => o.GetParent() is GridContainer).ToList();
            foreach (var dropdown in options)
                for (var i = 0; i < dropdown.ItemCount; i++)
                {
                    dropdown.Select(i);
                    dropdown.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                    await Frames(1);
                }
            Log($"shop: {def.Id} with {options.Count} configurations");
        }
        // The last one shown, bought and leased: both wait on the dealer's lot.
        foreach (var deal in new[] { "Buy", "Lease" })
        {
            var button = shop.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == deal);
            var before = Ms.All.Count;
            if (!button.Disabled) button.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(5);
            Log($"shop: {deal} {(Ms.All.Count > before ? $"delivered {Ms.All[^1]}" : "did nothing")}, money ${Sim.Economy.Money:N0}");
            if (DisplayServer.GetName() != "headless") await Shot($"shop_{deal.ToLowerInvariant()}");
        }
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// Mow a strip of the farm's meadow, bale it, collect the bales and leave them at the dairy (shots only with a window).
    /// </summary>
    private async Task Bales()
    {
        Game.Camera.Zoom = 30f;
        await Frames(10);
        var meadow = Sim.World.FieldById(7)!.Shape;
        var strip = FieldInfo.Rect(7, meadow.Min.X, meadow.Min.Y, 12f, meadow.Size.Y);
        var at = new NVec2(meadow.Min.X + 3f, meadow.Min.Y - 8f);

        // --- Mow.
        var tractor = Ms.Spawn("tractor_125", at, 0f);
        var mower = Ms.Spawn("mower_3", at - new NVec2(0f, 3f), 0f);
        Ms.Attach(tractor, "rear", mower);
        Game.FocusOverride = () => tractor.Footprint.Center;
        Game.SimSubsteps = 4;
        var mowing = Sim.HireHelper(tractor, strip, maxLanes: 4);
        await Until(() => mowing.Finished, 240f);
        Game.SimSubsteps = 1;
        Log($"mowed {mower.WorkedHa:0.00} ha: {Lying("grass"):N0} L of grass in windrows");
        if (DisplayServer.GetName() != "headless") await Shot("windrows");

        // --- Bale what lies there.
        Ms.Detach(mower);
        Ms.Teleport(mower, at + new NVec2(-12f, -6f), 0f);
        var baler = Ms.Spawn("baler_125", tractor.Position - MathUtil.Forward(tractor.Heading) * 5f, tractor.Heading);
        Ms.Attach(tractor, "drawbar", baler);
        Ms.Teleport(tractor, at, 0f);
        Game.SimSubsteps = 4;
        var baling = Sim.HireHelper(tractor, strip);
        await Until(() => baling.Finished, 300f);
        Game.SimSubsteps = 1;
        baler.Get<Baler>()!.Drop(Sim);
        var bales = Sim.Objects.All.ToList();
        Log($"baled {Sim.Statistics.Harvested.GetValueOrDefault("grass"):N0} L into {bales.Count} bales, {Lying("grass"):N0} L left lying");
        if (DisplayServer.GetName() != "headless") await Shot("bales");

        // --- Collect them: the loader's pickup beside each in turn.
        var t95 = Ms.Spawn("tractor_95", at + new NVec2(20f, 0f), 0f);
        var loader = Ms.Spawn("baleloader_8", t95.Position - new NVec2(0f, 6f), 0f);
        Ms.Attach(t95, "drawbar", loader);
        var bed = loader.Get<BaleLoader>()!;
        bed.On = true;
        Game.FocusOverride = () => loader.Footprint.Center;
        foreach (var bale in bales.Take(bed.Capacity))
        {
            var pickup = bed.Def.Pickup;
            var heading = bale.Heading;
            var loaderAt = bale.Position - MathUtil.Left(heading) * pickup.X - MathUtil.Forward(heading) * pickup.Z;
            Ms.Teleport(t95, loaderAt + MathUtil.Forward(heading) * loader.Get<Attachable>()!.Def.Z - MathUtil.Forward(heading) * t95.Joint("drawbar")!.Z, heading);
            await Until(() => bale.Holder != null, 5f);
        }
        Log($"loaded {bed.Count} bales");
        if (DisplayServer.GetName() != "headless") await Shot("loaded");

        // --- Leave them at the dairy.
        var dairy = Sim.World.PoiById("dairy")!.Trigger("objects")!.Area;
        var money = Sim.Economy.Money;
        var facing = MathUtil.HeadingOf(dairy.AxisY);
        Ms.Teleport(t95, dairy.Center + dairy.AxisY * (dairy.HalfExtents.Y + 9f), facing);
        await Frames(10);
        bed.Unload(Sim);
        await Until(() => Sim.Economy.Money > money, 5f);
        Log($"sold the bales at the dairy for ${Sim.Economy.Money - money:N0}; {Sim.Objects.All.Count} bales left in the field");
        if (DisplayServer.GetName() != "headless") await Shot("dairy");

        float Lying(string fillType) => Enumerable.Range(0, Sim.World.Layers.Windrow.Length)
            .Where(i => Windrows.FillTypeAt(Sim.Content, Sim.World.Layers, i)?.Id == fillType)
            .Sum(i => Sim.World.Layers.Windrow[i]);
    }

    /// <summary>
    /// The front loader's tools: stack two bales with the spike, then mill wheat into flour pallets and fork one to the
    /// farm shop (shots only with a window).
    /// </summary>
    private async Task Loader()
    {
        Game.Camera.Zoom = 22f;
        await Frames(10);
        var yard = new NVec2(160f, 166f);
        var tractor = Ms.Spawn("tractor_125", yard, MathF.PI * 0.5f, configuration: new Dictionary<string, string> { ["frontLoader"] = "bracket" });
        var arm = Ms.Spawn("frontloader_arm", yard, tractor.Heading);
        Ms.Attach(tractor, "frontLoader", arm);
        var crane = arm.Get<CraneArm>()!;
        var spike = Ms.Spawn("bale_spike", yard, tractor.Heading);
        Ms.Attach(arm, "tool", spike);
        Sim.Player.Enter(tractor);
        Game.FocusOverride = () => tractor.Footprint.Center;
        await Frames(5);

        // --- Spike a bale, lift it onto another.
        var fork = spike.Get<Fork>()!;
        var top = Sim.Objects.Spawn("round_bale", spike.LocalToWorld(0f, 0.75f), tractor.Heading, Sim.Player.FarmId);
        top.Content!.Add("hay", 4000f);
        await Until(() => top.Holder != null, 3f);
        crane.MoveTo("lift", 45f);
        await Until(() => crane.Joint("lift")!.Value > 44f, 5f);
        var under = Sim.Objects.Spawn("round_bale", top.Position, tractor.Heading, Sim.Player.FarmId);
        under.Content!.Add("straw", 4000f);
        await Frames(5);
        if (DisplayServer.GetName() != "headless") await Shot("spike_lifted");
        fork.SetDown(Sim);
        await Frames(5);
        Log($"spiked a bale: set it down at {top.Elevation:0.00} m, on the other");
        if (DisplayServer.GetName() != "headless") await Shot("stacked");

        // --- Mill wheat: flour comes out on pallets; the fork takes one to the shop.
        var mill = Sim.World.PoiById("grainmill")!;
        mill.Get<Headland.Core.Components.FillUnits>()!.Add("wheat", 6000f);
        Sim.SkipHours(10);
        var pallets = Sim.Objects.LooseIn(mill.Trigger("pallets")!.Area).ToList();
        Log($"milled: {pallets.Count} pallets, {pallets.Sum(p => p.Content!.Level):N0} kg of flour");
        crane.MoveTo("lift", 0f);
        await Until(() => crane.Joint("lift")!.Value < 1f, 6f);
        Ms.Detach(spike);
        Ms.Teleport(spike, yard + new NVec2(0f, 8f), 0f);
        var forks = Ms.Spawn("pallet_fork", arm.Position, arm.Heading);
        Ms.Attach(arm, "tool", forks);
        var pallet = pallets.First();
        Reach(tractor, forks, pallet.Position, pallet.Heading);
        await Until(() => pallet.Holder != null, 3f);
        crane.MoveTo("lift", 15f);
        await Until(() => crane.Joint("lift")!.Value > 14f, 3f);
        if (DisplayServer.GetName() != "headless") await Shot("pallet_lifted");
        var stand = Sim.World.PoiById("supplies")!.Trigger("objects")!.Area;
        Reach(tractor, forks, stand.Center, stand.Heading);
        var money = Sim.Economy.Money;
        await Frames(5);
        forks.Get<Fork>()!.SetDown(Sim);
        await Until(() => Sim.Economy.Money > money, 3f);
        Log($"sold a pallet of {pallet.Content!.Level:N0} kg of flour at the farm shop for ${Sim.Economy.Money - money:N0}");
        if (DisplayServer.GetName() != "headless") await Shot("pallet_sold");

        void Reach(Machine t, Machine tool, NVec2 p, float heading)
        {
            Ms.Teleport(t, t.Position, heading);
            var tines = tool.LocalToWorld(0f, tool.Get<Fork>()!.Def.Area.Z);
            Ms.Teleport(t, t.Position + p - tines, heading);
        }
    }

    /// <summary>Tips a trailer of wheat on the farm's land, takes it up with a bucket on the front loader and pours it into a trailer.</summary>
    private async Task Heaps()
    {
        Game.Camera.Zoom = 26f;
        await Frames(10);
        var yard = new NVec2(160f, 100f);
        var east = MathF.PI * 0.5f;
        var shots = DisplayServer.GetName() != "headless";

        // --- Tip a trailer of wheat on the ground, driving on whenever the heap comes up to the tailgate.
        var hauler = Ms.Spawn("tractor_95", yard + new NVec2(6f, 0f), east);
        var trailer = Ms.Spawn("trailer_16", yard, east);
        Ms.Attach(hauler, "drawbar", trailer);
        trailer.Unit("main")!.Add("wheat", 12000f);
        Sim.Player.Enter(hauler);
        Game.FocusOverride = () => trailer.Footprint.Center;
        var bed = trailer.Get<Tipper>()!;
        Sim.Perform(InputActions.TipGround);
        Log($"tipping on the ground: {bed.Tipping}");
        for (var k = 0; k < 6 && !bed.Load.IsEmpty; k++)
        {
            await Until(() => bed.Load.IsEmpty || bed.HeapUp, 40f);
            if (bed.HeapUp) Ms.Teleport(hauler, hauler.Position + MathUtil.Forward(east) * 4f, east);
            await Frames(2);
        }
        Sim.Heaps.SettleAll();
        var (amount, top) = HeapAround(yard - new NVec2(3f, 0f), 12f);
        Log($"tipped on the ground: {amount:N0} L of wheat, {top:0.00} m high");
        if (shots) await Shot("heap");

        // --- Drive a bucket into it, the arm down and the bucket level.
        Ms.Teleport(hauler, yard + new NVec2(30f, 0f), east);
        var loader = Ms.Spawn("tractor_125", yard - new NVec2(16f, 0f), east, configuration: new Dictionary<string, string> { ["frontLoader"] = "bracket" });
        var arm = Ms.Spawn("frontloader_arm", loader.Position, east);
        Ms.Attach(loader, "frontLoader", arm);
        var bucket = Ms.Spawn("bucket", arm.Position, east);
        Ms.Attach(arm, "tool", bucket);
        var crane = arm.Get<CraneArm>()!;
        var load = bucket.Unit("bucket")!;
        Sim.Player.Exit(Sim);
        Game.FocusOverride = () => loader.Footprint.Center;
        loader.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = 0.12f } };
        await Until(() => load.Free < 1f, 20f);
        loader.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Brake = true } };
        Log($"bucket: {load.Level:N0} L of {load.FillType}, the tractor {Sim.World.HeapAt(loader.Position):0.00} m up the heap");
        if (shots) await Shot("bucket_full");

        // --- Lift it over a trailer and tip it forward.
        crane.MoveTo("lift", 50f);
        crane.MoveTo("tilt", -50f);
        await Until(() => crane.Joint("lift")!.Value > 49f, 5f);
        var edge = bucket.Get<Shovel>()!.Def.Edge;
        var p = bucket.OnCrane(new System.Numerics.Vector3(0f, edge.Y, edge.Z))!.Value;
        var truck = Ms.Spawn("trailer_16", arm.PartToWorld(p.X, p.Z).position, 0f);
        crane.MoveTo("tilt", -100f);
        await Until(() => load.IsEmpty, 8f);
        Log($"poured into a trailer: {truck.Unit("main")!.Level:N0} L");
        if (shots) await Shot("bucket_poured");

        (float amount, float top) HeapAround(NVec2 c, float r)
        {
            var w = Sim.World;
            var (sum, high) = (0f, 0f);
            var (x0, z0) = w.WorldToCell(c - new NVec2(r));
            var (x1, z1) = w.WorldToCell(c + new NVec2(r));
            for (var cz = z0; cz <= z1; cz++)
            for (var cx = x0; cx <= x1; cx++)
            {
                var i = w.CellIndex(cx, cz);
                sum += Sim.Heaps.AmountAt(i);
                high = MathF.Max(high, Sim.Heaps.Has(i) ? w.Layers.Heap[i] : 0f);
            }
            return (sum, high);
        }
    }

    private Machine Find(string defId) => Ms.All.First(m => m.Def.Id == defId);

    /// <summary>Cultivated cells within 20 m of a field but outside it.</summary>
    private int OutsideCells(FieldInfo f)
    {
        var w = Sim.World;
        var n = 0;
        for (var cz = 0; cz < w.CellsZ; cz++)
        for (var cx = 0; cx < w.CellsX; cx++)
        {
            var p = w.CellCenter(cx, cz);
            var d = f.Shape.Distance(p);
            if (d > 0f && d < 20f && w.Layers.Ground[w.CellIndex(cx, cz)] == (byte)GroundType.Cultivated) n++;
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

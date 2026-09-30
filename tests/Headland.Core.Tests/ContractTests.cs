using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Machines.Work;
using Headland.Core.Ownership;
using Headland.Core.Pois.Components;
using Headland.Core.Saves;
using Headland.Core.World;
using static Headland.Core.Tests.PoiTests;

namespace Headland.Core.Tests;

public class ContractTests
{
    /// <summary>
    /// A small map: the farm's parcel with field 1 (stubble), Ada Morrow's with these fields, an elevator and a
    /// machinery dealer.
    /// </summary>
    private static Simulation Neighbors(params FieldDef[] fields) => Neighbors(null, fields);

    /// <summary>The same, with the content changed first by <paramref name="setup"/>.</summary>
    private static Simulation Neighbors(Action<ContentDatabase>? setup, params FieldDef[] fields)
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        setup?.Invoke(db);
        db.Maps["neighbors"] = new MapDef
        {
            Id = "neighbors", Name = "Neighbors", Size = 256, Seed = 3, HillAmplitude = 0f, ScatteredTreesPerHa = 0f,
            Farmlands =
            [
                new FarmlandDef { Id = 1, Npc = "hendricks", Farm = Farm.PlayerId, X = 0, Z = 0, W = 64, H = 128 },
                new FarmlandDef { Id = 2, Npc = "morrow", X = 64, Z = 0, W = 192, H = 128 },
            ],
            Fields = [new FieldDef { Id = 1, X = 8, Z = 8, W = 48, H = 48, Ground = "stubble" }, .. fields],
            Pois =
            [
                new PoiPlacementDef { Id = "elevator", Type = "grain_elevator", X = 128, Z = 200 },
                new PoiPlacementDef { Id = "dealer", Type = "machinery_dealer", X = 215, Z = 190 },
            ],
            PlayerX = 2, PlayerZ = 2,
        };
        db.Game.Map = "neighbors";
        return Simulation.Create(db);
    }

    /// <summary>The game starts in <paramref name="month"/>, and the neighbors only offer <paramref name="jobs"/>.</summary>
    private static Action<ContentDatabase> Only(int month, params string[] jobs) => db =>
    {
        db.Game.StartMonth = month;
        foreach (var id in db.ContractTypes.Keys.Except(jobs).ToList()) db.ContractTypes.Remove(id);
    };

    /// <summary>Puts every cell of field <paramref name="id"/> through <paramref name="set"/>.</summary>
    private static void SetCells(Simulation sim, int id, Action<FieldLayers, int> set)
    {
        var L = sim.World.Layers;
        for (var i = 0; i < L.FieldId.Length; i++)
            if (L.FieldId[i] == id) set(L, i);
    }

    /// <summary>A 50 m square neighbor's field; ids 2 to 7 fill two rows of three.</summary>
    private static FieldDef Field(int id, string ground, string? crop = null, string? stage = null) => new()
    {
        Id = id, X = 72 + (id - 2) % 3 * 60, Z = 8 + (id - 2) / 3 * 60, W = 50, H = 50, Ground = ground, Crop = crop, Stage = stage,
    };

    private static string Describe(Contract c) =>
        $"{c} {c.Npc?.Id} {c.Crop?.Id} {c.Poi?.Id} {c.Goods?.Id} {c.Amount} {c.Reward} {c.Days} {c.OfferedDay} {c.FarmId} {c.DueDay} " +
        $"{c.Progress:0.0000} {c.Harvested} {c.Delivered}";

    /// <summary>Puts field <paramref name="id"/>'s whole ground in the given state, as if worked.</summary>
    private static void SetGround(Simulation sim, int id, GroundType ground)
    {
        var L = sim.World.Layers;
        for (var i = 0; i < L.FieldId.Length; i++)
            if (L.FieldId[i] == id) L.Ground[i] = (byte)ground;
    }

    private static void Run(Simulation sim, float seconds, Func<bool>? until = null)
    {
        for (var s = 0f; s < seconds && until?.Invoke() != true; s += 1f / 60f) sim.Tick(1f / 60f);
    }

    [Fact]
    public void ContractTypesComeFromTheData()
    {
        var types = TestContent.Content.ContractTypes;
        Assert.Equal(["cultivate", "deliver", "fertilize", "harvest", "mow", "plow", "sow", "spray"], types.Keys.ToArray());
        var harvest = types["harvest"];
        Assert.Equal(("harvester", 0.9f), (harvest.Work, harvest.Deliver!.Share));
        Assert.Equal(["harvestable"], harvest.Offer.Crop);
        Assert.Equal("sprayer", types["spray"].Work);
        Assert.Equal(["small", "grown"], types["spray"].Offer.Weeds);
        Assert.Equal(["none", "sprayed"], types["spray"].Done.Weeds);
        Assert.Equal((false, true), (types["fertilize"].Offer.Fertilized, types["fertilize"].Done.Fertilized));
        Assert.Equal("", types["deliver"].Work);
        Assert.Equal([4000f, 12000f], types["deliver"].Deliver!.Amount);
    }

    [Fact]
    public void ContractTypesAreValidated()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.ContractTypes["juggle"] = new ContractTypeDef
        {
            Id = "juggle", Name = "Juggle", Work = "juggler", Months = [13], Days = [4, 2], Weight = 0,
            Offer = new FieldStateDef { Ground = ["road"], Crop = ["ripe"], Weeds = ["dandelions"] },
            Deliver = new ContractDeliveryDef { Share = 0.5f, Amount = [1000f, 2000f] },
        };
        db.ContractTypes["haul"] = new ContractTypeDef
        {
            Id = "haul", Name = "Haul", Done = new FieldStateDef { Ground = ["cultivated"] },
            Deliver = new ContractDeliveryDef { Share = 0.5f, FillTypes = ["gravel"], PriceFactor = 0 },
        };
        var errors = db.Validate();
        Assert.Contains("contract type 'juggle': months must be 1..12", errors);
        Assert.Contains("contract type 'juggle': days needs [min, max] >= 1", errors);
        Assert.Contains("contract type 'juggle': weight must be > 0 and rewardPerHa >= 0", errors);
        Assert.Contains("contract type 'juggle': offer ground 'road' is not a field's (grass, cultivated, seeded, stubble, plowed)", errors);
        Assert.Contains("contract type 'juggle': offer crop state 'ripe' is unknown (none, dead, sown, growing, harvestable)", errors);
        Assert.Contains("contract type 'juggle': offer weed state 'dandelions' is unknown (none, small, grown, sprayed)", errors);
        Assert.Contains("contract type 'juggle': unknown work 'juggler' (cultivator, plow, seeder, harvester, spreader, sprayer, mower)", errors);
        Assert.Contains("contract type 'juggle': a field job needs offer and done states", errors);
        Assert.Contains("contract type 'juggle': deliver.amount is for delivery jobs (no work)", errors);
        Assert.Contains("contract type 'juggle': deliver.share must be in (0, 1], on jobs that harvest", errors);
        Assert.Contains("contract type 'haul': a delivery job (no work) needs deliver.amount [min, max] > 0", errors);
        Assert.Contains("contract type 'haul': offer and done are for field jobs", errors);
        Assert.Contains("contract type 'haul': deliver.share is for harvest jobs", errors);
        Assert.Contains("contract type 'haul': deliver.priceFactor must be > 0", errors);
        Assert.Contains("contract type 'haul': unknown fill type 'gravel'", errors);
        Assert.Equal(15, errors.Count);
    }

    [Fact]
    public void ContractRulesAreValidated()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Economy.Contracts = new ContractRulesDef { MaxOffers = -1, OfferDays = 0, Threshold = 1.5f, Penalty = 2f };
        Assert.Equal(["economy.contracts: maxOffers and offersPerDay must be >= 0, offerDays and maxActive >= 1",
            "economy.contracts.threshold must be in (0, 1]", "economy.contracts.penalty must be 0..1"], db.Validate());
    }

    [Fact]
    public void NeighborsOfferTheWorkTheirFieldsNeed()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        var offers = sim.Contracts.Offers.ToList();

        // August: field 4's grass is ready to mow and field 6 (stubble) needs cultivating, field 5's corn is still
        // growing, and fields 1 to 3 and the meadow 7 are the farm's own.
        var jobs = offers.Where(c => c.Field != null).OrderBy(c => c.Field!.Id).ToList();
        Assert.Equal(["Mow grass on Field 4", "Cultivate Field 6"], jobs.Select(c => c.Label));
        Assert.Equal(["aldridge", "brandt"], jobs.Select(c => c.Npc!.Id));
        Assert.All(jobs, c => Assert.InRange(c.Reward, 0.9f * c.Type.RewardPerHa * c.Field!.AreaHa - 5f, 1.1f * c.Type.RewardPerHa * c.Field.AreaHa + 5f));
        Assert.All(jobs, c => Assert.InRange(c.Days, c.Type.Days[0], c.Type.Days[1]));

        // Each buyer asks for one kind of goods at a time, for their market price and 30% more.
        var deliveries = offers.Where(c => c.Field == null).OrderBy(c => c.Poi!.Id).ToList();
        Assert.Equal(["elevator", "mill"], deliveries.Select(c => c.Poi!.Id));
        Assert.Equal("wheat", deliveries[1].Goods!.Id);
        Assert.All(deliveries, c =>
        {
            Assert.Equal((0f, c.Poi!.Name), (c.Amount % 1000f, c.Client));
            Assert.InRange(c.Amount, 4000f, 12000f);
            Assert.Equal(MathF.Round(c.Amount * sim.Economy.Price(c.Goods!.Id, 8) * 1.3f / 10f) * 10f, c.Reward);
        });
        Assert.Equal($"Deliver {deliveries[1].Amount:N0} L wheat to Flour Mill", deliveries[1].Label);
        var first = sim.Contracts.Offers.First();
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"New contract: {first.Label} for {first.Client}, ${first.Reward:N0}");
    }

    [Fact]
    public void RipeCropsGetHarvestJobsAndSeedbedsSowingJobs()
    {
        var sim = Neighbors(Field(2, "seeded", "corn", "harvestable"), Field(3, "cultivated"), Field(4, "seeded", "wheat", "2"));
        sim.SkipHours(24);
        var harvest = Assert.Single(sim.Contracts.Offers, c => c.Type.Id == "harvest");
        Assert.Equal(("Harvest corn on Field 2", "elevator", "corn"), (harvest.Label, harvest.Poi!.Id, harvest.Goods!.Id));
        Assert.Equal("Ada Morrow", harvest.Client);
        // Canola is the only crop sown in August; field 4's wheat needs nothing yet and field 1 is the farm's.
        var sow = Assert.Single(sim.Contracts.Offers, c => c.Type.Id == "sow");
        Assert.Equal("Sow canola on Field 3", sow.Label);
        Assert.Equal([2, 3], sim.Contracts.Offers.Where(c => c.Field != null).Select(c => c.Field!.Id).Order());
    }

    [Fact]
    public void TheBoardAndTheFarmHaveLimits()
    {
        var sim = Neighbors(Field(2, "stubble"), Field(3, "stubble"), Field(4, "stubble"), Field(5, "grass"), Field(6, "grass"), Field(7, "grass"));
        Assert.Equal(2, sim.Contracts.Offers.Count());
        sim.SkipHours(24 * 3);
        Assert.Equal(6, sim.Contracts.Offers.Count());

        var offers = sim.Contracts.Offers.ToList();
        Assert.All(offers.Take(3), c => Assert.True(sim.Contracts.Accept(c)));
        Assert.Equal("You have 3 contracts under way already", sim.Contracts.AcceptBlocker(offers[3]));
        Assert.False(sim.Contracts.Accept(offers[3]));
        Assert.Equal("This contract is not on the board anymore", sim.Contracts.AcceptBlocker(offers[0]));
        // Taken contracts make room on the board: the seventh job there is (six fields, one buyer) goes up.
        sim.SkipHours(24);
        Assert.Equal((4, 3), (sim.Contracts.Offers.Count(), sim.Contracts.ActiveOf(Farm.PlayerId).Count()));
    }

    [Fact]
    public void OffersComeDownAfterThreeDays()
    {
        var sim = TestContent.NewSim();
        var withdrawn = Record<ContractWithdrawn>(sim);
        var first = sim.Contracts.Offers.ToList();
        TestContent.SkipTo(sim, 8, 3, 23f);
        Assert.All(first, c => Assert.Contains(c, sim.Contracts.Offers));
        // Posted on August 1st: down at midnight on September 1st (August has three days).
        sim.SkipHours(1);
        Assert.All(first, c => Assert.Equal(ContractState.Withdrawn, c.State));
        Assert.Equal(first.Select(c => new ContractWithdrawn(c)), withdrawn);
        Assert.All(first, c => Assert.DoesNotContain(c, sim.Contracts.All));
    }

    [Fact]
    public void TakenContractsFailWhenNotDoneInTime()
    {
        var sim = TestContent.NewSim();
        var accepted = Record<ContractAccepted>(sim);
        var lastDay = Record<ContractLastDay>(sim);
        var failed = Record<ContractFailed>(sim);
        var job = sim.Contracts.Offers.First();
        Assert.True(sim.Contracts.Accept(job));
        Assert.Equal((ContractState.Active, Farm.PlayerId, sim.Clock.DayIndex + job.Days), (job.State, job.FarmId, job.DueDay));
        Assert.Equal([new ContractAccepted(job)], accepted);
        Assert.Contains(job, sim.Contracts.ActiveOf(Farm.PlayerId));
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Contract taken: {job.Label}, {job.Days} days to do it");

        // Due at the midnight starting its due day, with a warning the day before; missing it costs the penalty.
        sim.SkipHours((int)(job.DueDay * 24L - sim.Clock.TotalHours) - 1);
        Assert.Equal([new ContractLastDay(job)], lastDay);
        Assert.Equal(ContractState.Active, job.State);
        var money = sim.Economy.Money;
        sim.SkipHours(1);
        var penalty = MathF.Round(job.Reward / 100f) * 10f;
        Assert.Equal(ContractState.Failed, job.State);
        Assert.Equal([new ContractFailed(job, penalty)], failed);
        Assert.Equal(money - penalty, sim.Economy.Money);
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Contract not done in time: {job.Label} (${penalty:N0} penalty)");
        Assert.DoesNotContain(job, sim.Contracts.All);
    }

    [Fact]
    public void GivingAContractBackCostsAShareOfItsReward()
    {
        var sim = TestContent.NewSim();
        var canceled = Record<ContractCanceled>(sim);
        var job = sim.Contracts.Offers.First();
        Assert.False(sim.Contracts.Cancel(job));
        Assert.True(sim.Contracts.Accept(job));
        var money = sim.Economy.Money;
        // 10% of the reward, in tens of dollars.
        var penalty = sim.Contracts.Penalty(job);
        Assert.Equal(MathF.Round(job.Reward / 100f) * 10f, penalty);

        Assert.True(sim.Contracts.Cancel(job));
        Assert.Equal((ContractState.Canceled, money - penalty), (job.State, sim.Economy.Money));
        Assert.Equal(-penalty, sim.Economy.Ledger.Today[MoneyCategory.Contracts]);
        Assert.Equal([new ContractCanceled(job, penalty)], canceled);
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Contract canceled: {job.Label} (${penalty:N0} penalty)");
        Assert.False(sim.Contracts.Cancel(job));
        Assert.DoesNotContain(job, sim.Contracts.All);
    }

    [Fact]
    public void BoughtLandTakesItsOffersAndLandUnderContractStaysTheNeighbors()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        var field4 = sim.Contracts.On(sim.World.FieldById(4)!)!;
        var field6 = sim.Contracts.On(sim.World.FieldById(6)!)!;
        Assert.True(sim.Contracts.Accept(field6));

        Assert.True(sim.Farms.Buy(sim.World.FarmlandById(5)!));
        Assert.Equal(ContractState.Withdrawn, field4.State);
        Assert.Null(sim.Contracts.On(sim.World.FieldById(4)!));
        Assert.Equal("Field 6 is under contract", sim.Farms.BuyBlocker(sim.World.FarmlandById(7)!));
        // The farm's own fields never get offers.
        sim.SkipHours(24 * 6);
        Assert.DoesNotContain(sim.Contracts.All, c => c.Field?.Id is 1 or 2 or 3 or 4);
    }

    [Fact]
    public void TheBoardAndTakenContractsAreSaved()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        Assert.True(sim.Contracts.Accept(sim.Contracts.Offers.First(c => c.Field != null)));
        // A delivery with a load being tipped for it.
        var delivery = sim.Contracts.Offers.First(c => c.Field == null);
        Assert.True(sim.Contracts.Accept(delivery));
        var pit = delivery.Poi!.Triggers.First(t => t.Type == "unload");
        sim.Pois.Unload(sim.Machines.Spawn("trailer_16", pit.Area.Center, 0f), pit, delivery.Goods!.Id, 2000f);

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        Assert.Equal(sim.Contracts.All.Select(Describe), loaded.Contracts.All.Select(Describe));
        var tipped = Record<ContractDelivery>(loaded);
        loaded.Tick(1f / 60f);
        Assert.Equal((loaded.Contracts.ById(delivery.Id), 2000f), (tipped.Single().Contract, tipped[0].Amount));

        // And the board goes on the same way.
        sim.SkipHours(24 * 4);
        loaded.SkipHours(24 * 4);
        Assert.Equal(sim.Contracts.All.Select(Describe), loaded.Contracts.All.Select(Describe));
    }

    /// <summary>Cells of field <paramref name="field"/> whose grass was cut back to its regrowth stage.</summary>
    private static int Mown(Simulation sim, int field)
    {
        var (L, grass) = (sim.World.Layers, (byte)(sim.Content.CropIndex("grass") + 1));
        return Enumerable.Range(0, L.Ground.Length).Count(i => L.FieldId[i] == field && L.Crop[i] == grass && L.Stage[i] == 1);
    }

    [Fact]
    public void WorkOnlyAppliesOnTheFarmsLandAndTheFieldsItHasAContractOn()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var mower = sim.Machines.Spawn("mower_3", new Vector2(269f, 278f), 0f);
        sim.Machines.Attach(t, "rear", mower);
        sim.Player.Enter(t);

        // Field 4 is Tom Aldridge's meadow: no helper, and a lowered mower leaves it as it is.
        sim.Perform(InputActions.Helper);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Field 4 belongs to Tom Aldridge: take a contract on it first");
        Assert.Null(t.Get<Drivable>()!.Controller as FieldWorkController);
        mower.Get<Attachable>()!.Lowered = true;
        mower.Get<WorkAreas>()!.On = true;
        sim.Player.Controls.Input = new VehicleInput { Throttle = 1f };
        for (var s = 0f; s < 6f; s += 1f / 60f) sim.Tick(1f / 60f);
        Assert.Equal(0, Mown(sim, 4));
        Assert.Equal(new NotAllowed("Field 4 belongs to Tom Aldridge: take a contract on it first"), Assert.Single(mower.Conditions));

        // With the contract to mow it, it's mown.
        var job = sim.Contracts.On(sim.World.FieldById(4)!)!;
        Assert.Equal("Mow grass on Field 4", job.Label);
        Assert.True(sim.Contracts.Accept(job));
        for (var s = 0f; s < 6f; s += 1f / 60f) sim.Tick(1f / 60f);
        Assert.True(Mown(sim, 4) > 6 * 40, $"mown cells: {Mown(sim, 4)}");
        Assert.Empty(mower.Conditions);
    }

    [Fact]
    public void AContractAllowsItsOwnWorkAndCrop()
    {
        var sim = Neighbors(Field(2, "cultivated"));
        var (canola, wheat) = (sim.Content.CropById("canola"), sim.Content.CropById("wheat"));
        var field = sim.World.FieldById(2)!;
        var sow = sim.Contracts.On(field)!;
        Assert.Equal("Sow canola on Field 2", sow.Label);
        Assert.Equal("Field 2 belongs to Ada Morrow: take a contract on it first", sim.Farms.FieldBlocker(Farm.PlayerId, field, "seeder", canola));

        Assert.True(sim.Contracts.Accept(sow));
        Assert.Null(sim.Farms.FieldBlocker(Farm.PlayerId, field, "seeder", canola));
        Assert.Equal("The contract on Field 2 is to sow canola", sim.Farms.FieldBlocker(Farm.PlayerId, field, "seeder", wheat));
        Assert.Equal("The contract on Field 2 is to sow", sim.Farms.FieldBlocker(Farm.PlayerId, field, "cultivator", null));
        Assert.Equal("Field 2 belongs to Ada Morrow: take a contract on it first", sim.Farms.FieldBlocker(Farm.None, field, "seeder", canola));

        // The farm's own land takes any work; the neighbor's land off its fields and land outside every parcel, none.
        Assert.Null(sim.Farms.FieldBlocker(Farm.PlayerId, sim.World.FieldById(1)!, "harvester", null));
        int Cell(float x, float z) => sim.World.CellIndex((int)(x / WorldMap.CellSize), (int)(z / WorldMap.CellSize));
        Assert.True(sim.Farms.MayWork(Farm.PlayerId, Cell(60f, 100f), "cultivator", null));
        Assert.Equal("Farmland 2 belongs to Ada Morrow", sim.Farms.WorkBlocker(Farm.PlayerId, Cell(70f, 100f), "cultivator", null));
        Assert.Equal("Only farmland can be worked", sim.Farms.WorkBlocker(Farm.PlayerId, Cell(30f, 160f), "cultivator", null));
    }

    [Fact]
    public void AFieldJobIsDoneOnceMostOfTheFieldIs()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        var completed = Record<ContractCompleted>(sim);
        var job = sim.Contracts.On(sim.World.FieldById(6)!)!;
        Assert.True(sim.Contracts.Accept(job));
        var L = sim.World.Layers;
        var cells = Enumerable.Range(0, L.FieldId.Length).Where(i => L.FieldId[i] == 6).ToList();
        void Cultivate(float share) => cells.Take((int)(cells.Count * share)).ToList().ForEach(i => L.Ground[i] = (byte)GroundType.Cultivated);
        var money = sim.Economy.Money;

        Cultivate(0.94f);
        sim.SkipHours(1);
        Assert.Equal((ContractState.Active, 0.94f), (job.State, MathF.Round(job.Progress, 2)));
        Cultivate(0.95f);
        sim.SkipHours(1);
        Assert.Equal(ContractState.Completed, job.State);
        Assert.Equal([new ContractCompleted(job, job.Reward)], completed);
        Assert.Equal((money + job.Reward, job.Reward), (sim.Economy.Money, sim.Economy.Ledger.Today[MoneyCategory.Contracts]));
        Assert.Equal(1, sim.Statistics.ContractsCompleted);
        Assert.Null(sim.Contracts.On(sim.World.FieldById(6)!));
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Contract done: Cultivate Field 6, ${job.Reward:N0} paid");
    }

    [Fact]
    public void AJobIsDoneAsSoonAsTheWorkIs()
    {
        var sim = Neighbors(new FieldDef { Id = 2, X = 72, Z = 30, W = 21, H = 30, Ground = "stubble" });
        var field = sim.World.FieldById(2)!;
        var job = sim.Contracts.On(field)!;
        Assert.True(sim.Contracts.Accept(job));
        sim.Clock.Paused = true;
        var t = sim.Machines.Spawn("tractor_125", field.Shape.Min + new Vector2(2f, -10f), 0f);
        sim.Machines.Attach(t, "rear", sim.Machines.Spawn("cultivator_3", field.Shape.Min + new Vector2(2f, -12f), 0f));
        sim.HireHelper(t, field);

        // Checked as the field is worked: no world hour goes by with the clock paused.
        var hour = sim.Clock.LastHour;
        Run(sim, 600f, () => job.State != ContractState.Active);
        Assert.Equal((ContractState.Completed, hour), (job.State, sim.Clock.LastHour));
        Assert.InRange(job.Progress, 0.95f, 1f);
    }

    [Fact]
    public void AHarvestIsDoneOnceItsCropIsAtTheBuyer()
    {
        var sim = Neighbors(new FieldDef { Id = 2, X = 72, Z = 30, W = 23, H = 30, Ground = "seeded", Crop = "corn", Stage = "harvestable" });
        var field = sim.World.FieldById(2)!;
        var job = sim.Contracts.On(field)!;
        Assert.Equal(("Harvest corn on Field 2", "elevator"), (job.Label, job.Poi!.Id));
        Assert.True(sim.Contracts.Accept(job));
        sim.Clock.Paused = true;
        var combine = sim.Machines.Spawn("combine_7", field.Shape.Min + new Vector2(3f, -10f), 0f);
        sim.Machines.Attach(combine, "header", sim.Machines.Spawn("header_corn_6", field.Shape.Min + new Vector2(3f, -8f), 0f));
        var helper = sim.HireHelper(combine, field);
        Run(sim, 600f, () => helper.Finished);

        // The field is done, but the corn is still in the tank.
        var tank = combine.Unit("tank")!;
        Assert.True(tank.Level > 500f, $"harvested {tank.Level} L");
        Assert.Equal(tank.Level, job.Harvested, 1);
        Assert.InRange(job.Progress, 0.95f, 1f);
        Assert.Equal(ContractState.Active, job.State);

        // At the elevator the neighbor's corn goes to the contract, unpaid; the farm's own corn is sold.
        var delivered = Record<ContractDelivery>(sim);
        var sold = Record<FillSold>(sim);
        var pit = sim.World.PoiById("elevator")!.Trigger("unload")!;
        var money = sim.Economy.Money;
        tank.Add("corn", 1000f);
        tank.Remove(sim.Pois.Unload(combine, pit, "corn", job.Harvested / 2f));
        Run(sim, 1f);
        Assert.Equal(job.Harvested / 2f, delivered.Single().Amount, 1);
        Assert.Equal((ContractState.Active, money), (job.State, sim.Economy.Money));
        tank.Remove(sim.Pois.Unload(combine, pit, "corn", tank.Level));
        Run(sim, 1f);
        Assert.Equal(ContractState.Completed, job.State);
        Assert.Equal(job.Harvested, job.Delivered, 1);
        Assert.Equal(1000f, sold.Single().Amount, 1);
        Assert.Equal(money + job.Reward + sold[0].Income, sim.Economy.Money, 1);
    }

    [Fact]
    public void ADeliveryIsDoneOnceItsGoodsAreAtTheBuyer()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        var job = sim.Contracts.Offers.Single(c => c.Poi?.Id == "mill");
        Assert.True(sim.Contracts.Accept(job));
        var pit = sim.World.PoiById("mill")!.Trigger("unload")!;
        var trailer = sim.Machines.Spawn("trailer_16", pit.Area.Center, 0f);
        var money = sim.Economy.Money;

        sim.Pois.Unload(trailer, pit, "wheat", job.Amount - 1000f);
        Run(sim, 1f);
        Assert.Equal((ContractState.Active, job.Amount - 1000f, money), (job.State, job.Delivered, sim.Economy.Money));
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Delivered {job.Amount - 1000f:N0} L Wheat for the contract: {job.Label}, 1,000 L to go");
        // The last 1,000 L go to the contract, the rest is sold.
        var income = 2000f * sim.Pois.Price((SellingStation)pit.Station, "wheat");
        sim.Pois.Unload(trailer, pit, "wheat", 3000f);
        Run(sim, 1f);
        Assert.Equal((ContractState.Completed, job.Amount), (job.State, job.Delivered));
        Assert.Equal(money + job.Reward + income, sim.Economy.Money, 1);
    }

    [Fact]
    public void LeasedMachinesComeWithTheJobAndGoBackOnceItIsDone()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        var leased = Record<MachinesLeased>(sim);
        var returned = Record<LeaseReturned>(sim);
        var job = sim.Contracts.On(sim.World.FieldById(6)!)!;
        Assert.Equal(["tractor_125", "cultivator_3"], job.Lease!.Machines);
        Assert.Equal(MathF.Round(150f * job.Field!.AreaHa / 10f) * 10f, job.LeaseFee);
        var before = sim.Machines.All.Count;
        Assert.True(sim.Contracts.Accept(job, lease: true));

        // On the dealer's lot, the cultivator on the tractor: the farm's to drive while the contract lasts.
        var lot = sim.World.PoiById("dealer")!.Trigger("delivery")!;
        var machines = Assert.Single(leased).Machines;
        Assert.Equal((before + 2, true), (sim.Machines.All.Count, job.Leased));
        Assert.Same(machines[0], machines[1].Parent);
        Assert.All(machines, m => Assert.Equal((Farm.PlayerId, job.Id), (m.FarmId, m.LeaseContract)));
        Assert.All(machines, m => Assert.True(lot.Contains(m.Footprint.Center)));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Leased Fieldmaster 125, Tiller 300: they wait at Machinery Dealer");
        sim.Player.Enter(machines[0]);

        // Done: the reward less the lease, and the machines go back, the farmer stepping out.
        var money = sim.Economy.Money;
        SetGround(sim, 6, GroundType.Cultivated);
        sim.SkipHours(1);
        Assert.Equal(ContractState.Completed, job.State);
        Assert.Equal((money + job.Reward - job.LeaseFee, -job.LeaseFee), (sim.Economy.Money, sim.Economy.Ledger.Today[MoneyCategory.Leasing]));
        Assert.Equal(before, sim.Machines.All.Count);
        Assert.DoesNotContain(machines[0], sim.Machines.All);
        Assert.Null(sim.Player.Vehicle);
        Assert.Equal(job.LeaseFee, Assert.Single(returned).Fee);
    }

    [Fact]
    public void ALeaseIsPaidAndGoesBackWhenTheContractIsGivenBack()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        var dismissed = Record<HelperDismissed>(sim);
        var job = sim.Contracts.On(sim.World.FieldById(6)!)!;
        Assert.True(sim.Contracts.Accept(job, lease: true));
        var tractor = sim.Machines.All.Single(m => m.LeaseContract == job.Id && m.Has<Motor>());
        // The farm's own trailer on the leased tractor, and a helper driving it.
        var trailer = sim.Machines.Spawn("trailer_16", tractor.Position, tractor.Heading);
        Assert.True(sim.Machines.Attach(tractor, "drawbar", trailer));
        sim.HireHelper(tractor, job.Field!);
        var money = sim.Economy.Money;

        Assert.True(sim.Contracts.Cancel(job));
        Assert.Equal(money - sim.Contracts.Penalty(job) - job.LeaseFee, sim.Economy.Money, 1);
        Assert.Single(dismissed);
        Assert.DoesNotContain(sim.Machines.All, m => m.LeaseContract == job.Id);
        Assert.Contains(trailer, sim.Machines.All);
        Assert.Null(trailer.Parent);
    }

    [Fact]
    public void AHarvestLeaseBringsAHeaderForItsCrop()
    {
        var sim = Neighbors(Field(2, "seeded", "corn", "harvestable"), Field(3, "seeded", "wheat", "harvestable"));
        sim.SkipHours(24);
        var corn = sim.Contracts.On(sim.World.FieldById(2)!)!;
        var wheat = sim.Contracts.On(sim.World.FieldById(3)!)!;
        Assert.Contains("header_corn_6", corn.Lease!.Machines);
        Assert.Contains("header_grain_6", wheat.Lease!.Machines);
        Assert.Equal("No machines can be leased for this job", sim.Contracts.LeaseBlocker(sim.Contracts.Offers.First(c => c.Field == null)));

        Assert.True(sim.Contracts.Accept(corn, lease: true));
        var set = sim.Machines.All.Where(m => m.LeaseContract == corn.Id).ToList();
        Assert.Equal(["combine_7", "header_corn_6", "tractor_95", "trailer_16"], set.Select(m => m.Def.Id));
        Assert.Equal((set[0], set[2]), (set[1].Parent, set[3].Parent));
        var lot = sim.World.PoiById("dealer")!.Trigger("delivery")!;
        Assert.All(set, m => Assert.True(lot.Contains(m.Footprint.Center)));
        for (var i = 0; i < set.Count; i++)
        for (var j = i + 1; j < set.Count; j++)
            Assert.False(Geometry.Overlaps(set[i].Footprint, set[j].Footprint), $"{set[i]} overlaps {set[j]}");
    }

    [Fact]
    public void NoLeaseWithoutRoomOnTheLot()
    {
        var sim = Neighbors(Field(2, "seeded", "wheat", "harvestable"));
        var job = sim.Contracts.On(sim.World.FieldById(2)!)!;
        var lot = sim.World.PoiById("dealer")!.Trigger("delivery")!;
        while (sim.Pois.DeliverSet(["combine_7"], Farm.PlayerId, lot) != null) { }
        var machines = sim.Machines.All.Count;

        Assert.False(sim.Contracts.Accept(job, lease: true));
        Assert.Equal((ContractState.Offered, machines), (job.State, sim.Machines.All.Count));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "No room on the lot of Machinery Dealer for the leased machines: clear it first");
    }

    [Fact]
    public void LeasedMachinesAreSavedWithTheirContract()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(24);
        var job = sim.Contracts.On(sim.World.FieldById(4)!)!;
        Assert.True(sim.Contracts.Accept(job, lease: true));
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var copy = loaded.Contracts.ById(job.Id)!;
        Assert.Equal((true, job.LeaseFee, job.Lease), (copy.Leased, copy.LeaseFee, copy.Lease));
        Assert.Equal(sim.Machines.All.Select(m => (m.Id, m.LeaseContract)), loaded.Machines.All.Select(m => (m.Id, m.LeaseContract)));
        Assert.True(loaded.Contracts.Cancel(copy));
        Assert.DoesNotContain(loaded.Machines.All, m => m.LeaseContract != 0);
    }

    [Fact]
    public void LeasesAreValidated()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.ContractTypes["deliver"].Leases = [new ContractLeaseDef { Machines = ["tractor_95"] }];
        db.ContractTypes["cultivate"].Leases = [new ContractLeaseDef { Machines = ["seeder_3", "rocket"], FeePerHa = -1 }];
        Assert.Equal(
        [
            "contract type 'cultivate' lease 1: feePerHa must be >= 0",
            "contract type 'cultivate' lease 1: unknown machine 'rocket'",
            "contract type 'cultivate' lease 1: no machine does the job's work (cultivator)",
            "contract type 'cultivate' lease 1: needs a vehicle",
            "contract type 'deliver': leases are for field jobs",
        ], db.Validate());
    }

    [Fact]
    public void RipeMeadowsGetMowingJobsAndRipeCropsHarvests()
    {
        var sim = Neighbors(Only(8, "mow", "harvest", "cultivate", "plow"),
            Field(2, "grass", "grass", "harvestable"), Field(3, "seeded", "wheat", "harvestable"));
        var mow = sim.Contracts.On(sim.World.FieldById(2)!)!;
        Assert.Equal(("Mow grass on Field 2", "Harvest wheat on Field 3"), (mow.Label, sim.Contracts.On(sim.World.FieldById(3)!)!.Label));
        Assert.Equal(["tractor_95", "mower_3"], mow.Lease!.Machines);
        Assert.Equal(MathF.Round(70f * mow.Field!.AreaHa / 10f) * 10f, mow.LeaseFee);

        // Done once the grass is cut back to grow again.
        Assert.True(sim.Contracts.Accept(mow));
        SetCells(sim, 2, (L, i) => L.Stage[i] = 1);
        sim.SkipHours(1);
        Assert.Equal(ContractState.Completed, mow.State);
    }

    [Fact]
    public void NoOneIsOfferedWorkNoMachineDoes()
    {
        var sim = Neighbors(db =>
        {
            Only(8, "mow", "harvest")(db);
            db.RemoveMachine("mower_3");
        }, Field(2, "grass", "grass", "harvestable"));
        sim.SkipHours(24);
        Assert.Empty(sim.Contracts.Offers);
    }

    [Fact]
    public void StubbleGetsPlowingJobsInAutumn()
    {
        var sim = Neighbors(Only(10, "plow"), Field(2, "stubble"), Field(3, "seeded", "wheat", "1"));
        var plow = Assert.Single(sim.Contracts.Offers);
        Assert.Equal("Plow Field 2", plow.Label);
        Assert.Equal(["tractor_125", "plow_5"], plow.Lease!.Machines);
        Assert.True(sim.Contracts.Accept(plow));
        SetGround(sim, 2, GroundType.Plowed);
        sim.SkipHours(1);
        Assert.Equal(ContractState.Completed, plow.State);
    }

    [Fact]
    public void GrowingCropsGetFertilizingJobsInSpring()
    {
        var sim = Neighbors(Only(4, "fertilize"), Field(2, "seeded", "wheat", "3"), Field(3, "seeded", "barley", "2"), Field(4, "cultivated"));
        SetCells(sim, 3, (L, i) => L.Fertilized[i] = 1);
        foreach (var c in sim.Contracts.Offers.ToList()) Assert.True(sim.Contracts.Accept(c) && sim.Contracts.Cancel(c));
        sim.SkipHours(24);
        // Field 3 was fertilized already, and field 4 has no crop.
        var job = Assert.Single(sim.Contracts.Offers);
        Assert.Equal("Fertilize wheat on Field 2", job.Label);
        Assert.Equal(["tractor_95", "spreader_24"], job.Lease!.Machines);
        Assert.True(sim.Contracts.Accept(job));
        SetCells(sim, 2, (L, i) => SpreaderWork.Fertilize(L, i, 68f));
        sim.SkipHours(1);
        Assert.Equal(ContractState.Completed, job.State);
    }

    [Fact]
    public void WeedyFieldsGetSprayingJobs()
    {
        var sim = Neighbors(Only(5, "spray"), Field(2, "seeded", "wheat", "3"), Field(3, "seeded", "barley", "2"));
        Assert.Empty(sim.Contracts.Offers);
        SetCells(sim, 2, (L, i) => L.Weeds[i] = WeedState.Grown);
        SetCells(sim, 3, (L, i) => L.Weeds[i] = WeedState.Sprayed);
        sim.SkipHours(24);
        var job = Assert.Single(sim.Contracts.Offers);
        Assert.Equal("Spray Field 2", job.Label);
        Assert.Equal(["tractor_95", "sprayer_12"], job.Lease!.Machines);
        Assert.True(sim.Contracts.Accept(job));
        SetCells(sim, 2, (L, i) => L.Weeds[i] = WeedState.Sprayed);
        sim.SkipHours(1);
        Assert.Equal(ContractState.Completed, job.State);
    }
}

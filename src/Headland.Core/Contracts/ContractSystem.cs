using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;
using Headland.Core.Pois;
using Headland.Core.Pois.Components;
using Headland.Core.Time;
using Headland.Core.World;

namespace Headland.Core.Contracts;

/// <summary>
/// The contract board. Every midnight the neighbors post jobs their fields need in that season (contracts/), and
/// buyers ask for goods; offers nobody takes come down after a few days. The farm takes a few at a time, each due
/// within its days, with its own machines or leased ones delivered at a dealer's lot. A field job is done once enough
/// of the field is in its done state; goods count once tipped at the buyer, who takes them for the contract instead of
/// paying for them. Done contracts pay their reward; canceled or late ones cost a penalty. Leased machines go back
/// when the contract ends, and the lease is paid then.
/// </summary>
public sealed class ContractSystem
{
    /// <summary>Share of a field in a job's offer state for the job to be offered.</summary>
    public const float OfferShare = 0.5f;
    /// <summary>Real seconds between progress checks while fields are being worked.</summary>
    private const float CheckInterval = 0.5f;

    private readonly Simulation _sim;
    private readonly List<Contract> _all = [];
    private readonly Dictionary<FieldInfo, int[]> _cells = new();
    private bool _changed;
    private float _sinceCheck = CheckInterval;

    public ContractSystem(Simulation sim, ulong seed)
    {
        _sim = sim;
        Rng = new Rng(seed);
        sim.Events.Subscribe<FarmlandOwnerChanged>(e => WithdrawOn(e.Farmland));
        sim.Events.Subscribe<FieldWorked>(_ => _changed = true);
        sim.Events.Subscribe<CropHarvested>(e => OnHarvested(e.Harvester, e.FieldId, e.FillType, e.Amount));
        sim.Events.Subscribe<WindrowPickedUp>(e => OnHarvested(e.Machine, e.FieldId, e.FillType, e.Amount));
    }

    /// <summary>Rolls what goes on the board.</summary>
    internal Rng Rng { get; }
    /// <summary>Id of the next contract posted (ids are never reused).</summary>
    internal int NextId { get; set; } = 1;

    public ContractRulesDef Rules => _sim.Content.Economy.Contracts;
    /// <summary>Contracts on the board and under way.</summary>
    public IReadOnlyList<Contract> All => _all;
    public IEnumerable<Contract> Offers => _all.Where(c => c.State == ContractState.Offered);
    public IEnumerable<Contract> ActiveOf(int farmId) => _all.Where(c => c.State == ContractState.Active && c.FarmId == farmId);
    public Contract? ById(int id) => _all.Find(c => c.Id == id);
    /// <summary>The contract on a field, offered or under way (one at a time).</summary>
    public Contract? On(FieldInfo field) => _all.Find(c => c.Field == field);

    private int Player => _sim.Farms.Player.Id;

    /// <summary>The last day to do a taken contract.</summary>
    public GameDate DueDate(Contract c) => _sim.Calendar.DateOfDay(c.DueDay - 1);

    // ------------------------------------------------------------------ Taking a contract

    /// <summary>Why the player's farm can't take <paramref name="c"/>, or null if it can.</summary>
    public string? AcceptBlocker(Contract c)
    {
        if (c.State != ContractState.Offered) return "This contract is not on the board anymore";
        return ActiveOf(Player).Count() >= Rules.MaxActive ? $"You have {Rules.MaxActive} contracts under way already" : null;
    }

    /// <summary>Why the player's farm can't take <paramref name="c"/> with its leased machines, or null if it can.</summary>
    public string? LeaseBlocker(Contract c)
    {
        if (AcceptBlocker(c) is { } why) return why;
        if (c.Lease == null) return "No machines can be leased for this job";
        return Lot() == null ? "No dealer leases machines now" : null;
    }

    /// <summary>
    /// Takes <paramref name="c"/> for the player's farm; with <paramref name="lease"/>, its lease machines come to the
    /// dealer's lot (false when there is no room for them).
    /// </summary>
    public bool Accept(Contract c, bool lease = false)
    {
        if ((lease ? LeaseBlocker(c) : AcceptBlocker(c)) != null) return false;
        var lot = lease ? Lot() : null;
        List<Machine>? machines = null;
        if (lot != null)
        {
            machines = _sim.Pois.DeliverSet(c.Lease!.Machines, Player, lot);
            if (machines == null)
            {
                _sim.Notifications.Post($"No room on the lot of {lot.Poi.Name} for the leased machines: clear it first", Severity.Warning);
                return false;
            }
            machines.ForEach(m => m.LeaseContract = c.Id);
            c.Leased = true;
        }
        c.State = ContractState.Active;
        c.FarmId = Player;
        c.DueDay = _sim.Clock.DayIndex + c.Days;
        _sim.Events.Publish(new ContractAccepted(c));
        if (machines != null) _sim.Events.Publish(new MachinesLeased(c, machines, lot!.Poi));
        return true;
    }

    /// <summary>Where leased machines are delivered: the lot of an open delivery spot that leases them.</summary>
    private PoiTrigger? Lot() => _sim.World.Pois
        .Select(p => p.Get<DeliverySpot>())
        .FirstOrDefault(d => d is { Def.Leases: true } && _sim.Pois.Closed(d.Poi, d.Def) == null)?.Lot;

    /// <summary>What giving <paramref name="c"/> back, or missing its due day, costs: a share of its reward.</summary>
    public float Penalty(Contract c) => Round10(c.Reward * Rules.Penalty);

    /// <summary>Gives a contract under way back, for its <see cref="Penalty"/>.</summary>
    public bool Cancel(Contract c)
    {
        if (c.State != ContractState.Active || c.FarmId != Player) return false;
        End(c, ContractState.Canceled);
        return true;
    }

    // ------------------------------------------------------------------ Time

    /// <summary>After the machines moved: checks the contracts whose fields were worked or goods delivered.</summary>
    internal void Update(float dt)
    {
        _sinceCheck += dt;
        if (!_changed || _sinceCheck < CheckInterval) return;
        Check();
    }

    /// <summary>
    /// Runs each world hour: crops that grew or died change the fields, so contracts are checked. At midnight,
    /// contracts past due fail, old offers come down and new ones go up.
    /// </summary>
    internal void TickHour(long hour)
    {
        Check();
        if (hour % 24 != 0) return;
        var day = (int)(hour / 24);
        foreach (var c in _all.ToList())
        {
            if (c.State == ContractState.Offered && day >= c.OfferedDay + Rules.OfferDays) End(c, ContractState.Withdrawn);
            else if (c.State == ContractState.Active && day >= c.DueDay) End(c, ContractState.Failed);
            else if (c.State == ContractState.Active && day == c.DueDay - 1) _sim.Events.Publish(new ContractLastDay(c));
        }
        Post(day, Rules.OffersPerDay);
    }

    /// <summary>Updates the progress of the contracts under way, and completes the ones that are done.</summary>
    private void Check()
    {
        _changed = false;
        _sinceCheck = 0f;
        foreach (var c in _all.Where(c => c.State == ContractState.Active).ToList())
        {
            if (c.Field != null) c.Progress = Progress(c);
            if (IsDone(c)) Complete(c);
        }
    }

    /// <summary>A field job needs the threshold of its field done, and a harvest its share of the crop tipped at the buyer.</summary>
    private bool IsDone(Contract c) =>
        (c.Field == null || c.Progress >= Rules.Threshold) && c.Delivered >= c.ToDeliver - 0.001f;

    private void Complete(Contract c)
    {
        if (c.FarmId == Player) _sim.Economy.Earn(c.Reward, MoneyCategory.Contracts);
        End(c, ContractState.Completed);
    }

    /// <summary>The share of the contract's field in its job's done state (with the job's crop).</summary>
    private float Progress(Contract c)
    {
        var done = new FieldState(c.Type.Done);
        var crop = c.Crop != null ? _sim.Content.Crops.IndexOf(c.Crop) + 1 : 0;
        var L = _sim.World.Layers;
        var cells = CellsOf(c.Field!);
        var n = 0;
        foreach (var i in cells)
            if (done.Matches(L, _sim.Content.Crops, i, crop)) n++;
        return (float)n / Math.Max(1, cells.Length);
    }

    /// <summary>
    /// Crop threshed (or baled) on a field the machine's farm has a contract on, asking for it at a buyer, is the
    /// neighbor's.
    /// </summary>
    private void OnHarvested(Machine machine, int fieldId, string fillType, float amount)
    {
        if (_sim.World.FieldById(fieldId) is not { } field || On(field) is not { State: ContractState.Active } c) return;
        if (c.FarmId == machine.FarmId && c.Type.Deliver != null && c.Goods?.Id == fillType) c.Harvested += amount;
    }

    /// <summary>The farm's contract that <paramref name="fillType"/> tipped at <paramref name="poi"/> would go to, if any.</summary>
    public Contract? Taking(int farmId, Poi poi, string fillType) =>
        _all.Find(c => c.State == ContractState.Active && c.FarmId == farmId && c.Poi == poi && c.Goods?.Id == fillType && c.Owed > 0f);

    /// <summary>
    /// Goods tipped at <paramref name="poi"/> by <paramref name="farmId"/>'s machines: the part a contract of the farm is
    /// owed (<see cref="Contract.Owed"/>) is the contract's, which the buyer takes without paying. Returns that
    /// contract and how much it took.
    /// </summary>
    internal (Contract? contract, float credited) Credit(int farmId, Poi poi, string fillType, float amount)
    {
        if (Taking(farmId, poi, fillType) is not { } c) return (null, 0f);
        var credited = MathF.Min(amount, c.Owed);
        c.Delivered += credited;
        _changed = true;
        return (c, credited);
    }

    private void End(Contract c, ContractState state)
    {
        var penalty = state is ContractState.Failed or ContractState.Canceled ? Penalty(c) : 0f;
        if (penalty > 0f && c.FarmId == Player) _sim.Economy.Spend(penalty, MoneyCategory.Contracts);
        c.State = state;
        _all.Remove(c);
        switch (state)
        {
            case ContractState.Withdrawn: _sim.Events.Publish(new ContractWithdrawn(c)); break;
            case ContractState.Failed: _sim.Events.Publish(new ContractFailed(c, penalty)); break;
            case ContractState.Canceled: _sim.Events.Publish(new ContractCanceled(c, penalty)); break;
            case ContractState.Completed: _sim.Events.Publish(new ContractCompleted(c, c.Reward)); break;
        }
        if (c.Leased) ReturnLease(c);
    }

    /// <summary>A contract's leased machines go back to the dealer, however it ended, and the farm pays the lease.</summary>
    private void ReturnLease(Contract c)
    {
        if (c.FarmId == Player && c.LeaseFee > 0f) _sim.Economy.Spend(c.LeaseFee, MoneyCategory.Leasing);
        var machines = _sim.Machines.All.Where(m => m.LeaseContract == c.Id).ToList();
        _sim.RemoveMachines(machines);
        _sim.Events.Publish(new LeaseReturned(c, machines, c.LeaseFee));
    }

    /// <summary>A parcel bought by a farm: its neighbor's offers on it come down.</summary>
    private void WithdrawOn(Farmland land)
    {
        if (land.FarmId == Farm.None) return;
        foreach (var c in Offers.Where(c => c.Field != null && land.Fields.Contains(c.Field)).ToList()) End(c, ContractState.Withdrawn);
    }

    internal void Restore(IEnumerable<Contract> contracts, int nextId)
    {
        _all.Clear();
        _all.AddRange(contracts);
        NextId = Math.Max(nextId, _all.Select(c => c.Id + 1).DefaultIfEmpty(1).Max());
        foreach (var c in _all.Where(c => c.State == ContractState.Active && c.Field != null)) c.Progress = Progress(c);
    }

    // ------------------------------------------------------------------ Offers

    /// <summary>Posts up to <paramref name="count"/> offers, while the board has room, picked among the jobs that fit.</summary>
    internal void Post(int day, int count)
    {
        var month = _sim.Calendar.DateOfDay(day).Month;
        for (var k = 0; k < count && Offers.Count() < Rules.MaxOffers; k++)
        {
            var jobs = Jobs(month);
            if (jobs.Count == 0) return;
            var roll = Rng.NextFloat() * jobs.Sum(j => j.Type.Weight);
            var (type, field) = jobs[^1];
            foreach (var job in jobs)
            {
                if ((roll -= job.Type.Weight) >= 0f) continue;
                (type, field) = job;
                break;
            }
            var c = field != null ? FieldJob(type, field, day, month) : Delivery(type, day, month);
            _all.Add(c);
            _sim.Events.Publish(new ContractOffered(c));
        }
    }

    /// <summary>The jobs that could go on the board this month: one per neighbor's field that needs it, and deliveries.</summary>
    private List<(ContractTypeDef Type, FieldInfo? Field)> Jobs(int month)
    {
        var jobs = new List<(ContractTypeDef, FieldInfo?)>();
        foreach (var type in _sim.Content.ContractTypes.Values)
        {
            if (type.Months.Length > 0 && !type.Months.Contains(month)) continue;
            if (type.Work == "")
            {
                if (Deliveries(type).Count > 0) jobs.Add((type, null));
                continue;
            }
            foreach (var field in _sim.World.Fields)
                if (_sim.World.FarmlandById(field.FarmlandId) is { FarmId: Farm.None } && On(field) == null && Plan(type, field, month, null, out _, out _))
                    jobs.Add((type, field));
        }
        return jobs;
    }

    private Contract FieldJob(ContractTypeDef type, FieldInfo field, int day, int month)
    {
        Plan(type, field, month, Rng, out var crop, out var buyer);
        var lease = LeaseFor(type, crop);
        return new Contract
        {
            Id = NextId++, Type = type, Npc = _sim.World.FarmlandById(field.FarmlandId)!.Npc, Field = field, Crop = crop,
            Poi = buyer, Goods = buyer != null ? _sim.Content.FillTypes[crop!.FillType] : null,
            Reward = Round10(type.RewardPerHa * field.AreaHa * Rng.Range(0.9f, 1.1f)),
            Days = Rng.Range(type.Days[0], type.Days[1] + 1), OfferedDay = day,
            Lease = lease, LeaseFee = lease != null ? Round10(lease.FeePerHa * field.AreaHa) : 0f,
        };
    }

    /// <summary>The first of the job's lease sets that can do it: a work area of its work (a header that cuts its crop).</summary>
    private ContractLeaseDef? LeaseFor(ContractTypeDef type, CropDef? crop) =>
        type.Leases.FirstOrDefault(l => l.Machines.Any(id => Does(_sim.Content.Machines[id], type, crop)));

    /// <summary>Whether <paramref name="machine"/> has a work area doing <paramref name="type"/>'s work (on <paramref name="crop"/>, when there's one).</summary>
    private static bool Does(MachineDef machine, ContractTypeDef type, CropDef? crop) =>
        machine.Get<WorkAreasDef>()?.Areas.Any(wa => wa.Type == type.Work && (crop == null || wa.Work.Handles(wa, crop))) == true;

    /// <summary>Whether some machine in the game does the job (on its crop): no one is offered work nothing can do.</summary>
    private bool Doable(ContractTypeDef type, CropDef? crop) => _sim.Content.Machines.Values.Any(m => Does(m, type, crop));

    private Contract Delivery(ContractTypeDef type, int day, int month)
    {
        var d = type.Deliver!;
        var choices = Deliveries(type);
        var (poi, fillType) = choices[Rng.Range(0, choices.Count)];
        var amount = Math.Clamp(MathF.Round(Rng.Range(d.Amount[0], d.Amount[1]) / 1000f) * 1000f, d.Amount[0], d.Amount[1]);
        return new Contract
        {
            Id = NextId++, Type = type, Poi = poi, Goods = _sim.Content.FillTypes[fillType], Amount = amount,
            Reward = Round10(amount * _sim.Economy.Price(fillType, month) * d.PriceFactor),
            Days = Rng.Range(type.Days[0], type.Days[1] + 1), OfferedDay = day,
        };
    }

    private static float Round10(float money) => MathF.Round(money / 10f) * 10f;

    /// <summary>
    /// Whether <paramref name="field"/> needs the job: at least <see cref="OfferShare"/> of it in the offer state and
    /// not enough of it done yet. Its crop is the one ripe (or growing) on the field, or for a job that sows one, a crop
    /// in its sowing window, picked with <paramref name="rng"/> (without, only checked for). A job with goods needs a
    /// buyer for its crop.
    /// </summary>
    private bool Plan(ContractTypeDef type, FieldInfo field, int month, Rng? rng, out CropDef? crop, out Poi? buyer)
    {
        crop = null;
        buyer = null;
        if (!Doable(type, null)) return false;
        var offerState = new FieldState(type.Offer);
        var doneState = new FieldState(type.Done);
        var (offer, done, grown) = Survey(field, offerState, doneState);
        if (offer < OfferShare || done >= Rules.Threshold) return false;
        if (offerState.NamesCrop) crop = grown;
        else if (doneState.NamesCrop)
        {
            var sowable = _sim.Content.Crops.Where(c => _sim.Crops.InSowingWindow(c, month)).ToList();
            if (sowable.Count > 0) crop = sowable[rng?.Range(0, sowable.Count) ?? 0];
        }
        if ((offerState.NamesCrop || doneState.NamesCrop) && crop == null) return false;
        if (crop != null && !Doable(type, crop)) return false;
        if (type.Deliver == null) return true;
        var buyers = crop != null ? Buyers(crop.FillType).ToList() : [];
        if (buyers.Count == 0) return false;
        buyer = buyers[rng?.Range(0, buyers.Count) ?? 0];
        return true;
    }

    /// <summary>Shares of the field in the offer and done states, and the crop most of its offer-state cells grow.</summary>
    private (float offer, float done, CropDef? crop) Survey(FieldInfo field, FieldState offerState, FieldState doneState)
    {
        var L = _sim.World.Layers;
        var crops = _sim.Content.Crops;
        var cells = CellsOf(field);
        var counts = new int[crops.Count + 1];
        int offer = 0, done = 0;
        foreach (var i in cells)
        {
            if (offerState.Matches(L, crops, i, 0))
            {
                offer++;
                counts[L.Crop[i]]++;
            }
            if (doneState.Matches(L, crops, i, 0)) done++;
        }
        var top = 1;
        for (var c = 2; c < counts.Length; c++)
            if (counts[c] > counts[top]) top = c;
        var n = Math.Max(1, cells.Length);
        return ((float)offer / n, (float)done / n, counts[top] > 0 ? crops[top - 1] : null);
    }

    /// <summary>POIs of no farm that buy <paramref name="fillType"/>: the goods of contracts go there.</summary>
    private IEnumerable<Poi> Buyers(string fillType) => _sim.World.Pois.Where(p =>
        p.FarmId == Farm.None && p.Get<SellingStation>()?.Def.FillTypes.Contains(fillType) == true);

    /// <summary>What a delivery job can ask for: goods a buyer takes, from buyers without a delivery on the board or under way.</summary>
    private List<(Poi poi, string fillType)> Deliveries(ContractTypeDef type) => _sim.World.Pois
        .Where(p => p.FarmId == Farm.None && !_all.Any(c => c.Field == null && c.Poi == p))
        .SelectMany(p => (p.Get<SellingStation>()?.Def.FillTypes ?? [])
            .Where(ft => type.Deliver!.FillTypes.Length == 0 || type.Deliver.FillTypes.Contains(ft))
            .Select(ft => (p, ft)))
        .ToList();

    /// <summary>The cells of a field: inside its outline and not taken by a field drawn over it.</summary>
    internal int[] CellsOf(FieldInfo field)
    {
        if (_cells.TryGetValue(field, out var cells)) return cells;
        var w = _sim.World;
        var list = new List<int>();
        field.Shape.Rasterize(WorldMap.CellSize, w.CellsX, w.CellsZ, (cx, cz) =>
        {
            var i = w.CellIndex(cx, cz);
            if (w.Layers.FieldId[i] == field.Id) list.Add(i);
        });
        return _cells[field] = list.ToArray();
    }
}

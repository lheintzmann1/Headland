using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Ownership;
using Headland.Core.Pois;
using Headland.Core.Time;
using Headland.Core.World;

namespace Headland.Core.Contracts;

/// <summary>
/// The contract board. Every midnight the neighbors post jobs their fields need in that season (contracts.json), and
/// buyers ask for goods; offers nobody takes come down after a few days. The farm takes a few at a time, each due
/// within its days.
/// </summary>
public sealed class ContractSystem
{
    /// <summary>Share of a field in a job's offer state for the job to be offered.</summary>
    public const float OfferShare = 0.5f;

    private readonly Simulation _sim;
    private readonly List<Contract> _all = [];
    private readonly Dictionary<FieldInfo, int[]> _cells = new();

    public ContractSystem(Simulation sim, ulong seed)
    {
        _sim = sim;
        Rng = new Rng(seed);
        sim.Events.Subscribe<FarmlandOwnerChanged>(e => WithdrawOn(e.Farmland));
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

    public bool Accept(Contract c)
    {
        if (AcceptBlocker(c) != null) return false;
        c.State = ContractState.Active;
        c.FarmId = Player;
        c.DueDay = _sim.Clock.DayIndex + c.Days;
        _sim.Events.Publish(new ContractAccepted(c));
        return true;
    }

    // ------------------------------------------------------------------ Time

    /// <summary>Runs each world hour. At midnight, contracts past due fail, old offers come down and new ones go up.</summary>
    internal void TickHour(long hour)
    {
        if (hour % 24 != 0) return;
        var day = (int)(hour / 24);
        foreach (var c in _all.ToList())
        {
            if (c.State == ContractState.Offered && day >= c.OfferedDay + Rules.OfferDays) End(c, ContractState.Withdrawn);
            else if (c.State == ContractState.Active && day >= c.DueDay) End(c, ContractState.Failed);
        }
        Post(day, Rules.OffersPerDay);
    }

    private void End(Contract c, ContractState state)
    {
        c.State = state;
        _all.Remove(c);
        if (state == ContractState.Withdrawn) _sim.Events.Publish(new ContractWithdrawn(c));
        else _sim.Events.Publish(new ContractFailed(c));
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
        return new Contract
        {
            Id = NextId++, Type = type, Npc = _sim.World.FarmlandById(field.FarmlandId)!.Npc, Field = field, Crop = crop,
            Poi = buyer, Goods = buyer != null ? _sim.Content.FillTypes[crop!.FillType] : null,
            Reward = Round10(type.RewardPerHa * field.AreaHa * Rng.Range(0.9f, 1.1f)),
            Days = Rng.Range(type.Days[0], type.Days[1] + 1), OfferedDay = day,
        };
    }

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
        p.FarmId == Farm.None && p.Def.Actions.Any(a => a.Type == "sell" && a.FillTypes.Contains(fillType)));

    /// <summary>What a delivery job can ask for: goods a buyer takes, from buyers without a delivery on the board or under way.</summary>
    private List<(Poi poi, string fillType)> Deliveries(ContractTypeDef type) => _sim.World.Pois
        .Where(p => p.FarmId == Farm.None && !_all.Any(c => c.Field == null && c.Poi == p))
        .SelectMany(p => p.Def.Actions.Where(a => a.Type == "sell").SelectMany(a => a.FillTypes).Distinct()
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

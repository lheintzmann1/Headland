using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Events;

namespace Headland.Core;

public enum Severity { Info, Good, Warning }

public sealed record Notification(string Text, Severity Severity, double RealTime);

/// <summary>Short HUD messages. The presentation drains and displays them.</summary>
public sealed class Notifications
{
    private readonly List<Notification> _items = [];
    private readonly Dictionary<string, double> _lastByText = new();

    public double Now { get; set; }

    public IReadOnlyList<Notification> Items => _items;

    /// <summary>Posts a message, de-duplicating identical text within <paramref name="cooldown"/> seconds.</summary>
    public void Post(string text, Severity severity = Severity.Info, double cooldown = 2.0)
    {
        if (_lastByText.TryGetValue(text, out var last) && Now - last < cooldown) return;
        _lastByText[text] = Now;
        _items.Add(new Notification(text, severity, Now));
        if (_items.Count > 32) _items.RemoveAt(0);
    }

    public void Expire(double maxAge) => _items.RemoveAll(n => Now - n.RealTime > maxAge);

    /// <summary>Posts a message for the events a player wants to hear about (sales, helpers, hitching).</summary>
    public void Follow(EventBus events, ContentDatabase content)
    {
        string Amount(string fillType, float amount)
        {
            var ft = content.FillTypes[fillType];
            return $"{amount:N0} {ft.Unit} {ft.Name}";
        }

        events.Subscribe<FillSold>(e => Post($"Sold {Amount(e.FillType, e.Amount)} for ${e.Income:N0}", Severity.Good, 0));
        events.Subscribe<FillStored>(e => Post($"Stored {Amount(e.FillType, e.Amount)} in {e.Poi.Name}", Severity.Good, 0));
        events.Subscribe<ObjectsSold>(e =>
        {
            if (e.Farm != Ownership.Farm.PlayerId) return;
            var what = e.Def.Name.ToLowerInvariant();
            var objects = e.Count == 1 ? $"a {what}" : $"{e.Count} {what}s";
            Post($"Sold {objects} of {content.FillTypes[e.FillType].Name.ToLowerInvariant()} ({e.Amount:N0} {content.FillTypes[e.FillType].Unit}) to {e.Poi.Name} for ${e.Income:N0}", Severity.Good, 0);
        });
        events.Subscribe<FillLoaded>(e => Post($"Loaded {Amount(e.FillType, e.Amount)} from {e.Poi.Name}", Severity.Good, 0));
        events.Subscribe<MachineBought>(e => Post($"Bought the {e.Machine.Def.Name} for ${e.Price:N0}: it waits at {e.Poi.Name}", Severity.Good, 0));
        events.Subscribe<MachineLeased>(e => Post(
            $"Leased the {e.Machine.Def.Name} for ${e.Lease.Fee:N0} and ${e.Lease.PerHour:N0} an hour it runs: it waits at {e.Poi.Name}", Severity.Good, 0));
        events.Subscribe<HighDemandStarted>(e => Post(
            $"High demand for {content.FillTypes[e.FillType].Name.ToLowerInvariant()} at {e.Poi.Name}: +{(e.Factor - 1f) * 100f:0}% " +
            $"until {Time.Calendar.MonthNames[e.Until.Month - 1][..3]} {e.Until.Day}", Severity.Good, 0));
        events.Subscribe<HighDemandEnded>(e => Post($"The high demand for {content.FillTypes[e.FillType].Name.ToLowerInvariant()} at {e.Poi.Name} is over"));
        events.Subscribe<FillBought>(e => Post($"Bought {Amount(e.FillType, e.Amount)} for ${e.Cost:N0}", Severity.Good));
        events.Subscribe<FillOrdered>(e => Post($"Ordered {Amount(e.FillType, e.Amount)} into {e.Poi.Name.ToLowerInvariant()} for ${e.Cost:N0}", Severity.Good));
        events.Subscribe<FarmlandBought>(e => Post($"Bought {e.Farmland.Label} ({e.Farmland.AreaHa:0.00} ha) for ${e.Price:N0}", Severity.Good, 0));
        events.Subscribe<FarmlandSold>(e => Post($"Sold {e.Farmland.Label} for ${e.Price:N0}", Severity.Good, 0));
        string Penalty(float penalty) => penalty >= 0.5f ? $" (${penalty:N0} penalty)" : "";
        events.Subscribe<ContractOffered>(e => Post($"New contract: {e.Contract.Label} for {e.Contract.Client}, ${e.Contract.Reward:N0}", Severity.Info, 0));
        events.Subscribe<ContractAccepted>(e => Post($"Contract taken: {e.Contract.Label}, {e.Contract.Days} days to do it", Severity.Good, 0));
        events.Subscribe<MachinesLeased>(e => Post($"Leased {string.Join(", ", e.Machines.Select(m => m.Def.Name))}: they wait at {e.Poi.Name}", Severity.Good, 0));
        events.Subscribe<LeaseReturned>(e => Post($"The leased machines went back: ${e.Fee:N0} for the lease", Severity.Info, 0));
        events.Subscribe<ContractLastDay>(e => Post($"Last day for the contract: {e.Contract.Label}", Severity.Warning, 0));
        events.Subscribe<ContractDelivery>(e =>
        {
            var c = e.Contract;
            var left = c.State == ContractState.Active && c.ToDeliver - c.Delivered >= 1f ? $", {c.ToDeliver - c.Delivered:N0} {c.Goods!.Unit} to go" : "";
            Post($"Delivered {Amount(e.FillType, e.Amount)} for the contract: {c.Label}{left}", Severity.Good, 0);
        });
        events.Subscribe<ContractCompleted>(e => Post($"Contract done: {e.Contract.Label}, ${e.Reward:N0} paid", Severity.Good, 0));
        events.Subscribe<ContractFailed>(e => Post($"Contract not done in time: {e.Contract.Label}{Penalty(e.Penalty)}", Severity.Warning, 0));
        events.Subscribe<ContractCanceled>(e => Post($"Contract canceled: {e.Contract.Label}{Penalty(e.Penalty)}", Severity.Warning, 0));
        events.Subscribe<LoanTaken>(e => Post($"Borrowed ${e.Amount:N0}: the loan is ${e.Loan:N0}", Severity.Info, 0));
        events.Subscribe<LoanRepaid>(e => Post(e.Loan > 0f ? $"Repaid ${e.Amount:N0}: the loan is ${e.Loan:N0}" : "The loan is paid off", Severity.Good, 0));
        events.Subscribe<AccountOverdrawn>(_ => Post("The account is overdrawn: sell goods or borrow before buying anything", Severity.Warning));
        events.Subscribe<MachineRepaired>(e => Post($"Repaired {e.Machine.Def.Name} for ${e.Cost:N0}", Severity.Good));
        events.Subscribe<MachineWorn>(e =>
        {
            if (e.Machine.FarmId == Ownership.Farm.PlayerId) Post($"{e.Machine.Def.Name} is worn: repair it at a workshop", Severity.Warning, 0);
        });
        events.Subscribe<MachineConfigured>(e => Post($"Refitted {e.Machine.Def.Name} ({Changes(e.From, e.Machine.Def)}) for ${e.Cost:N0}", Severity.Good));
        events.Subscribe<MachineRepainted>(e => Post($"Repainted {e.Machine.Def.Name} for ${e.Cost:N0}", Severity.Good));
        events.Subscribe<MachineSold>(e => Post($"Sold the {e.Machine.Def.Name} for ${e.Price:N0}", Severity.Good, 0));
        events.Subscribe<MachineReturned>(e => Post(
            $"Gave the leased {e.Machine.Def.Name} back: ${e.Lease.Fee + e.Lease.Paid:N0} for the lease in all", Severity.Info, 0));
        events.Subscribe<MachineWashed>(e => Post(e.Cost > 0.5f ? $"Washed {e.Machine.Def.Name} for ${e.Cost:N0}" : $"Washed {e.Machine.Def.Name}", Severity.Good));
        events.Subscribe<ImplementAttached>(e => Post($"Attached {e.Implement.Def.Name}", Severity.Good));
        events.Subscribe<ImplementDetached>(e => Post($"Detached {e.Implement.Def.Name}"));
        events.Subscribe<WaypointReached>(_ => Post("Waypoint reached"));
        events.Subscribe<HelperHired>(e => Post($"Helper {e.Number} started on {e.Field.Label} ({e.Field.AreaHa:0.00} ha)", Severity.Good));
        events.Subscribe<HelperDismissed>(e =>
        {
            var wages = e.Wages >= 0.5f ? $" (${e.Wages:N0} in wages)" : "";
            switch (e.End)
            {
                case HelperEnd.Finished: Post($"Helper {e.Number} finished {e.Field.Label}{wages}", Severity.Good); break;
                case HelperEnd.Stopped: Post($"Helper {e.Number} stopped on {e.Field.Label}: {e.Reason?.Text}{wages}", Severity.Warning); break;
                default: Post($"Helper {e.Number} dismissed{wages}"); break;
            }
        });
    }

    /// <summary>The options that differ: "wheels: Dual, engine: 145 hp".</summary>
    private static string Changes(MachineDef from, MachineDef to) => string.Join(", ", to.Configurations
        .Where(c => from.Chosen(c) != to.Chosen(c))
        .Select(c => $"{c.Name.ToLowerInvariant()}: {to.Chosen(c)?.Name}"));
}

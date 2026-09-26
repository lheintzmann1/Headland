using Headland.Core;
using Headland.Core.Contracts;
using Headland.Core.Events;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// C: the contract board. The farm's contracts under way, with their progress, due day and a button to give them
/// back, and the offers on the board to take, with the farm's machines or leased ones.
/// </summary>
public partial class ContractsScreen : Screen
{
    private readonly List<Action> _refreshers = [];
    private Label _balance = null!;
    private Label _underWay = null!;
    private GridContainer _active = null!;
    private GridContainer _offers = null!;
    private IDisposable? _changes;
    private bool _rebuild = true;
    private double _refresh;

    public Simulation Sim { get; init; } = null!;

    protected override void Build()
    {
        var content = new VBoxContainer { ThemeTypeVariation = "DialogBox" };
        var account = new HBoxContainer();
        account.AddChild(Widgets.Label("Balance"));
        _balance = Widgets.Label();
        account.AddChild(_balance);
        content.AddChild(account);

        _underWay = Widgets.Label(variation: "StrongLabel");
        content.AddChild(_underWay);
        _active = new GridContainer { Columns = 6, ThemeTypeVariation = "TableGrid" };
        content.AddChild(_active);
        content.AddChild(Widgets.Label("On the board", "StrongLabel"));
        _offers = new GridContainer { Columns = 8, ThemeTypeVariation = "TableGrid" };
        content.AddChild(_offers);

        var rules = Sim.Contracts.Rules;
        AddChild(Widgets.Dialog("Contracts", content,
            $"The neighbors' field work and the buyers' orders; new offers every morning. Giving a contract back, or not " +
            $"finishing it in time, costs {rules.Penalty * 100f:0}% of its reward. Esc closes."));
        _changes = Sim.Events.SubscribeAll(e =>
        {
            if (e is IContractEvent) _rebuild = true;
        });
        Rebuild();
    }

    public override void _ExitTree() => _changes?.Dispose();

    public override void _Process(double delta)
    {
        if (_rebuild) Rebuild();
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        Refresh();
    }

    /// <summary>One row per contract, rebuilt when contracts come, go or change hands.</summary>
    private void Rebuild()
    {
        _rebuild = false;
        _refreshers.Clear();
        foreach (var table in new[] { _active, _offers })
        foreach (var cell in table.GetChildren())
            cell.Free();

        var mine = Sim.Contracts.ActiveOf(Sim.Farms.Player.Id).ToList();
        _underWay.Text = $"Under way ({mine.Count} of {Sim.Contracts.Rules.MaxActive})";
        if (mine.Count == 0) _active.AddChild(Widgets.Label("Nothing yet: take a contract from the board.", "DimLabel"));
        else Headers(_active, "Job", "For", "Progress", "Due", "Reward", "");
        foreach (var c in mine)
        {
            _active.AddChild(Widgets.Label(c.Leased ? $"{Title(c)} (leased machines)" : Title(c)));
            _active.AddChild(Widgets.Label(c.Client));
            var progress = Widgets.Label();
            _active.AddChild(progress);
            var due = Widgets.Label();
            _active.AddChild(due);
            _active.AddChild(Widgets.Money(c.Reward));
            _active.AddChild(Widgets.Button($"Give back (${Sim.Contracts.Penalty(c):N0})", () => Sim.Contracts.Cancel(c)));
            _refreshers.Add(() =>
            {
                progress.Text = Progress(Sim, c);
                var left = c.DueDay - Sim.Clock.DayIndex;
                due.Text = left <= 1 ? "Today" : $"{Sim.Contracts.DueDate(c).Short} ({left} days)";
                StringName variation = left <= 1 ? "ExpenseLabel" : "";
                if (due.ThemeTypeVariation != variation) due.ThemeTypeVariation = variation;
            });
        }

        var offers = Sim.Contracts.Offers.ToList();
        if (offers.Count == 0) _offers.AddChild(Widgets.Label("Empty: new offers go up every morning.", "DimLabel"));
        else Headers(_offers, "Job", "For", "Size", "Time", "Reward", "", "", "");
        foreach (var c in offers)
        {
            _offers.AddChild(Widgets.Label(Title(c)));
            _offers.AddChild(Widgets.Label(c.Client));
            _offers.AddChild(Widgets.Label(c.Field != null ? $"{c.Field.AreaHa:0.00} ha" : $"{c.Amount:N0} {c.Goods!.Unit}"));
            _offers.AddChild(Widgets.Label($"{c.Days} days"));
            _offers.AddChild(Widgets.Money(c.Reward));
            var take = Widgets.Button("Take", () => Sim.Contracts.Accept(c));
            _offers.AddChild(take);
            var lease = c.Lease != null ? Widgets.Button($"Lease machines (${c.LeaseFee:N0})", () => Sim.Contracts.Accept(c, lease: true)) : null;
            if (lease != null)
                lease.TooltipText = $"{string.Join(", ", c.Lease!.Machines.Select(id => Sim.Content.Machines[id].Name))}: delivered at the " +
                                    $"dealer, gone back when the contract ends. ${c.LeaseFee:N0} is taken from the reward.";
            _offers.AddChild(lease ?? (Control)Widgets.Label());
            var why = Widgets.Label(variation: "DimLabel");
            _offers.AddChild(why);
            _refreshers.Add(() =>
            {
                var blocker = Sim.Contracts.AcceptBlocker(c);
                take.Disabled = blocker != null;
                var leaseBlocker = lease != null ? Sim.Contracts.LeaseBlocker(c) : null;
                if (lease != null) lease.Disabled = leaseBlocker != null;
                why.Text = blocker ?? leaseBlocker ?? "";
            });
        }
        Refresh();
        var first = _active.GetChildren().Concat(_offers.GetChildren()).OfType<Button>().FirstOrDefault(b => !b.Disabled);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Refresh()
    {
        Widgets.Balance(_balance, Sim.Economy.Money);
        foreach (var refresh in _refreshers) refresh();
    }

    private static void Headers(GridContainer table, params string[] headers)
    {
        foreach (var header in headers) table.AddChild(Widgets.Label(header, "DimLabel"));
    }

    /// <summary>The contract, and where a harvest's crop goes.</summary>
    private static string Title(Contract c) => c.Field != null && c.Poi != null ? $"{c.Label}, to {c.Poi.Name}" : c.Label;

    /// <summary>How far a contract under way is: "64% of 95%", "3,000 of 8,000 L tipped".</summary>
    public static string Progress(Simulation sim, Contract c)
    {
        var parts = new List<string>();
        if (c.Field != null) parts.Add($"{c.Progress * 100f:0}% of {sim.Contracts.Rules.Threshold * 100f:0}%");
        if (c.Poi != null && (c.Field == null || c.Harvested > 0f)) parts.Add($"{c.Delivered:N0} of {c.ToDeliver:N0} {c.Goods!.Unit} tipped");
        return string.Join(" · ", parts);
    }
}

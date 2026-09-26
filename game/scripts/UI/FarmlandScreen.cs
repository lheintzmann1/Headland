using Headland.Core;
using Headland.Core.World;
using Godot;

namespace Headland.Game.UI;

/// <summary>L: every parcel on the map with its fields, owner and price, to buy from its NPC or sell back.</summary>
public partial class FarmlandScreen : Screen
{
    private readonly List<(Farmland land, Label owner, Button deal, Label why)> _rows = [];
    private Label _balance = null!;
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

        var table = new GridContainer { Columns = 7, ThemeTypeVariation = "TableGrid" };
        foreach (var header in new[] { "Parcel", "Area", "Fields", "Owner", "Price", "", "" })
            table.AddChild(Widgets.Label(header, "DimLabel"));
        foreach (var land in Sim.World.Farmlands.OrderBy(l => l.Id))
        {
            table.AddChild(Widgets.Label(land.Label));
            table.AddChild(Right(Widgets.Label($"{land.AreaHa:0.00} ha")));
            table.AddChild(Widgets.Label(land.Fields.Count > 0 ? string.Join(", ", land.Fields.Select(f => f.Id).Order()) : "–"));
            var owner = Widgets.Label();
            table.AddChild(owner);
            table.AddChild(Right(Widgets.Label($"$ {Sim.Farms.Price(land):N0}")));
            var deal = Widgets.Button("", () => Deal(land));
            table.AddChild(deal);
            var why = Widgets.Label(variation: "DimLabel");
            table.AddChild(why);
            _rows.Add((land, owner, deal, why));
        }
        content.AddChild(table);

        AddChild(Widgets.Dialog("Farmland", content, "Parcels come with the fields in them, and sell back for their price. Esc closes."));
        Refresh();
        if (_rows.FirstOrDefault(r => !r.deal.Disabled).deal is { } first) first.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        Refresh();
    }

    private void Deal(Farmland land)
    {
        if (land.FarmId == Sim.Farms.Player.Id) Sim.Farms.Sell(land);
        else Sim.Farms.Buy(land);
        Refresh();
    }

    private void Refresh()
    {
        Widgets.Balance(_balance, Sim.Economy.Money);
        foreach (var (land, owner, deal, why) in _rows)
        {
            var yours = land.FarmId == Sim.Farms.Player.Id;
            owner.Text = Sim.Farms.OwnerName(land);
            var blocker = yours ? Sim.Farms.SellBlocker(land) : Sim.Farms.BuyBlocker(land);
            deal.Text = yours ? "Sell" : "Buy";
            deal.Disabled = blocker != null;
            why.Text = blocker ?? "";
        }
    }

    private static Label Right(Label label)
    {
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }
}

using Headland.Core;
using Headland.Core.Contracts;
using Headland.Core.Machines.Work;
using Headland.Core.World;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's statistics (FS: the statistics page): the farm's records since the game began (days, money, land,
/// machines, the work done, what it harvested, sold and bought), and how its fields are doing, and those it works under
/// contract: what grows on them and how far, the weeds, the fertilizing, the moisture and what the crop would yield now.
/// </summary>
public partial class StatisticsPage : MenuPage
{
    private GridContainer _farm = null!;
    private GridContainer _amounts = null!;
    private GridContainer _fields = null!;
    private double _refresh;

    public Simulation Sim { get; init; } = null!;

    public override string Subtitle => "The farm's records since the game began, and how its fields are doing.";

    protected override void Build()
    {
        var columns = new HBoxContainer { ThemeTypeVariation = "DialogBox", SizeFlagsVertical = SizeFlags.ExpandFill };
        var left = new VBoxContainer { ThemeTypeVariation = "DialogBox" };
        left.AddChild(Widgets.Label("Farm", "StrongLabel"));
        _farm = new GridContainer { Columns = 2, ThemeTypeVariation = "TableGrid" };
        left.AddChild(_farm);
        left.AddChild(Widgets.Label("Goods", "StrongLabel"));
        _amounts = new GridContainer { Columns = 4, ThemeTypeVariation = "TableGrid" };
        left.AddChild(_amounts);
        columns.AddChild(left);
        columns.AddChild(new VSeparator());
        var right = new VBoxContainer { ThemeTypeVariation = "DialogBox", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(Widgets.Label("Fields", "StrongLabel"));
        _fields = new GridContainer { Columns = 8, ThemeTypeVariation = "TableGrid" };
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_fields);
        right.AddChild(scroll);
        columns.AddChild(right);
        AddChild(columns);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 2;
        Refresh();
    }

    private void Refresh()
    {
        RefreshFarm();
        RefreshAmounts();
        RefreshFields();
    }

    private void RefreshFarm()
    {
        var stats = Sim.Statistics;
        var farm = Sim.Player.FarmId;
        var land = Sim.Farms.FarmlandOf(farm).ToList();
        var machines = Sim.Machines.All.Count(m => m.FarmId == farm && m.LeaseContract == 0);
        var rows = new List<(string, string)>
        {
            ("Days played", $"{stats.DaysPlayed:N0}"),
            ("Money earned", $"$ {Sim.Economy.TotalIncome:N0}"),
            ("Money spent", $"$ {Sim.Economy.TotalExpenses:N0}"),
            ("Land", $"{land.Sum(l => l.AreaHa):0.00} ha in {land.Count} {(land.Count == 1 ? "parcel" : "parcels")}"),
            ("Machines", $"{machines:N0}"),
            ("Contracts done", $"{stats.ContractsCompleted:N0}"),
            ("Helpers hired", $"{stats.HelpersHired:N0}"),
            ("Bales made", $"{stats.BalesMade:N0}"),
        };
        // The work done, in the order the kinds of work come.
        foreach (var work in WorkTypes.All)
            if (stats.HectaresWorked.GetValueOrDefault(work.Id) is var ha and >= 0.005f)
                rows.Add((work.Done, $"{ha:N2} ha"));
        Fill(_farm, rows.SelectMany(r => new[] { r.Item1, r.Item2 }).ToList(), headerRow: false, dimFirstColumn: true);
    }

    /// <summary>What the farm harvested (threshed or baled), sold and bought, by fill type.</summary>
    private void RefreshAmounts()
    {
        var stats = Sim.Statistics;
        var cells = new List<string> { "", "Harvested", "Sold", "Bought" };
        foreach (var ft in Sim.Content.FillTypeList)
        {
            var amounts = new[] { stats.Harvested, stats.Sold, stats.Bought }.Select(d => d.GetValueOrDefault(ft.Id)).ToList();
            if (amounts.All(a => a < 0.5f)) continue;
            cells.Add(ft.Name);
            cells.AddRange(amounts.Select(a => a >= 0.5f ? $"{a:N0} {ft.Unit}" : "–"));
        }
        Fill(_amounts, cells, headerRow: true);
    }

    /// <summary>The farm's fields, and those it has a contract on: how each is doing.</summary>
    private void RefreshFields()
    {
        var farm = Sim.Player.FarmId;
        var cells = new List<string> { "Field", "Area", "", "Crop", "State", "Weeds", "Fertilized", "Expected" };
        foreach (var field in Sim.World.Fields.OrderBy(f => f.Id))
        {
            var own = Sim.World.FarmlandById(field.FarmlandId)?.FarmId == farm;
            var contract = Sim.Contracts.On(field) is { State: ContractState.Active } c && c.FarmId == farm ? c : null;
            if (!own && contract == null) continue;
            var s = FieldSurvey.Of(Sim.World, Sim.Content, field);
            cells.Add(field.Label);
            cells.Add($"{field.AreaHa:0.00} ha");
            cells.Add(own ? "yours" : $"contract: {contract!.Job.ToLowerInvariant()}");
            cells.Add(s.Crop?.Name ?? "–");
            cells.Add(s.StateShare < 0.95f ? $"{s.State} ({s.StateShare * 100f:0}%)" : s.State);
            cells.Add(s.Weeds >= 0.005f ? $"{s.Weeds * 100f:0}%" : "none");
            cells.Add($"{s.Fertilized * 100f:0}%");
            cells.Add(s.Crop != null && s.Yield >= 1f ? $"{s.Yield:N0} {Sim.Content.FillTypes[s.Crop.FillType].Unit}" : "–");
        }
        Fill(_fields, cells, headerRow: true);
    }

    /// <summary>Puts <paramref name="texts"/> in a table, row by row: its first row as headers, or its first column, dimmed.</summary>
    private static void Fill(GridContainer table, IReadOnlyList<string> texts, bool headerRow, bool dimFirstColumn = false)
    {
        foreach (var cell in table.GetChildren()) cell.Free();
        for (var k = 0; k < texts.Count; k++)
        {
            var dim = headerRow && k < table.Columns || dimFirstColumn && k % table.Columns == 0;
            table.AddChild(Widgets.Label(texts[k], dim ? "DimLabel" : ""));
        }
    }
}

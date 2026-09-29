using Headland.Core;
using Headland.Core.Content;
using Headland.Core.Machines;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's shop (FS: the store): the machines for sale by category, of one brand or all, cheapest first. The one
/// picked shows turning in a preview, with its description, what it is and its options; its price follows the options
/// as they're picked, and it's bought or leased with them, to wait on the dealer's lot. It opens on the category and
/// machine last shown.
/// </summary>
public partial class ShopPage : MenuPage
{
    private const float DetailWidth = 470f;

    private static string? _brand;
    private static string? _category;
    private static string? _machine;

    private Label _balance = null!;
    private OptionButton _brands = null!;
    private VBoxContainer _categories = null!;
    private GridContainer _machines = null!;
    private VBoxContainer _detail = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private MachinePreview _preview = null!;
    private Label _description = null!;
    private GridContainer _specs = null!;
    private GridContainer _options = null!;
    private Label _price = null!;
    private Label _leaseTerms = null!;
    private Button _buy = null!;
    private Button _lease = null!;
    private Label _why = null!;
    private Label _done = null!;
    private MachineDef? _selected;
    private Dictionary<string, string> _picked = [];
    private double _refresh;

    public Simulation Sim { get; init; } = null!;

    public override string Subtitle =>
        "Machines by category, with their options: bought or leased, they wait on the dealer's lot. A leased one costs a " +
        "fee, then its hours as it runs.";

    /// <summary>The machine picked, with the options picked.</summary>
    private MachineDef? Configured => _selected?.Configure(_picked);

    protected override void Build()
    {
        var account = new HBoxContainer();
        account.AddChild(Widgets.Label("Balance"));
        _balance = Widgets.Label();
        account.AddChild(_balance);
        AddChild(account);

        var columns = new HBoxContainer { ThemeTypeVariation = "DialogBox" };
        AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) };
        var brands = Sim.Shop.Brands.ToList();
        _brands = Widgets.Dropdown(brands.Select(b => b.Name).Prepend("All brands"), brands.FindIndex(b => b.Id == _brand) + 1, index =>
        {
            _brand = index > 0 ? brands[index - 1].Id : null;
            ShowCategories();
        });
        left.AddChild(_brands);
        _categories = new VBoxContainer();
        left.AddChild(_categories);
        columns.AddChild(left);

        _machines = new GridContainer { Columns = 3, ThemeTypeVariation = "TableGrid" };
        columns.AddChild(Scrolled(_machines, 330, 600));

        // What the machine is scrolls; its price and the buttons to get it stay in view below.
        var info = new VBoxContainer { ThemeTypeVariation = "DialogBox", CustomMinimumSize = new Vector2(DetailWidth, 0) };
        _title = Widgets.Label(variation: "TitleLabel");
        info.AddChild(_title);
        _subtitle = Widgets.Label(variation: "DimLabel");
        info.AddChild(_subtitle);
        _preview = new MachinePreview { CustomMinimumSize = new Vector2(DetailWidth, 230) };
        info.AddChild(_preview);
        _description = Widgets.Label();
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _description.CustomMinimumSize = new Vector2(DetailWidth, 0);
        info.AddChild(_description);
        _specs = new GridContainer { Columns = 2, ThemeTypeVariation = "TableGrid" };
        info.AddChild(_specs);
        _options = new GridContainer { Columns = 2, ThemeTypeVariation = "TableGrid" };
        info.AddChild(_options);
        _detail = new VBoxContainer();
        _detail.AddChild(Scrolled(info, DetailWidth + 20, 470));
        var price = new HBoxContainer();
        price.AddChild(Widgets.Label("Price", "StrongLabel"));
        _price = Widgets.Label(variation: "MoneyLabel");
        price.AddChild(_price);
        _detail.AddChild(price);
        _leaseTerms = Widgets.Label(variation: "DimLabel");
        _detail.AddChild(_leaseTerms);
        var deal = new HBoxContainer();
        _buy = Widgets.Button("Buy", () => Deal(Sim.Shop.Buy));
        deal.AddChild(_buy);
        _lease = Widgets.Button("Lease", () => Deal(Sim.Shop.Lease));
        deal.AddChild(_lease);
        _detail.AddChild(deal);
        _why = Widgets.Label(variation: "DimLabel");
        _detail.AddChild(_why);
        _done = Widgets.Label(variation: "IncomeLabel");
        _detail.AddChild(_done);
        columns.AddChild(_detail);

        ShowCategories();
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        Refresh();
    }

    /// <summary>Opens on <paramref name="def"/>, in its category, with every brand.</summary>
    public void Show(MachineDef def)
    {
        (_brand, _category, _machine) = (null, def.Category, def.Id);
        _brands.Selected = 0;
        ShowCategories();
    }

    private static ScrollContainer Scrolled(Control content, float width, float height)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(width, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(content);
        return scroll;
    }

    private BrandDef? Brand => _brand != null ? Sim.Content.Brands.GetValueOrDefault(_brand) : null;

    /// <summary>A button per category with machines of the brand picked; the one last shown (else the first) is opened.</summary>
    private void ShowCategories()
    {
        foreach (var child in _categories.GetChildren()) child.Free();
        var categories = Sim.Shop.Categories(Brand).ToList();
        var shown = categories.Find(c => c.Id == _category) ?? categories.FirstOrDefault();
        var group = new ButtonGroup();
        foreach (var c in categories)
        {
            var tab = Widgets.Tab($"{c.Name} ({Sim.Shop.Machines(c, Brand).Count()})", group, c == shown, () => ShowMachines(c));
            tab.Alignment = HorizontalAlignment.Left;
            _categories.AddChild(tab);
        }
        if (shown != null)
        {
            ShowMachines(shown);
            _categories.GetChild<Button>(categories.IndexOf(shown)).CallDeferred(Control.MethodName.GrabFocus);
        }
        else Select(null);
    }

    /// <summary>A row per machine of the category (and brand): the one last shown, else the first, is picked.</summary>
    private void ShowMachines(ShopCategoryDef category)
    {
        _category = category.Id;
        foreach (var child in _machines.GetChildren()) child.Free();
        var machines = Sim.Shop.Machines(category, Brand).ToList();
        var shown = machines.Find(m => m.Id == _machine) ?? machines.FirstOrDefault();
        var group = new ButtonGroup();
        foreach (var m in machines)
        {
            var pick = Widgets.Tab(m.Name, group, m == shown, () => Select(m));
            pick.Alignment = HorizontalAlignment.Left;
            _machines.AddChild(pick);
            _machines.AddChild(Widgets.Label(Sim.Shop.BrandOf(m)?.Name ?? "", "DimLabel"));
            var price = Widgets.Label($"$ {Sim.Shop.Price(m):N0}");
            price.HorizontalAlignment = HorizontalAlignment.Right;
            _machines.AddChild(price);
        }
        Select(shown);
    }

    /// <summary>Shows a machine as it comes, with a drop-down list per configuration.</summary>
    private void Select(MachineDef? def)
    {
        _selected = def;
        _picked = def != null ? new Dictionary<string, string>(def.Choices) : [];
        _detail.Visible = def != null;
        _done.Text = "";
        if (def == null) return;
        _machine = def.Id;
        _title.Text = def.Name;
        var category = Sim.Content.ShopCategories.GetValueOrDefault(def.Category)?.Name;
        _subtitle.Text = string.Join(" · ", new[] { Sim.Shop.BrandOf(def)?.Name, category }.Where(s => s != null));
        _description.Text = def.Description;
        _description.Visible = def.Description != "";
        foreach (var child in _options.GetChildren()) child.Free();
        foreach (var c in def.Configurations)
        {
            _options.AddChild(Widgets.Label(c.Name));
            _options.AddChild(Widgets.Options(c, def, Sim.Economy.PriceLevel, option =>
            {
                _picked[c.Id] = option;
                ShowConfigured();
            }));
        }
        ShowConfigured();
    }

    /// <summary>The machine with the options picked: its looks, what it is and its price.</summary>
    private void ShowConfigured()
    {
        if (Configured is not { } def) return;
        _preview.Def = def;
        foreach (var child in _specs.GetChildren()) child.Free();
        foreach (var spec in Sim.Shop.Specs(def))
        {
            _specs.AddChild(Widgets.Label(spec.Name, "DimLabel"));
            _specs.AddChild(Widgets.Label(spec.Value));
        }
        _price.Text = $"$ {Sim.Shop.Price(def):N0}";
        var (fee, perHour) = Sim.Shop.LeaseTerms(def);
        _leaseTerms.Text = $"Leased: ${fee:N0}, then ${perHour:N0} for each hour it runs (its engine on, or hitched to one that is)";
        Refresh();
    }

    /// <summary>Buys or leases the machine with the options picked, and says where it waits.</summary>
    private void Deal(Func<MachineDef, Machine?> deal)
    {
        if (Configured is not { } def || deal(def) is not { } machine) return;
        var lot = Sim.Pois.TriggerAt(machine.Footprint.Center, "delivery")?.Poi.Name ?? "the dealer";
        _done.Text = $"The {machine.Def.Name} waits on the lot of {lot}.";
        Refresh();
    }

    private void Refresh()
    {
        Widgets.Balance(_balance, Sim.Economy.Money);
        if (Configured is not { } def) return;
        var buy = Sim.Shop.BuyBlocker(def);
        var lease = Sim.Shop.LeaseBlocker(def);
        _buy.Disabled = buy != null;
        _lease.Disabled = lease != null;
        _why.Text = buy ?? lease ?? "";
    }
}

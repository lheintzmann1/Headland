using System.Text;
using Headland.Core;
using Headland.Core.Contracts;
using Headland.Core.Ownership;
using Headland.Core.World;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.UI;

/// <summary>What a spot on the ground is, DF-style, as BBCode: the HUD's inspector under the mouse.</summary>
public static class CellText
{
    /// <summary>Its field and farmland, contract, ground, soil, crop and how it's doing; null off the map.</summary>
    public static string? Describe(Simulation sim, NVec2 p)
    {
        var r = sim.InspectCell(p);
        if (!r.Valid) return null;
        var sb = new StringBuilder();
        var field = r.FieldId != 0 ? sim.World.FieldById(r.FieldId) : null;
        var farmland = r.FarmlandId != 0 ? sim.World.FarmlandById(r.FarmlandId) : null;
        sb.Append(field != null ? $"[b]{field.Label}[/b] ({field.AreaHa:0.00} ha)" : "[b]Open ground[/b]");
        if (farmland != null)
        {
            // A field's land goes by the field: only land outside the fields is named.
            var owner = farmland.FarmId == sim.Player.FarmId ? "your land" : $"{sim.Farms.OwnerName(farmland)}'s land";
            if (farmland.FarmId == Farm.None) owner += $", for sale at ${sim.Farms.Price(farmland):N0}";
            sb.Append(field != null && farmland.Fields.Contains(field) ? $" · {owner}" : $" · {farmland.Label}, {owner}");
        }
        sb.Append($"   {Widgets.Colored($"{r.Position.X:0}, {r.Position.Y:0} · {r.Height:0.0} m", Palette.Dim)}\n");
        if (field != null && sim.Contracts.On(field) is { } contract)
            sb.Append((contract.State == ContractState.Active
                ? Widgets.Colored($"Contract: {contract.Job}, by {sim.Contracts.DueDate(contract).Short}", Palette.Contract)
                : Widgets.Colored($"Offer: {contract.Job} for {contract.Client}, ${contract.Reward:N0}", Palette.Dim)) + "\n");
        sb.Append($"{GroundName(r.Ground)} on [b]{r.Soil?.Name}[/b]");
        if (r.Crop != null)
        {
            var ripe = r.Crop.Stages.ElementAtOrDefault(r.Stage)?.Harvestable == true;
            sb.Append($" · [b]{r.Crop.Name}[/b] — {r.StageName}{(ripe ? " " + Widgets.Colored("(ready to harvest)", Palette.Good) : "")}");
        }
        sb.Append('\n');
        if (!WorldMap.IsSealed(r.Ground))
        {
            sb.Append($"Moisture [b]{r.Moisture * 100:0}%[/b]   Nitrogen [b]{r.Nitrogen:0}[/b] kg/ha");
            if (r.Crop != null && r.StageName != "Dead") sb.Append($"   Health [b]{r.Health * 100:0}%[/b]");
            sb.Append('\n');
            var treated = new List<string>();
            if (r.Weeds != WeedState.None) treated.Add(WeedName(r.Weeds));
            if (r.Fertilized > 0) treated.Add(r.Fertilized == 1 ? "fertilized" : $"fertilized {r.Fertilized}×");
            if (treated.Count > 0) sb.Append(Widgets.Colored(string.Join(" · ", treated), Palette.Dim) + "\n");
            if (r.WindrowFill is { } lying)
                sb.Append($"{lying.Name} lying cut: [b]{r.Windrow / WorldMap.CellArea:0.0}[/b] {lying.Unit}/m²\n");
        }
        if (r.HeapFill is { } heap)
            sb.Append($"{heap.Name} heap: [b]{r.HeapHeight:0.00} m[/b] high, {r.Heap / WorldMap.CellArea:N0} {heap.Unit}/m²\n");
        if (r.Crop is { VernalizationDays: > 0 } vc && r.StageName != "Dead" && r.Stage < Array.FindIndex(vc.Stages, s => s.RequiresVernalization))
            sb.Append(r.Chill >= vc.VernalizationDays
                ? Widgets.Colored("Vernalized: ready to shoot in spring", Palette.Info) + "\n"
                : Widgets.Colored($"Vernalization {r.Chill:0}/{vc.VernalizationDays:0} cold days", Palette.Info) + "\n");
        if (r.Crop != null && !float.IsNaN(r.DaysToHarvest) && r.DaysToHarvest > 0)
        {
            var days = float.IsInfinity(r.DaysToHarvest) ? "over 2 years" : $"~{r.DaysToHarvest:0} days";
            sb.Append(Widgets.Colored($"Harvest in {days} · water factor {r.WaterFactor * 100:0}% · expected {r.ExpectedYieldPerHa:N0} L/ha", Palette.Dim) + "\n");
        }
        if (r.Crop == null && r.Soil != null && !WorldMap.IsSealed(r.Ground))
            sb.Append(Widgets.Colored(r.Soil.Description, Palette.Dim) + "\n");
        foreach (var warn in r.Warnings) sb.Append(Widgets.Warning(warn) + "\n");
        return sb.ToString().TrimEnd('\n');
    }

    private static string WeedName(byte weeds) => weeds switch
    {
        WeedState.Small => "Small weeds",
        WeedState.Grown => "Weeds",
        WeedState.Sprayed => "Sprayed against weeds",
        _ => "",
    };

    private static string GroundName(GroundType g) => g switch
    {
        GroundType.Grass => "Grass",
        GroundType.Cultivated => "Cultivated seedbed",
        GroundType.Seeded => "Sown",
        GroundType.Stubble => "Stubble",
        GroundType.Plowed => "Plowed",
        GroundType.Road => "Road",
        GroundType.Yard => "Gravel yard",
        GroundType.Dirt => "Dirt track",
        GroundType.Forest => "Forest floor",
        GroundType.Water => "Water",
        _ => g.ToString(),
    };
}

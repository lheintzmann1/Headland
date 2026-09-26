using Headland.Core.Content;
using Headland.Core.Ownership;
using Headland.Core.Pois;
using Headland.Core.World;

namespace Headland.Core.Contracts;

public enum ContractState
{
    /// <summary>On the board, for the farm to take.</summary>
    Offered,
    /// <summary>Taken by a farm, under way.</summary>
    Active,
    /// <summary>Taken off the board before anyone took it.</summary>
    Withdrawn,
    /// <summary>Not done by its due day.</summary>
    Failed,
    /// <summary>Done, and paid.</summary>
    Completed,
}

/// <summary>
/// A job from contracts.json, offered by a neighbor for one of their fields (<see cref="Field"/>) or by a buyer for
/// goods (<see cref="Poi"/>). It stays on the board for a few days; once a farm takes it, it's due within its days.
/// </summary>
public sealed class Contract
{
    public int Id { get; init; }
    public required ContractTypeDef Type { get; init; }
    /// <summary>The neighbor offering a field job, who owns the field. Deliveries are the buyer's.</summary>
    public NpcDef? Npc { get; init; }
    /// <summary>The field to work (field jobs).</summary>
    public FieldInfo? Field { get; init; }
    /// <summary>The crop to sow or harvest, for jobs that have one.</summary>
    public CropDef? Crop { get; init; }
    /// <summary>The buyer the goods go to (harvests and deliveries), and what they are.</summary>
    public Poi? Poi { get; init; }
    public FillTypeDef? Goods { get; init; }
    /// <summary>Delivery jobs: units asked for.</summary>
    public float Amount { get; init; }
    public float Reward { get; init; }
    /// <summary>Game days to finish it once taken.</summary>
    public int Days { get; init; }
    /// <summary>Day index it went on the board.</summary>
    public int OfferedDay { get; init; }

    public ContractState State { get; internal set; }
    /// <summary>The farm doing it (<see cref="Farm.None"/> while on the board).</summary>
    public int FarmId { get; internal set; }
    /// <summary>Day index it's due: unless done, it fails at the midnight starting this day.</summary>
    public int DueDay { get; internal set; }

    /// <summary>Field jobs: the share of the field in the job's done state, as of the last check.</summary>
    public float Progress { get; internal set; }
    /// <summary>Harvest jobs: what the farm threshed on the field, all of it the neighbor's.</summary>
    public float Harvested { get; internal set; }
    /// <summary>Goods tipped at the buyer for the contract.</summary>
    public float Delivered { get; internal set; }

    /// <summary>Goods that must reach the buyer: a delivery's amount, or a share of what the harvest yielded.</summary>
    public float ToDeliver => Field == null ? Amount : Type.Deliver is { } d ? d.Share * Harvested : 0f;

    /// <summary>Goods the farm has of the contract's, still to be tipped: the rest of a delivery, or the harvest's crop.</summary>
    public float Owed => MathF.Max(0f, (Field == null ? Amount : Type.Deliver != null ? Harvested : 0f) - Delivered);

    /// <summary>Who offers it: the field's owner, or the buyer.</summary>
    public string Client => Npc?.Name ?? Poi?.Name ?? "";

    /// <summary>The work, without the place: "Cultivate", "Harvest corn", "Deliver".</summary>
    public string Job => Crop != null ? $"{Type.Name} {Crop.Name.ToLowerInvariant()}" : Type.Name;

    /// <summary>"Cultivate Field 4", "Harvest corn on Field 5", "Deliver 8,000 L wheat to Grain Elevator".</summary>
    public string Label => Field == null
        ? $"{Type.Name} {Amount:N0} {Goods!.Unit} {Goods.Name.ToLowerInvariant()} to {Poi!.Name}"
        : Crop != null ? $"{Job} on {Field.Label}" : $"{Type.Name} {Field.Label}";

    public override string ToString() => $"{Label} (#{Id}, {State})";
}

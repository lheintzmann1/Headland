using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Economics;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Pois;
using Headland.Core.Pois.Components;
using Headland.Core.World;

namespace Headland.Core.Saves;

/// <summary>A save's three parts, as stored in the slot's zip.</summary>
public sealed record SaveFile(SaveMeta Meta, byte[] State, byte[] Layers);

/// <summary>A loaded game, and what couldn't be restored (content removed since the save).</summary>
public sealed record LoadedGame(Simulation Sim, IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a <see cref="Simulation"/> into a <see cref="SaveFile"/> and back. Loading rebuilds the world from the
/// map, then restores everything that changed; the game goes on exactly as it would have without the save.
/// </summary>
public static class SaveGame
{
    /// <summary>Save format version. Bump it (and migrate older saves in <see cref="Load"/>) on breaking changes.</summary>
    /// <remarks>
    /// 2: machines keep their state per component. 3: POIs too (storage, production, demand). 4: a machine's condition
    /// is its wearable's.
    /// </remarks>
    public const int Format = 4;

    /// <summary>A helper's route segments as saved, one letter per <see cref="PathSegment"/> value.</summary>
    private const string SegmentLetters = "dwr";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Snapshots the game. Cheap enough for the main thread; writing the file can happen elsewhere.</summary>
    public static SaveFile Capture(Simulation sim, string gameVersion)
    {
        var meta = new SaveMeta
        {
            Format = Format,
            GameVersion = gameVersion,
            Mods = [.. sim.Content.Mods],
            SavedAtUtc = DateTime.UtcNow,
            Map = sim.Map.Id,
            MapName = sim.Map.Name,
            FarmName = sim.Farms.Player.Name,
            Date = $"{sim.Clock.Date} {sim.Clock.TimeString}",
            Money = sim.Economy.Money,
            PlayTimeSeconds = sim.RealTime,
        };
        return new SaveFile(meta, JsonSerializer.SerializeToUtf8Bytes(CaptureState(sim), Json), LayerCodec.Write(sim.World));
    }

    public static LoadedGame Load(ContentDatabase content, SaveFile file)
    {
        if (file.Meta.Format < 1) throw new SaveException("This is not a Headland save");
        if (file.Meta.Format > Format)
            throw new SaveException($"This save comes from a newer version of Headland ({file.Meta.GameVersion})");

        SaveState state;
        try
        {
            var node = JsonNode.Parse(file.State, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject
                       ?? throw new SaveException("state.json is empty");
            if (file.Meta.Format < 2) MachinesToComponents(node);
            if (file.Meta.Format < 3) PoisToComponents(node);
            if (file.Meta.Format < 4) WearToComponents(node);
            state = node.Deserialize<SaveState>(Json) ?? throw new SaveException("state.json is empty");
        }
        catch (JsonException e)
        {
            throw new SaveException($"state.json is damaged: {e.Message}");
        }
        if (!content.Maps.ContainsKey(state.Setup.Map)) throw new SaveException($"The save's map '{state.Setup.Map}' is not installed");
        if (!content.Climates.ContainsKey(state.Setup.Climate))
            throw new SaveException($"The save's climate '{state.Setup.Climate}' is not installed");

        var warnings = new List<string>();
        foreach (var mod in file.Meta.Mods.Where(m => !content.Mods.Contains(m)))
            warnings.Add($"Saved with mod {mod.Id} {mod.Version}, which is not loaded");
        if (!content.Difficulties.ContainsKey(state.Setup.Difficulty))
        {
            warnings.Add($"Difficulty '{state.Setup.Difficulty}' no longer exists: prices follow {content.Difficulties[content.Game.Difficulty].Name}");
            state.Setup.Difficulty = content.Game.Difficulty;
        }
        var sim = Simulation.CreateForLoad(content, state.Setup);
        Restore(sim, state, file.Layers, warnings);
        return new LoadedGame(sim, warnings);
    }

    // ------------------------------------------------------------------ Capture

    private static SaveState CaptureState(Simulation sim)
    {
        var content = sim.Content;
        var weather = sim.Weather;
        var stats = sim.Statistics;
        var (pendingFrom, pendingCount) = sim.PendingHours;
        var mineral = new Dictionary<string, float>();
        for (var i = 0; i < content.Soils.Count; i++) mineral[content.Soils[i].Id] = sim.Crops.MineralAccumulators[i];

        return new SaveState
        {
            Setup = sim.Setup,
            RealTime = sim.RealTime,
            Clock = new ClockSave
            {
                TotalSeconds = sim.Clock.TotalSeconds, LastHour = sim.Clock.LastHour,
                TimeScale = sim.Clock.TimeScale, Paused = sim.Clock.Paused,
            },
            PendingHourFrom = pendingFrom,
            PendingHours = pendingCount,
            Weather = new WeatherSave
            {
                RngState = weather.Rng.State, Anomaly = weather.Anomaly, Days = [.. weather.Days],
                GroundWetness = weather.GroundWetness, SnowCover = weather.SnowCover, SnowpackMm = weather.SnowpackMm,
            },
            Crops = content.Crops.Select(c => c.Id).ToList(),
            Mineralization = mineral,
            Economy = new EconomySave
            {
                Money = sim.Economy.Money, TotalIncome = sim.Economy.TotalIncome, TotalExpenses = sim.Economy.TotalExpenses,
                Loan = sim.Economy.Loan,
                Days = sim.Economy.Ledger.Days.Select(CapturePeriod).ToList(),
                Months = sim.Economy.Ledger.Months.Select(CapturePeriod).ToList(),
            },
            Statistics = new StatisticsSave
            {
                HectaresWorked = new(stats.HectaresWorked), Harvested = new(stats.Harvested), Sold = new(stats.Sold),
                Bought = new(stats.Bought), HelpersHired = stats.HelpersHired, ContractsCompleted = stats.ContractsCompleted,
                DaysPlayed = stats.DaysPlayed,
            },
            Farms = sim.Farms.All.Select(f => new FarmSave { Id = f.Id, Name = f.Name }).ToList(),
            Farmlands = sim.World.Farmlands.Select(l => new FarmlandSave { Id = l.Id, Farm = l.FarmId }).ToList(),
            Pois = sim.World.Pois.Select(p => new PoiSave { Id = p.Id, Farm = p.FarmId, Components = p.SaveComponents(content) }).ToList(),
            PoiRngState = sim.Pois.Rng.State,
            Contracts = sim.Contracts.All.Select(CaptureContract).ToList(),
            NextContractId = sim.Contracts.NextId,
            ContractRngState = sim.Contracts.Rng.State,
            NextMachineId = sim.Machines.NextId,
            Machines = sim.Machines.All.Select(m => CaptureMachine(sim, m)).ToList(),
            Player = new PlayerSave
            {
                X = sim.Player.Position.X, Z = sim.Player.Position.Y, Heading = sim.Player.Heading,
                Farm = sim.Player.FarmId, Vehicle = sim.Player.Vehicle?.Id,
            },
        };
    }

    private static PeriodSave CapturePeriod(FinancePeriod p) => new()
    {
        Index = p.Index,
        Amounts = Ledger.Categories.Where(c => p[c] != 0f).ToDictionary(c => Json.PropertyNamingPolicy!.ConvertName(c.ToString()), c => p[c]),
    };

    private static ContractSave CaptureContract(Contract c) => new()
    {
        Id = c.Id, Type = c.Type.Id, Npc = c.Npc?.Id, Field = c.Field?.Id, Crop = c.Crop?.Id, Poi = c.Poi?.Id, Goods = c.Goods?.Id,
        Amount = c.Amount, Reward = c.Reward, Days = c.Days, OfferedDay = c.OfferedDay, Farm = c.FarmId, DueDay = c.DueDay,
        Harvested = c.Harvested, Delivered = c.Delivered,
        Lease = c.Lease != null ? Array.IndexOf(c.Type.Leases, c.Lease) : null, LeaseFee = c.LeaseFee, Leased = c.Leased,
    };

    private static MachineSave CaptureMachine(Simulation sim, Machine m)
    {
        var save = new MachineSave
        {
            Id = m.Id, Def = m.Def.Id, Configuration = m.Def.Choices.Count > 0 ? new(m.Def.Choices) : null,
            Farm = m.FarmId, Lease = m.LeaseContract != 0 ? m.LeaseContract : null,
            Leased = m.Lease is { } lease ? new LeaseSave { Fee = lease.Fee, PerHour = lease.PerHour, Paid = lease.Paid } : null,
            OperatingHours = m.OperatingHours, AgeMonths = m.AgeMonths,
            X = m.Position.X, Z = m.Position.Y, Heading = m.Heading, Speed = m.Speed,
            Parent = m.Parent?.Id, Joint = m.ParentJoint,
            WorkedHa = m.WorkedHa,
            Dirt = m.Dirt,
            Components = m.SaveComponents(sim.Content),
        };
        if (sim.Pois.Deliveries.TryGetValue(m, out var d))
            save.Delivery = new DeliverySave
            {
                Poi = d.Poi.Id, FillType = d.FillType, Amount = d.Amount, Income = d.Income, Stored = d.Stored, Contract = d.Contract?.Id,
            };
        if (sim.Pois.Loadings.TryGetValue(m, out var l))
            save.Loading = new LoadingSave { Poi = l.Silo.Poi.Id, FillType = l.FillType, Amount = l.Amount };
        if (m.Get<Drivable>()?.Controller is FieldWorkController h)
            save.Helper = new HelperSave
            {
                Field = h.Field.Id,
                Shape = h.Field.Shape.Points.Select(p => new[] { p.X, p.Y }).ToArray(),
                SpeedKmh = h.SpeedKmh,
                MaxLanes = h.MaxLanes,
                Margin = h.Margin,
                Route = h.Path.Points.Select(p => new[] { p.X, p.Y }).ToArray(),
                Segments = string.Concat(h.Path.Segments.Select(s => SegmentLetters[(int)s])),
                Waypoint = h.Driver.Index,
                DriveStart = h.Driver.Start is { } s ? [s.X, s.Y] : null,
                WagePerHour = h.WagePerHour,
                WorkedSeconds = h.WorkedSeconds,
                WagesPaid = h.WagesPaid,
            };
        return save;
    }

    // ------------------------------------------------------------------ Restore

    private static void Restore(Simulation sim, SaveState s, byte[] layers, List<string> warnings)
    {
        var content = sim.Content;
        sim.Clock.Restore(s.Clock.TotalSeconds, s.Clock.LastHour);
        sim.Clock.TimeScale = s.Clock.TimeScale;
        sim.Clock.Paused = s.Clock.Paused;

        var weather = sim.Weather;
        if (s.Weather.Days.Where((d, i) => d.DayIndex != i).Any()) throw new SaveException("The saved weather days are out of order");
        weather.Rng.State = s.Weather.RngState;
        weather.Anomaly = s.Weather.Anomaly;
        weather.Days.Clear();
        weather.Days.AddRange(s.Weather.Days);
        weather.GroundWetness = s.Weather.GroundWetness;
        weather.SnowCover = s.Weather.SnowCover;
        weather.SnowpackMm = s.Weather.SnowpackMm;

        // Cells store crop index + 1: map the saved crop list onto today's.
        var cropRemap = new byte[256];
        for (var i = 0; i < s.Crops.Count && i < 254; i++)
        {
            var now = content.CropIndex(s.Crops[i]);
            if (now < 0) warnings.Add($"Crop '{s.Crops[i]}' no longer exists: fields growing it were cleared");
            cropRemap[i + 1] = (byte)(now + 1);
        }
        LayerCodec.Read(layers, sim.World, cropRemap, warnings);
        for (var i = 0; i < content.Soils.Count; i++)
            sim.Crops.MineralAccumulators[i] = s.Mineralization.GetValueOrDefault(content.Soils[i].Id);

        sim.Economy.Restore(s.Economy.Money, s.Economy.TotalIncome, s.Economy.TotalExpenses, s.Economy.Loan);
        sim.Economy.Ledger.Restore(s.Economy.Days.Select(RestorePeriod), s.Economy.Months.Select(RestorePeriod), sim.Clock.DayIndex);
        var stats = sim.Statistics;
        foreach (var (target, saved) in new[]
                 {
                     (stats.HectaresWorked, s.Statistics.HectaresWorked), (stats.Harvested, s.Statistics.Harvested),
                     (stats.Sold, s.Statistics.Sold), (stats.Bought, s.Statistics.Bought),
                 })
        foreach (var (key, value) in saved)
            target[key] = value;
        stats.HelpersHired = s.Statistics.HelpersHired;
        stats.ContractsCompleted = s.Statistics.ContractsCompleted;
        stats.DaysPlayed = s.Statistics.DaysPlayed;

        foreach (var f in s.Farms)
            if (sim.Farms.ById(f.Id) is { } farm) farm.Name = f.Name;
        foreach (var l in s.Farmlands)
        {
            if (sim.World.FarmlandById(l.Id) is not { } land) warnings.Add($"Farmland {l.Id} no longer exists on this map");
            else if (l.Farm != Ownership.Farm.None && sim.Farms.ById(l.Farm) == null) warnings.Add($"Farmland {l.Id} belonged to an unknown farm");
            else land.FarmId = l.Farm;
        }
        RestorePois(sim, s, warnings);
        RestoreContracts(sim, s, warnings);

        var machines = RestoreMachines(sim, s, warnings);
        var p = s.Player;
        sim.Player.Position = new Vector2(p.X, p.Z);
        sim.Player.Heading = p.Heading;
        sim.Player.FarmId = p.Farm;
        sim.Player.Restore(p.Vehicle is { } id ? machines.GetValueOrDefault(id) : null);
        sim.RestoreTime(s.RealTime, s.PendingHourFrom, s.PendingHours);
    }

    /// <summary>A page of the books; money of a category this version doesn't know goes under Other.</summary>
    private static FinancePeriod RestorePeriod(PeriodSave p)
    {
        var period = new FinancePeriod(p.Index);
        foreach (var (name, amount) in p.Amounts)
            period.Amounts[(int)(Enum.TryParse<MoneyCategory>(name, ignoreCase: true, out var c) ? c : MoneyCategory.Other)] += amount;
        return period;
    }

    private static void RestorePois(Simulation sim, SaveState s, List<string> warnings)
    {
        var context = new SaveContext(sim.Content, warnings);
        if (s.PoiRngState != 0) sim.Pois.Rng.State = s.PoiRngState;
        foreach (var p in s.Pois)
        {
            if (sim.World.PoiById(p.Id) is not { } poi)
            {
                if (p.Components.ContainsKey("fillUnits")) warnings.Add($"POI '{p.Id}' no longer exists on this map: what it stored is lost");
                continue;
            }
            if (p.Farm != Ownership.Farm.None && sim.Farms.ById(p.Farm) == null) warnings.Add($"{poi.Name} belonged to an unknown farm");
            else poi.FarmId = p.Farm;
            poi.LoadComponents(p.Components, context);
        }
    }

    /// <summary>Contracts whose job, field, crop, buyer or goods are gone are dropped: offers quietly, taken ones with a warning.</summary>
    private static void RestoreContracts(Simulation sim, SaveState s, List<string> warnings)
    {
        var content = sim.Content;
        var contracts = new List<Contract>();
        foreach (var c in s.Contracts)
        {
            var type = content.ContractTypes.GetValueOrDefault(c.Type);
            var npc = c.Npc != null ? content.Npcs.GetValueOrDefault(c.Npc) : null;
            var field = c.Field is { } f ? sim.World.FieldById(f) : null;
            var crop = c.Crop != null ? content.CropById(c.Crop) : null;
            var poi = c.Poi != null ? sim.World.PoiById(c.Poi) : null;
            var goods = c.Goods != null ? content.FillTypes.GetValueOrDefault(c.Goods) : null;
            var active = c.Farm != Ownership.Farm.None;
            if (type == null || (c.Npc != null && npc == null) || (c.Field != null && field == null) || (c.Crop != null && crop == null)
                || (c.Poi != null && poi == null) || (c.Goods != null && goods == null) || (active && sim.Farms.ById(c.Farm) == null))
            {
                if (active) warnings.Add($"A contract ({c.Type}) can't go on: what it was about no longer exists");
                continue;
            }
            contracts.Add(new Contract
            {
                Id = c.Id, Type = type, Npc = npc, Field = field, Crop = crop, Poi = poi, Goods = goods,
                Amount = c.Amount, Reward = c.Reward, Days = c.Days, OfferedDay = c.OfferedDay,
                State = active ? ContractState.Active : ContractState.Offered, FarmId = c.Farm, DueDay = c.DueDay,
                Harvested = Math.Max(0f, c.Harvested), Delivered = Math.Max(0f, c.Delivered),
                Lease = c.Lease is { } k && k >= 0 && k < type.Leases.Length ? type.Leases[k] : null, LeaseFee = c.LeaseFee,
                Leased = active && c.Leased,
            });
        }
        sim.Contracts.Restore(contracts, s.NextContractId);
        if (s.ContractRngState != 0) sim.Contracts.Rng.State = s.ContractRngState;
    }

    private static Dictionary<int, Machine> RestoreMachines(Simulation sim, SaveState s, List<string> warnings)
    {
        var content = sim.Content;
        var ms = sim.Machines;
        var byId = new Dictionary<int, Machine>();
        foreach (var m in s.Machines)
        {
            if (!content.Machines.TryGetValue(m.Def, out var def))
            {
                warnings.Add($"Machine '{m.Def}' no longer exists: it was removed");
                continue;
            }
            if (m.Lease is { } lease && sim.Contracts.ById(lease) is not { Leased: true })
            {
                warnings.Add($"The leased {def.Name} went back: its contract is over");
                continue;
            }
            if (m.Configuration is { } chosen) def = Configure(def, chosen, warnings);
            var machine = new Machine(m.Id, def, new Vector2(m.X, m.Z), m.Heading, m.Farm)
            {
                LeaseContract = m.Lease ?? 0,
                Lease = m.Leased is { } l ? new MachineLease { Fee = l.Fee, PerHour = MathF.Max(0f, l.PerHour), Paid = l.Paid } : null,
                OperatingHours = Math.Max(0.0, m.OperatingHours),
                AgeMonths = Math.Max(0, m.AgeMonths),
            };
            ms.All.Add(machine);
            byId[m.Id] = machine;
        }
        ms.NextId = Math.Max(s.NextMachineId, byId.Keys.DefaultIfEmpty(0).Max() + 1);

        // Hitch first: hitching places the child and resets its lift, which the saved state then overrides.
        foreach (var m in s.Machines)
        {
            if (m.Parent is not { } parentId || !byId.TryGetValue(m.Id, out var child)) continue;
            if (!byId.TryGetValue(parentId, out var parent) || m.Joint == null || !ms.Hitch(parent, m.Joint, child))
                warnings.Add($"{child.Def.Name} could not be re-attached");
        }

        var context = new SaveContext(content, warnings);
        foreach (var m in s.Machines)
        {
            if (!byId.TryGetValue(m.Id, out var machine)) continue;
            machine.Position = new Vector2(m.X, m.Z);
            machine.Heading = m.Heading;
            machine.Speed = m.Speed;
            machine.WorkedHa = m.WorkedHa;
            machine.Dirt = Math.Clamp(m.Dirt, 0f, 1f);
            machine.LoadComponents(m.Components, context);
            if (m.Delivery is { } d && sim.Pois.ById(d.Poi) is { } poi && content.FillTypes.ContainsKey(d.FillType))
                sim.Pois.Deliveries[machine] = new Delivery(poi, d.FillType, d.Amount, d.Income, d.Stored, d.Contract is { } id ? sim.Contracts.ById(id) : null);
            if (m.Loading is { } l && sim.Pois.ById(l.Poi)?.Get<Silo>() is { } silo && content.FillTypes.ContainsKey(l.FillType))
                sim.Pois.Loadings[machine] = new Loading(silo, l.FillType, l.Amount);
        }

        // Helpers last: they work with the implements attached.
        foreach (var m in s.Machines)
        {
            if (m.Helper is not { } h || !byId.TryGetValue(m.Id, out var vehicle)) continue;
            if (vehicle.Get<Drivable>() is not { } seat || !vehicle.Has<Motor>() || h.Shape.Length < 3 || !vehicle.Chain().Any(c => c.Has<WorkAreas>()))
            {
                warnings.Add($"The helper on {vehicle.Def.Name} could not resume");
                continue;
            }
            var shape = new Polygon(h.Shape.Select(q => new Vector2(q[0], q[1])));
            var mapField = sim.World.FieldById(h.Field);
            var field = mapField != null && mapField.Shape.Points.SequenceEqual(shape.Points)
                ? mapField
                : new FieldInfo { Id = h.Field, Shape = shape, FarmlandId = mapField?.FarmlandId ?? 0 };
            FieldWorkController helper;
            if (RestoreRoute(h) is { } route)
            {
                helper = new FieldWorkController(vehicle, field, h.SpeedKmh, h.MaxLanes, h.Margin, route);
                helper.Driver.Index = Math.Clamp(h.Waypoint, 0, route.Points.Count);
                helper.Driver.Start = h.DriveStart is [var sx, var sz] ? new Vector2(sx, sz) : null;
            }
            else helper = new FieldWorkController(sim, vehicle, field, h.SpeedKmh, h.MaxLanes); // planned again from where it is
            helper.WagePerHour = h.WagePerHour ?? sim.HelperWage;
            helper.WorkedSeconds = Math.Max(0.0, h.WorkedSeconds);
            helper.WagesPaid = Math.Max(0f, h.WagesPaid);
            seat.Controller = helper;
        }
        return byId;
    }

    /// <summary>A machine type with its saved options; an option that no longer exists gives the default.</summary>
    private static MachineDef Configure(MachineDef def, Dictionary<string, string> chosen, List<string> warnings)
    {
        foreach (var (id, option) in chosen)
            if (def.Configurations.FirstOrDefault(c => c.Id == id) is { } c && c.Option(option) == null)
                warnings.Add($"The {def.Name}'s {c.Name.ToLowerInvariant()} '{option}' no longer exists: it has {c.Default?.Name ?? "none"}");
        try
        {
            return def.Configure(chosen);
        }
        catch (JsonException)
        {
            warnings.Add($"The {def.Name}'s options no longer go together: it has the standard ones");
            return def;
        }
    }

    // ------------------------------------------------------------------ Migrations

    /// <summary>
    /// Format 1 kept a machine's state in flat fields (lowered, pipeOut, fillUnits…); format 2 keeps it per component.
    /// Each component takes its part, whatever the machine is: those it doesn't have are ignored on load.
    /// </summary>
    private static void MachinesToComponents(JsonObject state)
    {
        foreach (var node in state["machines"] as JsonArray ?? [])
        {
            if (node is not JsonObject m) continue;
            JsonNode? Take(string key) => m.Remove(key, out var value) ? value : null;
            JsonObject Part(params (string key, JsonNode? value)[] fields)
            {
                var part = new JsonObject();
                foreach (var (key, value) in fields)
                    if (value != null) part[key] = value;
                return part;
            }
            var on = Take("turnedOn");
            var pose = Take("workPose");
            m["components"] = new JsonObject
            {
                ["runningGear"] = Part(("steerAngle", Take("steerAngle")), ("distance", Take("distance"))),
                ["attachable"] = Part(("lowered", Take("lowered")), ("lowerAnim", Take("lowerAnim"))),
                ["fillUnits"] = Part(("units", Take("fillUnits"))),
                ["workAreas"] = Part(("on", on?.DeepClone()), ("crop", Take("seedCrop")), ("poses", pose != null ? new JsonArray(pose) : null)),
                ["thresher"] = Part(("on", on)),
                ["pipe"] = Part(("out", Take("pipeOut")), ("anim", Take("pipeAnim"))),
                ["tipper"] = Part(("tipping", Take("tipping")), ("anim", Take("tipAnim"))),
            };
        }
    }

    /// <summary>
    /// Format 2 kept a POI's storage, production progress and demand in flat fields; format 3 keeps them per component.
    /// Storage goes into fill units named after the fill type each holds (as POIs name their storage units), and
    /// demand to the selling station; the part of a production cycle done is dropped (under an hour of work).
    /// </summary>
    /// <summary>Format 3 and older kept a machine's condition beside its components: it's its wearable's now.</summary>
    private static void WearToComponents(JsonObject state)
    {
        foreach (var node in state["machines"] as JsonArray ?? [])
        {
            if (node is not JsonObject m || !m.Remove("condition", out var condition) || condition == null) continue;
            var components = m["components"] as JsonObject ?? new JsonObject();
            components["wearable"] = new JsonObject { ["condition"] = condition };
            m["components"] = components;
        }
    }

    private static void PoisToComponents(JsonObject state)
    {
        foreach (var node in state["pois"] as JsonArray ?? [])
        {
            if (node is not JsonObject p) continue;
            JsonNode? Take(string key) => p.Remove(key, out var value) ? value : null;
            var components = Take("components") as JsonObject ?? new JsonObject();
            if (Take("storage") is JsonObject { Count: > 0 } storage)
                components["fillUnits"] = new JsonObject
                {
                    ["units"] = new JsonArray(storage.Select(kv => (JsonNode)new JsonObject
                    {
                        ["id"] = kv.Key, ["fillType"] = kv.Key, ["level"] = kv.Value?.DeepClone(),
                    }).ToArray()),
                };
            var demand = Take("demand");
            var high = Take("highDemand");
            if (demand is JsonObject { Count: > 0 } || high != null)
                components["sellingStation"] = new JsonObject { ["demand"] = demand, ["highDemand"] = high };
            Take("progress");
            p["components"] = components;
        }
    }

    /// <summary>A helper's saved route, or null when there is none (an older save) or it doesn't read.</summary>
    private static FieldPath? RestoreRoute(HelperSave h)
    {
        if (h.Route.Length == 0 || h.Route.Length != h.Segments.Length || h.Route.Any(p => p.Length != 2)) return null;
        var segments = h.Segments.Select(c => SegmentLetters.IndexOf(c)).ToList();
        if (segments.Contains(-1)) return null;
        return new FieldPath(h.Route.Select(p => new Vector2(p[0], p[1])).ToList(), segments.Select(k => (PathSegment)k).ToList());
    }
}

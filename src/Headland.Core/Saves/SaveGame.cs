using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Machines;
using Headland.Core.Pois;
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
    public const int Format = 1;

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
        // Format 1 is the first: older formats would be migrated here.

        SaveState state;
        try
        {
            state = JsonSerializer.Deserialize<SaveState>(file.State, Json) ?? throw new SaveException("state.json is empty");
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
                Bought = new(stats.Bought), HelpersHired = stats.HelpersHired, DaysPlayed = stats.DaysPlayed,
            },
            Farms = sim.Farms.All.Select(f => new FarmSave { Id = f.Id, Name = f.Name }).ToList(),
            Farmlands = sim.World.Farmlands.Select(l => new FarmlandSave { Id = l.Id, Farm = l.FarmId }).ToList(),
            Pois = sim.World.Pois.Select(p => new PoiSave
            {
                Id = p.Id, Farm = p.FarmId,
                Storage = p.Storage?.Levels.Where(kv => kv.Value > 0f).ToDictionary(kv => kv.Key, kv => kv.Value) ?? [],
                Progress = p.Progress.Any(x => x > 0f) ? [.. p.Progress] : null,
                Demand = new(p.Demand),
                HighDemand = p.HighDemand is { } h ? new HighDemandSave { FillType = h.FillType, Factor = h.Factor, EndDay = h.EndDay } : null,
            }).ToList(),
            PoiRngState = sim.Pois.Rng.State,
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

    private static MachineSave CaptureMachine(Simulation sim, Machine m)
    {
        var save = new MachineSave
        {
            Id = m.Id, Def = m.Def.Id, Farm = m.FarmId,
            X = m.Position.X, Z = m.Position.Y, Heading = m.Heading,
            Speed = m.Speed, SteerAngle = m.SteerAngle, Distance = m.Distance,
            Parent = m.Parent?.Id, Joint = m.ParentJoint,
            Lowered = m.Lowered, TurnedOn = m.TurnedOn, PipeOut = m.PipeOut, Tipping = m.Tipping,
            LowerAnim = m.LowerAnim, PipeAnim = m.PipeAnim, TipAnim = m.TipAnim,
            SeedCrop = m.Def.SeedTank != null ? sim.Content.Crops[m.SelectedCrop].Id : null,
            WorkedHa = m.WorkedHa,
            Condition = m.Condition,
            Dirt = m.Dirt,
            FillUnits = m.FillUnits.Select(u => new FillUnitSave { Id = u.Def.Id, FillType = u.FillType, Level = u.Level }).ToList(),
            WorkPose = m.HasWorkPose ? [m.PrevWorkCenter.X, m.PrevWorkCenter.Y, m.PrevWorkHeading] : null,
        };
        if (sim.Pois.Deliveries.TryGetValue(m, out var d))
            save.Delivery = new DeliverySave { Poi = d.Poi.Id, FillType = d.FillType, Amount = d.Amount, Income = d.Income, Stored = d.Stored };
        if (sim.Pois.Loadings.TryGetValue(m, out var l))
            save.Loading = new LoadingSave { Poi = l.Trigger.Poi.Id, Trigger = l.Trigger.Id, FillType = l.FillType, Amount = l.Amount };
        if (m.Controller is FieldWorkController h)
            save.Helper = new HelperSave
            {
                Field = h.Field.Id,
                Shape = h.Field.Shape.Points.Select(p => new[] { p.X, p.Y }).ToArray(),
                SpeedKmh = h.SpeedKmh,
                MaxLanes = h.MaxLanes,
                PlannedFrom = [h.PlannedFrom.X, h.PlannedFrom.Y],
                Margin = h.Margin,
                Waypoint = h.Driver.Index,
                DriveStart = h.Driver.Start is { } s ? [s.X, s.Y] : null,
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
        var content = sim.Content;
        if (s.PoiRngState != 0) sim.Pois.Rng.State = s.PoiRngState;
        foreach (var p in s.Pois)
        {
            if (sim.World.PoiById(p.Id) is not { } poi)
            {
                if (p.Storage.Count > 0) warnings.Add($"POI '{p.Id}' no longer exists on this map: what it stored is lost");
                continue;
            }
            if (p.Farm != Ownership.Farm.None && sim.Farms.ById(p.Farm) == null) warnings.Add($"{poi.Name} belonged to an unknown farm");
            else poi.FarmId = p.Farm;
            foreach (var (ft, level) in p.Storage)
            {
                if (poi.Storage?.Keeps(ft) == true) poi.Storage.Set(ft, level);
                else warnings.Add($"{poi.Name} no longer stores '{ft}': {level:N0} was lost");
            }
            foreach (var (ft, demand) in p.Demand)
                if (content.FillTypes.ContainsKey(ft)) poi.Demand[ft] = Math.Clamp(demand, 0f, 1f);
            if (p.HighDemand is { } h && content.FillTypes.ContainsKey(h.FillType)) poi.HighDemand = new HighDemand(h.FillType, h.Factor, h.EndDay);
            if (p.Progress is { } progress)
                for (var i = 0; i < Math.Min(progress.Length, poi.Progress.Length); i++)
                    if (poi.Def.Actions[i].Type == "process") poi.Progress[i] = Math.Clamp(progress[i], 0f, 1f);
        }
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
            var machine = new Machine(m.Id, def, new Vector2(m.X, m.Z), m.Heading, m.Farm);
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

        foreach (var m in s.Machines)
        {
            if (!byId.TryGetValue(m.Id, out var machine)) continue;
            machine.Position = new Vector2(m.X, m.Z);
            machine.Heading = m.Heading;
            machine.Speed = m.Speed;
            machine.SteerAngle = m.SteerAngle;
            machine.Distance = m.Distance;
            machine.Lowered = m.Lowered;
            machine.TurnedOn = m.TurnedOn;
            machine.PipeOut = m.PipeOut;
            machine.Tipping = m.Tipping;
            machine.LowerAnim = m.LowerAnim;
            machine.PipeAnim = m.PipeAnim;
            machine.TipAnim = m.TipAnim;
            machine.WorkedHa = m.WorkedHa;
            machine.Condition = Math.Clamp(m.Condition, 0f, 1f);
            machine.Dirt = Math.Clamp(m.Dirt, 0f, 1f);
            if (m.SeedCrop != null && content.CropIndex(m.SeedCrop) is var crop and >= 0) machine.SelectedCrop = crop;
            foreach (var u in m.FillUnits)
            {
                if (machine.Unit(u.Id) is not { } unit) continue;
                if (u.FillType != null && !content.FillTypes.ContainsKey(u.FillType))
                {
                    warnings.Add($"Fill type '{u.FillType}' no longer exists: {machine.Def.Name} was emptied");
                    continue;
                }
                unit.Level = Math.Clamp(u.Level, 0f, unit.Capacity);
                unit.FillType = unit.Level > 0f ? u.FillType : null;
            }
            if (m.WorkPose is [var x, var z, var heading])
            {
                machine.HasWorkPose = true;
                machine.PrevWorkCenter = new Vector2(x, z);
                machine.PrevWorkHeading = heading;
            }
            if (m.Delivery is { } d && sim.Pois.ById(d.Poi) is { } poi && content.FillTypes.ContainsKey(d.FillType))
                sim.Pois.Deliveries[machine] = new Delivery(poi, d.FillType, d.Amount, d.Income, d.Stored);
            if (m.Loading is { } l && sim.Pois.ById(l.Poi)?.Trigger(l.Trigger) is { Type: "load" } spout && content.FillTypes.ContainsKey(l.FillType))
                sim.Pois.Loadings[machine] = new Loading(spout, l.FillType, l.Amount);
        }

        // Helpers last: their route depends on the implements attached.
        foreach (var m in s.Machines)
        {
            if (m.Helper is not { } h || !byId.TryGetValue(m.Id, out var vehicle)) continue;
            if (!vehicle.IsMotorized || h.Shape.Length < 3 || h.PlannedFrom.Length != 2 || !vehicle.Chain().Any(c => c.Def.WorkArea != null))
            {
                warnings.Add($"The helper on {vehicle.Def.Name} could not resume");
                continue;
            }
            var shape = new Polygon(h.Shape.Select(q => new Vector2(q[0], q[1])));
            var mapField = sim.World.FieldById(h.Field);
            var field = mapField != null && mapField.Shape.Points.SequenceEqual(shape.Points)
                ? mapField
                : new FieldInfo { Id = h.Field, Shape = shape, FarmlandId = mapField?.FarmlandId ?? 0 };
            var helper = new FieldWorkController(vehicle, field, h.SpeedKmh, h.MaxLanes, new Vector2(h.PlannedFrom[0], h.PlannedFrom[1]), h.Margin);
            helper.Driver.Index = Math.Clamp(h.Waypoint, 0, helper.Path.Points.Count);
            helper.Driver.Start = h.DriveStart is [var sx, var sz] ? new Vector2(sx, sz) : null;
            vehicle.Controller = helper;
        }
        return byId;
    }
}

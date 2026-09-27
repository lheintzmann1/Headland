# TODO

Everything planned for Headland, grouped by area. **Order** says what to tackle first, since many features
depend on others.

Rules for every item:

- Simulation goes in `Headland.Core` with tests; Godot only renders and feeds input.
- New content types load and validate through `ContentDatabase`, so mods get each feature for free.
- Anything a player owns or changes goes into the save file.

## Order

1. **Quick wins**: numbered fields, switch-vehicle key.
2. **Foundations**: save/load, ownership and farmland, event bus, UI framework. Most later items need these.
3. **Economy**: POIs, finances and loans, vehicle shop, wear/fuel/maintenance, contracts.
4. **Controls**: input layer with contexts and modifiers, the in-game menu. The tools in 5 need it.
5. **Vehicles**: component-based machines, running gear, configurations, lights, tools; the old machine
   code goes as its replacements land.
6. **World**: authored elevation, water, spline roads, towns and buildings, AI traffic.
7. **Modding**: mod loader, Lua, map SDK (after the formats from 3–6 settle).
8. **Audio**: any time after the settings menu; engine sounds after the vehicle refactor.
9. **Art**: Blockbench models replace placeholders as they're made.

## Quick wins

- [x] **Number fields instead of naming them.** Drop `name` from `FieldDef`, `FieldInfo` and the map
      JSON; show the number on field signs (`PropsRenderer`), the HUD, helper notifications and the
      inspector.
- [x] **Switch-vehicle key** (FS: Tab / Shift+Tab): jump into the next/previous farm vehicle. Physical
      key, label via `InputSetup.Label`, listed in F1 help and README controls. A helper driving a vehicle
      keeps working.
- [x] Fix the stale `scripts/Core/InputSetup.cs` path in the `game/project.godot` header comment.

## Foundations

- [x] **Save/load.** Versioned format: JSON for entities, compressed binary for `FieldLayers`. Slots,
      autosave, game version and mod list recorded. Covers clock, weather (anomaly, generated days,
      snowpack), crop mineralization accumulators, field layers, machines (pose, attachments, fill,
      configuration, wear, fuel), economy, player, POI storage, contracts, loans. Configuration, wear,
      fuel, POI storage, contracts and loans join `Saves/SaveData.cs` as they're built.
- [x] **Ownership.** Farm id on machines, farmland and storage; NPC owners for the rest. Needed by the
      shop, contracts, vehicle switching and field access.
- [x] **Farmland and fields** (FS model): farmland parcels are bought, fields are the crop areas inside.
      Polygon shapes, not only rectangles. Field ids are a byte today (max 255).
- [x] **Event bus in Core.** Typed events (time ticks, weather, field work, harvest, sale, purchase,
      attach…) feeding notifications, sounds, statistics and Lua.
- [x] **UI framework.** A `Theme` resource and a credited OFL/CC0 font, a screen stack (Esc closes),
      shared widgets; replaces the per-control styles in `Hud.cs`.
- [x] **User settings file** (`user://settings.cfg`): graphics, audio, controls, gameplay.

## Points of interest (POIs)

One generic POI replaces `SellPointDef`, `ShopDef` and the visual-only `BuildingDef`. A POI has a
footprint, a model or placeholder, a map icon, triggers, storage and **actions**: inputs → outputs, with
conditions. Money is just another input or output:

| POI            | Action                                |
|----------------|---------------------------------------|
| Grain elevator | in: 1 wheat → out: price $            |
| Flour mill     | in: x wheat → out: y flour (per hour) |
| Farm shop      | in: $ → out: seed                     |
| Gas station    | in: $ → out: diesel                   |

- [x] POI types in `data/pois/`, placements in the map JSON, validated by `ContentDatabase`.
- [x] Triggers: unload (tipper, pipe), load (fill a trailer from storage), fill (fuel, seed), wash,
      repair, vehicle delivery spot.
- [x] Actions: sell, buy, process (rate, cycle time, running cost), store (capacity per fill type),
      refuel, repair, wash.
- [x] Conditions: accepted fill types, opening hours, months, storage full, minimum amount; Lua
      conditions later.
- [x] Prices: per-POI factors on top of the monthly curves in `filltypes.json`, demand that drops as you
      sell and recovers, occasional high-demand events.
- [x] Production output goes to POI storage: sold automatically or loaded into a trailer. Pallets and
      bales later.
- [x] Farm silo as a player-owned storage POI.
- [x] Move sell/buy code out of `MachineSystem`; migrate the elevator and supplies shop in `default.json`;
      labels in `PropsRenderer` come from the POI def.
- [ ] Later: buy and place POIs/buildings on your own farmland (FS construction mode).

## Economy

- [x] **Finances**: daily/monthly income and expenses by category (sales, purchases, fuel, maintenance,
      wages, leasing, land, loan interest, contracts) and a finances screen.
- [x] **Loans**: borrow and repay in steps (FS: 5,000), credit limit, annual interest charged per day or
      month scaled to the compressed calendar. Negative money: only costs that come due by themselves
      overdraw the account, and nothing can be bought until it's back above zero.
- [x] Helper wages (the field helper is free today).
- [x] Farmland purchase and sale, price per ha.
- [x] Difficulty presets: start money, starting loan, price level.

## Contracts

- [x] Generation: NPC fields offer jobs by crop state and season (cultivate, sow, harvest), buyers ask for
      goods (deliver to a POI). Reward from area and type, time limit, cap on open contracts.
- [ ] Plow, fertilize, spray, mow and bale jobs: entries in `contracts.json` once machines do that work.
- [x] Field access: work only applies on the player's farmland or fields with an active contract, and only
      the contract's work. Contract fields show in the world (sign, outline) and in the inspector; the map
      screen shows them once it exists.
- [x] Equipment rental: a contract can offer a leased machine set, fee taken from the reward; machines
      appear at a delivery spot and leave when the contract ends.
- [x] Completion from field-layer progress with a threshold (FS: 95%); harvest contracts require
      delivering a share to a given POI.
- [x] Cancel penalty, contract board screen, notifications.
- [x] Contract types in data (`data/contracts.json`). New types in Lua come with the Lua extension points.

## Vehicles

### Component-based machines (FS "specializations")

`MachineDef` is a fixed set of optional blocks and the kinematics use one wheelbase. Move to composable
components so new machine kinds, including mod machines, are built from blocks.

- [x] Components: running gear, motor, drivable, attacher joints, attachable, fill units, work areas,
      pipe, tipper, lights, animated/foldable parts, crane arm, hook/winch, saw, front-loader bracket. Each
      has its def, runtime state, save data and view.
- [x] Convert the 8 existing machines in one go.

### Running gear

- [x] Axles instead of a flat wheel list: any count, each with position, track width, steering (fixed,
      front, rear, crab, all-wheel, self-steering trailer axle) and a wheel set.
- [x] Wheel sets: single, dual, narrow row-crop, flotation; tracks (crawlers) with their own visuals.
- [x] Kinematics from the running gear: multi-axle steering, articulated steering (loaders, big 4WD,
      forwarders), skid steer for full tracks, half-tracks (steered front wheels + rear tracks).
- [ ] Ground effects: slip on wet ground, lower ground pressure with duals or tracks, slope affects speed
      and fuel.
- [ ] Multi-axle trailers, dollies, semi-trailers.

### Configurations (FS-like options)

- [ ] `configurations` per machine; each option changes price, mass and components: wheels
      (single/dual/tracks), front loader (with/without), front hitch (3-point/weight/none), beacons
      (none/left/right/both/lightbar), engine power, color, fill capacity, work width.
- [ ] Model node visibility per option so one `.glb` holds every variant; document in `docs/MODELING.md`.
- [ ] Chosen options stored per machine and saved; changeable at a workshop for a fee.

### Tools and special machines

- [ ] Lights: headlights, front/rear work lights, beacons, lightbar, turn signals, hazards; player
      toggles instead of the automatic headlights in `LightsView`.
- [ ] Front loader and tools (bucket, bale fork, pallet fork); the bucket needs bulk heaps.
- [ ] Cranes: multi-joint arms (direct joint control or simple IK) and grabs.
- [ ] Hooks and winches (hook-lift containers, rope winch).
- [ ] Forestry: trees as entities (forests are props today), saw heads, logs as objects, forwarders,
      wood sell point.

### Wear, fuel and maintenance

FS19 reference values; tune in data.

- [ ] Condition 100% → 0%, dropping only while moving or working: about 100% per 16 operating hours,
      faster on fields and much faster while working.
- [ ] Effects at 0%: −30% power and +30% fuel for vehicles; ×0.7 max work speed for implements; +30%
      seed use for seeders.
- [ ] Repair at a workshop POI: price / 100 × (1 − condition), +20% when done remotely from the garage
      menu.
- [ ] Fuel: tank per motor, use from power actually delivered, refuel at a gas station or farm tank,
      engine stops when empty.
- [ ] Dirt from fields and wet weather, washing (cosmetic, shader).
- [ ] Paint wear and repaint, operating hours, age.
- [ ] Resale value from price, age, hours and condition.

### Shop and garage

- [ ] Vehicle shop screen: categories, brands, specs (uses the unused `Price`, `Brand`, `Category`,
      `Description`), configuration picker with live price, 3D preview.
- [ ] Buy or lease (upfront fee + cost per hour); delivered at the dealer's delivery spot.
- [ ] Sell vehicles; garage list with condition, fuel, hours, location, value; repair, repaint,
      reconfigure.

### Removing the old machine code

What the component refactor left in place goes as its replacement lands. Each step deletes the old code with
its docs and tests in the same change; no fallback is kept for later.

- [ ] Placeholders: once the 8 machines have their Blockbench models (Art), delete the placeholder archetypes
      (`PlaceholderBuilder`, `visual.placeholder`) and the placeholder parts the component views build
      (wheels, pipe, linkages, booms, hook, saw blade). A machine without a model shows a plain box of its
      `size`, and the content check warns about it. The farmer and POIs likewise once their models exist (POI
      `parts` stay as collision shapes).
- [x] Kinematics out of `MachineSystem.Drive` into the components: motor (speed, power, fuel), running gear
      (steering, turning from its axles) and drivable (input); the system keeps placing the chain. With
      "Kinematics from the running gear".
- [ ] Work types as a registry instead of the `cultivator`/`seeder`/`harvester` switches in `MachineSystem`,
      `WorkOps` and `WorkAreaDef.Types`: each type's cell change, what it needs (seed, a thresher) and what
      helpers and contracts check. Plowing, fertilizing, spraying and mowing (the contract jobs waiting under
      Contracts) are then one type each, and Lua can add more.
- [ ] Joint and lamp types from data instead of `AttacherJointDef.Types` and `LampDef.Types`.
- [ ] Typed machine conditions (out of seed, tank full, wrong header, underpowered) from the components
      instead of `Machine.Status` strings, which `FieldWorkController` matches by prefix to stop; the HUD
      words them.
- [ ] The HUD's vehicle panel from the components (each gives its state) instead of the special cases in
      `Hud.UpdateVehicle` (threshing, seed crop, pipe out); with the HUD redesign.
- [ ] Format-1 saves: drop `SaveGame.MachinesToComponents` and the `machines-format1.json` fixture once 0.7
      saves are no longer supported; decide and document how many versions back saves load.

## Controls

FS-style controls: a few keys do what the selected tool needs, modifiers and the mouse do the fine work, and
one menu holds every screen. Today each action has its own key, the tool components (lights, folding parts,
crane arms, winches, saws, front loaders) have none, and each screen has its own key.

### Input

- [ ] Input layer over Godot's `InputMap`: actions on keys with Ctrl/Shift/Alt, mouse buttons and axes, and
      gamepad; press, hold and double-tap; physical keys as today. Bindings saved in `settings.cfg`, rebinding
      with conflict checks per context (the settings screen in UI).
- [ ] Contexts: on foot, in a vehicle, in a menu, and mouse modes. The same key does what the context needs;
      the HUD's key hints and the F1 help list the current context's actions, built from what the vehicle's
      components offer.
- [ ] Selected implement (FS): a key cycles through the vehicle's chain; lower, turn on, fold and tool keys
      act on the selection, or on the whole chain with the vehicle itself selected (as now). The HUD marks the
      selection.
- [ ] Mouse control (FS): hold the right button to move the selected tool's joints with the mouse (crane slew
      and boom, loader lift and tilt), with Ctrl or Shift choosing which pair of joints the axes move; left
      click for its action (grab, release, cut). The camera stops following the mouse meanwhile.

### Wiring the components

Commands in Core (`MachineSystem`, tested), bound through the input layer.

- [ ] Lights: cycle the lamp groups (off, head, head and work lights), beacons, turn signals, hazards; see
      Lights under Vehicles.
- [ ] Folding: a fold key (`MachineSystem.ToggleFold` exists); lowering a folded implement then stops
      unfolding it by itself. Parts that don't fold (covers, markers, support legs) move for the selected
      implement.
- [ ] Crane arms: joints driven from the mouse or keys, and a simple IK mode moving the tip; a front loader's
      lift and tilt the same way.
- [ ] Winch: reel in and out, hook and unhook.
- [ ] Saw: on and off with the turn-on key (done); cutting once trees are entities.
- [ ] Front loader: hitch the arm to the bracket (and park it on its stands), tools on the arm.
- [ ] Pipe, tipping and seed selection (U and X today) as actions of the selected implement.

### In-game menu

- [ ] Esc opens a menu with tabs, switched with Q/E or the mouse, instead of a key per screen: map, prices,
      contracts, finances and loans, farmland, vehicles (garage), statistics, helpers, controls help,
      settings, save/load/quit. The existing screens become its pages; F2, L and C go. Esc still closes the
      top screen first.
- [ ] Remember the last tab; a few direct shortcuts (M for the map) open the menu on their tab.

## Map and terrain

- [ ] **Terrain3D** (MIT) replaces `TerrainRenderer`: clipmap LOD for large maps, and editor sculpting
      and painting the map SDK reuses. Port `terrain.gdshader` to a custom Terrain3D shader that still
      shows the per-cell field data (ground, soil, moisture, work angle, crop cover). Core keeps its own
      `HeightMap`, loaded from the same heightmap. Prototype on a branch first; if it doesn't fit, extend
      the chunk renderer (LOD, streaming) instead.
- [ ] **Elevation**: authored heightmaps (16-bit PNG/EXR) per map, noise as fallback; widen the height
      range `WorldMap.Raycast` assumes.
- [ ] **Water**: spline rivers/streams and lake/pond polygons with a water level, carved into the
      terrain, replacing stream tiles. Water shader: depth-based color and transparency, shore foam, flow
      along the spline, normal-map waves, rain ripples from `g_wetness`, reflections. Deep water blocks
      machines (fording depth per machine).
- [ ] **Roads, tracks and paths as splines**: curves, diagonals and junctions, replacing the
      axis-aligned tile runs and the 16 m tile grid (update that line in `CLAUDE.md`); lanes and speed
      limits for traffic.
- [ ] **AI traffic**: cars follow road lanes, spawn out of sight near the player and despawn far away,
      brake for obstacles and the player, give way at junctions, density by time of day, lights at night.
- [ ] **Towns and buildings**: town areas with houses and the POIs (elevator, mill, dealer, gas station,
      workshop); fences, signs, street lights, hedges. Buildings take a `visual.model` like machines.
- [ ] Authored soil maps (soils are noise-placed today).
- [ ] Rebuild the default map with all of the above.

## UI

Feature screens (shop, garage, contracts, finances) are listed with their feature; in game, they are tabs of the
in-game menu (see Controls).

- [ ] Main menu: new game (map, difficulty), continue, load, settings, mods, credits, quit.
- [ ] Pause: resume, save, load, settings, quit to menu, in the in-game menu.
- [ ] Settings: graphics (resolution, window mode, vsync, render scale, shadows, view distance), audio
      volumes, controls (rebinding on physical keys, saved), gameplay (units, autosave), language.
- [ ] Map tab (M): numbered fields, POIs with icons and filters, vehicles, contract fields, ownership;
      layers for crop, growth stage, soil, moisture; click to set a waypoint.
- [ ] Minimap in the HUD with POIs and vehicles.
- [ ] HUD redesign on the theme: vehicle panel with speed, fuel, condition and fill levels.
- [ ] Prices tab (where each crop sells best), field and farm statistics, helper list.
- [ ] Translatable strings (Godot `tr()`), English and French.

## Audio

Godot's built-in audio: 3D players, buses, effects, `AudioStreamInteractive`/`AudioStreamSynchronized`.

- [ ] Buses (Master, Music, Vehicles, Environment, UI) with volume settings.
- [ ] Engine: RPM and load in Core's motor model; layered loops crossfaded and pitched by RPM; start and
      stop.
- [ ] Machines: implement loops, hydraulics (lower, pipe, tipping), grain flow, reverse beeper, horn.
- [ ] Environment by weather, time and season (wind, rain, birds, insects), driven by
      `EnvironmentController`.
- [ ] UI sounds; optional music.
- [ ] Sounds referenced from machine and POI JSON so mods add their own; licenses in `CREDITS.md`.

## Modding

A mod is an add-on the game loads, never a fork.

### Mod loader

- [ ] `user://mods`; each mod is a folder or a zip with `mod.json` (id, name, version, author, game
      version, dependencies). No Godot `.pck` packs: they can carry GDScript, which can't be sandboxed.
- [ ] Content layering: one `IContentSource` per mod after the base game; mods add ids and override or
      patch existing ones (duplicates throw today); validation names the mod at fault.
- [ ] Runtime asset loading outside `res://`: `.glb` via `GLTFDocument`, textures, sounds.
- [ ] Mods screen (enable, order, errors); saves record the mods they used.
- [ ] Example mods (vehicle, crop, POI) and a modding guide in `docs/`.

### Lua scripting

- [ ] Pick the runtime:
  - MoonSharp or Lua-CSharp: pure C# interpreters that live in Core, so scripts run in the Core tests;
    no native libraries.
  - [lua-gdextension](https://github.com/gilzoide/lua-gdextension): real Lua 5.4 or LuaJIT, MIT,
    per-state library sandboxing built in, prebuilt for Linux, Windows and macOS. It's a native
    GDExtension on the Godot side, so the Lua API would live in `game/` and wrap Core from there.
- [ ] Sandboxed runtime (no `io`/`os`, CPU and memory limits), errors isolated per mod.
- [ ] API over Core: event bus subscriptions, read access to world, fields, machines and economy, safe
      commands.
- [ ] Extension points: contract types, POI actions and conditions, machine components, weather
      generation, season rules (move the hard-coded season months out of `GameClock` into data first),
      crop growth hooks. New crops and fill types stay plain JSON.
- [ ] API reference docs; hot reload in development.

### Map SDK

- [ ] Separate Godot project (`sdk/`) with an editor plugin: heightmap import/sculpt/paint (Terrain3D's
      tools), spline roads and water, field and farmland polygons, POI and building placement from defs, forests, spawn points.
- [ ] Export to a map mod (JSON + heightmap + layer images), checked with `ContentDatabase.Validate`
      (the SDK references `Headland.Core`).
- [ ] Build Headland's own maps with it; SDK docs; CI builds and validates it.

## Art

- [ ] Blockbench models for `tractor_125`, `tractor_95`, `combine_7`, `header_grain_6`, `header_corn_6`,
      `cultivator_3`, `seeder_3`, `trailer_16`, with configuration parts as separate nodes (dual wheels,
      tracks, front loader, beacons, lightbar, front hitch).
- [ ] Buildings and POIs, traffic cars, player character, trees and props.
- [ ] `docs/MODELING.md`: roles for configurations, lights, tracks, crane joints; building models.

## Links

- FS vehicle types and specializations: <https://codeberg.org/Farming-Simulator/KNOWLEDGE-BASE/src/branch/main/Vehicle-Types/vehicle-types.md>
- GIANTS i3d format: <https://gdn.giants-software.com/documentation_i3d.php#i3d_introduction>
- FS25 scripting, `TreeSaw`: <https://gdn.giants-software.com/documentation_scripting_fs25.php?category=78&class=815&version=script>
- FS19 scripting, `AIVehicleUtil`: <https://gdn.giants-software.com/documentation_scripting_fs19.php?version=script&category=41&class=441>
- FS19 maintenance: <https://farmingsimulator.wiki.gg/wiki/Maintenance/Farming_Simulator_19>

# Headland

Top-down/isometric farming simulation (Farming Simulator gameplay, Dwarf Fortress / CDDA-style depth) in
**Godot 4.7.2 .NET**. Player-facing overview in `README.md`.

## Layout

- `src/Headland.Core/`: the whole simulation in plain C# (net8.0, **no Godot references**): time, weather,
  world/field layers, crops, machines, economy, player. Godot only renders and feeds input.
- `tests/Headland.Core.Tests/`: xUnit, including agronomy calibration (`CalibrationTests`) and helper coverage.
- `game/`: the Godot project (assembly `Headland`, namespaces `Headland.Game.*`). `scripts/` (C# presentation),
  `shaders/` (GDShader), `data/` (JSON content "raws": crops, machines, soils, climates, maps, npcs),
  `ui/theme.tres` (the project `Theme`), `assets/` (textures, fonts, icons, future models, `CREDITS.md`),
  `export_presets.cfg` (Linux/Windows/macOS, used by CI).
- `.github/workflows/`: `ci.yml` (build + tests + content validation), `build.yml` (Godot exports; tags `v*` publish a release).
- `tools/`: asset pipeline (Python + PIL/numpy): `convert_tiles.py` (iso tiles → top-down atlases),
  `fetch_textures.py` (CC0 ambientCG ground textures), `gen_crop_cards.py` (procedural crop atlas).

## Commands

```sh
dotnet build Headland.sln                                         # everything
dotnet test tests/Headland.Core.Tests                             # ~30 s
~/.local/bin/godot --headless --path game --import                # after changing textures (re-import)
~/.local/bin/godot --path game                                    # play
~/.local/bin/godot --path game -- --scenario=loop --shots=/abs/dir  # scripted end-to-end run + screenshots
~/.local/bin/godot --path game -- --scenario=tour --shots=/abs/dir  # quick render check
~/.local/bin/godot --path game -- --load=quicksave                  # start from a save slot (user://saves/<slot>.zip)
~/.local/bin/godot -e --path game                                 # editor
```

On the main dev machine Godot is `~/.local/bin/godot`, which is not on PATH. `global.json` pins the .NET 8 SDK because the installed
.NET 10 SDK has a broken workload manifest (`dotnet workload repair` would fix it system-wide).

## Conventions

- Language priority: C# first; GDShader for rendering; C++ (GDExtension) only for profiled hot paths.
- Game logic goes in Core with tests; presentation code only mirrors Core state. Tune gameplay in `game/data/*.json`
  (comments allowed); `ContentDatabase.Validate` checks cross-references and runs at startup and in tests.
- Systems announce what happens as typed records on `Simulation.Events` (`Events/GameEvents.cs`); notifications and
  `Statistics` subscribe there rather than being called from the systems.
- Coordinates: Core uses `System.Numerics.Vector2` on the ground plane (X = east, Y = Godot Z = south).
  Machine local space = glTF model space: **+Z forward, +X left**, origin at the non-steered axle.
  Heading θ ⇒ forward (sin θ, cos θ) = Godot `rotation.y`.
- Field cells are 0.5 m, chunks 32 m; roads/tracks/paths/streams share a **16 m tile grid** (`MapDef.TileSize`).
- Calendar is compressed (3 days/month); agronomy scales by real days per game day so crops keep real months.
- Godot C#: one Node class per file (file name = class name), parameterless constructors with `init` properties
  (`new TerrainRenderer { Sim = sim }`), `FileAccess` means Godot's (global alias in `scripts/GlobalUsings.cs`).
- Input actions use **physical keys** (AZERTY gets ZQSD automatically); labels come from `InputSetup.Label`.
  Player overrides and other preferences live in `user://settings.cfg` (`Common/UserSettings.cs`); a new
  setting gets a default, validation on load, and a README line.
- UI: build controls with `UI/Widgets`, style them through `ui/theme.tres` type variations (no per-control theme
  overrides), BBCode colors from `UI/Palette`, icons as `Widgets.Icon` (Material Symbols SVGs in `assets/icons`,
  white so they can be tinted). Screens derive from `Screen` and go on the `ScreenStack` (Esc closes the top one).
- Art direction: realistic proportions, low poly, muted/desaturated (Project Zomboid-like), no toy look.
  Machines are procedural placeholders until `.glb` models (Blockbench exports) are set in a machine's
  `visual.model`; conventions and part roles are in `docs/MODELING.md`.
- Versioning: `config/version` in `game/project.godot` is the game version (semver). **Every release bumps it** in the
  commit that gets tagged `v<version>` (e.g. `0.2.0` → `v0.2.0`); `build.yml` fails a tag that doesn't match it.
- Licenses: code GPL-3.0-or-later (`LICENSE`), original art CC BY-SA 4.0 (`LICENSE-ASSETS.md`).
  Every third-party asset goes in `game/assets/CREDITS.md` with its license.

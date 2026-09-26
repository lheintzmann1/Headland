# Headland

A top-down farming simulation: the machines and field work of *Farming Simulator*, with the systemic depth of
*Dwarf Fortress* and *Cataclysm: DDA* (soils, weather, crop agronomy), seen through an isometric camera.
Built with **Godot 4.7** and **C#**.

> **Status:** early prototype. The core farming loop is playable; machines, buildings and the farmer are
> procedural placeholder models for now.

![Harvesting wheat](docs/screenshots/harvest.jpg)

| | |
|---|---|
| ![Unloading the combine into a trailer](docs/screenshots/unloading.jpg) | ![A field helper turning in the headland with its cultivator raised](docs/screenshots/helper-headland-turn.jpg) |
| ![The farmyard](docs/screenshots/farmyard.jpg) | ![Flowering canola and the cell inspector](docs/screenshots/canola-inspect.jpg) |

## What's in it

- **The farming loop.** Cultivate stubble, sow in season, let the crop grow, harvest it with the right header,
  unload the combine into a trailer and tip the grain at the elevator.
- **Machines.** Tractors, a combine with swappable grain and corn headers, a tipping trailer, a cultivator and a
  trailed seed drill. Steering is kinematic, trailers articulate, mounted implements lift on the three-point hitch,
  and working speed depends on the implement, the engine's power, the load and soft ground.
- **Places to trade and service.** Tip grain at the elevator, or at the flour mill, which pays more but only takes
  what it can mill. Keep grain in the farm silo and load it back into a trailer later, buy seed at the farm shop,
  refuel and wash at the gas station, and get machines repaired at the workshop. Prices follow the season, drop as
  you flood a buyer and recover over time, and now and then a buyer pays more for a few days. Each place is a point
  of interest defined in JSON: areas where machines unload, load, fill up or park, what happens there, opening
  hours, storage and production that runs by the hour.
- **Finances.** Every sale and purchase goes into the farm's books under its category: sales, purchases, fuel,
  maintenance, production costs, wages, loan interest. F2 shows them day by day or month by month, and is where the
  farm borrows from the bank: $5,000 at a time up to $500,000, at 5% a year charged every day. Interest, wages and
  running costs can overdraw the account; until the balance is back above zero, nothing can be bought and no helper
  hired.
- **Field helpers.** Press H and a helper works the field lane by lane. It turns on the headland in tight arcs and
  lifts the implement whenever it leaves the field. Helpers earn $150 per hour of work, whatever the clock speed.
- **Soils and crops.** Every 0.5 m cell tracks soil type, moisture, nitrogen, crop stage and health. Crops grow by
  growing degree-days; winter wheat and canola need a winter (vernalization) before they shoot; drought,
  waterlogging, frost and nitrogen shortage cost health and yield. On a compressed calendar (three days per month)
  crops still ripen in their real months.
- **Weather.** A seeded climate model with a three-day forecast, rain, storms, snow, fog, wet ground, snow cover,
  drifting cloud shadows, and a sun that follows the time of day and the season.
- **Inspect anything.** Hover the ground to read its soil, moisture, nitrogen, crop stage, vernalization progress
  and a harvest estimate.
- **Saves.** F5 quicksaves, F8 quickloads, and the game autosaves every 10 minutes. A save is a zip in Godot's
  user data folder (`saves/`): readable JSON for everything on the map, plus the compressed field layers.
- **Data-driven.** Crops, machines, buildings and other points of interest, soils, the climate, the map and the money
  rules are JSON files in [`game/data`](game/data).

## Controls

Keys follow their position on the keyboard, so AZERTY players get ZQSD for movement. Press F1 in game for the full list;
Esc closes screens. Keys can be changed in the settings file (see below).

| Key | Action |
|---|---|
| W A S D | Walk, or drive the vehicle you are in |
| F | Enter or leave a vehicle |
| Tab / Shift+Tab | Switch to the next or previous vehicle |
| G | Attach or detach an implement |
| V | Lower or raise implements |
| B | Turn on or off (seed drill, combine) |
| U | Unfold the combine's pipe, or tip a trailer into an unloading area |
| X | Change the seed |
| R | Use the POI you are parked at: buy supplies, load a trailer from storage, refuel, repair, wash |
| H | Hire or dismiss a field helper |
| F2 | Finances and loans |
| Q / E | Rotate the camera |
| Mouse wheel, middle drag | Zoom, pan |
| 1 to 6, P | Time speed (×1 to ×240), pause |
| F9 | Sleep until the next morning |
| F5 / F8 | Quicksave, quickload |

## Settings

Until the settings screen exists, edit `settings.cfg` in Godot's user data folder (on Linux
`~/.local/share/godot/app_userdata/Headland/`). The game writes it with the defaults on first launch and reads it at
startup; a missing or invalid value falls back to its default.

| Section | Keys |
|---|---|
| `[graphics]` | `window_mode` (windowed, maximized, fullscreen, exclusive_fullscreen), `resolution`, `vsync`, `max_fps` (0 = no cap), `render_scale` (0.5–1, FSR below 1), `antialiasing` (off, fxaa, msaa2, msaa4), `shadows` (off, low, medium, high) |
| `[audio]` | `master`, `music`, `vehicles`, `environment`, `ui`: volumes from 0 to 1 |
| `[controls]` | One key per action, named as on a US QWERTY keyboard (`"W"`, `"Shift+Tab"`): the position counts, not the letter |
| `[gameplay]` | `autosave_minutes` (0 = off) |

## Running from source

You need the **Godot 4.7.2 .NET** editor and the **.NET 8 SDK**.

```sh
git clone https://github.com/lheintzmann1/Headland.git
cd Headland
dotnet build Headland.sln
godot --path game            # or open game/project.godot in the editor and press Play
```

Run the tests with `dotnet test tests/Headland.Core.Tests`. They include content validation and multi-year crop
calibration runs.

Linux, Windows and macOS builds from every push to `main` are available as artifacts of the
[Build game](../../actions/workflows/build.yml) workflow. Tags named `v*` (matching `config/version` in
`game/project.godot`) publish them as a release.

The macOS build is a universal app (Intel and Apple Silicon) that is not notarized, so macOS blocks its first launch:
allow it under System Settings → Privacy & Security → Open Anyway, or run `xattr -dr com.apple.quarantine Headland.app`.

## Project layout

| Path | Contents |
|---|---|
| `src/Headland.Core` | The simulation in plain C#, with no Godot dependency: time, weather, world, crops, machines, economy. |
| `tests/Headland.Core.Tests` | xUnit tests, including agronomy calibration and field-helper coverage. |
| `game` | The Godot project: C# presentation scripts, shaders, JSON content and assets. |
| `tools` | Python asset pipeline: isometric tile conversion, ground textures, procedural crop cards. |
| `.github/workflows` | CI (build and tests) and game exports. |

The scripted scenario `godot --path game -- --scenario=loop --shots=<dir>` plays the whole loop with helpers and
saves a screenshot at each step.

## Models

Headland's own models are made in [Blockbench](https://www.blockbench.net), low poly and in one style. The game loads
any glTF (`.glb`) model, so mods (modding support is planned) can use Blender, 3ds Max or any other tool, at any level
of detail. [`docs/MODELING.md`](docs/MODELING.md) covers the conventions (scale, orientation, moving parts) and how to
hook a model up to a machine.

## Credits

- Road, track and stream tiles: [Screaming Brain Studios](https://opengameart.org/content/700-isometric-road-tiles),
  CC0, converted from isometric to top-down by `tools/convert_tiles.py`.
- Ground textures: [ambientCG](https://ambientcg.com), CC0.
- Crop cards are generated by `tools/gen_crop_cards.py`.
- Font: [Barlow](https://github.com/jpt/barlow) by Jeremy Tribby, SIL Open Font License.
- Icons: [Material Symbols](https://github.com/google/material-design-icons) by Google, Apache License 2.0.

See [`game/assets/CREDITS.md`](game/assets/CREDITS.md) for the full list.

## License

- **Code** (C#, shaders, tools, JSON game data): [GNU GPL v3.0 or later](LICENSE).
- **Original art**: [CC BY-SA 4.0](LICENSE-ASSETS).
- **Third-party assets** keep their own licenses, see [`game/assets/CREDITS.md`](game/assets/CREDITS.md).

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
- **Field helpers.** Press H and a helper works the field lane by lane. It turns on the headland in tight arcs and
  lifts the implement whenever it leaves the field.
- **Soils and crops.** Every 0.5 m cell tracks soil type, moisture, nitrogen, crop stage and health. Crops grow by
  growing degree-days; winter wheat and canola need a winter (vernalization) before they shoot; drought,
  waterlogging, frost and nitrogen shortage cost health and yield. On a compressed calendar (three days per month)
  crops still ripen in their real months.
- **Weather.** A seeded climate model with a three-day forecast, rain, storms, snow, fog, wet ground, snow cover,
  drifting cloud shadows, and a sun that follows the time of day and the season.
- **Inspect anything.** Hover the ground to read its soil, moisture, nitrogen, crop stage, vernalization progress
  and a harvest estimate.
- **Data-driven.** Crops, machines, soils, the climate and the map are JSON files in [`game/data`](game/data).

## Controls

Keys follow their position on the keyboard, so AZERTY players get ZQSD for movement. Press F1 in game for the full list.

| Key | Action |
|---|---|
| W A S D | Walk, or drive the vehicle you are in |
| F | Enter or leave a vehicle |
| Tab / Shift+Tab | Switch to the next or previous vehicle |
| G | Attach or detach an implement |
| V | Lower or raise implements |
| B | Turn on or off (seed drill, combine) |
| U | Unfold the combine's pipe, or tip a trailer at a sell point |
| X | Change the seed |
| R | Buy supplies at a shop |
| H | Hire or dismiss a field helper |
| Q / E | Rotate the camera |
| Mouse wheel, middle drag | Zoom, pan |
| 1 to 6, P | Time speed (×1 to ×240), pause |
| F9 | Sleep until the next morning |

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

See [`game/assets/CREDITS.md`](game/assets/CREDITS.md) for the full list.

## License

- **Code** (C#, shaders, tools, JSON game data): [GNU GPL v3.0 or later](LICENSE). You can play, study, modify and
  share it, mods included; anything you distribute that is built on it must stay open under the same license.
- **Original art**: [CC BY-SA 4.0](LICENSE-ASSETS.md). Credit Headland and share adaptations alike.
- **Third-party assets** keep their own licenses, listed in [`game/assets/CREDITS.md`](game/assets/CREDITS.md)
  (currently all CC0).

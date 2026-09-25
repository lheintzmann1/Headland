# Making models for Headland

Machines, buildings and the farmer are procedural placeholders until real models replace them. Models are made in
[Blockbench](https://www.blockbench.net) (free, and easy to pick up) and exported as **glTF binary (`.glb`)**.

Why glTF: it keeps Blockbench's groups as named parts with their pivots, so wheels can turn and pipes can swing, and
it embeds the textures. OBJ flattens everything into one mesh (nothing could move), and Blockbench's own `.bbmodel`
format would need a custom importer. Keep the `.bbmodel` next to the export as the editable source.

## Style

Realistic proportions, low polycount, muted colors, like *Project Zomboid*. Avoid a toy look. Keep vehicles under
about 10,000 triangles and textures at 512 px or less.

## Setting up the model

1. Create a **Generic Model** project. It allows free rotations and has no Minecraft restrictions.
2. **Scale:** one block (16 pixels) is one meter. A tractor is about 4.7 m long, so about 75 pixels. If the model
   arrives in the game 16 times too big or too small, set `"scale"` in its JSON (for example `0.0625` or `16`).
3. **Orientation:** the front of the machine faces **South** (+Z in Blockbench). If yours faces North, set
   `"yawDeg": 180` in the machine's JSON instead of rebuilding it.
4. **Origin:** put the model's origin on the ground at the machine's reference point, which is the same one its
   JSON uses:
   - tractors, trailers and trailed implements: the center of the rear (non-steered) axle,
   - combines: the center of the front axle,
   - mounted implements and headers: the hitch point.

   If that is inconvenient, use `"offset": [x, y, z]` (meters; +Z forward, +X left).
5. **Moving parts:** put each one in its own group, with the group's pivot where it rotates:

   | Role | Pivot | Modeled as |
   |---|---|---|
   | `wheel0`, `wheel1`, … | wheel center | one group per wheel, in the same order as `wheels` in the JSON |
   | `pipe` | base hinge of the combine's unloading pipe | folded backward; it swings 90° out to the left |
   | `tipper` | rear hinge of a trailer's bed | the bed; it tilts its front up by 42° |
   | `reel` | reel axle of a header | spins while the combine is threshing |
   | `load` | bottom of the load | the grain heap at full height; it is scaled with the fill level |

## Exporting and hooking it up

1. **File → Export → Export glTF Model**, as `.glb`, into `game/assets/models/<category>/`, for example
   `game/assets/models/tractors/fieldmaster_125.glb`. Save the `.bbmodel` beside it.
2. In the machine's JSON (`game/data/machines/*.json`), point `visual` at it and name its moving parts:

   ```json
   "visual": {
     "placeholder": "tractor",
     "color": "#7b2f25",
     "model": "res://assets/models/tractors/fieldmaster_125.glb",
     "nodes": { "wheel0": "wheel_rl", "wheel1": "wheel_rr", "wheel2": "wheel_fl", "wheel3": "wheel_fr" }
   }
   ```

   Optional: `"scale"` (default 1), `"yawDeg"` (default 0), `"offset"` (default `[0, 0, 0]`).
3. Import it: open the project in the Godot editor once, or run `godot --headless --path game --import`.
4. Run the game. The console lists the parts it found, for example
   `tractor_125: model res://…/fieldmaster_125.glb (parts: wheel0, wheel1, wheel2, wheel3)`, and warns about any
   node name it could not find.

The machine's size, wheel positions and hitch points still come from its JSON, so check that they match the model.

## License and credit

Models contributed to this repository are licensed CC BY-SA 4.0 (see [`LICENSE-ASSETS.md`](../LICENSE-ASSETS.md)).
Add a line for each one to [`game/assets/CREDITS.md`](../game/assets/CREDITS.md). If you adapt someone else's model,
make sure its license allows it and credit it there.

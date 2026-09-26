# Making models for Headland

Machines, buildings and the farmer are procedural placeholders until real models replace them. The game loads models
as **glTF binary (`.glb`)**: glTF keeps named parts with their pivots, so wheels can turn and pipes can swing, and it
embeds the textures.

Headland's own models are made in [Blockbench](https://www.blockbench.net) (free, and easy to pick up). Mods (modding
support is planned) can use any tool that exports glTF, such as Blender or 3ds Max, at any level of detail. The
conventions below are the same whatever the tool.

## Style

Headland's own models have realistic proportions, a low polycount and muted colors, like *Project Zomboid*, without a
toy look: vehicles stay under about 10,000 triangles, with textures at 512 px or less. Mods are free to go further.

## Setting up the model

In Blockbench, start a **Generic Model** project: it allows free rotations and has no Minecraft restrictions.

1. **Scale:** one unit is one meter. In Blockbench that is one block (16 pixels), so a 4.7 m tractor is about
   75 pixels long. If the model arrives in the game 16 times too big or too small, set `"scale"` in its JSON (for
   example `0.0625` or `16`).
2. **Orientation:** Y is up and the front of the machine faces **+Z** (South in Blockbench). If yours faces the other
   way, set `"yawDeg": 180` in the machine's JSON instead of rebuilding it.
3. **Origin:** put the model's origin on the ground at the machine's reference point, which is the same one its JSON
   uses:
   - tractors, trailers and trailed implements: the center of the rear (non-steered) axle,
   - combines: the center of the front axle,
   - mounted implements and headers: the hitch point.

   If that is inconvenient, use `"offset": [x, y, z]` (meters; +Z forward, +X left).
4. **Moving parts:** make each one a separate node (a group in Blockbench, an object in Blender) with its pivot where
   it rotates:

   | Role | Pivot | Modeled as |
   |---|---|---|
   | `wheel0`, `wheel1`, … | wheel center | one node per wheel, in the same order as `wheels` in the JSON |
   | `pipe` | base hinge of the combine's unloading pipe | folded backward; it swings 90° out to the left |
   | `tipper` | rear hinge of a trailer's bed | the bed; it tilts its front up by 42° |
   | `reel` | reel axle of a header | spins while the combine is threshing |
   | `load` | bottom of the load | the grain heap at full height; it is scaled with the fill level |

## Exporting and hooking it up

1. Export the model as `.glb` into `game/assets/models/<category>/`, for example
   `game/assets/models/tractors/fieldmaster_125.glb`. In Blockbench, use **File → Export → Export glTF Model** and save
   the `.bbmodel` beside the export as the editable source.
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

## Buildings and other POIs

Buildings and sites (points of interest, `game/data/pois/*.json`) follow the same scale and orientation: the front
faces **+Z** and the origin sits on the ground at the center of the POI's footprint (`w` × `d` in its JSON). Export to
`game/assets/models/buildings/` and set `"visual": { "model": "res://assets/models/buildings/<name>.glb" }` on the POI
(with `scale`, `yawDeg` and `offset` as for machines). The model replaces the placeholder `parts`, which still decide
what machines and the farmer bump into, so keep them roughly matching the walls.

## License and credit

Models contributed to this repository are licensed CC BY-SA 4.0 (see [`LICENSE-ASSETS.md`](../LICENSE-ASSETS.md)).
Add a line for each one to [`game/assets/CREDITS.md`](../game/assets/CREDITS.md). If you adapt someone else's model,
make sure its license allows it and credit it there.

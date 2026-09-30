# Making models for Headland

The game loads models as **glTF binary (`.glb`)**: glTF keeps named parts with their pivots, so wheels can turn and
pipes can swing, and it embeds the textures. Machines, buildings and the farmer are all models.

The models in the game today are simple bases made of boxes, generated from the shapes the game used to draw
procedurally (the plow, spreader, sprayer and mower, which came later, in the same way), laid out and named as this
guide says: machines in `game/assets/models/<category>/<machine id>.glb` (with every option in
them), buildings in `buildings/<poi id>.glb`, the farmer in `characters/farmer.glb`. Each has a Blockbench project
(`.bbmodel`) of the same model beside it. Start a real model from either one, keeping the names of its parts. New
bases come from `tools/box_model.py`, which builds both from a JSON tree of named nodes and boxes.

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
3. **Root:** put the whole machine under one node named `root` (a group in Blockbench, an empty in Blender). The game
   shows only what `root` holds, so the file can keep reference objects, cameras or lights beside it. A model without
   `root` doesn't load (see [Exporting and hooking it up](#exporting-and-hooking-it-up)).
4. **Origin:** put `root`'s origin (its pivot in Blockbench) on the ground at the machine's reference point, which is
   the same one its JSON uses. The game puts `root` there, whatever its position in the file, and keeps its rotation
   and scale. The reference point is:
   - tractors, trailers and trailed implements: the center of the rear (fixed) axle, or the middle of the fixed axles,
   - combines: the center of the front axle,
   - mounted implements and headers: the hitch point.

   If that is inconvenient, use `"offset": [x, y, z]` (meters; +Z forward, +X left).
5. **Parts:** lay the model out and name its parts as below, so that the game finds them without any mapping in the
   JSON.

## Layout and names

The game finds each part of a model by its name: moving parts are named after their role, and the pieces an option
adds after that option. Here is the Fieldmaster 125 (`tractor_125`) with every option it can have:

```text
root                              origin on the ground, at the center of the rear axle
├── body                          what is always there, under any names
│   ├── cab
│   └── hood
├── runningGear
│   ├── wheel0L                   rear left wheel: rolls
│   │   └── wheels_dual_0L        its outer tire, with duals only: rolls with it
│   ├── wheel0R
│   │   └── wheels_dual_0R
│   ├── wheel1L                   front left wheel: rolls and steers
│   │   └── wheels_dual_1L
│   ├── wheel1R
│   │   └── wheels_dual_1R
│   ├── track0L                   rear tracks: shown instead of wheel0L and wheel0R
│   └── track0R
├── steeringWheel
├── rearLinkage                   lower links of the rear three-point linkage: lift
├── frontLinkage                  the front one's, shown with the front linkage only
└── options
    ├── frontHitch
    │   ├── frontHitch_threePoint
    │   └── frontHitch_weight
    ├── frontLoader
    │   └── frontLoader_bracket
    └── beacons
        ├── beacons_left
        ├── beacons_right
        └── beacons_lightbar
```

- **Where a node sits** only matters for what moves together: a moving part carries whatever is under it (a dual's
  outer tire, a front frame's wheels, a crane's next joint, the load in a tipper's bed, a boom's folding wings). The
  game looks for each name anywhere under `root`, so the groups that only sort things (`body`, `runningGear`, `options`
  and a group per configuration in it) are a suggestion, not a requirement.
- **Every name is unique** in the file. Blender insists on it, and Godot renames the second of two nodes with the
  same name when it imports the model (`hood2`), after which the game doesn't find it.
- **Names use letters, digits and `_` only**, and case matters (`frontHitch`, not `fronthitch`). Godot turns `.` `:`
  `@` `/` `"` `%` into `_`, and reads endings such as `-col` or `-noimp` as import instructions.
- **Reserved names:** `root`, the roles of moving parts (see [Moving parts](#moving-parts)), and any name starting
  with a configuration's id and `_` (see [One model for every configuration](#one-model-for-every-configuration)).
  Everything else is free: name parts after what they are (`cab`, `hood`, `exhaust`).

## Moving parts

Make each moving part a node of its own, named after its role, with its pivot (origin) where it turns. The machine's
components (see [`COMPONENTS.md`](COMPONENTS.md)) decide which roles it has:

| Role | Component | Pivot | Modeled as |
|---|---|---|---|
| `wheel0L`, `wheel0R`, `wheel1L`, … | `runningGear` | wheel center | one node per side of each axle (`L` left, `R` right), axles numbered from 0 as in its `axles`, holding both tires of a dual; it rolls, and steers when the axle does |
| `track0L`, `track0R`, … | `runningGear` | middle of the track on the ground | an axle's track, instead of its wheels; not moved, but the links of its belt run round (see below) |
| `frontFrame` | `runningGear` with `articulation` | the hinge | the front frame with everything on it (its wheels too); it swings about Y, left when positive |
| `steeringWheel` | `drivable` | hub, its Y axis up the column | turns 270° either way at full lock, left counterclockwise |
| `rearLinkage`, … (a joint's `id` and `Linkage`) | `attacherJoints`, joints of a linkage type (`threePoint`) | anywhere | the linkage's lower links, lowered; they lift straight up with the implement they carry |
| `pipe` | `pipe` | base hinge of the unloading pipe | folded backward; it swings 90° out to the left, and the game pours the grain where it unloads |
| `tipper` | `tipper` | rear hinge of the bed | the bed; it tilts its front up by the tipper's `angleDeg` (42°), or as the side it tips to says, about that side's hinge |
| `reel` | `workAreas` (harvester) | reel axle of a header | spins while the combine is threshing |
| `load` | `fillUnits` | bottom of the load | the load at full height; it is scaled with the fill level |
| a part's `id` | `animatedParts` | where it hinges | in its rest pose (the working pose for parts that fold or lower) |
| a joint's `id` | `craneArm` | the joint's pivot | at the joint's value 0; `extend` joints slide along their own Z |
| `hook` | `winch` | the hook's eye | anywhere: it is moved to the end of the rope |
| `saw` | `saw` | the blade's center | spins about its own Y axis while turned on |

A crane's joints are nested: each joint's node is a child of the previous one's, placed as the `offset`s in the JSON
say, so that tools and ropes hang where the game expects them.

A track's belt is a node named after the track and `_belt` (`track0L_belt`) whose children are its links, each
modeled flat and centered on its origin. The game spaces them evenly round the belt, as long and as round as the
axle's wheel set says (`length`, `radius`), and runs them round as the machine drives.

A moving part that only some options give the machine is hidden without them. An axle's sides are `wheel…` roles, or
`track…` when that axle has tracks, so with rear tracks `wheel0L` and `wheel0R` are hidden, and without them `track0L`
and `track0R`; likewise a crane joint or a pipe that comes with an option. Nothing needs to be written in the JSON for
it.

## One model for every configuration

A machine's options (its `configurations`, see [`COMPONENTS.md`](COMPONENTS.md#configurations)) all come from the same
model: model everything any option adds, and put each option's pieces under a node named after it, its
configuration's id, `_` and its own id, as written in the JSON. That node and everything under it are hidden unless the
machine has that option; the rest of the model is always there.

```jsonc
{ "id": "frontHitch", "name": "Front hitch", "options": [
  { "id": "threePoint", "name": "Front linkage", "changes": { … } },  // frontHitch_threePoint
  { "id": "weight", "name": "Front weight" },                         // frontHitch_weight
  { "id": "none", "name": "None" }                                    // nothing to model
] }
```

- An option that adds nothing to see (none, an engine, a bigger tank) needs no node.
- An option can have its own version of a moving part, named after the option and the role:
  `wheels_rowCrop_wheel0L` holds the taller row-crop wheel, with its pivot at that wheel's center. With the option,
  it moves instead of `wheel0L`, which is hidden.
- A piece that has to sit somewhere else, under a moving part, is named after its option followed by `_` and anything:
  a dual's outer tire rolls with its wheel, so it is `wheels_dual_0L` under `wheel0L`. Any number of nodes can do
  that (`frontLoader_bracket_l`, `frontLoader_bracket_r`), and Blender's `.001` endings, which import as `_001`, count
  too.
- Pieces that several options share are listed in `show`: both beacons are the left one and the right one, so
  `{ "id": "both", "name": "Both sides", "show": ["beacons_left", "beacons_right"] }`. A node is shown when any chosen
  option has it.
- A name starting with a configuration's id and `_` belongs to its options: the console warns about one that matches
  none of them, such as `frontHitch_wieght`.
- Color options change the machine's `visual.color`, which its paint takes (see [Materials](#materials)).

## Materials

Materials keep their colors and textures, except two kinds the game colors, matched by name:

- **`paint`** takes the machine's color (its `visual.color`, which color options change), multiplied by the
  material's own color: white gives the paint as it is, grey a darker shade of it. Shades are named `paint_` and
  anything (`paint_dark`); a texture on them is tinted too, so white areas show the paint and darker ones dirt or
  shadow.
- **`fill`** (and `fill_…`), on the load, takes the color of what the machine holds (wheat, canola…), multiplied the
  same way.

An ending after a `.` counts too (`paint.png`, Blender's `paint.001`). Don't give a node the name of a material: Godot
then renames the material when it imports the model (`reel2`).

## Exporting and hooking it up

1. Export the model as `.glb` into `game/assets/models/<category>/`, for example
   `game/assets/models/tractors/tractor_125.glb`. In Blockbench, use **File → Export → Export glTF Model** and save
   the `.bbmodel` beside the export as the editable source. If the exporter offers to turn groups into an armature or
   bones, leave that off: the game looks for nodes.
2. In the machine's JSON (`game/data/machines/*.json`), point `visual` at it:

   ```json
   "visual": {
     "color": "#7b2f25",
     "model": "res://assets/models/tractors/tractor_125.glb"
   }
   ```

   `color` is the paint (see [Materials](#materials)). Optional: `"scale"` (default 1), `"yawDeg"` (default 0), `"offset"` (default `[0, 0, 0]`), and `"nodes"` for a model
   whose moving parts aren't named after their roles, such as one made for something else:
   `"nodes": { "wheel0L": "wheel_rl", "wheel0R": "wheel_rr" }` (role → node name; an option can change them in its
   `changes`).
3. Import it: open the project in the Godot editor once, or run `godot --headless --path game --import`.
4. Run the game. The console lists the parts it found and the roles it found no node for, for example for a trailer
   whose load isn't modeled yet:
   `trailer_16: model res://…/trailer_16.glb (parts: wheel0L, wheel0R, wheel1L, wheel1R, tipper; not in the model: load)`.
   It warns about the names in `nodes` and `show` it could not find, and about names that look like an option's but
   match none.

A machine without a model that loads (it has none, the file is missing or wasn't imported, or it has no `root`
node), as it comes or with any of its options, is left out of the game: the console says why, and what went with it (its places on the map,
the contract lease sets it's in). Nothing stands in for it.

The machine's size, wheel positions, hitch points and crane joints still come from its JSON, so check that they
match the model.

## Buildings and other POIs

Buildings and sites (points of interest, `game/data/pois/*.json`) follow the same scale, orientation and `root` node:
the front faces **+Z** and `root`'s origin sits on the ground at the center of the POI's footprint (`w` × `d` in its
JSON). Export to `game/assets/models/buildings/` and set
`"visual": { "model": "res://assets/models/buildings/<poi id>.glb" }` on the POI (with `scale`, `yawDeg` and `offset`
as for machines). What machines and the farmer bump into is not the model but the POI's `colliders` (boxes, or circles
for round buildings such as silos), so keep them matching the walls. A POI whose model doesn't load still works, but
nothing is drawn for it.

A POI's moving parts (a door, a gate) are nodes named after their role, as on machines: its `animatedParts` give the
roles (the parts' ids), and `visual.nodes` maps a role to a node named otherwise. Its lamps are placed by its JSON.

## The farmer

The farmer follows the same scale, orientation and `root` node: `root`'s origin on the ground between the feet, the
front facing **+Z**, about 1.9 m tall. The legs are `legL` and `legR`, each with its pivot at the hip: they swing
forward and back (about X) as the farmer walks, and the rest of the model turns the way the farmer goes. The model is
`player.model` in `game/data/game.json`.

## License and credit

Models contributed to this repository are licensed CC BY-SA 4.0 (see [`LICENSE-ASSETS.md`](../LICENSE-ASSETS.md)).
Add a line for each one to [`game/assets/CREDITS.md`](../game/assets/CREDITS.md). If you adapt someone else's model,
make sure its license allows it and credit it there.

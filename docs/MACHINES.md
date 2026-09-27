# Defining machines

A machine is a JSON object in `game/data/machines/*.json`: what it is (name, price, mass), its size, how it looks,
and the **components** it is built from. A component is one working part: wheels, an engine, a seat, a hitch, a grain
tank, a tipping bed, a crane. The game has no fixed kinds of machines: a tractor is running gear, a motor, a seat,
attacher joints and a fuel tank; a trailer is running gear, an attachable, a fill unit and a tipper. New machines,
including those of mods, are built by combining components in the same way.

The game checks every machine when it starts and names the machine and component at fault. Settings left out take
the defaults listed below.

## Space

Positions are in meters, in the machine's own space: **+z forward, +x left, +y up**, with the origin on the ground:

- vehicles: the center of the fixed (non-steered) axle, so the rear axle of a tractor and the front one of a combine,
- trailed implements: the center of their axle (their drawbar eye is ahead of it),
- mounted implements and headers: the hitch point.

## The machine

```jsonc
{
  "id": "tractor_125", "name": "Fieldmaster 125", "category": "tractor", "brand": "Fieldmaster",
  "price": 98000, "mass": 5600,
  // The box it takes up: what it bumps into, and where it can be driven through.
  "size": { "length": 4.7, "width": 2.45, "height": 3.0, "centerZ": 1.25 },
  "components": {
    "runningGear": { "maxSteerDeg": 40, "steerRateDeg": 80, "wheels": [
      { "x":  0.92, "z": 0,    "radius": 0.8,  "width": 0.55 },
      { "x": -0.92, "z": 0,    "radius": 0.8,  "width": 0.55 },
      { "x":  0.88, "z": 2.65, "radius": 0.55, "width": 0.42, "steer": true },
      { "x": -0.88, "z": 2.65, "radius": 0.55, "width": 0.42, "steer": true } ] },
    "motor": { "powerHp": 125, "maxSpeedKmh": 40, "maxReverseKmh": 16, "acceleration": 2.2, "braking": 6, "fuelUnit": "fuel" },
    "drivable": {},
    "attacherJoints": { "joints": [ { "id": "rear", "type": "threePoint", "x": 0, "z": -1.2, "y": 0.55 } ] },
    "fillUnits": { "units": [ { "id": "fuel", "capacity": 250, "fillTypes": ["diesel"], "startFillType": "diesel", "startLevel": 250 } ] },
    "lights": { "lamps": [ { "type": "head", "x": 0.735, "y": 2.2, "z": 3.6 }, { "type": "head", "x": -0.735, "y": 2.2, "z": 3.6 } ] }
  },
  "visual": { "placeholder": "tractor", "color": "#7b2f25" },
  "description": "Mid-size utility tractor."
}
```

`visual` picks the procedural placeholder (`tractor`, `combine`, `trailer`, `cultivator`, `seeder`, `header`,
`cornheader`, anything else is a box) or a glTF model with its moving parts: see [`MODELING.md`](MODELING.md).

## Components

| Kind | What it is |
|---|---|
| `runningGear` | Wheels, and how the steered ones turn. |
| `motor` | An engine: the machine drives itself. |
| `drivable` | A seat: the farmer or a helper drives it. |
| `attacherJoints` | Where implements hitch: three-point linkages, drawbars, a feeder house. |
| `frontLoaderBracket` | Consoles for a front loader arm, with its joint. |
| `attachable` | Hitches to a joint of its type, mounted or trailed; lowered and raised. |
| `fillUnits` | Tanks and bins: fuel, seed, a grain tank, a trailer's bed. |
| `animatedParts` | Parts that move between two poses, and folding for transport. |
| `workAreas` | Where it works the ground: cultivating, sowing, cutting a crop. |
| `thresher` | A combine's threshing drum, filling a tank with what its header cuts. |
| `pipe` | An unloading pipe. |
| `tipper` | A tipping bed. |
| `lights` | Headlights, work lights and beacons. |
| `craneArm` | A chain of joints: a forestry crane, a loader's boom. |
| `winch` | A rope with a hook. |
| `saw` | A saw blade. |

The turn-on key switches every `workAreas` with an area that `requiresOn`, `thresher` and `saw` in the vehicle's
chain; the lower key lowers every lowerable `attachable`.

### runningGear

| Setting | Default | |
|---|---|---|
| `wheels` | none | Each `x`, `z`, `radius` (0.5), `width` (0.4), and `steer` (false). |
| `maxSteerDeg` | 38 | Angle of the steered wheels at full lock. |
| `steerRateDeg` | 90 | How fast they turn, in degrees per second. |

When some wheels steer, the fixed ones sit on the origin's axle (z = 0) and the steered ones all on one side of it:
their distance from it is the wheelbase. Steered wheels behind the fixed ones steer like a combine's.

### motor

Needs a `runningGear` with steered wheels.

| Setting | Default | |
|---|---|---|
| `powerHp` | 100 | Working implements slow it down when they ask for more. |
| `maxSpeedKmh`, `maxReverseKmh` | 40, 15 | |
| `acceleration`, `braking` | 2.5, 6 | m/s². |
| `fuelUnit` | none | The fill unit holding its fuel, filled up at refuel stations. |

### drivable

No settings. Needs a `motor`.

### attacherJoints

`joints`: each has an `id`, a `type` (`threePoint`, `drawbar`, `header` or `frontLoader`) and its position `x`, `z`,
`y` (0.6). Joint ids are unique on the machine.

### frontLoaderBracket

Adds a joint of type `frontLoader`, where a loader arm's pivots are.

| Setting | Default | |
|---|---|---|
| `joint` | `frontLoader` | Its id. |
| `x`, `z`, `y` | 0, 1.5, 1.2 | The middle between the pivots. |
| `width` | 1.8 | From one console to the other. |

### attachable

| Setting | Default | |
|---|---|---|
| `type` | `threePoint` | The type of joint it hitches to. |
| `mode` | `mounted` | `mounted`: carried rigidly. `trailed`: pulled by its drawbar eye, following the hitch. |
| `x`, `z` | 0, 0 | The hitch point (mounted) or drawbar eye (trailed, with z > 0). |
| `maxArticulationDeg` | 80 | Trailed: how far it swings from the vehicle's heading. |
| `lowerable` | false | Lowered and raised with the lower key. Its work areas only work lowered. |
| `lift` | 0.45 | Mounted: how high the linkage lifts it when raised. |

### fillUnits

`units`: each has an `id` (unique on the machine), a `capacity`, the `fillTypes` it takes, and optionally a
`startFillType` and `startLevel`. A unit holds one fill type at a time.

### animatedParts

| Setting | Default | |
|---|---|---|
| `parts` | none | Each has an `id` (the model node role it moves), its moved pose from its rest pose, `rotationDeg` [x, y, z] and `offset` [x, y, z], the `seconds` it takes, and `fold`. |
| `startFolded` | false | Comes folded. |

Parts with `fold: true` move together when the machine folds for transport; their rest pose is the working one. A
folded machine (or one still unfolding) doesn't work and can't go down: lowering it unfolds it first, and a helper
unfolds it too.

### workAreas

`areas`: each is a rectangle `width` × `length` centered on `x`, `z`, with:

| Setting | Default | |
|---|---|---|
| `type` | `cultivator` | `cultivator` (stubble, grass or a failed crop into a seedbed), `seeder` (sows the selected crop), `harvester` (cuts ripe crops for the thresher it hangs on). |
| `maxWorkSpeedKmh` | 12 | |
| `requiredPowerHp` | 60 | |
| `requiresOn` | false | Only works turned on. |
| `fillUnit` | none | Seeder: the unit its seed comes from. |
| `harvestGroups` | none | Harvester: the crops' harvest groups it cuts (`grain`, `corn`). |

A harvester needs an `attachable`: it works while the vehicle it hangs on threshes.

### thresher

`fillUnit` (`tank`): where the crop goes. Turned on with the turn-on key.

### pipe

| Setting | Default | |
|---|---|---|
| `fillUnit` | `tank` | The unit it empties. |
| `x`, `z` | 4, 1 | Its outlet: over a trailer's bed, or a POI's unloading area. |
| `ratePerSecond` | 150 | |

### tipper

| Setting | Default | |
|---|---|---|
| `fillUnit` | `main` | The unit it tips. |
| `ratePerSecond` | 400 | |
| `angleDeg` | 42 | How far the bed tilts up. |

### lights

`lamps`: each has a `type` (`head`, `workFront`, `workRear` or `beacon`), a position `x`, `y` (1.5), `z`, where it
points (`pitchDeg` -18, down when negative; `yawDeg` 0, left when positive, 180 backward), and its beam: `range`
(30 m), `angleDeg` (32), `energy` (4) and `color` (`#fff0d1`). Headlights come on by themselves after dark.

### craneArm

`joints`, each moving the ones after it:

| Setting | Default | |
|---|---|---|
| `id` | | Unique on the machine; the model node role it moves. |
| `axis` | `pitch` | `yaw` turns about the vertical (positive to the left), `pitch` lifts (positive up), `extend` slides out along its own z. |
| `offset` | [0, 0, 0] | Its pivot: in the machine's space for the first joint, else in the previous joint's. |
| `min`, `max`, `rest` | -45, 45, 0 | Its travel, and where it starts: degrees, or meters for `extend`. |
| `speed` | 30 | Per second. |
| `length` | 0 | Placeholder only: the boom drawn from its pivot along z. |

### winch

| Setting | Default | |
|---|---|---|
| `joint` | none | The crane joint the rope leaves from; none: the machine itself. |
| `offset` | [0, 1, 0] | Where the rope leaves, in that joint's space. |
| `minLength`, `maxLength` | 0.5, 10 | |
| `speed` | 1 | Meters reeled per second. |

### saw

| Setting | Default | |
|---|---|---|
| `joint` | none | The crane joint carrying it; none: the machine itself. |
| `offset` | [0, 0.5, 0] | The blade's center, in that joint's space. |
| `diameter` | 0.75 | |
| `maxCut` | 0.6 | The thickest trunk it cuts. |

## Saves

A save keeps each machine's state under its components' kinds (the pipe unfolded, the level of each fill unit, the
crane's joints…). A machine whose definition gains a component gets it fresh; one that loses a component loses its
state.

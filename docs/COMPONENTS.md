# Entities and components

The things the game puts on the map are **entities**: machines (`game/data/machines/*.json`) and points of interest
(POIs, `game/data/pois/*.json`) today, later the farmer, pallets, bales and animals. Each is a JSON object with a few
settings of its own (its name, its size, how it looks) and the **components** it is built from. A component is one
working part: wheels, an engine, a seat, a hitch, a grain tank, a tipping bed, a crane. The game has no fixed kinds of
machines: a tractor is running gear, a motor, a seat, attacher joints and a fuel tank; a trailer is running gear, an
attachable, a fill unit and a tipper. New entities, including those of mods, are built by combining components in the
same way.

Each kind of component goes on the entities it makes sense for (see [Components](#components)). The game checks every
entity when it starts and names the entity and component at fault, a kind on something it doesn't go on included.
Settings left out take the defaults listed below.

## Space

Positions are in meters, in the entity's own space: **+z forward, +x left, +y up**, with the origin on the ground:

- vehicles: where they turn about, the center of the fixed axle (the middle of them with several), so the rear axle of
  a tractor and the front one of a combine, and the rear frame's axle of an articulated one,
- trailed implements: likewise the middle of their fixed axles (their drawbar eye is ahead of it),
- mounted implements and headers: the hitch point,
- POIs: the center of their footprint, their front (+z) being the side machines come from.

## Machines

```jsonc
{
  "id": "tractor_125", "name": "Fieldmaster 125", "category": "tractor", "brand": "Fieldmaster",
  "price": 98000, "mass": 5600,
  // The box it takes up: what it bumps into, and where it can be driven through.
  "size": { "length": 4.7, "width": 2.45, "height": 3.0, "centerZ": 1.25 },
  "components": {
    "runningGear": { "maxSteerDeg": 40, "steerRateDeg": 80, "axles": [
      { "z": 0,    "track": 1.84, "wheels": { "radius": 0.8,  "width": 0.55 } },
      { "z": 2.65, "track": 1.76, "steering": "front", "wheels": { "radius": 0.55, "width": 0.42 } } ] },
    "motor": { "powerHp": 125, "maxSpeedKmh": 40, "maxReverseKmh": 16, "acceleration": 2.2, "braking": 6, "fuelUnit": "fuel" },
    "drivable": {},
    "attacherJoints": { "joints": [ { "id": "rear", "type": "threePoint", "x": 0, "z": -1.2, "y": 0.55 } ] },
    "fillUnits": { "units": [ { "id": "fuel", "capacity": 250, "fillTypes": ["diesel"], "startFillType": "diesel", "startLevel": 250 } ] },
    "lights": { "lamps": [ { "type": "head", "x": 0.735, "y": 2.2, "z": 3.6 }, { "type": "head", "x": -0.735, "y": 2.2, "z": 3.6 } ] }
  },
  "visual": { "color": "#7b2f25", "model": "res://assets/models/tractors/tractor_125.glb" },
  "description": "Mid-size utility tractor."
}
```

`visual` names the machine's glTF `model`, whose parts the game finds by their names, and its paint `color`: see
[`MODELING.md`](MODELING.md). A machine without a model that loads is left out of the game. Its options are its
[configurations](#configurations).

## POIs

A POI is a building or site: a farmhouse, a silo, a shop, a grain elevator. Maps place them (see
[Placing POIs](#placing-pois)); a POI belongs to the farm the map gives it, or to an NPC. What it does comes from its
components, as FS builds placeables from specializations: a `sellingStation` buys loads, a `silo` stores them, a
`workshop` repairs machines (see [POI components](#poi-components)).

```jsonc
{
  "id": "farm_silo", "name": "Farm silo", "w": 22, "d": 20,
  "visual": { "model": "res://assets/models/buildings/farm_silo.glb" },
  "colliders": [
    { "x": -5, "z": -6, "w": 8, "d": 8, "round": true },
    { "x": 5, "z": -6, "w": 8, "d": 8, "round": true }
  ],
  "components": {
    "fillUnits": { "units": [
      { "id": "wheat", "capacity": 100000, "fillTypes": ["wheat"] },
      { "id": "barley", "capacity": 100000, "fillTypes": ["barley"] } ] },
    "silo": {
      "unloadTrigger": { "x": -5, "z": 4, "w": 10, "d": 9 },
      "loadTrigger": { "x": 6, "z": 4, "w": 9, "d": 9 } },
    "hotspots": { "spots": [ { "icon": "warehouse" } ] }
  }
}
```

| Setting | Default | |
|---|---|---|
| `id`, `name`, `description` | | Maps place it by its `id`. |
| `w`, `d` | 10, 10 | The ground it covers, centered on its origin: `w` along x, `d` along z. |
| `colliders` | none | What machines and the farmer bump into: boxes centered on `x`, `z`, `w` along x and `d` along z (4 × 4), turned by `rotDeg`; or circles `w` across with `round: true` (silos, tanks). |
| `visual` | | Its glTF `model`, as for machines (see [`MODELING.md`](MODELING.md#buildings-and-other-pois)). A POI whose model doesn't load still works, but nothing is drawn for it. |
| `components` | none | What it does and has: the [POI components](#poi-components), and those that go on anything, such as `fillUnits` (its storage), `hotspots` (its map icon), `lights` and `animatedParts`. |

Other settings are refused, so a POI written in the format from before components (with `triggers`, `storage` and
`actions`) is reported rather than loaded empty.

### Placing POIs

A map's `pois` place them: a unique `id` (saves refer to it), the POI `type`, where its origin goes (`x`, `z`), its
`headingDeg` (0 faces +z, south; 90 faces +x, east), its `farm` (0: an NPC's, the default; 1: the player's) and
optionally a `name` in place of the type's.

## Components

| Kind | Goes on | What it is |
|---|---|---|
| `runningGear` | machines | Axles and their wheels, and how they steer. |
| `motor` | machines | An engine: the machine drives itself. |
| `drivable` | machines | A seat: the farmer or a helper drives it. |
| `attacherJoints` | machines | Where implements hitch: three-point linkages, drawbars, a feeder house. |
| `frontLoaderBracket` | machines | Consoles for a front loader arm, with its joint. |
| `attachable` | machines | Hitches to a joint of its type, mounted or trailed; lowered and raised. |
| `fillUnits` | anything | Tanks and bins: fuel, seed, a grain tank, a trailer's bed, a silo's bins. |
| `animatedParts` | anything | Parts that move between two poses: folding for transport, a door opening as someone comes by. |
| `workAreas` | machines | Where it works the ground: tilling, sowing, fertilizing, spraying, cutting a crop, mowing. |
| `thresher` | machines | A combine's threshing drum, filling a tank with what its header cuts. |
| `pipe` | machines | An unloading pipe. |
| `tipper` | machines | A tipping bed. |
| `lights` | anything | Headlights, work lights and beacons, switched by the driver; a building's lamps, lit in the dark, on a timer or as someone comes by. |
| `craneArm` | machines | A chain of joints: a forestry crane, a loader's boom. |
| `winch` | machines | A rope with a hook. |
| `saw` | machines | A saw blade. |
| `wearable` | machines | Wear: its condition drops with use, and a worn machine does worse. |
| `hotspots` | anything | Icons on the map. |
| `sellingStation` | POIs | Buys the loads tipped or piped into its trigger. |
| `buyingStation` | POIs | Sells supplies and fuel to the machines parked in its trigger. |
| `silo` | POIs | Stores its owner's loads in the POI's fill units, and loads them back into trailers. |
| `productionPoint` | POIs | Turns goods into others every hour. |
| `workshop` | POIs | Repairs machines, and changes their options. |
| `washingStation` | POIs | Washes machines. |
| `deliverySpot` | POIs | Where new or leased machines appear. |

The turn-on key switches every `workAreas` with an area that `requiresOn`, `thresher` and `saw` in the vehicle's
chain; the lower key lowers every lowerable `attachable`.

Some components react to who is around, as FS triggers do: a lamp coming on, a door opening. Their `trigger` is an
area in the entity's space, centered on `x`, `z` (0, 0), `w` along x and `d` along z (10 × 10), and `by` says who it
reacts to: `anyone` (the default), the `farmer` (on foot or in a vehicle) or `machines` (driven or not, by their
footprint's center).

### runningGear

| Setting | Default | |
|---|---|---|
| `axles` | none | Front to back or in any order, as many as it has. |
| `maxSteerDeg` | 38 | Full lock: the angle of the steered axle farthest from the turning center, or of the hinge. |
| `steerRateDeg` | 90 | How fast the steering turns, in degrees per second. |
| `articulation` | none | `{ "z": … }`: articulated steering about a hinge there (see below). |
| `spinRateDeg` | 30 | Skid steer: how fast it turns on the spot at full lock, in degrees per second. |

Each axle:

| Setting | Default | |
|---|---|---|
| `z` | 0 | Where it is along the machine. |
| `track` | 1.8 | From the middle of the left wheels to the middle of the right ones. |
| `steering` | `fixed` | See below. |
| `wheels` | single | What runs on each side: see below. |

The machine turns about a point on its length, the **turning center**: the middle of its fixed axles. Its origin must
be there (z = 0). Every steered axle turns so that its wheels roll around the same point, the inner wheel of a turn
more than the outer one, and the farthest from the turning center at `maxSteerDeg` at full lock:

| `steering` | |
|---|---|
| `fixed` | Rolls straight. |
| `front` | Steers into the turn, ahead of the turning center: a tractor's front axle. |
| `rear` | Steers the other way, behind it: a combine's rear axle. Front and rear axles without a fixed one steer all four wheels, turning about the middle between them. |
| `allWheel` | Steers only in the all-wheel and crab steering modes, and rolls straight in normal steering. |
| `self` | Self-steering: turns freely to follow the path (a trailer's rear axle, a truck's tag axle), and locks straight when reversing, so that it then holds the machine like a fixed one. |

`wheels`:

| Setting | Default | |
|---|---|---|
| `type` | `single` | `single`: one tire. `dual`: a second tire outside the first. `rowCrop`: a narrow tire at high pressure, to run between rows. `flotation`: a wide tire at low pressure. `tracks`: a rubber track. |
| `radius` | 0.5 | The tire's radius, or the radius of the track's wheel at each end. |
| `width` | 0.4 | Each tire's width, or the track's. |
| `gap` | 0.05 | `dual`: between the inner and the outer tire. The axle's `track` is measured between the inner tires. |
| `length` | | `tracks`: the belt on the ground, between its end wheels' centers. |

Tracks go on fixed axles.

The wheel sets carry the machine's weight on the ground under them: a tire over the length it is pressed flat (longer
on a flotation tire, shorter on a row-crop one), both tires of a dual, a track's whole belt. The lower that ground
pressure, the less the machine sinks into soft ground, where it takes more to keep it rolling. Duals, flotation tires
and above all tracks also grip better, so their wheels slip less under a heavy pull.

How the machine steers follows from what it is built of:

- **Steered axles** (above). With tracks on the fixed axle and steered wheels ahead of it, it is a half-track.
- **Articulated** (`articulation`): a hinge between a front frame and a rear one, each with fixed axles. Steering swings
  the front frame, with its axles, joints and lamps, up to `maxSteerDeg` either way; the machine turns about the middle
  of its rear frame's axles, where its origin is. Loaders, big four-wheel-drive tractors, forwarders.
- **Skid steer**: tracks and no steered axle, a crawler. One track runs faster than the other: it turns as a steered axle
  as far ahead as its tracks are long would, and standing (unless braking) it turns on the spot at `spinRateDeg`.

A machine with `allWheel` axles has three steering modes, switched with the steering key: **normal** (they roll
straight), **all-wheel** (they steer against the front axles, turning about the middle between them: a tighter turn)
and **crab** (every steered axle turns the same way, so the machine moves sideways without turning). It can't have
fixed axles, which crab steering would drag sideways. Helpers always steer normally.

### motor

Needs a `runningGear` that steers.

| Setting | Default | |
|---|---|---|
| `powerHp` | 100 | Working implements slow it down when they ask for more. |
| `maxSpeedKmh`, `maxReverseKmh` | 40, 15 | |
| `acceleration`, `braking` | 2.5, 6 | m/s². |
| `fuelUnit` | none | The fill unit holding its fuel, filled up at refuel stations. |
| `fuelPerHour` | 0.19 × `powerHp` | Fuel it burns in an hour at full power. |

How fast it goes depends on what it drives over and pulls. Everything in its chain takes some force to keep rolling:
little on a road, more on a field, more on loose soil, and more still as the ground softens with the soil's moisture
and the rain (the more so the higher the ground pressure). A slope adds the weight's pull up it, and a working implement
the force it draws through the ground. The engine's power, less what the work takes from it directly (a header
threshing, a mower, a spreader, a sprayer), sets how fast it can move all that, and the driven wheels slip more the
closer the pull comes to what they grip: a little on dry ground, a lot in a soaked field. Downhill the engine has less
to do.

While someone drives it (the farmer or a helper), the engine burns fuel from its `fuelUnit` by the power it delivers:
what the implements take from it (threshing, a mower's or a spreader's discs), and what it takes to move its chain
against all of the above, speed it up and make up for the slip, from 8% of `fuelPerHour` idling to all of it at full
power. With the tank empty the engine stops, and a helper with it. Without a `fuelUnit` it never runs out.

### drivable

No settings. Needs a `motor`.

### attacherJoints

`joints`: each has an `id`, a `type` and its position `x`, `z`, `y` (0.6). Joint ids are unique on the machine.

The types are in `game/data/jointtypes.json` (`threePoint`, `drawbar`, `fifthWheel`, `header`, `frontLoader`), each
with an `id`, a `name` and `linkage` (false): a three-point linkage, whose lower links lift with the implement mounted
on it (see [`MODELING.md`](MODELING.md#moving-parts)). An implement hitches to a joint of its `attachable`'s type, so a
new kind of hitch is a new entry there.

### frontLoaderBracket

Adds a joint where a loader arm's pivots are.

| Setting | Default | |
|---|---|---|
| `joint` | `frontLoader` | Its id. |
| `type` | `frontLoader` | Its type (jointtypes.json). |
| `x`, `z`, `y` | 0, 1.5, 1.2 | The middle between the pivots. |
| `width` | 1.8 | From one console to the other. |

### attachable

| Setting | Default | |
|---|---|---|
| `type` | `threePoint` | The type of joint it hitches to. |
| `mode` | `mounted` | `mounted`: carried rigidly. `trailed`: pulled by its drawbar eye, following the hitch. |
| `x`, `z` | 0, 0 | The hitch point (mounted), or the drawbar eye or kingpin (trailed, with z > 0). |
| `maxArticulationDeg` | 80 | Trailed: how far it swings from the vehicle's heading. |
| `hitchLoad` | 0 | Trailed: the share of its weight (with its load) resting on the hitch, below 1. |
| `lowerable` | false | Lowered and raised with the lower key. Its work areas only work lowered. |
| `lift` | 0.45 | Mounted: how high the linkage lifts it when raised. |

A trailed machine needs a `runningGear`. It is drawn by its eye, turning about the middle of its fixed axles (its
origin), and its self-steering axles follow; backing up they lock, and it turns about the middle of all its axles.
Trailers are built from that:

- **Multi-axle trailers**: a tandem or tridem axle group, the rear axle self-steering to spare the tires in turns.
- **Semi-trailers**: the kingpin (type `fifthWheel`) far ahead of the axles, under the trailer's front, with a good share
  of the weight on the fifth wheel (`hitchLoad` 0.3 or so): the towing vehicle's wheels carry it, and grip the better.
- **Dollies**: a short trailer with a drawbar and a `fifthWheel` joint, to take a semi-trailer behind a drawbar.

### fillUnits

`units`: each has an `id` (unique on the entity), a `capacity`, what it takes, and optionally a `startFillType` and
`startLevel`. A unit holds one fill type at a time. On a POI they are its storage: what it keeps of a fill type is
spread over the units that take it, those already holding it filled first.

What a unit takes is its `fillTypes` and the fill types of its `fillTypeCategories` (FS: fill type categories), each
fill type naming its `categories` in `game/data/filltypes.json` (`grain`, `seed`, `fertilizer`, `herbicide`, `fuel`,
`product`, `forage`). A unit or station taking a category takes a new fill type of it too: a mod's oats go into grain
trailers and sell at the elevator. Stations and silos take categories the same way, and so do delivery contracts
(`deliver.fillTypeCategories`).

### animatedParts

| Setting | Default | |
|---|---|---|
| `parts` | none | Each has an `id` (the model node role it moves), its moved pose from its rest pose, `rotationDeg` [x, y, z] and `offset` [x, y, z], the `seconds` it takes, and `fold` or a `trigger`. |
| `startFolded` | false | Comes folded. |

Parts with `fold: true` (on machines only) move together when the machine folds for transport; their rest pose is the
working one. The fold key folds and unfolds them, raising the machine as it folds. A folded machine (or one still
unfolding) doesn't work and doesn't go down: it's unfolded first, and a helper unfolds it by itself. A part with a
`trigger` moves while someone is in it and back once they left, such as a shed's door (FS: animated objects).

### workAreas

`areas`: each is a rectangle `width` × `length` centered on `x`, `z`, with:

| Setting | Default | |
|---|---|---|
| `type` | `cultivator` | The kind of work it does (see below). |
| `maxWorkSpeedKmh` | 12 | |
| `requiredPowerHp` | 60 | |
| `requiresOn` | false | Only works turned on. |
| `fillUnit` | none | Seeder, spreader, sprayer: the unit what it sows or spreads comes from. |
| `ratePerHa` | 0 | Spreader, sprayer: units of its fill spread on a hectare. |
| `harvestGroups` | none | Harvester, mower: the crops' harvest groups it cuts (`grain`, `corn`, `grass`). |

The kinds of work (work types, registered in code; mods will add their own):

| `type` | What it does to the ground it passes over |
|---|---|
| `cultivator` | Stubble, grass, a meadow or a failed crop into a seedbed. Kills small weeds; grown ones survive it. |
| `plow` | Turns the ground over (plowed, sown like a seedbed), burying the crop and every weed. |
| `seeder` | Sows the selected crop into a seedbed or plowed ground, from its `fillUnit` at the crop's `seedKgPerHa`. Out of its sowing window the crop comes up weak. |
| `spreader` | Spreads the fertilizer in its `fillUnit` at `ratePerHa`, once each time it passes over: the soil gets the fertilizer's `nitrogen` (filltypes.json), and the field counts as fertilized until harvested or cut. |
| `sprayer` | Sprays the herbicide in its `fillUnit` at `ratePerHa` on tilled, sown or stubble ground: the weeds die, and none come up until the ground is tilled or harvested. |
| `harvester` | Cuts ripe crops of its `harvestGroups` for the thresher it hangs on, and clears dead ones, leaving stubble. Needs an `attachable`: it works while the vehicle it hangs on threshes. |
| `mower` | Cuts ripe crops of its `harvestGroups`: grass grows back from its `regrowStage` (crops JSON); the cut grass lies on the field until balers exist. |

The work of a `harvester`, `spreader`, `sprayer` or `mower` takes its power from the engine; that of the others is
drawn through the ground (see [motor](#motor)). A helper lowers an implement as it reaches the field and raises it as it
leaves; one that isn't lowered but turned on, such as a spreader, it turns on and off instead.

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

`lamps`: each has a `type`, a position `x`, `y` (1.5), `z`, where it points (`pitchDeg` -18, down when negative, -90
straight down; `yawDeg` 0, left when positive, 180 backward), and its beam: `range` (30 m), `angleDeg` (32), `energy`
(4) and `color` (`#fff0d1`). An optional `id`, unique on the entity, lets a configuration option add the lamp or change
it (see below). Its `switch` says what turns it on:

| `switch` | |
|---|---|
| `driver` | The default: the driver switches the lamps of its type, with the control the type answers to (see below). An implement's follow the vehicle it hangs on, and a helper driving at night lights every step. When the farmer gets out, the lights, beacons and turn signals go off; the hazard lights stay on. |
| `dark` | A light sensor: on at night, or when rain, snow or fog darken the sky (a yard light). |
| `hours` | A timer: on between `hours: [from, to]` (game hours; past midnight when from > to). |
| `trigger` | On while someone is in its `trigger` (see above): a workshop's bay light. |

The types are in `game/data/lamptypes.json` (`head`, `tail`, `workFront`, `workRear`, `beacon`, `turnLeft`,
`turnRight`, `brake`, `reverse`), each
with an `id`, a `name`, the driver's `control` it answers to, `rotating` (false: the beam turns round, as a beacon's)
and `blinking` (false: it flashes, as a turn signal):

| `control` | |
|---|---|
| `lights` | The default: the light key steps through the types' `step`s (1): off, then the lamps of step 1 (headlights and tail lights), then those of step 2 too (work lights)… and off again. |
| `beacons` | The beacon key. |
| `turnLeft`, `turnRight` | The turn signal keys; the hazard key lights both sides. |
| `brake` | While the vehicle slows down: its driver brakes, or pushes the throttle against the way it rolls. |
| `reverse` | While the vehicle backs up, or its driver asks it to from a standstill. |

### craneArm

`joints`, each moving the ones after it:

| Setting | Default | |
|---|---|---|
| `id` | | Unique on the machine; the model node role it moves. |
| `axis` | `pitch` | `yaw` turns about the vertical (positive to the left), `pitch` lifts (positive up), `extend` slides out along its own z. |
| `offset` | [0, 0, 0] | Its pivot: in the machine's space for the first joint, else in the previous joint's. |
| `min`, `max`, `rest` | -45, 45, 0 | Its travel, and where it starts: degrees, or meters for `extend`. |
| `speed` | 30 | Per second. |

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

### wearable

A machine's condition drops from 100% to 0% while it moves or works, never standing idle, and a
[workshop](#workshop) brings it back. A worn machine does worse, the more so the more worn: at 0% its engine has
`powerLoss` less power and burns `usageIncrease` more fuel for the power it delivers, and its work areas work at
`speedLoss` less speed and use `usageIncrease` more seed, fertilizer or herbicide. Below 20% it says it's worn.

| Setting | Default | |
|---|---|---|
| `hours` | 16 | Operating hours (real time) from 100% to 0%, moving on a road. |
| `fieldFactor` | 2 | How much faster it wears on a field. |
| `workFactor` | 5 | How much faster again it wears working: its work areas working the ground as it moves (a vehicle pulling or carrying them too), or its thresher on. |
| `powerLoss` | 0.3 | At 0%: the share of its engine's power lost. |
| `speedLoss` | 0.3 | At 0%: the share of its work areas' `maxWorkSpeedKmh` lost. |
| `usageIncrease` | 0.3 | At 0%: how much more fuel, seed, fertilizer and herbicide it uses. |

The defaults are Farming Simulator 19's. `"wearable": {}` gives a machine those.

### hotspots

`spots`: icons on the map (FS: hotspots), each with an `icon` (a Material Symbols icon in `game/assets/icons`, by file
name, such as `storefront`), where it is on the entity (`x`, `z`) and optionally a `name` (the entity's by default). A
POI's first icon also stands over its trigger areas.

## POI components

Machines use most of them at a **trigger**: an area in the POI's space, centered on `x`, `z` (0, 0), `w` along x and
`d` along z (10 × 10), drawn on the ground with what can be done there. They are open at some times only when they
have `openHours: [from, to]` (game hours; past midnight when from > to) and `months` (1..12); none: always.

The buying station, the silo's spout, the workshop and the washing station are **activatables** (FS: `Activatable`):
while the player's vehicle stands in one's trigger (or the farmer walks into a workshop or a wash bay beside the farm's
machines), it offers the use key what it does there, and the HUD shows it. Where several are offered, the use key runs
the nearest; one that can't be used now (closed, another farm's, nothing to do) says why.

Prices: `priceFactor` (1) and `"priceFactors": { "wheat": 1.1 }` for the fill types whose factor differs multiply
the market price (`filltypes.json`). What the farm pays is also multiplied by the difficulty's price level; what it's
paid is not.

### sellingStation

Buys loads tipped or piped into its `trigger`: its `fillTypes` and `fillTypeCategories` (FS: selling station), at the
market price times its factors, less as its demand drops, more in high demand. `minAmount` (0) is the smallest load it
takes; a load under way may finish below it. A sale of a fill type the POI's `fillUnits` keep goes into them, so a mill
takes only what it has room to mill, and stops buying when full.

`demand` lowers the price of a fill type as loads of it come in and brings it back day by day, and sometimes puts one
of its fill types in high demand:

| `demand` | Default | |
|---|---|---|
| `drop` | 0.04 | Price drop for each 100,000 units sold. |
| `floor` | 0.7 | The lowest the demand factor goes. |
| `recovery` | 0.02 | Demand factor regained per game day. |
| `highChance` | 0.03 | Chance per game day that one of its fill types goes in high demand. |
| `highFactor`, `highDays` | [1.2, 1.5], [1, 3] | High demand: [min, max] price factor, and game days it lasts. |

Goods a contract asks for at this POI go to the contract instead, unpaid (the contract pays).

### buyingStation

Sells its `fillTypes` and `fillTypeCategories` to the machines parked in its `trigger`, with the use key: into any
fill unit that takes them (seed into a drill), and into a motor's fuel tank, which is refueling (booked as fuel).
`minAmount` (0) is the smallest amount it sells.

### silo

Stores its owner's goods in the POI's `fillUnits` (FS: silo): loads tipped or piped into its `unloadTrigger` go in,
and the owner's trailers parked under its `loadTrigger` fill up from them with the use key, `loadRate` units a second
(400). It needs one of the triggers or both. `fillTypes` and `fillTypeCategories` limit what it stores and loads (none:
whatever its fill units keep); `minAmount` (0) is the smallest load it takes. Other farms' machines can't use it.

### productionPoint

`productions`, each turning goods into others (FS: production point) from the POI's `fillUnits`, which must keep its
inputs and outputs:

| Setting | Default | |
|---|---|---|
| `id` | | Unique on the POI; saves refer to it. |
| `cycleHours` | 1 | Game hours per cycle (below 1 for several cycles an hour). |
| `inputs`, `outputs` | | What a cycle takes and makes: `fillType` and `amount`. An output's `mode` is `store` (kept for the owner's trailers at a silo's spout) or `sell` (sold every hour at the market price times its factors, for the owner). |
| `runningCost` | 0 | What the owner pays for each hour it runs. |
| `priceFactor`, `priceFactors` | 1 | On the outputs it sells. |
| `openHours`, `months` | | When it runs. A closed production waits with its cycle half done. |

A production short of inputs, or of room for its outputs, starts its cycle over.

### workshop

Repairs the machine chain parked in its `trigger`, with the use key (driving in, or walking in beside the farm's
machines): 1% of each machine's price for each 100% of wear,
times `repairPriceFactor` (1). With `configure`, it also changes the options of the machines parked there (a screen
with each machine's choices): each option fitted costs what it costs more than the one it replaces (a cheaper one
gives nothing back) times `configure.priceFactor` (1), and `configure.price` (0) for the work.

### washingStation

Washes the machine chain parked in its `trigger`, with the use key (driving in, or walking in beside the farm's
machines), for `price` (0) for a fully dirty machine.

### deliverySpot

Where new machines appear, in its `trigger`, facing the POI's front. With `leases: true`, machines leased for
contracts ([`contracts.json`](../game/data/contracts.json)) are delivered there.

## Configurations

A machine can come with options, as in the shop of *Farming Simulator*: wheels, a front hitch, the engine, the color,
the capacity, the working width. `configurations` lists the choices, each with its options; a machine has one option of
each.

```jsonc
"configurations": [
  { "id": "wheels", "name": "Wheels", "options": [
    { "id": "single", "name": "Single" },
    { "id": "dual", "name": "Dual", "price": 5800, "mass": 620,
      "changes": { "size": { "width": 3.6 },
        "components": { "runningGear": { "axles": [ { "wheels": { "type": "dual" } }, { "wheels": { "type": "dual" } } ] } } } }
  ] },
  { "id": "frontHitch", "name": "Front hitch", "options": [
    { "id": "threePoint", "name": "Front linkage",
      "changes": { "components": { "attacherJoints": { "joints": [ { "id": "front", "type": "threePoint", "z": 3.75, "y": 0.55 } ] } } } },
    { "id": "none", "name": "None", "price": -3400, "mass": -220 }
  ] }
]
```

| Setting | Default | |
|---|---|---|
| `id`, `name` | | The choice: ids are unique on the machine, and saves refer to them. Ids are letters and digits, as model nodes are named after them (`frontHitch_weight`). |
| `options` | | Each with an `id` (unique in its choice, letters and digits) and a `name`. |
| `default` | false | On an option: the machine comes with it. Without one marked, the first option. |
| `price`, `mass` | 0 | What the option adds to the machine's price and mass, or takes off when negative. |
| `changes` | none | What the option changes in the machine's JSON. |
| `show` | none | Model nodes the option shares with others, such as `["beacons_left", "beacons_right"]` for both beacons. Like its own node (`beacons_both`), they are hidden without it, so that one model holds every option (see [`MODELING.md`](MODELING.md#one-model-for-every-configuration)). |

`changes` is merged into the machine's JSON, any of it but its `id`, `price`, `mass` and `configurations`:

- Objects merge setting by setting: `{ "size": { "width": 3.6 } }` changes the width and keeps the rest. A setting set
  to `null` is removed: back to its default, or, for a component, gone.
- Lists of objects merge item by item. An item with an `id` (joints, fill units, lamps, animated parts) changes the item
  with that id, or is added when there is none. An item without one changes the item at the same place (`{}` leaves one
  as it is: `"axles": [ {}, { "track": 2 } ]` changes the second axle), or is added past the end.
- Anything else replaces what was there: a number, a text, a list of texts.

Maps can place a machine with options (`"configuration": { "wheels": "dual" }` on it), and the workshop changes them
for the farm: parked in its bay, the use key opens a screen with each machine's choices. An option costs what it costs
more than the one it replaces (a cheaper one gives nothing back), plus the workshop's price for the work; what hangs on
a joint the new options take away is unhitched.

So a component the machine has with some options only (a front loader bracket, a front joint, beacons) goes in the
options that have it, not in the machine. The machine as it comes and each option in turn are checked when the game
starts, and errors name the option (`machine 'tractor_125' (wheels: tracks) runningGear: …`); options of different
choices are best kept to different settings, so that any combination holds.

## Saves

A save keeps each machine's options (`configuration`: configuration id → option id; an option that no longer exists
loads as the default), and each POI's owner. Every entity keeps its state under its components' kinds (the steering
mode, the pipe unfolded, the level of each fill unit, a POI's stored goods, production under way and demand…). An
entity whose definition gains a component gets it fresh; one that loses a component loses its state, and goods in a
fill unit it no longer has are lost (with a warning).

A POI's storage units are best named after the fill type each holds (`wheat`, `flour`): saves from before POI
components (0.13 and older) kept goods by fill type, and hand them to the unit of that name.

A machine's condition is its `wearable`'s: saves from before it (0.14 and older) hand it over, and a machine without
a `wearable` loses it.

# Crops

One file per crop, named after its `id`. The game reads every `.json` here: the crops in the order of their file names.

- `gdd`: growing degree-days (°C·day above `baseTempC`) needed to leave the stage.
- Stage 0 is bare sown soil; the harvestable stage is the last growing stage.
- `vernalizationDays`: cold (real days between -4 and 8 °C) a winter crop needs before the stage marked
  `requiresVernalization` can start; until then it waits (tillering, rosette) through winter.
- `card`: column in the crop atlas (0..6), so several stages can share a look.
- `atlasRow`: the row in `assets/textures/crops/crop_atlas.png`.
- `mapColor`: its color on the map's crop layer (`#rrggbb`).
- `ground`: what its cells show once sown: `seeded` (the default) or `grass` (a meadow, where weeds don't come up).
- `regrowStage`: a crop that grows back once mown starts over from this stage (grass); others are cleared.
- `weedYieldLoss`: yield lost where weeds grew up among the crop (default 0.2; half of it where they're small).
- `windrow`: what cutting it leaves lying on the field, `fillType` and `perHa` (units on a hectare, less as the crop
  yields less): mown grass lies in the mower's windrow, a combine drops straw in its swath. Balers pick it up.

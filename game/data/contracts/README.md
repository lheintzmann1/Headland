# Contract types

One file per kind of job the neighbors offer, named after its `id`. The game reads every `.json` here.

A field job is done on one of their fields with a work area of type `work` (cultivator, plow, seeder, spreader,
sprayer, harvester, mower), and only offered when some machine does that work (on the field's crop). It is offered
when at least half of the field is in its `offer` state and not enough of it (see `threshold` in `economy.json`) is in
its `done` state yet.

A state is any of its `ground` types (grass, cultivated, seeded, stubble, plowed), any of its `crop` states (none,
dead, sown: a living crop at any stage, growing: not ripe yet, harvestable), any of its `weeds` (none, small, grown,
sprayed) and `fertilized` (true or false: since the last harvest); what's left out allows anything. In `done`, sown,
growing and harvestable mean the job's own crop: the one ripe on the field, or for a sowing job one whose sowing
window is open when the job is offered.

`rewardPerHa` pays by the field's area, `days`: [min, max] game days to finish once taken, `months` limit when it is
offered, and `weight` makes it come up more or less often than the other jobs that fit.

`deliver` on a harvest job: the crop belongs to the neighbor and at least `share` of what was harvested must be tipped
at the buyer named on the contract. A delivery job (no `work`) asks for `amount`: [min, max] units of goods a buyer on
the map takes (`fillTypes` and `fillTypeCategories`, default any), and pays their market price times `priceFactor`.

`leases`: machine sets a field job can be taken with, from a POI whose delivery spot leases them (the dealer). An offer
gets the first set that can do it (for a harvest, one whose header cuts the crop), for `feePerHa` by the field's area,
taken from the reward. The machines wait on the dealer's lot, implements hitched, and go back when the contract ends.

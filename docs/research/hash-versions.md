# State-hash versions

`GameSimulation.HashVersion` numbers the shape of the simulation state that
`GameSimulation.StateHash()` folds, and `WorldSnapshot.FormatVersion` is kept
equal to it. `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` pins
one literal hash for a 500-tick run of `TestWorld.Simulation(seed: 0xDEADBEEF)`.

**The rule.** A deliberate change to simulation state, or to the hash itself,
bumps `HashVersion`, bumps `FormatVersion` to match, and updates the pinned
literal — all in the same commit. Any other way of breaking the pin means
determinism is broken and the change must not ship. See
`docs/architecture/simulation-loop.md` for what the pin is for and why the hash
is FNV-1a rather than `GetHashCode`.

**What the pin cannot see (Q206).** `TestWorld.World` spawns no creatures, so a
change to `CreatureState` reaches the pinned value only through `HashVersion` —
verified by hashing a constant in `CreatureState.HashInto` and watching the pin
still pass. Notes below that explain a move by what the fixture's creatures were
doing predate that finding; what moved the value in those commits was the
version bump beside the change. The pin still does its real job — struct layout
drift, dictionary iteration order, string hashing — but for a simulation whose
state is mostly creatures it guards a world that has none.

**Composition versus behaviour.** Most bumps here moved the value on
*composition*: a field entered or left the hash, and every instance of it held
its default for the whole run. That is the useful shape for a pin — a field
added to an empty list still has to change the hash, which is what proves the
hash covers the field rather than the value.

This file is the per-version history. The decision behind each change lives in
its ADR (`docs/DECISIONS.md`); the evidence lives in the research doc named
beside it.

## Versions

**41** — `0xDEF9B4B1985D90DE` (current). Trigger-record active and repeat state,
including one-shot action retirement, enters the hash and snapshot. The fixture
has no trigger tree, so its record count remains zero.

**40** — `0x6445940E0B0F23A9`. The level's action points, in
THINGS-file order: rectangles, ids, loader-filtered flags, passage counters and
wait timers. Even this fixture's empty list adds its count to the hash.
`docs/research/action-points.md`.

**39** — `0x81D58B966898BEC8`. Corrected the trigger timer bank from 256 slots
to **sixteen**, 240 fewer dwords in the hash. Two readings agree on sixteen: the
next field on the owning object sits 0x40 bytes after the bank, and the
condition path compares the id against 0x10 before indexing.

**38** — reserved, never on master. Taken by the action-point branch in the
codex worktree so the two could not collide; the merged result took 40 rather
than reusing it.

**37** — `0xD17C2E424DDE0AF2`. `WorldState.Triggers`: the level's trigger flag
and timer banks (ADR-0102). They are hashed because an action record writes a
flag and a later condition reads it, so two worlds disagreeing about one diverge
as soon as that condition is evaluated. 128 flag bytes and — at this version —
256 timer dwords enter the hash even when every slot is zero. Version 39
corrected the timer bank to sixteen.

**36** — `0xD893704FB5D00309`. `PlayerState.ResearchPoints`, the Library's
accrual. A new player field moves the value by itself.

**35** — `0x368FDF9DF3D0A6EA`. Two changes, either of which moves the value
alone: `SimulationTiming.TicksPerSecond` went 20 to **four**, DK2's own rate,
re-timing every scheduled system; and `TileState.UpwardWork` was **removed**,
because DK2 accumulates an upward job in the tile's own health and converts at
the terrain's `MaxHealth`, so the cost is the deficit and not the whole maximum
(Q1; ADR-0024 reversed by ADR-0089).

**34** — `0x46FAACC93F8323FF`. Reason not recorded here; see git history.

**33** — `0x21A7E4D24F37DF48`. Job selection for non-workers (ADR-0082):
`CurrentJob`, `JobTileIndex`, a choice timestamp per mood, and the mood band
itself, which DK2 stores rather than recomputing and which this now stores for
the same reason — anger reaches a creature from four places and three of them
run before the anger system in the same tick.

**32** — `0x30722F6054924CC5`. The anger split (ADR-0081): one int becomes six.
DK2 keeps a creature's anger in an `int[6]` indexed by grievance and bands the
**largest** of them; this simulation kept one pooled counter. Two half-grievances
no longer add up to desertion, and a kindness in one bucket no longer pays off a
grievance in another.

**31** — `0xC026542547419606`. Payday (ADR-0078): three creature fields — the
wage owed, the failed-attempt counter and `SkipNextPayment` — and the next
payday deadline on the player. The pinned world has no treasury and its
creatures are level 1, so the value moved on composition.

**30** — `0x2E7E116CA573398B`. Two commits arrived at 30 from 28, with 29 spent
on a branch:

- The Hand of Evil stopped holding creature ids and started holding **things**
  (ADR-0075, Q196). The owner confirmed a keeper can pick up a chicken — with a
  screenshot of four held at once — so a hand slot carries a kind beside its id,
  and an object carries `ObjectState.InHand` the way a creature carries
  `CreatureState.InHand`. Composition only: every hand is empty for the whole
  run, and the world holds no objects.
- Fog of war: every tile gained a per-keeper discovery mask and the world gained
  the boolean game setting that makes it active. Both are persisted simulation
  state in `DKII.EXE` — its world save writes the live tile's fog byte — so both
  enter the hash. This fixture leaves fog **disabled** and every discovery mask
  at zero. `docs/research/fog-of-war.md`.

Did not move it: the prisoner eating of ADR-0074, one commit earlier — the
fixture imprisons nobody and `PrisonerEatSystem` draws no randomness.

**29** — exists only on the fog-of-war branch. It was taken there while the hand
work was being written in the main tree, which is exactly the collision a
version number exists to catch: two branches numbering the same save format
differently, either of which would read the other's file and be wrong. The hand
went to 30 rather than fighting over 29.

**28** — `0x19A512BB12A71575`. The aimed bonus turned out to be two fields
rather than one (ADR-0069). `JustDropped` is now only a one-shot ticket for the
drop scan; the Efficiency bonus is the new `DroppedTask`, which holds the
**kind** of work the drop found and which DK2 clears in exactly one place.
`CreatureState` also gained `LastTaskKind`.

Did not move it: the behaviour. Nothing in this fixture is picked up, so
`DroppedTask` is `None` for the whole run and the retry branch that decides
whether an aimed worker keeps its bonus is never entered.

**27** — `0x0D75C57C1848268C`. Eating (ADR-0067). The hunger clock stopped
saturating and started accumulating — `ChickensNeeded` became `HungerNeed`, and
the periodic tick **adds** `HungerFill` rather than setting it — and
`CreatureState` gained `FoodAttemptTicks`. `docs/research/eating.md`.

Did not move it, and that is the point: `CreatureEatSystem` was registered
**above** `CreatureSleepSystem` in the same commit, which changes the order every
creature is offered its needs in, and the run is identical. Imps neither eat nor
sleep, so this fixture cannot see it — the next fixture with a goblin in it will.

**26** — `0x154E9041172A7F7F`. Reason not recorded here; see git history.

**23, 24, 25** — no pinned value recorded. 23 was carrying (ADR-0063):
`CreatureState` gained `CarriedCreatureId`, `CarriedByCreatureId` and
`TargetBodyId`, all `-1` for every creature in this fixture. Death changed shape
in the same commit and did not move the value: a creature that leaves a body
becomes a corpse instead of being removed, and the fixture kills nobody. 24 and
25 are not recorded here; see git history.

**22** — `0xDFF77147B12A64C3`. The Hand of Evil (ADR-0062). `CreatureState`
gained `InHand`, `PickupTick`, `LastSlapTick`, `WorkFasterTicks` and
`JustDropped`, and `PlayerState` gained the hand itself.

Did not move it: Efficiency became a real multiplier on every work application
in the same commit and the suite passed unchanged, because
`EfficiencyRules.Scale` returns its input at the default 80%, which is the rate
every worker here is at.

**21** — `0x6AF149EE8E20D98A`. The angry chooser stopped keeping its draw and
started keeping a timestamp (Q122, ADR-0061): `CreatureState.AngryChoice` became
`LastAngryChoiceTick`, because what the original keeps between selections is
when the last one succeeded and nothing else.

**20** — `0x5E96E634C7E5037D`. Every tile gained a `FreshlyDug` bit (Q154), set
when excavation destroys it and cleared by the next change of terrain, so that
an imp claims the tile it has just dug before it looks for the next mark. The
fixture digs, so the bit is set on real tiles here.

Did not move it: the mover's two-phase diagonal walk (ADR-0059), which landed in
the same commit — the fixture has no off-centre diagonal leg in it.

**19** — `0x12E5615E803C5796`. The free-imp supply stopped keeping a countdown
and started keeping a **deadline**, which is what the original does (Q56).
`PlayerState.FreeImpCountdownTicks` became `NextFreeImpTick`, and the fixture's
keeper — who has a dungeon heart and starts with no imps — is staffed on tick
zero instead of five seconds in.

**18** — `0x55BEDDA879B4A281`. An object gained the union's Gold member,
`ObjectState.GoldHeld` (Q31, Q65), because gold on the floor is an object —
type 1, "Gold", carrying its amount in the same four bytes a bed reads a
creature id out of. Composition only: the world has no objects.

**17** — `0x6B5ECAD0E9EDB2F0`. The world gained an **object** list and a
creature **lost** a field (Q130). A bed is now a thing standing on a tile — an
`ObjectState` whose `CreatureId` says whose it is — where it used to be
`CreatureState.LairTileIndex` saying which tile was mine. DK2 keeps the relation
on the object (`docs/research/objects.md` §7), so there is one copy of the fact
instead of two.

**16** — `0x092072D0E7C9B04E`. `CreatureState` gained `ExperiencePoints`
(Q124). `docs/research/creature-levels.md`.

**15** — `0x38CCE7E988729817`. The `Level` field itself (Q119), whose arithmetic
is a proven no-op at level 1: composition only.

**14** — `0x5DB64D0C91D99368`. Reason not recorded here; see git history.

**13** — `0xAEAF871CC4814DA1`.
**12** — `0x9D2695B2F62707D2`.
**11** — `0x12E4F45E48ACEB9B`.
**10** — `0x06F71648486D0FEC`.
**9** — `0x4F824667CDA7EF25`. Reasons for 9–13 are not recorded here; see git
history.

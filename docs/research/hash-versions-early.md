# Early state-hash versions

Versions 9–31, split from [the current version record](hash-versions.md) to keep each research document within its budget.

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

**14** — `0x5DB64D0C91D99368`; **13** — `0xAEAF871CC4814DA1`; **12** — `0x9D2695B2F62707D2`;
**11** — `0x12E4F45E48ACEB9B`; **10** — `0x06F71648486D0FEC`; **9** — `0x4F824667CDA7EF25`. Reasons for 9–14: see git history.

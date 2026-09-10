# Hero parties and effect generators: verbatim placed state

This note records the data-side evidence needed to stop discarding the last two
parsed THINGS record types, and then — in the last section, added later — how
action type 14 reaches a spawn. Storage is established; the spawn itself is not.

## Result in one sentence

The 48-level campaign contains 220 hero parties, and the shipped corpus contains
one effect generator; every parsed field now enters ordered, hashed and
snapshotted world state verbatim, while spawning and generation remain UNKNOWN.
Across the complete 66-level shipped sweep, 35 levels contain a party.

## Record shapes

| record | fields stored | confidence |
|---|---|---|
| hero party | `Name`, `TriggerId`, `PartyId`, `Unknown_0023`, `Unknown_0027`, `Members` | **CONFIRMED from parser and shipped data** |
| party member | all `GoodCreatureThing` fields, including the two-byte `Unknown_001D` tail | **CONFIRMED from parser and tests** |
| effect generator | `X`, `Y`, four scalar unknowns, variable `EffectIds`, `Frequency`, `GeneratorId`, six-byte `Unknown_001E` | **CONFIRMED from parser and tests** |

Projection copies values without coordinate conversion, validation, flag masks
or scaling. Arrays are copied into world-owned storage so parser-side mutation
cannot alter state. List counts enter both hash and snapshot. Fixed tails are
written without lengths, so their state setters enforce the parser's two- and
six-byte widths; accepting another width would desynchronise every later field.

## Party members are templates

A party has sixteen `GoodCreatureThing` slots in shipped records. They describe
an unspawned party, not creatures standing in the level. Projection stores those
records under `HeroPartyState.Members`; it does not call `SpawnCreature`, and the
projection's creature count remains unchanged. Action type 14 is the route that
would spawn a party, but its behaviour is not implemented (Q254).

`TriggerTree` still independently builds its 24 reduced party slots from the
source `HeroPartyThing` sequence. The same records therefore have two homes for
now. Refactoring that ownership without the spawn path would be speculation, so
Q256 records it rather than this change rewiring the tree.

## No behaviour

Parties do not spawn. Effect generators do not generate or choose effects. Field
meanings, transformations, cadence and consumption are UNKNOWN (Q254–Q259).
Verbatim storage loses no information; its conservative risk is hashing a value
or bit DK2 discards or transforms.

Adding both collections changes the state shape even in an empty world, so
`HashVersion` and snapshot `FormatVersion` are 47. The pin is recorded in
`hash-versions.md`.

## Corpus gate

`PlacedThingsOnRealDataTests` walks all shipped levels, compares every projected
party, member and generator field with its parsed source, asserts 220 parties,
one generator and zero `NotYetProjected`, and proves party members do not inflate
the placed-creature count.

## Action type 14, read from the binary (2026-09-10)

The doc above said "no binary was read for this work". This section is that
work, and it stops short of the spawn itself.

**The arm.** The world action dispatcher `0x00518AD0` sends type 14 to
`0x00519182`, three instructions wide, which calls `0x00519F90` on the world
with three bytes of the record:

```
world->FUN_00519F90( byte[0], byte[4], byte[1] )
```

Our parser calls those `PartyId`, `ActionPointId` and `PartyType`
(`trigger-semantics.md`), and the consumer agrees with all three — independent
of how the names were arrived at, which was the corpus.

**`PartyType` decides whether a guard runs.** Non-zero and the target walks the
global object table `0x006EE540` from the list headed at `[world+0x84]`
(head `u16 +0x06`, link `u16 +0x10`) looking for `byte [obj+0x417] == PartyId`.
Where that matches **and** `u16 [obj+0x3CB]` is zero **and** `u32 [obj+0x3E6]`
has bit 21 (`0x200000`) set, the action leaves without spawning. So a party
already in the world is not spawned twice, and three fields say so.

**The action point is found by a second walk**, from `u16 [world+0x12]` along
link `u16 +0x08`, matching `byte [obj+0x37] == ActionPointId`.

**The spawn position is the centre of that action point's rectangle**, and the
rectangle is four words on the instance:

```
x = (u16[ap+0x38] + u16[ap+0x3C]) / 2      ; signed halve, toward zero
y = (u16[ap+0x3A] + u16[ap+0x3E]) / 2
position = (x << 12, y << 12, 0)           ; tiles to 4096ths
```

That triple is passed by pointer to the world's own vtable slot **`+0x1D4`**,
whose result is an object the code immediately reads `byte +0x0A` from.

**Where this stops.** What `+0x1D4` returns, and everything after it, is
UNKNOWN — that is where the creatures are actually made. The meaning of bit 21
at `+0x3E6` and of `+0x3CB` is UNKNOWN beyond their role in the guard. Nothing
here is implemented; the next probe is the vtable slot and the code from
`0x0051A0AF` (Q254).

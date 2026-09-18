# Measuring the router against DK2's own route log

DK2 logs every routing decision it makes, behind a global nothing writes
(`[0x006EC9E8]`, `navigation-mesh-and-search.md` §5). Set to 1 in a running
game it writes `nav.log` beside the exe, and every request the original made can
then be replayed through our router. This note is the log of those captures and
what each one settled; the algorithms they test are
`navigation-mesh-and-search.md`, and `navigation-and-steering.md` owns the
funnel and the mover.

Original version: DK2 v1.70 (GOG)

## The instrument

Two levels were captured with nobody at the controls.
`reverse-engineering/dk2move.py` holds the recipe: it pokes the gate, reads the
entity table and the navigation cache, and dumps DK2's own triangle and point
tables. Both its outputs and `nav.log` are game-derived and stay out of the
repository. The log's line grammar — ` Path`, ` retC`, ` route=`, ` way=`,
` DOUBLE PATH`, ` Area` — is `navigation-mesh-and-search.md` §1.

**Level 7, 2026-09-17, seven minutes.** Its keeper Gormagon is scripted to fall
within seconds (owner), so this watched the player's imps and the heroes.
Replayed on DK2's own dumped cache — each byte change applied when the log's
single-tile ` Area` line for that tile passes, the probe's tick and the log's
frame counter drifting by up to fifteen ticks — **21,288 of 21,298 agree,
99.95 %** (with the endpoint rule of `navigation-mesh-and-search.md` §2.3; 93 corner targets disagreed without it);
waypoint counts match in 4,923 of 4,941 probes. The ten left are one portal-hole
target, three lava targets and six lava crossings the requesting creature's own
flags allowed; the cache also answered Q352 (structure-blocks.md §5.4). Steps per
tick match the loader rule: the imps' 1888 against 1884–1887.

**Level 10, 2026-09-18, a keeper playing.** Twelve minutes, 87,619 requests
logged, 86,140 with a tick, 1,883 byte changes, 107 creatures. **Whose creatures
is not known**: the probe's owner offset was wrong and past the end of its header
read, so every sample recorded owner 0 (it is `word[thing+0x28]`, fixed
2026-09-18). The owner sees heroes on this map between the two dungeons, so
Asmodeus is not the only one moving. Replayed with six movers (each
owner, respecting doors, with and without lava, or attacking), **every request
but three is explained by one of them** — but the six cover owners 3 and 4 only,
so which profile explains which request is not evidence until a capture records
the owner; the three enter a portal hole, the
leaving word's bit 2. The probe also dumped DK2's triangle and point tables
(§2.1): at tick 7 its point set is **exactly ours** once the base rectangle and
the byte-0 skip are built, 1,417 for 1,417, and 2,765 triangles inside the map
against our 2,765, **2,612 identical**. Of the 153 that differ 124 are in rock,
which neither coarsening walks; the rest are quads whose two diagonals tie,
settled by insertion order. Corridor lengths are **exact in 84 %**, but **3,232
of the 3,254 differences leave the waypoint count unchanged**. Nor does our
whole-map build drift where DK2 patches the mesh a tile at a time: across the
probe's 353 dumps its triangles stay 92-94 % ours, 88 % at the worst. No creature
stepped between two solid tiles in 216,882 steps (Q28).

**What was left is the instrument, not the router.** Of the 2,014 single-tile
rebuilds DK2 logged, **1,980 are on the 23 tiles that ever carried a door byte**
and 34 are digging: a door tile alternates between `owner << 4` and plain floor,
and each flaps as creatures pass. The probe saw 1,854, so **at least 126 flips
fell between two of its samples** — it walks 4,096 entity slots a pass, which
caps it near 8 Hz, and a door can open and shut in a tick. Every request whose
waypoint count disagreed is one of those: toggling **one door tile** — (24,16),
(27,14), (30,16) or (32,14), the four with the most missed flips — reproduces
DK2's `route=` **and** `way=` exactly, 23 times in 23, and 20 of them had our
door open where DK2's was shut. The probe now re-reads the cache every 5 ms
while its creature pass waits (Q351).

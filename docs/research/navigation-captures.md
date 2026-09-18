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
logged, 86,140 with a tick, 1,883 byte changes, 107 creatures. **Most of them are
heroes.** The probe's owner offset was wrong here and read past the end of its
header, so this capture recorded owner 0 for everything; a later one with the
offset fixed to `word[thing+0x28]` counted **74 creatures of player 1 against 15
of Asmodeus's, 14 of the owner's and 4 neutral**. Replayed with nine movers
(owners 1, 3 and 4, respecting doors, with and without lava, or attacking),
**every request but three is explained by one of them**, and the hero profile
explains the most of any single one — 53,479 agreements against 52,934 for
Asmodeus and 52,553 for the owner, which is the ownership showing up
independently in the routing; the three enter a portal hole, the
leaving word's bit 2. The probe also dumped DK2's triangle and point tables
(§2.1): at tick 7 its point set is **exactly ours** once the base rectangle and
the byte-0 skip are built, 1,417 for 1,417, and 2,765 triangles inside the map
against our 2,765, **2,612 identical**. Of the 153 that differ 124 are in rock,
which neither coarsening walks; the rest are quads whose two diagonals tie,
settled by insertion order. Measured against
the hero profile, corridor lengths are **exact in 17,678 of 20,979** and the
waypoint count differs in only **22**. Nor does our
whole-map build drift where DK2 patches the mesh a tile at a time: across the
probe's 353 dumps its triangles stay 92-94 % ours, 88 % at the worst. No creature
stepped between two solid tiles in 216,882 steps (Q28).

**What was left is a door's state, and it is not the sampling rate.** Of the
2,014 single-tile rebuilds DK2 logged, **1,980 are on the 23 tiles that ever
carried a door byte** and 34 are digging. Every request whose waypoint count
disagreed is one of those: toggling **one door tile** — (24,16), (27,14),
(30,16) or (32,14) — reproduces DK2's `route=` **and** `way=` exactly, 23 times
in 23, and 20 of them had our door open where DK2's was shut. So the state we
replayed was wrong; **why** is open.

It is **not** that the 8 Hz probe missed flips, which is what the 126-change gap
between rebuilds and recorded changes looked like. Sampling the cache from its
own thread instead, about 25 times faster, moved that gap by nothing: on one
replayed session, tile for tile, 8 Hz recorded 109, 96, 92 and 93 changes on the
four busiest doors where the thread recorded 109, 96, 92 and 93. **About a third
of DK2's single-tile rebuilds therefore write the byte the tile already
carried**, and the probe was seeing every change all along (CONFIRMED by
capture; which writers do it is unread). Nor is it **when** we apply a change. Placing each one at
the last rebuild of its own tile at or before the sample that saw it, rather
than at the first rebuild left unmatched, moved **corridor agreement from 84.3 %
to 87.2 %** — worth keeping on its own — and left the waypoint residual at 22.
Nor is it the capture's resolution: the same replay against a capture whose
changes are timestamped to **15 ms instead of 125** leaves 22 as well. Three
candidates eliminated; the cause is Q357 and still open (Q351).

## DK2 records and replays a session itself (Q356)

The shipped exe carries a **packet recorder**, parsed at `0x005A65D5`-`0x005A668F`:
`-PSAVE <name>` writes `PacketRecord\<name>.sav`, `-PLOAD <name> <n>` plays one
back, and `-PNUMBER`, `-PQUIT` and `-PMESH` sit beside them. **`-LEVEL` must be
given on both**: the record's header names its level but the loader does not act
on it, and without it the game feeds the packets to the front end — itself a
7x14 level — and desyncs on turn 1. The second `-PLOAD` argument is **0**, which
is what the game's own config path passes (`0x004016E8`).

A record is a level name, twelve header dwords, then one record per game turn: a
**4-byte turn counter**, the turn's **RNG seed** and **state checksum** as two
dwords, and a short packet payload — 17 bytes on 2,588 of one session's 2,925
turns, because most turns carry nothing but the camera. Playback compares both
stored values against the live ones every turn (`0x005268FD`) and logs
`Gone Out Of Sync (Seed|Checksum) GT %d [%08X:%08X]` on a mismatch.

**It replays faithfully.** A five-minute played session of Level 10, recorded
2026-09-18 and replayed at once: **no seed ever diverged** over 2,924 turns, and
the state checksum diverged **once**, at GT 1099, by 0x3F8 on what the near-equal
values suggest is a sum. Every gross counter in `MESSAGE.LOG` matched the
recording exactly — 2,939 world frames, **98,313 navigation calls**, 65,583 imp
calls, 2,922 computer-player calls.

So a played session can be captured once and replayed unattended as often as
wanted, which is what makes any probe change an A/B test rather than a new
sample. Playing also generates routing far faster than watching: 98,313 calls in
five minutes against 87,619 in twelve idle ones. The `.sav` is game-derived and
stays out of the repository.

## The probe's own limits

The creature pass walks 4,096 entity slots and takes **~128 ms**, so asking it
for 8 Hz leaves no idle time at all — a first attempt to sample the navigation
cache in that idle time never executed a single iteration. Sampling now runs on
its own thread, which `ReadProcessMemory` allows because it releases the GIL,
and resolves changes about 15 ms apart rather than 125. It does **not** record
more of them: see above.

# Mechanic: the steering behaviours' bodies (phase 2 of ADR-0157)

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- `structure-blocks.md` §5 for the object enumerators this page finds installed

Original version: DK2 v1.70 (GOG)

Questions: **Q349** (the bodies of `avoid_walls` and `follow_path`, and the
integrator's obstacle clipping) and **Q344** (`grtc:`). Continues
`navigation-and-steering.md`, which owns the combination, the constants and
the integration; this page owns what each behaviour computes.

**Only `Separation` is built** (`SeparationRules`, ADR-0156). The rest waits on
this page.

---

## 1. The four installed callbacks

The steering initialiser `FUN_004D10D0` registers the behaviours (`0x004D56E0`,
`navigation-and-steering.md` §3.3) and then installs four function pointers
through the setters at `0x004D2770`–`0x004D27D0`:

| slot | function | what it is | used by |
|---|---|---|---|
| `[0x006ED3FC]` | `0x004D1170` | the **ungated** object enumerator (§5.4 of `structure-blocks.md`) | `Separation`'s neighbour query |
| `[0x006ED400]` | `0x004D12B0` | the enumerator that **gates on `Obstacle`** and forwards position, size, flags | `Repel_Obstacle` |
| `[0x006ED404]` | `0x004D15E0` | UNKNOWN | not yet seen |
| `[0x006ED408]` | `0x004D16B0` | the **navigation-byte probe** at a position (reads `world+0x9F2`) | `Avoid_Walls`' feelers |

So `structure-blocks.md`'s inference — that furniture is continuous collision
geometry rather than a blocked tile — is now closed from the consumer's side:
the `Obstacle` enumerator's only steering consumer is `Repel_Obstacle`.

## 2. `Repel_Obstacle` (`0x004D31D0`, weight 0.4)

Two exemptions first, then the work.

- **Arrival.** If `path_flags` bit 2 is set and the body is on its **last**
  waypoint, and the octile distance to it is at most `radius + 1.0`, the force
  is zero. The same shape as `Separation`'s exemption (§3.2 of the steering
  page): nothing is shoved about once it has arrived.
- **Stuck.** If `+0x10F` bit 1 (`escape_stuck_mode`) is set, the force is zero.

Otherwise it points the body's two leading dwords at a **twelve-slot scratch
list** at `body+0x8`..`+0x80` (ten-byte records; count at `+0x80`), zeroes the
accumulator, and asks the `Obstacle` enumerator for everything within
`radius + 1.0`, with `0x004D3020` as the per-obstacle callback:

1. `v = body.pos − obstacle.pos`, `d = |v|` through the three-band integer
   square root, `n = normalise(v)` (`0x004D1F10`). `d = 0` contributes nothing.
2. `reach = obstacle.size + body.radius`. Nothing happens unless `d ≤ reach`.
3. If the obstacle's flag **bit 0 is clear**: `force += n × max_force`. Full
   force, straight away, **no falloff** — contact is binary.
4. If bit 0 is **set**: the obstacle is not pushed against but **recorded** —
   `n`, and a word `1 | ((flags & 2) << 1)` — into the scratch list, up to
   twelve. The integrator (`FUN_004D4760`) walks that list after the forces are
   summed and clips velocity against it (Q349).

Bit 0 of the forwarded flags is what §5 of `structure-blocks.md` identified as
the object's **`SolidObstacle`**: fifteen objects carry it, all room furniture.
So furniture is not pushed away from; it is slid along. Everything else that is
an `Obstacle` — beds, gold piles, the Heart's tile-wide collider — shoves.

Two helpers seen on the way: `0x004D4600` projects a target onto the body's
`local_xaxis`/`local_yaxis` (forward and lateral offsets, 16.16), and
`0x004D2070` is a **ray–circle intersection** returning the distance along a
heading to a circle, or `INT_MIN` for a miss. Both serve `0x004D32E0`, a
repel-along-heading routine whose caller is not yet identified.

## 3. What this leaves (Q349, Q344)

- `Avoid_Walls` (`0x004D35C0`, weight **0.6** — the largest): three feelers
  through the probe and the transition-cost function, a second four-feeler
  block under `escape_stuck_mode`, ray-circle geometry. Skeleton seen, body
  unread.
- `Follow_Path`'s body (`0x004D3C40`, weight 0.35) beyond its early-outs.
- The integrator's clipping of velocity against the recorded solid obstacles.
- `grtc:` (`0x004CF490`–`0x004CFD00`), which shapes the aim points the mover
  is actually given (Q344).
- The third callback `0x004D15E0`.

## Confidence

**CONFIRMED (binary):** the four installed functions and which behaviour calls
each; `Repel_Obstacle`'s two exemptions, its query radius, the overlap test, the
full-force push for flag-bit-0-clear obstacles and the recording of the others;
the twelve-slot list and its record shape.

**CONFIRMED (binary + repo):** flag bit 0 is `SolidObstacle` — the enumerator
forwards the object flags `structure-blocks.md` already decoded.

**STRONGLY INFERRED:** that `0x004D2070` is a ray–circle intersection (its
arithmetic is the quadratic's discriminant and root in 16.16).

**UNKNOWN:** everything in §3.

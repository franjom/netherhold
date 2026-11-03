"""Q716: the order of the free cast's angle draws and the creation process's heading draws.

    python tools/levelgen/q716_draws.py <capture.dk2cap> [--max-steps 200000]

For every new Imp in a dk2record.py capture, finds which world-seed draws (world +0x15A0, the
0x005558D0 LCG) gave its ring angle and its heading. Between two frames the seed has stepped some
number of times; each step's value is rotr(seed, 13) mod n. An angle draw a matches when the
Heart's centre (player +0xB43/+0xB45, tile*0x1000 + 0x800) plus ((sin a * 0x2000) >> 16) in X and
minus ((cos a * 0x2000) >> 16) in Y is the Imp's position (0x004BB2A0); a heading matches as
random(0x800) (process 1, 0x004AD957) or random(4) << 9 (process 36). Draws are numbered across
the whole capture, so an Imp made a turn after its cast still finds both. The capture's
framing, torn frames and missed ticks are dk2record.py's.
Reads the capture through reverse-engineering/dk2capture.py (gitignored, local).
"""
import argparse
import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
for _p in (os.path.join(_HERE, "..", "..", "reverse-engineering"), r"D:\DungeonKeeperRemake\reverse-engineering"):
    if os.path.exists(os.path.join(_p, "dk2capture.py")):
        sys.path.insert(0, _p)
        break
from dk2capture import Capture  # noqa: E402

SEED = 0x15A0          # world +0x15A0 = 0x006F3FA8, the simulation stream
HEADING = 0xF4         # creature +0xF4
HEART = 0xB43          # player DungeonHeartPosition, u16 x then u16 y
IMP = 1                # creature TypeId


def step(s):
    return (s * 0x19660D + 0x3C6EF35F) & 0xFFFFFFFF


def rotr13(s):
    return ((s >> 13) | (s << 19)) & 0xFFFFFFFF


SINE = [0] * 2560
for _i in range(513):
    SINE[_i] = int(65536.0 * math.sin(2.0 * math.pi * _i / 2048.0))
SINE[512] = 65536
for _i in range(513, 1025):
    SINE[_i] = SINE[1024 - _i]
for _i in range(1025, 2048):
    SINE[_i] = -SINE[_i - 1024]
for _i in range(2048, 2560):
    SINE[_i] = SINE[_i - 2048]


def ring(hx, hy, a):
    return (hx * 0x1000 + 0x800 + ((SINE[a] * 0x2000) >> 16),
            hy * 0x1000 + 0x800 - ((SINE[a + 512] * 0x2000) >> 16))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("capture")
    ap.add_argument("--max-steps", type=int, default=200000, help="most draws looked for between two frames")
    a = ap.parse_args()

    cap = Capture(a.capture)
    draws = []             # (index, tick, rotated value) across the capture
    seed = last_tick = None
    known = set()
    imps = []              # (tick, slot, owner, x, y, heading, heart)
    for snap in cap.frames():
        w = snap.world
        if w is None:
            continue
        now = int.from_bytes(w[SEED:SEED + 4], "little")
        if seed is not None and now != seed:
            s, n = seed, 0
            while s != now and n < a.max_steps:
                s = step(s)
                n += 1
                draws.append((len(draws), snap.tick, rotr13(s)))
            if s != now:
                print("t%5d seed %08X not reached from %08X in %d steps; draws before here are unreliable"
                      % (snap.tick, now, seed, a.max_steps))
                draws.clear()
        if snap.torn or (last_tick is not None and snap.tick != last_tick + 1):
            print("t%5d frame %s (previous t%s)" % (snap.tick, "torn" if snap.torn else "after a gap", last_tick))
        seed, last_tick = now, snap.tick
        for rec in snap.things("creature"):
            if rec.ptr in known or rec.f("creature_id") != IMP:
                continue
            known.add(rec.ptr)
            owner = rec.f("owner")
            p = snap.player(owner - 1)
            heart = (int.from_bytes(p[HEART:HEART + 2], "little"), int.from_bytes(p[HEART + 2:HEART + 4], "little"))
            imps.append((snap.tick, rec.slot, owner, rec.f("x"), rec.f("y"), rec.u(HEADING, "<H") & 0x7FF, heart))

    print("%d draws numbered, %d Imps" % (len(draws), len(imps)))
    for tick, slot, owner, x, y, heading, (hx, hy) in imps:
        angles = [d for d in draws if d[1] <= tick and ring(hx, hy, d[2] % 0x800) == (x, y)]
        h800 = [d for d in draws if d[2] % 0x800 == heading]
        h4 = [d for d in draws if (d[2] % 4) << 9 == heading]
        near = [d for d in h800 + h4 if tick - 30 <= d[1] <= tick]
        print("t%5d Imp slot %d owner %d at (%d,%d) = (%.3f,%.3f) heading %d, Heart (%d,%d)"
              % (tick, slot, owner, x, y, x / 4096.0, y / 4096.0, heading, hx, hy))
        print("       angle draws  %s" % [(i, t, v % 0x800) for i, t, v in angles])
        print("       heading 0x800 %s" % [(i, t) for i, t, _ in h800 if tick - 30 <= t <= tick])
        print("       heading 4<<9  %s" % [(i, t) for i, t, _ in h4 if tick - 30 <= t <= tick][-12:])
        if not angles:
            print("       no draw gives this point from the Heart: moved before the frame, or not a ring cast")
        if not near:
            print("       no heading draw within 30 ticks")
    by_tick = {}
    for tick, slot, owner, *_ in imps:
        by_tick.setdefault(tick, []).append(owner)
    for tick, owners in sorted(by_tick.items()):
        print("t%5d new Imps for owners %s" % (tick, owners))


if __name__ == "__main__":
    main()

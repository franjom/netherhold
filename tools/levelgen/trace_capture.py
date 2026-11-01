"""Trace the objects of one type and every live creature through a dk2record.py capture.

    python tools/levelgen/trace_capture.py <capture.dk2cap> [--object-type 74]

Prints only changes: an object appearing, moving its roll direction, changing health or wear,
and disappearing; a creature changing health or state, and disappearing. Object fields are
boulder.md's (position +0x1A/+0x1E, health +0x40, roll +0x14A/+0x14E, wear +0x156); for other
object types the union fields mean something else (objects.md section 7).
Reads the capture through reverse-engineering/dk2capture.py (gitignored, local).
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "reverse-engineering"))
from dk2capture import Capture  # noqa: E402


def where(rec):
    return "(%5.2f,%5.2f)" % (rec.u(0x1A, "<i") / 4096.0, rec.u(0x1E, "<i") / 4096.0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("capture")
    ap.add_argument("--object-type", type=int, default=74, help="object TypeId to follow (74 = boulder)")
    a = ap.parse_args()

    objects, creatures, last = {}, {}, None
    for snap in Capture(a.capture).frames():
        seen_o, seen_c = set(), set()
        for rec in snap.things():
            if rec.kind == "object" and rec.f("type_id") == a.object_type:
                seen_o.add(rec.ptr)
                now = (rec.u(0x40, "<i"), rec.u(0x14A, "<i"), rec.u(0x14E, "<i"), rec.u(0x156, "<i"))
                old = objects.get(rec.ptr)
                if old is None:
                    print("t%5d object %08X NEW at %s health %d roll %d,%d wear %d" % ((snap.tick, rec.ptr, where(rec)) + now))
                elif old[1] != now:
                    (h0, x0, y0, w0), (h, x, y, w) = old[1], now
                    what = []
                    if (x, y) != (x0, y0):
                        what.append("roll %d,%d -> %d,%d" % (x0, y0, x, y))
                    if w != w0:
                        what.append("wear %d -> %d (%+d)" % (w0, w, w - w0))
                    if h != h0:
                        what.append("health %d -> %d" % (h0, h))
                    print("t%5d object %08X at %s %s" % (snap.tick, rec.ptr, where(rec), "; ".join(what)))
                objects[rec.ptr] = (snap.tick, now, where(rec))
            elif rec.kind == "creature" and rec.f("creature_id"):
                seen_c.add(rec.ptr)
                now = (rec.f("creature_id"), rec.f("owner"), rec.u(0x40, "<i"), rec.f("state"))
                old = creatures.get(rec.ptr)
                if old is None or old[1][2:] != now[2:]:
                    print("t%5d creature %08X type %d owner %d health %d state %d at %s" % ((snap.tick, rec.ptr) + now + (where(rec),)))
                creatures[rec.ptr] = (snap.tick, now, where(rec))
        for key in [k for k in objects if k not in seen_o]:
            t, now, at = objects.pop(key)
            print("t%5d object %08X GONE (last t%d at %s health %d wear %d)" % (snap.tick, key, t, at, now[0], now[3]))
        for key in [k for k in creatures if k not in seen_c]:
            t, now, at = creatures.pop(key)
            print("t%5d creature %08X GONE (last t%d at %s health %d)" % (snap.tick, key, t, at, now[2]))
        last = snap.tick
    print("last tick", last)


if __name__ == "__main__":
    main()

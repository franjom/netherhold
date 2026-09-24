"""Read DK2's world log (MESSAGE.LOG) and print each creature's state, dump by dump.

    python tools/worldlog.py <MESSAGE.LOG or .log.gz> [--timeline]

The world log is DKII.EXE's own dump, `0x0050EA70`, written when something calls it
(docs/research/debug-dumpers.md, "Running one"). One file holds every dump taken in
a session, each opened by `World Log at GameTick N` and closed by `End World Log`.

Printed per dump: every creature's current state against the state it will resume
(`UtilityNewState`, +0xDF) and the one it saved (`StateBeforeUtilityState`, +0x40),
counted by triple, with the state labels from creature-state-table.md. Then the
checks travel-states.md section 10 reports: which travel state carries which kind
of utility data, and every creature holding a resume outside a travel state.
"""
import collections
import gzip
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATE_NOTE = os.path.join(ROOT, "docs", "research", "creature-state-table.md")
TRAVEL = (6, 7)

# A creature's fields, by indent: four spaces for the creature dumper's own, six for
# the State: and Anger: sub-blocks (the state object and the job-and-needs object).
OWN = {"TypeId", "PlayerId", "Health", "Level"}
STATE = {"CurrentStateId", "StartedAt", "StateBeforeUtilityState", "UtilityNewState",
         "CurrentlyDoingJobId", "HungerNeed", "SleepNeeded"}


def state_labels():
    labels = {}
    with open(STATE_NOTE, encoding="utf-8-sig") as f:
        for line in f:
            for m in re.finditer(r"\| (\d+) `0x[0-9a-f]+` \| ([^|]+?) \|", line):
                labels[int(m.group(1))] = m.group(2).strip()
    return labels


def parse(path):
    """Every dump in the file: [(tick, {tag: {field: value}})]; creatures only."""
    opener = gzip.open if path.endswith(".gz") else open
    dumps, creatures, thing, section, want_data = [], None, None, None, False
    with opener(path, "rt", encoding="latin-1") as f:
        for raw in f:
            line = raw.rstrip("\r\n")
            m = re.match(r"World Log at GameTick (\d+)", line)
            if m:
                creatures = {}
                dumps.append((int(m.group(1)), creatures))
                thing = None
                continue
            if creatures is None:
                continue
            if line.startswith("End World Log"):
                creatures, thing = None, None
                continue
            m = re.match(r"  Thing TagId (\d+)$", line)
            if m:
                thing, section = {"tag": int(m.group(1))}, None
                continue
            if re.match(r"  \S", line):
                thing = None
                continue
            if thing is None:
                continue
            if want_data:
                thing["UtilityStateData"] = line.strip()
                want_data = False
                continue
            indent = len(line) - len(line.lstrip(" "))
            body = line.strip()
            if indent == 4 and body.endswith(":"):
                section = body[:-1]
            if body == "Utility State Data:":
                want_data = True
                continue
            key, _, value = body.partition(" ")
            if (indent == 4 and key in OWN) or (indent == 6 and key in STATE and section in ("State", "Anger")):
                thing.setdefault(key, int(value))
            if key == "CurrentStateId" and indent == 6:
                creatures[thing["tag"]] = thing
    return dumps


def main(argv):
    if len(argv) < 2:
        sys.exit(__doc__)
    labels = state_labels()
    name = lambda s: f"{s} {labels.get(s, 'NOT IN THE TABLE')}"
    dumps = parse(argv[1])
    print("dumps:", ", ".join(f"GT {t} ({len(c)} creatures)" for t, c in dumps))

    triples = collections.Counter()
    data_kind = collections.Counter()
    held = collections.Counter()
    for _, creatures in dumps:
        for c in creatures.values():
            s, resume, saved = c["CurrentStateId"], c["UtilityNewState"], c["StateBeforeUtilityState"]
            triples[(s, resume, saved)] += 1
            data_kind[(s in TRAVEL, s, c.get("UtilityStateData", "").split(" ")[0])] += 1
            if s not in TRAVEL and resume not in (0, 1):
                held[(s, resume, saved)] += 1

    print("\ncount  state -> resume (saved)")
    for (s, resume, saved), n in sorted(triples.items()):
        print(f"{n:5}  {name(s)} -> {name(resume)} ({name(saved)})")
    print("\nutility data by state (travel first)")
    for (_, s, kind), n in sorted(data_kind.items(), reverse=True):
        print(f"{n:5}  {name(s)}: {kind}")
    print("\na resume held outside a travel state")
    for (s, resume, saved), n in sorted(held.items()):
        print(f"{n:5}  {name(s)} holding {name(resume)} (saved {name(saved)})")

    if "--timeline" in argv:
        print("\ntimeline: state->resume per dump")
        tags = sorted(set().union(*(c.keys() for _, c in dumps)))
        for tag in tags:
            row = [c.get(tag) for _, c in dumps]
            cells = [f"{r['CurrentStateId']}->{r['UtilityNewState']}" if r else "-" for r in row]
            first = next(r for r in row if r)
            print(f"{tag:6} type {first['TypeId']:3} p{first['PlayerId']}  " + " | ".join(cells))


if __name__ == "__main__":
    main(sys.argv)

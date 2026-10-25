# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Summarize diagnostic CPU hierarchies without treating them as FPS acceptance."""

import argparse
import collections
import csv
import json
import statistics
from pathlib import Path


def summarize(capture, samples_name="cpu-samples.csv"):
    with (capture / "frames.csv").open(encoding="utf-8-sig", newline="") as source:
        frames = {int(r["unity_frame"]): r for r in csv.DictReader(source) if float(r["intro_seconds"]) >= 0}
    totals = collections.defaultdict(lambda: [0.0, 0.0, 0, 0, 0.0])
    worst = []
    covered = set()
    with (capture / samples_name).open(encoding="utf-8-sig", newline="") as source:
        for row in csv.DictReader(source):
            frame = int(row["unity_frame"])
            if frame not in frames:
                continue
            covered.add(frame)
            intro = float(frames[frame]["intro_seconds"])
            phase = "closeup" if intro < 6 else "after"
            duration, own = float(row["sample_ms"]), float(row["self_ms"])
            key = phase, row["thread"], row["name"], row["parent"]
            value = totals[key]
            value[0] += duration
            value[1] += own
            value[2] += int(row["allocation_bytes"])
            value[3] += 1
            value[4] = max(value[4], duration)
            if duration >= 16.6666667 and row["name"] and row["name"] not in ("PlayerLoop", "EditorLoop", "Idle", row["thread"]):
                worst.append(dict(frame=frame, intro_seconds=intro, thread=row["thread"], name=row["name"],
                                  parent=row["parent"], inclusive_ms=duration, self_ms=own))
    missing = [frame for frame, row in frames.items() if float(row["intro_seconds"]) < 6 and frame not in covered]
    if missing:
        raise ValueError(f"CPU profile is missing {len(missing)} closeup frames")
    phases = {}
    for phase, lo, hi in (("closeup", 0, 6), ("after", 6, float("inf"))):
        values = [float(r["loop_ms"]) for r in frames.values() if lo <= float(r["intro_seconds"]) < hi]
        entries = [dict(thread=k[1], name=k[2], parent=k[3], inclusive_ms=v[0], self_ms=v[1],
                        allocated_bytes=v[2], calls=v[3], max_ms=v[4]) for k, v in totals.items() if k[0] == phase]
        active_entries = [e for e in entries if e["name"] and e["name"] not in ("Idle", e["thread"])]
        phases[phase] = dict(frames=len(values), loop_median_ms=statistics.median(values) if values else None,
                             loop_max_ms=max(values) if values else None,
                             top_self=sorted(active_entries, key=lambda e: e["self_ms"], reverse=True)[:30],
                             top_allocations=sorted((e for e in entries if e["allocated_bytes"]),
                                                    key=lambda e: e["allocated_bytes"], reverse=True)[:20])
    return dict(scope="Development CPU diagnostics only. Nested inclusive samples and concurrent threads must not be added.",
                sample_file=samples_name,
                correlation="Unity frame labels match CSV callbacks; loop intervals span LateUpdate callbacks, not profiler frame boundaries.",
                phases=phases, worst_samples=sorted(worst, key=lambda e: e["inclusive_ms"], reverse=True)[:60])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--samples", default="cpu-samples.csv")
    args = parser.parse_args()
    report = json.dumps(summarize(args.capture, args.samples), indent=2, allow_nan=False)
    if args.output:
        args.output.write_text(report + "\n", encoding="utf-8")
    else:
        print(report)

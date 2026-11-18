# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Diffs two performance reports of the Godot harness (migration plan step 3o, godot/README.md
"Measuring performance").

    python godot/perf_compare.py BASE CANDIDATE [--phase all|still|moving] [--metrics frameMs,renderGpuMs]

BASE and CANDIDATE are each a report (.json), a directory of them, a label prefix (dir/classic: the
reports godot/perf.ps1 wrote with -Label classic) or a glob. Each side must hold one label. Reports are
grouped by level and render size; repeats of a group are combined by the median of each statistic, and
their spread (min..max of frameMs p50) is shown as the noise. A positive change is slower or bigger.
Settings that make two sides incomparable (camera script, frame counts, builds, GPU, seed) are warned
about. Standard library only.
"""
import argparse
import glob
import json
import os
import statistics
import sys

DEFAULT_METRICS = ["frameMs", "processMs", "renderCpuMs", "renderGpuMs", "setupCpuMs", "drawCalls", "objects",
                   "primitives", "videoMemoryMiB", "allocatedBytes", "gen0Collections"]
INFO_KEYS = ["cameraScript", "godot", "engineBuild", "assemblyBuild", "vsync", "gpu", "seed"]
FRAME_KEYS = ["measured", "warmUp"]


def reports(spec):
    label = None
    if os.path.isfile(spec):
        paths = [spec]
    elif os.path.isdir(spec):
        paths = glob.glob(os.path.join(spec, "*.json"))
    elif glob.glob(spec):
        paths = glob.glob(spec)
    else:
        # A label prefix: <label>-<level>-<size>-r<n>.json, and labels hold no '-', so dir/classic is not classic-hi.
        label = os.path.basename(spec)
        paths = [p for p in glob.glob(spec + "-*.json") if os.path.basename(p).split("-")[0] == label]
    loaded = []
    for path in sorted(paths):
        with open(path, encoding="utf-8-sig") as handle:
            report = json.load(handle)
        if report.get("schema") != "dk2-godot-perf/1":
            continue
        if label is not None and report.get("machine", {}).get("label", label) != label:
            continue
        loaded.append(report)
    if not loaded:
        sys.exit(f"no dk2-godot-perf/1 reports at {spec}")
    labels = sorted({r.get("machine", {}).get("label", "?") for r in loaded})
    if len(labels) > 1:
        sys.exit(f"{spec} holds the labels {', '.join(labels)}; name one (dir/<label>)")
    return loaded


def grouped(loaded):
    groups = {}
    for report in loaded:
        info = report.get("info", {})
        groups.setdefault((info.get("level", "?"), info.get("resolution", "?")), []).append(report)
    return groups


def stat_of(report, phase, metric, stat):
    return report.get("phases", {}).get(phase, {}).get(metric, {}).get(stat)


def combined(group, phase, metric, stat):
    values = [v for v in (stat_of(r, phase, metric, stat) for r in group) if v is not None]
    return statistics.median(values) if values else None


def spread(group, phase):
    values = [v for v in (stat_of(r, phase, "frameMs", "p50") for r in group) if v is not None]
    return f"{min(values):.2f}..{max(values):.2f}" if len(values) > 1 else "-"


def load(group):
    values = [v for v in (r.get("machine", {}).get("otherLoadMeanPercent") for r in group) if v is not None]
    return f"{statistics.mean(values):.0f}%" if values else "?"


def label(group):
    return group[0].get("machine", {}).get("label", "?")


def mismatches(b, c):
    found = []
    for key in INFO_KEYS:
        vb = {r.get("info", {}).get(key) for r in b}
        vc = {r.get("info", {}).get(key) for r in c}
        if vb != vc:
            found.append(f"{key} {sorted(map(str, vb))} vs {sorted(map(str, vc))}")
    for key in FRAME_KEYS:
        vb = {r.get("frames", {}).get(key) for r in b}
        vc = {r.get("frames", {}).get(key) for r in c}
        if vb != vc:
            found.append(f"frames.{key} {sorted(map(str, vb))} vs {sorted(map(str, vc))}")
    return found


def change(base, candidate):
    if base is None or candidate is None:
        return "    n/a"
    if base == 0:
        return "      =" if candidate == 0 else "    new"
    return f"{100.0 * (candidate - base) / abs(base):+6.1f}%"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("base")
    parser.add_argument("candidate")
    parser.add_argument("--phase", default="all", choices=["all", "still", "moving"])
    parser.add_argument("--metrics", default=",".join(DEFAULT_METRICS))
    parser.add_argument("--stats", default="mean,p50,p95,p99,max")
    args = parser.parse_args()
    metrics = args.metrics.split(",")
    stats = args.stats.split(",")
    base, candidate = grouped(reports(args.base)), grouped(reports(args.candidate))
    for key in sorted(set(base) | set(candidate)):
        level, resolution = key
        if key not in base or key not in candidate:
            print(f"{level} {resolution}: only in {'base' if key in base else 'candidate'}")
            continue
        b, c = base[key], candidate[key]
        print(f"{level} {resolution} [{args.phase}]: base {label(b)} x{len(b)} "
              f"({b[0].get('info', {}).get('gpu', '?')}, other load {load(b)}, frame p50 spread {spread(b, args.phase)}), "
              f"candidate {label(c)} x{len(c)} (other load {load(c)}, frame p50 spread {spread(c, args.phase)})")
        for warning in mismatches(b, c):
            print(f"  WARNING: not like for like: {warning}")
        print(f"  {'metric':<26}" + "".join(f"{s:>11} base  {'cand':>9} {'change':>7}  " for s in stats))
        for metric in metrics:
            line = f"  {metric:<26}"
            for stat in stats:
                vb, vc = combined(b, args.phase, metric, stat), combined(c, args.phase, metric, stat)
                fb = "n/a" if vb is None else f"{vb:.3f}"
                fc = "n/a" if vc is None else f"{vc:.3f}"
                line += f"{fb:>16} {fc:>9} {change(vb, vc)}  "
            print(line)
        slow_b = [r.get("phases", {}).get(args.phase, {}).get("slowFrames") for r in b]
        slow_c = [r.get("phases", {}).get(args.phase, {}).get("slowFrames") for r in c]
        slow_b = [v for v in slow_b if v is not None]
        slow_c = [v for v in slow_c if v is not None]
        budget = b[0].get("frames", {}).get("budgetMs", 1000 / 60)
        print(f"  slow frames (over {budget:.2f} ms): base {statistics.median(slow_b) if slow_b else 'n/a'}, "
              f"candidate {statistics.median(slow_c) if slow_c else 'n/a'}")
        print()


if __name__ == "__main__":
    main()

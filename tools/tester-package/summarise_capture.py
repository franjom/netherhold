# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Reads a tester's capture (the zip from capture.ps1, unzipped) and says how it did against a tier.

Frame intervals come from the game's own recorder (recorder/frames.csv) and, when the trace ran, from
PresentMon's presents; the draw-clock evenness of the opening fly-by comes from draw_clock.py. The first
two seconds of play are warm-up and are reported but not judged. Exit 1 on fail, 2 on an unusable capture.
"""
import argparse
import csv
import json
import math
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "game-unity-probe"))
from draw_clock import analyse as draw_clock  # noqa: E402

WARM_UP_SECONDS = 2.0


def read_pairs(path):
    return dict(line.split("=", 1) for line in path.read_text(encoding="utf-8-sig").splitlines() if "=" in line)


def read_rows(path):
    with path.open(newline="", encoding="utf-8-sig") as source:
        return list(csv.DictReader(source))


def interval_metrics(values, budget):
    if not values:
        return dict(samples=0)
    ordered = sorted(values)
    pick = lambda q: ordered[min(len(ordered) - 1, math.ceil(len(ordered) * q) - 1)]
    streak = longest = 0
    for value in values:
        streak = streak + 1 if value > budget else 0
        longest = max(longest, streak)
    return dict(samples=len(values), p50_ms=statistics.median(values), p95_ms=pick(.95), p99_ms=pick(.99),
                max_ms=ordered[-1], over_budget=sum(v > budget for v in values),
                over_budget_share=sum(v > budget for v in values) / len(values), longest_over_budget_run=longest)


def worst_frames(rows, count=10):
    """The slowest frames after warm-up, with where each went, so a slow frame has a cause beside it."""
    judged = [r for r in rows if float(r["play_seconds"]) >= WARM_UP_SECONDS]
    judged.sort(key=lambda r: -float(r["frame_ms"]))
    return [dict(play_seconds=float(r["play_seconds"]), frame_ms=float(r["frame_ms"]), ticks=int(r["ticks"]),
                 wait_tick_ms=float(r["wait_tick_ms"]), present_ms=float(r["present_ms"]),
                 animated_frame_ms=float(r["animated_frame_ms"]), chunk_rebuild_ms=float(r["chunk_rebuild_ms"]),
                 gc_collections=int(r["gc_collections"]), path=r["path"] == "1")
            for r in judged[:count]]


def summarise(rows, metadata, machine, presents, tier):
    """rows: the recorder's frames; presents: PresentMon rows or None; tier: name, minimum_fps, resolution."""
    budget = 1000.0 / tier["minimum_fps"]
    problems = []
    if not rows:
        problems.append("no frames recorded")
    if metadata.get("Development", "False") != "False":
        problems.append("a development player cannot stand for a release")
    if metadata.get("Backend") != "IL2CPP":
        problems.append(f"backend is {metadata.get('Backend')!r}, players ship as IL2CPP (ADR-0690)")
    if metadata.get("Resolution") != tier["resolution"]:
        problems.append(f"resolution {metadata.get('Resolution')!r} is not the tier's {tier['resolution']}")
    for key in ("CPU", "GPU"):
        if not metadata.get(key, "").strip():
            problems.append(f"metadata names no {key}")
    judged = [float(r["frame_ms"]) for r in rows if float(r["play_seconds"]) >= WARM_UP_SECONDS]
    warm_up = [float(r["frame_ms"]) for r in rows if 0 < float(r["play_seconds"]) < WARM_UP_SECONDS]
    frames = interval_metrics(judged, budget)
    result = dict(tier=tier["name"], budget_ms=budget, play_seconds=float(rows[-1]["play_seconds"]) if rows else 0,
                  machine=dict(cpu=metadata.get("CPU"), gpu=metadata.get("GPU"), driver=metadata.get("GraphicsDriver"),
                               api=metadata.get("API"), resolution=metadata.get("Resolution"), vsync=metadata.get("VSync"),
                               frame_cap=metadata.get("FrameCap"), refresh_hz=metadata.get("RefreshHz"),
                               windows=machine.get("OS"), gpu_driver_windows=[v for k, v in machine.items() if k == "GPU"]),
                  frames=frames, warm_up=interval_metrics(warm_up, budget), worst_frames=worst_frames(rows),
                  draw_clock=draw_clock(rows) if rows else None)
    if presents is not None:
        intervals = []
        for row in presents:
            try:
                value = float(row["MsBetweenPresents"])
            except (KeyError, ValueError):
                continue
            if math.isfinite(value) and value > 0:
                intervals.append(value)
        # The trace spans the whole run; its first two seconds of presents are the warm-up too.
        skip, total = 0, 0.0
        while skip < len(intervals) and total < WARM_UP_SECONDS * 1000:
            total += intervals[skip]
            skip += 1
        result["presents"] = interval_metrics(intervals[skip:], budget)
        if not intervals:
            problems.append("the PresentMon trace holds no presents")
    else:
        result["presents"] = None
    failed = frames.get("over_budget", 0) > 0 or (result["presents"] or {}).get("over_budget", 0) > 0
    clock = result["draw_clock"] or {}
    if clock.get("analysed") and clock["over_limit_share"] > 0.015:
        failed = True
    result["problems"] = problems
    result["status"] = "invalid" if problems else "fail" if failed else "pass"
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path, help="the unzipped capture folder")
    parser.add_argument("--tier", default="desktop")
    args = parser.parse_args()
    target = json.loads((Path(__file__).resolve().parent.parent / "classic-unity-probe" / "performance-target.json").read_text(encoding="utf-8"))
    tier = next((t for t in target["tiers"] if t["name"] == args.tier), None)
    if tier is None:
        raise SystemExit(f"unknown tier {args.tier!r}; performance-target.json lists {[t['name'] for t in target['tiers']]}")
    recorder = args.capture / "recorder"
    rows = read_rows(recorder / "frames.csv") if (recorder / "frames.csv").exists() else []
    metadata = read_pairs(recorder / "metadata.txt") if (recorder / "metadata.txt").exists() else {}
    machine = read_pairs(args.capture / "machine.txt") if (args.capture / "machine.txt").exists() else {}
    presents = read_rows(args.capture / "presentmon.csv") if (args.capture / "presentmon.csv").exists() else None
    result = summarise(rows, metadata, machine, presents, tier)
    print(json.dumps(result, indent=2))
    return {"pass": 0, "fail": 1, "invalid": 2}[result["status"]]


if __name__ == "__main__":
    sys.exit(main())

# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Compare repeated Level6a release captures. Exit 0 pass, 1 regression, 2 invalid evidence.

This is a relative regression gate, separate from frame_budget.py's absolute FPS acceptance.
Both builds need three distinct, hands-off 40-second captures on the same idle machine/settings.
"""
import argparse
import json
import math
import re
import shlex
import statistics
import struct
import sys
import zlib
from pathlib import Path

from summarise_capture import read_pairs, read_rows, interval_metrics, draw_clock

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "classic-unity-probe"))
from frame_budget import verify_render_proof  # noqa: E402

SETTINGS = ("CPU", "GPU", "GraphicsDriver", "API", "OS", "Unity", "Resolution", "Fullscreen",
            "FrameCap", "VSync", "RefreshHz", "Level", "Seed", "Backend", "Development")
NUMBERS = ("play_seconds", "frame_ms", "wait_tick_ms", "tick_ms", "chunk_rebuild_ms",
           "gc_collections", "render_tick", "cam_x", "cam_y", "cam_z")
# Timing comparisons use medians across runs; absolute join/clock limits apply to every run.
MAX_RATIO = 1.20
MIN_RUNS = 3
DURATION_SECONDS = 40
ANALYSIS_SECONDS = 30


def arguments(path):
    structured = path.with_suffix(".json")
    tokens = (json.loads(structured.read_text(encoding="utf-8-sig")) if structured.exists() else
              shlex.split(path.read_text(encoding="utf-8-sig"), posix=False))
    if not isinstance(tokens, list) or not all(isinstance(token, str) for token in tokens):
        raise ValueError("player arguments must be a string array")
    result = []
    skip = False
    for token in tokens:
        if skip:
            skip = False
        elif token in ("-recordFrames", "-logFile", "-dk2Install"):
            skip = True
        else:
            result.append(token)
    return result


def load_capture(path):
    metadata = read_pairs(path / "recorder/metadata.txt")
    for key in SETTINGS:
        if not metadata.get(key, "").strip():
            raise ValueError(f"metadata is missing {key}")
    if metadata["Backend"] != "IL2CPP" or metadata["Development"] != "False":
        raise ValueError("a release IL2CPP player is required")
    if metadata["Level"].lower() != "level6a" or metadata["Seed"] != "1":
        raise ValueError("the fixture is Level6a, seed 1")
    if metadata["Resolution"] != "1920x1080":
        raise ValueError("the fixture is 1920x1080")
    if (metadata["Fullscreen"], metadata["FrameCap"], metadata["VSync"]) != ("False", "-1", "0"):
        raise ValueError("the fixture is windowed, uncapped and VSync off")
    machine = read_pairs(path / "machine.txt")
    if machine.get("Seconds") != str(DURATION_SECONDS):
        raise ValueError("the fixture needs a 40-second capture")
    manifest = (path / "manifest.txt").read_text(encoding="utf-8-sig")
    if not re.search(r"(?m)^Commit=[0-9a-fA-F]{40}$", manifest) or not re.search(
            r"(?m)^SHA256 GameAssembly.dll=[0-9a-fA-F]{64}$", manifest):
        raise ValueError("manifest must name the commit and GameAssembly SHA256")
    log = (path / "Player.log").read_text(encoding="utf-8-sig", errors="replace")
    if re.search(r"Exception|(?:^|\n).*\b(?:Error|ERROR|Assert|Assertion)\b", log):
        raise ValueError("Player.log contains an exception, error or assertion")
    # capture.ps1 screenshots the desktop; the player resolution is checked independently above.
    verify_render_proof(path / "screenshot.png", expected_size=None)
    player_arguments = arguments(path / "player-arguments.txt")
    if not player_arguments:
        raise ValueError("missing player arguments")
    rows = read_rows(path / "recorder/frames.csv")
    if len(rows) < 100:
        raise ValueError("too few recorded frames")
    previous = -1
    for index, row in enumerate(rows):
        values = {key: float(row[key]) for key in NUMBERS}
        if not all(math.isfinite(value) for value in values.values()):
            raise ValueError("nonfinite frame measurement")
        if any(values[key] < 0 for key in NUMBERS if key not in ("render_tick", "cam_x", "cam_y", "cam_z")):
            raise ValueError("negative frame measurement")
        if ((values["frame_ms"] == 0 and index != 0) or values["play_seconds"] < previous
                or row["path"] not in ("0", "1")):
            raise ValueError("invalid frame interval, time order or path flag")
        previous = values["play_seconds"]
    if float(rows[0]["play_seconds"]) > .5 or not ANALYSIS_SECONDS <= previous <= 42:
        raise ValueError("40-second wall capture must contain at least 30 seconds of gameplay")
    measured_seconds = sum(float(row["frame_ms"]) for row in rows[1:]) / 1000
    if abs(measured_seconds - (previous - float(rows[0]["play_seconds"]))) > .5:
        raise ValueError("frame intervals do not cover the recorded time (missing samples)")
    # capture.ps1's timer includes level loading. Compare the same first 30 gameplay seconds.
    end = next(index for index, row in enumerate(rows) if float(row["play_seconds"]) >= ANALYSIS_SECONDS)
    rows = rows[:end + 1]
    path_rows = [row for row in rows if row["path"] == "1"]
    if len({(row["cam_x"], row["cam_y"], row["cam_z"]) for row in path_rows}) < 2:
        raise ValueError("no moving fly-by camera")
    clock = draw_clock(rows)
    if clock.get("analysed", 0) < 10 or not clock.get("steady_frames", 0):
        raise ValueError("insufficient fly-by draw-clock samples")
    frames = interval_metrics([float(row["frame_ms"]) for row in rows], 1000 / 60)
    ticks = [float(row["tick_ms"]) for row in rows if float(row["tick_ms"]) > 0]
    chunks = [float(row["chunk_rebuild_ms"]) for row in rows if float(row["chunk_rebuild_ms"]) > 0]
    if not ticks:
        raise ValueError("no simulation ticks measured")
    metrics = dict(frame_p99_ms=frames["p99_ms"], frame_max_ms=frames["max_ms"],
                   slow_frame_share=frames["over_budget_share"],
                   tick_p95_ms=interval_metrics(ticks, 0)["p95_ms"],
                   chunk_max_ms=max(chunks, default=0),
                   gc_per_second=sum(float(row["gc_collections"]) for row in rows) / ANALYSIS_SECONDS)
    return dict(path=str(path), settings={key: metadata[key] for key in SETTINGS},
                arguments=player_arguments, manifest=manifest, metrics=metrics,
                join_waits=sum(float(row["wait_tick_ms"]) > 1 for row in rows),
                draw_clock=clock)


def compare(baseline, candidate):
    result = dict(status="invalid", problems=[], regressions=[], baseline=[], candidate=[])
    for name, paths in (("baseline", baseline), ("candidate", candidate)):
        resolved = [Path(path).resolve() for path in paths]
        if len(resolved) < MIN_RUNS or len(set(resolved)) != len(resolved):
            result["problems"].append(f"{name}: supply at least three distinct capture directories")
        for path in resolved:
            try:
                result[name].append(load_capture(path))
            except (OSError, ValueError, KeyError, IndexError, ZeroDivisionError, struct.error, zlib.error) as error:
                result["problems"].append(f"{path}: {error}")
    all_runs = result["baseline"] + result["candidate"]
    if all_runs:
        reference = all_runs[0]
        for run in all_runs[1:]:
            if run["settings"] != reference["settings"] or run["arguments"] != reference["arguments"]:
                result["problems"].append(f"{run['path']}: hardware or player settings differ")
        for name in ("baseline", "candidate"):
            if len({run["manifest"] for run in result[name]}) > 1:
                result["problems"].append(f"{name}: captures come from different builds")
    if {Path(path).resolve() for path in baseline} & {Path(path).resolve() for path in candidate}:
        result["problems"].append("baseline and candidate reuse a capture")
    if result["problems"]:
        return result
    result["limits"] = dict(max_ratio=MAX_RATIO, max_join_wait_ms=1, max_draw_clock_share=.015)
    result["comparison"] = {}
    for key in result["baseline"][0]["metrics"]:
        aggregate = max if key == "frame_max_ms" else statistics.median
        before = aggregate(run["metrics"][key] for run in result["baseline"])
        after = aggregate(run["metrics"][key] for run in result["candidate"])
        # Avoid ratios against zero; one extra GC per window and tiny timing noise are tolerated.
        allowance = 1 / ANALYSIS_SECONDS if key == "gc_per_second" else .001 if key == "slow_frame_share" else .25
        limit = max(before * MAX_RATIO, before + allowance)
        result["comparison"][key] = dict(baseline=before, candidate=after, limit=limit)
        if after > limit:
            result["regressions"].append(key)
    for run in result["candidate"]:
        if run["join_waits"]:
            result["regressions"].append(f"{run['path']}: tick join over 1 ms")
        if run["draw_clock"]["steady_over_limit_share"] > .015:
            result["regressions"].append(f"{run['path']}: draw-clock unevenness")
    result["status"] = "fail" if result["regressions"] else "pass"
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", nargs="+", required=True, type=Path)
    parser.add_argument("--candidate", nargs="+", required=True, type=Path)
    args = parser.parse_args()
    result = compare(args.baseline, args.candidate)
    print(json.dumps(result, indent=2))
    return {"pass": 0, "fail": 1, "invalid": 2}[result["status"]]


if __name__ == "__main__":
    sys.exit(main())

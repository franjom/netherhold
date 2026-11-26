# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Evenness of a scripted fly-by in a frames.csv from the game probe or the release recorder.

The draw clock (render_tick) should advance 4 ticks a wall second whatever the frame rate; a frame
whose advance per second is off by more than 2 ticks is a visible step (docs/research/level8-intro-motion.md).
Prints the share of path frames that fail, p95 error, boundary holds and catch-ups, and the same
counted over steady frames only (neither this frame nor the one before over 25 ms), which separates the
clock from frame-time hitches. Exit 1 when the share of path frames over 2 ticks/s is above --limit.
"""
import argparse
import csv
import json
import sys

TICKS_PER_SECOND = 4.0
ERROR_LIMIT = 2.0
STEADY_FRAME_MS = 25.0


def wait(row):
    try:
        return float(row["wait_tick_ms"])
    except (KeyError, ValueError):
        return 0.0


def analyse(rows):
    """rows: dicts with frame_ms (the interval ending at the row), render_tick, path ('1' while playing)
    and, when present, wait_tick_ms: the clock is sampled after the tick join, so a frame that waited
    covers its wait as well, and the frame after it does not."""
    seq = [r for r in rows if r["path"] == "1"]
    if len(seq) < 10:
        return dict(path_frames=len(seq), analysed=0)
    errors, steps, steady_errors = [], [], []
    for before, now in zip(seq, seq[1:]):
        dt = (float(now["frame_ms"]) + wait(now) - wait(before)) / 1000.0
        if dt <= 0:
            continue
        step = float(now["render_tick"]) - float(before["render_tick"])
        error = abs(step / dt - TICKS_PER_SECOND)
        errors.append(error)
        steps.append(step)
        if float(now["frame_ms"]) <= STEADY_FRAME_MS and float(before["frame_ms"]) <= STEADY_FRAME_MS:
            steady_errors.append(error)
    ordered = sorted(errors)
    median_step = sorted(steps)[len(steps) // 2]
    over = sum(e > ERROR_LIMIT for e in errors)
    steady_over = sum(e > ERROR_LIMIT for e in steady_errors)
    return dict(
        path_frames=len(seq), analysed=len(errors),
        over_limit=over, over_limit_share=over / len(errors),
        p95_error=ordered[min(len(ordered) - 1, int(0.95 * len(ordered)))], max_error=ordered[-1],
        holds=sum(s < 0.5 * median_step for s in steps), catch_ups=sum(s > 1.5 * median_step for s in steps),
        steady_frames=len(steady_errors), steady_over_limit=steady_over,
        steady_over_limit_share=steady_over / len(steady_errors) if steady_errors else None)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("frames", help="frames.csv")
    parser.add_argument("--limit", type=float, default=0.015, help="largest share of path frames over 2 ticks/s that passes")
    args = parser.parse_args()
    with open(args.frames, newline="", encoding="utf-8") as source:
        result = analyse(list(csv.DictReader(source)))
    result["status"] = ("no path" if result["analysed"] == 0 else
                        "pass" if result["over_limit_share"] <= args.limit else "fail")
    print(json.dumps(result, indent=2))
    return 0 if result["status"] != "fail" else 1


if __name__ == "__main__":
    sys.exit(main())

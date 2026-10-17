# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Summarize verified idle/rotation frames; keep call-stack capture timings separate."""

import csv
import json
import math
import statistics
import sys
from pathlib import Path


def summarize(directory):
    directory = Path(directory)
    metadata = (directory / "metadata.txt").read_text(encoding="utf-8-sig")
    if "RenderingVerified=True" not in metadata or "Completed=True" not in metadata:
        raise ValueError("Capture has not passed rendering verification")
    with (directory / "frames.csv").open(newline="", encoding="utf-8") as stream:
        rows = list(csv.DictReader(stream))
    result = {"run": str(directory), "allocation_trace": "AllocationTrace=True" in metadata, "phases": {}}
    for phase in ("IdleFirst", "RotateFirst", "IdleMiddle", "RotateWarm", "IdleLast"):
        selected = [row for row in rows if row["phase"] == phase]
        if not selected:
            raise ValueError(f"Missing phase {phase}")
        summary = {"frames": len(selected)}
        for key in selected[0]:
            if key in ("phase", "unity_frame", "yaw_degrees"):
                continue
            values = sorted(float(row[key]) for row in selected if math.isfinite(float(row[key])))
            summary[key] = {
                "samples": len(values), "median": statistics.median(values) if values else None,
                "p95": values[math.ceil(len(values) * .95) - 1] if values else None,
                "max": values[-1] if values else None,
            }
        yaw = [float(row["yaw_degrees"]) for row in selected]
        travel = sum(abs((b - a + 180) % 360 - 180) for a, b in zip(yaw, yaw[1:]))
        if phase.startswith("Rotate") and travel < (1 if result["allocation_trace"] else 360):
            raise ValueError(f"Camera did not rotate sufficiently in {phase}: {travel}")
        if phase.startswith("Idle") and travel > .01:
            raise ValueError(f"Camera moved during idle phase {phase}: {travel}")
        summary["yaw_travel_degrees"] = travel
        result["phases"][phase] = summary
    return result


if __name__ == "__main__":
    result = summarize(sys.argv[1])
    output = json.dumps(result, indent=2, allow_nan=False) + "\n"
    Path(sys.argv[1], "summary.json").write_text(output, encoding="utf-8")
    print(output)

# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Summarize completed windowed frame captures without treating unavailable GPU data as zero."""

import csv
import json
import math
import statistics
import struct
import sys
from pathlib import Path


def summarize(directory):
    directory = Path(directory)
    with (directory / "frames.csv").open(newline="", encoding="utf-8") as stream:
        rows = list(csv.DictReader(stream))
    if len(rows) != 240:
        raise ValueError(f"Expected 240 captured rows, got {len(rows)}")
    metadata = (directory / "metadata.txt").read_text(encoding="utf-8")
    if "Completed=True" not in metadata or "BatchMode=False" not in metadata:
        raise ValueError("Capture did not complete through the non-batch player loop")
    result = {"run": str(directory), "rendering_verified": "RenderingVerified=True" in metadata, "views": {}}
    for view in ("quiet", "dense"):
        selected = [row for row in rows if row["view"] == view]
        if len(selected) != 120:
            raise ValueError(f"Expected 120 {view} rows, got {len(selected)}")
        summary = {}
        for name in selected[0]:
            if name in ("view", "frame"):
                continue
            values = [float(row[name]) for row in selected if math.isfinite(float(row[name]))]
            values.sort()
            summary[name] = {"valid_samples": len(values), "median": statistics.median(values) if values else None,
                             "p95": values[math.ceil(len(values) * .95) - 1] if values else None}
        header = (directory / (view + ".png")).read_bytes()[:24]
        if header[:8] != b"\x89PNG\r\n\x1a\n":
            raise ValueError(f"Missing PNG proof for {view}")
        summary["screenshot_size"] = list(struct.unpack(">II", header[16:24]))
        result["views"][view] = summary
    result["gpu_timing_available"] = result["rendering_verified"] and all(
        result["views"][view]["timing_gpu_ms"]["valid_samples"] > 0 for view in ("quiet", "dense"))
    return result


if __name__ == "__main__":
    result = summarize(sys.argv[1])
    output = json.dumps(result, indent=2, allow_nan=False)
    Path(sys.argv[1], "summary.json").write_text(output + "\n", encoding="utf-8")
    print(output)

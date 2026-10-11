# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
import csv
import math
import statistics
import sys
from pathlib import Path

for filename in sys.argv[1:]:
    path = Path(filename)
    with path.open(encoding="utf-8-sig", newline="") as source:
        rows = list(csv.DictReader(source))
    print(f"{path.name}: {len(rows)} frames")
    for key in rows[0]:
        values = sorted(float(row[key]) for row in rows if math.isfinite(float(row[key])))
        if not values:
            print(f"  {key}: unavailable")
            continue
        print(f"  {key}: median={statistics.median(values):.4f}, p95={values[math.ceil(.95 * len(values)) - 1]:.4f}, max={values[-1]:.4f}, available={len(values)}")

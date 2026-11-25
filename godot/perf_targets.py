# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Checks harness reports against the Godot performance targets (godot/README.md, "Measuring performance").

    python godot/perf_targets.py REPORTS [--targets tools/tester-package/performance-target.json]

REPORTS is what godot/perf_compare.py takes for one side (a report, a directory, dir/<label> or a glob).
The targets' "harness" section holds, per level at its render size, the medians measured on the floor
machine; a metric fails when the median of the repeats is above the larger of baseline * (1 + relative) and
baseline + absolute, the regression gate's rule (docs/PERFORMANCE.md). The harness runs Debug C# in the
editor binary: these are regression ceilings for changes, not the release tiers. Exit 0 within every
ceiling, 1 over one, 2 when no report matches a target level. Standard library only.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from perf_compare import combined, grouped, reports  # noqa: E402

DEFAULT_TARGETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools", "tester-package",
                               "performance-target.json")


def ceiling(baseline, allowance):
    return max(baseline * (1 + allowance["relative"]), baseline + allowance["absolute"])


def check(groups, harness):
    """Lines of the check and whether any metric was over its ceiling; None when no group matched."""
    lines, over, matched = [], False, False
    for level, metrics in sorted(harness["levels"].items()):
        group = groups.get((level, harness["renderSize"]))
        if not group:
            lines.append(f"{level} {harness['renderSize']}: no report")
            continue
        matched = True
        lines.append(f"{level} {harness['renderSize']} [{harness['phase']}] x{len(group)}")
        for metric, stats in sorted(metrics.items()):
            for stat, baseline in sorted(stats.items()):
                value = combined(group, harness["phase"], metric, stat)
                limit = ceiling(baseline, harness["allowance"])
                failed = value is None or value > limit
                over |= failed
                shown = "n/a" if value is None else f"{value:.3f}"
                lines.append(f"  {metric} {stat}: {shown} (baseline {baseline:.3f}, ceiling {limit:.3f})"
                             + ("  OVER" if failed else ""))
    return lines, (over if matched else None)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports")
    parser.add_argument("--targets", default=DEFAULT_TARGETS)
    args = parser.parse_args()
    with open(args.targets, encoding="utf-8") as handle:
        harness = json.load(handle)["harness"]
    lines, over = check(grouped(reports(args.reports)), harness)
    print("\n".join(lines))
    return 2 if over is None else 1 if over else 0


if __name__ == "__main__":
    sys.exit(main())

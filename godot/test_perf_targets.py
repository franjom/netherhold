# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

import json
import os
import unittest

from perf_targets import DEFAULT_TARGETS, ceiling, check

HARNESS = dict(renderSize="1920x1080", phase="all", allowance=dict(relative=0.2, absolute=0.25),
               levels=dict(Level1=dict(frameMs=dict(p50=5.0, p99=10.0)), Level7=dict(frameMs=dict(p50=1.0))))


def report(level, p50, p99=9.0):
    return dict(info=dict(level=level, resolution="1920x1080"), phases=dict(all=dict(frameMs=dict(p50=p50, p99=p99))))


class PerfTargetsTests(unittest.TestCase):
    def test_the_ceiling_is_the_larger_of_the_relative_and_absolute_allowance(self):
        self.assertAlmostEqual(6.0, ceiling(5.0, HARNESS["allowance"]))
        self.assertAlmostEqual(1.25, ceiling(1.0, HARNESS["allowance"]))

    def test_the_median_of_the_repeats_is_held_to_each_ceiling(self):
        groups = {("Level1", "1920x1080"): [report("Level1", 5.5), report("Level1", 7.0), report("Level1", 5.9)]}
        lines, over = check(groups, HARNESS)
        self.assertFalse(over)
        self.assertIn("Level7 1920x1080: no report", lines)
        groups[("Level1", "1920x1080")].append(report("Level1", 6.5))
        self.assertTrue(check(groups, HARNESS)[1])

    def test_no_matching_report_is_no_verdict(self):
        self.assertIsNone(check({("Level1", "1024x576"): [report("Level1", 5.0)]}, HARNESS)[1])

    def test_the_committed_targets_hold_a_harness_section_for_the_floor_machine(self):
        with open(DEFAULT_TARGETS, encoding="utf-8") as handle:
            harness = json.load(handle)["harness"]
        self.assertEqual("1920x1080", harness["renderSize"])
        self.assertTrue({"Level1", "Level7", "Level6a"} <= set(harness["levels"]))
        self.assertTrue(os.path.isfile(DEFAULT_TARGETS))


if __name__ == "__main__":
    unittest.main()

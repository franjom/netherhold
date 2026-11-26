# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

import unittest

from draw_clock import analyse


def frames(steps, frame_ms=16.667):
    tick, rows = 0.0, []
    for step in steps:
        tick += step
        rows.append(dict(frame_ms=str(frame_ms), render_tick=repr(tick), path="1"))
    return rows


class DrawClockTests(unittest.TestCase):
    def test_even_clock_passes(self):
        result = analyse(frames([4 / 60] * 100))
        self.assertEqual(0, result["over_limit"])
        self.assertEqual(0, result["holds"])
        self.assertEqual(0, result["catch_ups"])

    def test_boundary_hold_then_catch_up_counts_as_two_errors(self):
        steps = [4 / 60] * 100
        steps[50], steps[51] = 0.011, 0.15
        result = analyse(frames(steps))
        self.assertEqual(2, result["over_limit"])
        self.assertEqual(1, result["holds"])
        self.assertEqual(1, result["catch_ups"])

    def test_long_frame_is_not_a_steady_frame(self):
        rows = frames([4 / 60] * 100)
        rows[50]["frame_ms"] = "40"  # the clock advanced one frame's worth over 40 ms: an error, but a frame-time one
        result = analyse(rows)
        self.assertEqual(1, result["over_limit"])
        self.assertEqual(0, result["steady_over_limit"])

    def test_a_join_shifts_the_interval_the_clock_covers(self):
        # Frame 50 waited 10 ms for the tick after its interval was measured, so its clock step covers
        # 26.667 ms and frame 51's interval of 26.667 ms covers only a normal step.
        steps = [4 / 60] * 100
        steps[50], steps[51] = 4 / 60 * (26.667 / 16.667), 4 / 60
        rows = frames(steps)
        rows[50]["wait_tick_ms"] = "10"
        rows[51]["frame_ms"] = "26.667"
        self.assertEqual(0, analyse(rows)["over_limit"])

    def test_frames_off_the_path_are_ignored(self):
        rows = frames([4 / 60] * 20)
        for row in rows:
            row["path"] = "0"
        self.assertEqual(0, analyse(rows)["analysed"])


if __name__ == "__main__":
    unittest.main()

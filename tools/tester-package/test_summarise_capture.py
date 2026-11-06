# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

import unittest

from summarise_capture import summarise, interval_metrics

TIER = dict(name="desktop", minimum_fps=120, resolution="1920x1080")
METADATA = dict(Backend="IL2CPP", Development="False", Resolution="1920x1080", CPU="i5", GPU="RTX 3060")


def frames(count, frame_ms=8.0, path_until=0):
    rows, tick = [], 0.0
    for i in range(count):
        tick += frame_ms / 250.0  # 4 ticks a second
        rows.append(dict(play_seconds=f"{i * frame_ms / 1000:.3f}", frame_ms=f"{frame_ms:.3f}", ticks="0",
                         wait_tick_ms="0", present_ms="1", animated_frame_ms="0.5", chunk_rebuild_ms="0",
                         gc_collections="0", render_tick=repr(tick), path="1" if i < path_until else "0"))
    return rows


class SummariseTests(unittest.TestCase):
    def test_even_eight_millisecond_frames_pass_the_desktop_tier(self):
        result = summarise(frames(1500, path_until=600), METADATA, {}, None, TIER)
        self.assertEqual("pass", result["status"])
        self.assertEqual(0, result["frames"]["over_budget"])
        self.assertEqual(0, result["draw_clock"]["over_limit"])

    def test_one_slow_frame_after_warm_up_fails(self):
        rows = frames(1500)
        rows[1000]["frame_ms"] = "9.5"
        result = summarise(rows, METADATA, {}, None, TIER)
        self.assertEqual("fail", result["status"])
        self.assertEqual(1, result["frames"]["over_budget"])
        self.assertEqual(8.0, result["worst_frames"][1]["frame_ms"])
        self.assertEqual(9.5, result["worst_frames"][0]["frame_ms"])

    def test_slow_frames_in_the_first_two_seconds_are_warm_up(self):
        rows = frames(1500)
        rows[10]["frame_ms"] = "60"
        result = summarise(rows, METADATA, {}, None, TIER)
        self.assertEqual("pass", result["status"])
        self.assertEqual(1, result["warm_up"]["over_budget"])

    def test_mono_or_development_or_wrong_resolution_is_invalid(self):
        for change in (dict(Backend="Mono"), dict(Development="True"), dict(Resolution="2560x1440"), dict(GPU="")):
            metadata = dict(METADATA, **change)
            self.assertEqual("invalid", summarise(frames(1500), metadata, {}, None, TIER)["status"], change)

    def test_presents_are_judged_too(self):
        presents = [dict(MsBetweenPresents="8.0")] * 1000 + [dict(MsBetweenPresents="12.0")]
        result = summarise(frames(1500), METADATA, {}, presents, TIER)
        self.assertEqual("fail", result["status"])
        self.assertEqual(1, result["presents"]["over_budget"])

    def test_interval_metrics(self):
        metrics = interval_metrics([8, 8, 9, 9, 10], budget=8.5)
        self.assertEqual(3, metrics["over_budget"])
        self.assertEqual(3, metrics["longest_over_budget_run"])
        self.assertEqual(10, metrics["max_ms"])


if __name__ == "__main__":
    unittest.main()

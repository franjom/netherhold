# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

import csv
import importlib.util
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("arrival_cpu", Path(__file__).with_name("summarize-arrival-cpu.py"))
arrival_cpu = importlib.util.module_from_spec(spec)
spec.loader.exec_module(arrival_cpu)


class ArrivalCpuTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.capture = Path(self.directory.name)
        self.frames = [dict(unity_frame=i, intro_seconds=t, loop_ms=ms) for i, t, ms in ((1, 0, 50), (2, 5, 20), (3, 7, 4))]
        self.samples = [dict(unity_frame=i, thread="Main Thread", name="DK2.Work", parent="PlayerLoop",
                             sample_ms=10, self_ms=5, allocation_bytes=0) for i in (1, 2, 3)]

    def run_summary(self):
        for name, rows in (("frames.csv", self.frames), ("cpu-samples.csv", self.samples)):
            with (self.capture / name).open("w", encoding="utf-8", newline="") as stream:
                writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
                writer.writeheader()
                writer.writerows(rows)
        return arrival_cpu.summarize(self.capture)

    def test_closeup_and_after_are_separate(self):
        result = self.run_summary()
        self.assertEqual(35, result["phases"]["closeup"]["loop_median_ms"])
        self.assertEqual(4, result["phases"]["after"]["loop_median_ms"])

    def test_missing_first_use_profile_cannot_look_complete(self):
        self.samples = self.samples[1:]
        with self.assertRaisesRegex(ValueError, "missing 1 closeup frames"):
            self.run_summary()

    def test_idle_envelopes_do_not_rank_as_work(self):
        self.samples.append(dict(self.samples[0], name="Idle", sample_ms=100, self_ms=100))
        result = self.run_summary()
        self.assertEqual(["DK2.Work"], [r["name"] for r in result["phases"]["closeup"]["top_self"]])
        self.assertEqual([], result["worst_samples"])

    def test_parallel_threads_remain_distinct(self):
        self.samples.append(dict(self.samples[0], thread="Render Thread"))
        entries = self.run_summary()["phases"]["closeup"]["top_self"]
        self.assertEqual({"Main Thread", "Render Thread"}, {r["thread"] for r in entries})

    def test_zero_duration_allocations_are_counted(self):
        self.samples.append(dict(self.samples[0], name="GC.Alloc", sample_ms=0, self_ms=0, allocation_bytes=2048))
        entries = self.run_summary()["phases"]["closeup"]["top_allocations"]
        self.assertEqual(2048, entries[0]["allocated_bytes"])


if __name__ == "__main__":
    unittest.main()

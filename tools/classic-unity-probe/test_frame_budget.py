# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

import unittest
import struct
import tempfile
import zlib
from pathlib import Path

from frame_budget import evaluate, qualify_fps, verify_render_proof, verify_trace_log, read_capture_text


class FrameBudgetTests(unittest.TestCase):
    def setUp(self):
        self.metadata = dict(Resolution="1920x1080", Development="False", RenderingVerified="True",
                             Samples="3501", RenderingCallbacks="3500", EffectRequests="1", PathRequests="1",
                             FrameTimingEnabled="True", EffectOmitted="False", ScreenshotDuringMeasurement="False")
        self.rows = [dict(intro_seconds=str(i * .004), unity_frame=str(i + 1), loop_ms="4",
                          timing_stamp=str(i + 1), timing_gpu_ms="2") for i in range(3501)]

    def test_passing_capture(self):
        self.assertEqual("pass", evaluate(self.rows, self.metadata)["status"])

    def test_one_slow_frame_fails_even_when_average_is_fast(self):
        self.rows[100]["loop_ms"] = "40"
        result = evaluate(self.rows, self.metadata)
        self.assertEqual("fail", result["status"])
        self.assertEqual(1, result["cpu"]["over_budget"])
        self.assertEqual(40, result["cpu"]["max_ms"])

    def test_first_use_spike_is_not_discarded(self):
        self.rows[0]["loop_ms"] = "101"
        self.assertEqual("fail", evaluate(self.rows, self.metadata)["status"])

    def test_consecutive_slow_frames(self):
        for row in self.rows[10:13]:
            row["loop_ms"] = "17"
        self.assertEqual(3, evaluate(self.rows, self.metadata)["cpu"]["longest_over_budget_run"])

    def test_sixty_fps_default_accepts_sixteen_ms(self):
        self.rows[100]["loop_ms"] = "16"
        result = evaluate(self.rows, self.metadata)
        self.assertEqual(60, result["target_fps"])
        self.assertEqual("pass", result["status"])

    def test_sixty_fps_default_rejects_seventeen_ms(self):
        self.rows[100]["loop_ms"] = "17"
        self.assertEqual("fail", evaluate(self.rows, self.metadata)["status"])

    def test_hidden_window_is_invalid(self):
        self.metadata["RenderingVerified"] = "False"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_development_build_cannot_certify_release_target(self):
        self.metadata["Development"] = "True"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_missing_gpu_is_not_zero_cost(self):
        for row in self.rows:
            row["timing_gpu_ms"] = "NaN"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_duplicate_gpu_timestamp_is_invalid(self):
        self.rows[100]["timing_stamp"] = self.rows[99]["timing_stamp"]
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_gpu_spike_is_reported(self):
        self.rows[100]["timing_gpu_ms"] = "25"
        self.assertEqual("fail", evaluate(self.rows, self.metadata)["status"])

    def test_omitted_effect_cannot_certify_the_game(self):
        self.metadata["EffectOmitted"] = "True"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_truncated_capture_is_invalid(self):
        self.assertEqual("invalid", evaluate(self.rows[:-1000], self.metadata)["status"])

    def test_wrong_resolution_is_invalid(self):
        self.metadata["Resolution"] = "1280x720"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_screenshot_work_must_be_outside_measurement(self):
        self.metadata["ScreenshotDuringMeasurement"] = "True"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_frame_gap_is_invalid(self):
        self.rows[100]["unity_frame"] = "2000"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_nonfinite_cpu_is_invalid(self):
        self.rows[100]["loop_ms"] = "NaN"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata)["status"])

    def test_blank_png_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory, "proof.png")
            self.write_png(path, False)
            with self.assertRaisesRegex(ValueError, "Blank"):
                verify_render_proof(path)

    def test_rendered_png_is_accepted(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory, "proof.png")
            self.write_png(path, True)
            verify_render_proof(path)

    def gpu_history(self):
        self.metadata.update(IntroductionCpuTimestamp="1000000", EndCpuTimestamp="141000000",
                             CpuTimerFrequency="10000000", CpuCounterFrequency="10000000", GpuTimingDrainFrames="8",
                             CpuTimestampSource="WindowsQueryPerformanceCounter")
        return [dict(timing_stamp=str(1000000 + i * 40000), timing_gpu_ms="2") for i in range(3501)]

    def test_delayed_reordered_gpu_history_is_accepted(self):
        history = self.gpu_history()
        history.reverse()
        self.assertEqual("pass", evaluate(self.rows, self.metadata, timings=history)["status"])

    def test_incomplete_gpu_sample_does_not_consume_completed_timestamp(self):
        history = self.gpu_history()
        history.insert(0, dict(timing_stamp=history[0]["timing_stamp"], timing_gpu_ms="0"))
        self.assertEqual("pass", evaluate(self.rows, self.metadata, timings=history)["status"])

    def test_late_completion_at_end_is_included(self):
        history = self.gpu_history()
        history[-1]["timing_gpu_ms"] = "42"
        self.assertEqual("fail", evaluate(self.rows, self.metadata, timings=history)["status"])

    def test_warmup_and_drain_gpu_work_is_excluded_by_source_timestamp(self):
        history = self.gpu_history()
        history.extend([dict(timing_stamp="1", timing_gpu_ms="50"),
                        dict(timing_stamp="200000000", timing_gpu_ms="50")])
        self.assertEqual("pass", evaluate(self.rows, self.metadata, timings=history)["status"])

    def test_wrong_timestamp_clock_is_invalid(self):
        history = self.gpu_history()
        self.metadata["CpuCounterFrequency"] = "1"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata, timings=history)["status"])

    def test_incomplete_gpu_history_is_invalid(self):
        history = self.gpu_history()
        self.assertEqual("invalid", evaluate(self.rows, self.metadata, timings=history[:1000])["status"])

    def native_history(self):
        self.gpu_history()
        self.metadata.update(PresentMonProcessId="42", PresentMonExitCode="0")
        return [dict(TimeInQPC=str(1000000 + i * 40000), MsBetweenPresents="4", MsGPUTime="2",
                     ProcessID="42", SwapChainAddress="0x1") for i in range(3501)]

    def test_external_trace_supplies_missing_unity_gpu_data(self):
        native = self.native_history()
        result = evaluate(self.rows, self.metadata, timings=[], presents=native)
        self.assertEqual("pass", result["status"])
        self.assertEqual("PresentMon ETW", result["gpu_source"])

    def test_external_trace_can_validate_backend_without_unity_gpu_timer(self):
        native = self.native_history()
        self.metadata.update(FrameTimingEnabled="False", CpuTimerFrequency="0")
        self.assertEqual("pass", evaluate(self.rows, self.metadata, timings=[], presents=native)["status"])

    def test_missing_present_is_invalid(self):
        native = self.native_history()
        del native[100]
        self.assertEqual("invalid", evaluate(self.rows, self.metadata, timings=[], presents=native)["status"])

    def test_wrong_process_is_invalid(self):
        native = self.native_history()
        native[100]["ProcessID"] = "999"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata, timings=[], presents=native)["status"])

    def test_presentation_spike_fails(self):
        native = self.native_history()
        native[100]["MsBetweenPresents"] = "25"
        for row in native[100:]:
            row["TimeInQPC"] = str(int(row["TimeInQPC"]) + 210000)
        self.metadata["EndCpuTimestamp"] = "141210000"
        self.rows[-1]["intro_seconds"] = "14.021"
        self.assertEqual("fail", evaluate(self.rows, self.metadata, timings=[], presents=native)["status"])

    def test_killed_trace_cannot_certify(self):
        native = self.native_history()
        self.metadata["PresentMonExitCode"] = "1"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata, timings=[], presents=native)["status"])

    def test_missing_native_gpu_is_invalid(self):
        native = self.native_history()
        native[100]["MsGPUTime"] = "NA"
        self.assertEqual("invalid", evaluate(self.rows, self.metadata, timings=[], presents=native)["status"])

    def test_lost_etw_events_cannot_certify(self):
        with self.assertRaisesRegex(ValueError, "lost ETW"):
            verify_trace_log("warning: 692283 ETW events were lost.")

    def test_permission_information_is_not_event_loss(self):
        verify_trace_log("warning: process names may be unknown without elevation")

    def test_windows_powershell_utf16_log(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory, "trace.log")
            path.write_text("warning: 0 ETW events were lost.", encoding="utf-16")
            verify_trace_log(read_capture_text(path))

    def fps_history(self):
        native = self.native_history()
        self.metadata.update(CaptureMode="fps", PresentationTraceMode="fps", FrameTimingEnabled="False")
        for row in native:
            row.pop("MsGPUTime")
        return native

    def test_fps_qualification_does_not_require_heavy_gpu_queries(self):
        native = self.fps_history()
        result = qualify_fps(self.rows, self.metadata, native)
        self.assertEqual("pass", result["status"])
        self.assertEqual("not captured", result["gpu_source"])
        self.assertIsNone(result["gpu"]["max_ms"])

    def test_gpu_diagnostic_cannot_certify_fps(self):
        native = self.fps_history()
        self.metadata["CaptureMode"] = "diagnostic-gpu"
        self.assertEqual("invalid", qualify_fps(self.rows, self.metadata, native)["status"])

    def test_fps_qualification_requires_presentation_evidence(self):
        self.fps_history()
        self.assertEqual("invalid", qualify_fps(self.rows, self.metadata, None)["status"])

    def test_fps_qualification_rejects_one_cpu_hitch(self):
        native = self.fps_history()
        self.rows[100]["loop_ms"] = "40"
        self.assertEqual("fail", qualify_fps(self.rows, self.metadata, native)["status"])

    def test_fps_qualification_rejects_a_missing_present(self):
        native = self.fps_history()
        del native[100]
        self.assertEqual("invalid", qualify_fps(self.rows, self.metadata, native)["status"])

    def test_fps_qualification_rejects_gpu_timing_instrumentation(self):
        native = self.fps_history()
        self.metadata["FrameTimingEnabled"] = "True"
        self.assertEqual("invalid", qualify_fps(self.rows, self.metadata, native)["status"])

    def test_fps_qualification_rejects_presentation_only_hitch(self):
        native = self.fps_history()
        native[100]["MsBetweenPresents"] = "25"
        for row in native[100:]:
            row["TimeInQPC"] = str(int(row["TimeInQPC"]) + 210000)
        self.metadata["EndCpuTimestamp"] = "141210000"
        self.rows[-1]["intro_seconds"] = "14.021"
        self.assertEqual("fail", qualify_fps(self.rows, self.metadata, native)["status"])

    @staticmethod
    def write_png(path, varied):
        raw = bytearray((1920 * 3 + 1) * 1080)
        if varied:
            raw[4] = 255
        def chunk(kind, body):
            return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
        path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 1920, 1080, 8, 2, 0, 0, 0))
                         + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


if __name__ == "__main__":
    unittest.main()

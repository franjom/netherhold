# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

import csv
import json
import shutil
import subprocess
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

from compare_captures import compare


class ComparisonTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.baseline = [self.capture(f"base{i}") for i in range(3)]
        self.candidate = [self.capture(f"new{i}") for i in range(3)]

    def capture(self, name, frame_ms=10):
        path = self.root / name
        (path / "recorder").mkdir(parents=True)
        metadata = dict(Backend="IL2CPP", Development="False", Level="level6a", Seed="1",
                        Resolution="1920x1080", CPU="cpu", GPU="gpu", GraphicsDriver="driver",
                        API="Vulkan", OS="Windows", Unity="6000.6.0f1", Fullscreen="False",
                        FrameCap="-1", VSync="0", RefreshHz="165.00")
        (path / "recorder/metadata.txt").write_text("\n".join(f"{k}={v}" for k, v in metadata.items()))
        (path / "machine.txt").write_text("Seconds=40\n")
        (path / "manifest.txt").write_text("Commit=" + "a" * 40 + "\nSHA256 GameAssembly.dll=" + "b" * 64)
        (path / "Player.log").write_text("normal game log\n")
        def chunk(kind, payload):
            return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))
        pixels = b"\0\xff\0\0" + bytes(1919 * 3) + (b"\0" + bytes(1920 * 3)) * 1079
        (path / "screenshot.png").write_bytes(b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", struct.pack(">IIBBBBB", 1920, 1080, 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b""))
        (path / "player-arguments.txt").write_text("-level level6a -seed 1 -recordFrames x -logFile y")
        with (path / "recorder/frames.csv").open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=["play_seconds", "frame_ms", "wait_tick_ms",
                "tick_ms", "chunk_rebuild_ms", "gc_collections", "render_tick", "path", "cam_x", "cam_y", "cam_z"])
            writer.writeheader()
            for i in range(int(40000 / frame_ms) + 1):
                seconds = i * frame_ms / 1000
                writer.writerow(dict(play_seconds=seconds, frame_ms=frame_ms, wait_tick_ms=0,
                    tick_ms=4 if i % 25 == 0 else 0, chunk_rebuild_ms=0, gc_collections=0,
                    render_tick=seconds * 4, path="1" if seconds < 10 else "0", cam_x=seconds, cam_y=0, cam_z=0))
        return path

    def rewrite(self, path, column, value, predicate=lambda row: True):
        file = path / "recorder/frames.csv"
        with file.open(newline="") as stream:
            rows = list(csv.DictReader(stream))
        for row in rows:
            if predicate(row):
                row[column] = value
        with file.open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
            writer.writeheader()
            writer.writerows(rows)

    def test_equal_captures_pass(self):
        self.assertEqual("pass", compare(self.baseline, self.candidate)["status"])

    def test_slower_frames_fail_even_if_below_the_final_target(self):
        self.candidate = [self.capture(f"slow{i}", frame_ms=13) for i in range(3)]
        result = compare(self.baseline, self.candidate)
        self.assertEqual("fail", result["status"])
        self.assertIn("frame_p99_ms", result["regressions"])

    def test_a_single_first_use_hitch_is_not_discarded_as_warmup(self):
        self.rewrite(self.candidate[0], "frame_ms", "80", lambda r: r["play_seconds"] == "0.5")
        self.assertIn("frame_max_ms", compare(self.baseline, self.candidate)["regressions"])

    def test_one_run_with_reintroduced_join_fails(self):
        self.rewrite(self.candidate[0], "wait_tick_ms", "5", lambda r: r["play_seconds"] == "4.0")
        self.assertEqual("fail", compare(self.baseline, self.candidate)["status"])

    def test_missing_flyby_or_frozen_camera_is_invalid(self):
        for column, value in [("path", "0"), ("cam_x", "0")]:
            with self.subTest(column=column):
                path = self.capture(column)
                self.rewrite(path, column, value)
                self.assertEqual("invalid", compare(self.baseline, [path, *self.candidate[1:]])["status"])

    def test_mismatched_settings_or_hardware_are_invalid(self):
        file = self.candidate[0] / "recorder/metadata.txt"
        original = file.read_text()
        for old, new in [("GPU=gpu", "GPU=other"), ("FrameCap=-1", "FrameCap=60"),
                         ("Backend=IL2CPP", "Backend=Mono"), ("Level=level6a", "Level=level1")]:
            file.write_text(original.replace(old, new))
            self.assertEqual("invalid", compare(self.baseline, self.candidate)["status"])

    def test_equal_capped_runs_cannot_mask_a_rendering_regression(self):
        for path in self.baseline + self.candidate:
            file = path / "recorder/metadata.txt"
            file.write_text(file.read_text().replace("FrameCap=-1", "FrameCap=60"))
        self.assertEqual("invalid", compare(self.baseline, self.candidate)["status"])

    def test_nonfinite_and_missing_measurements_are_invalid(self):
        self.rewrite(self.candidate[0], "frame_ms", "nan")
        self.assertEqual("invalid", compare(self.baseline, self.candidate)["status"])

    def test_missing_frames_are_invalid(self):
        file = self.candidate[0] / "recorder/frames.csv"
        lines = file.read_text().splitlines()
        file.write_text("\n".join([lines[0], *lines[1::2]]))
        self.assertEqual("invalid", compare(self.baseline, self.candidate)["status"])

    def test_wall_capture_includes_loading_but_judges_the_same_gameplay_window(self):
        file = self.candidate[0] / "recorder/frames.csv"
        with file.open(newline="") as stream:
            rows = list(csv.DictReader(stream))
        with file.open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
            writer.writeheader()
            writer.writerows(row for row in rows if float(row["play_seconds"]) <= 36)
        self.assertEqual("pass", compare(self.baseline, self.candidate)["status"])

    def test_first_recorder_row_has_no_previous_frame_interval(self):
        self.rewrite(self.candidate[0], "frame_ms", "0", lambda r: r["play_seconds"] == "0.0")
        self.assertEqual("pass", compare(self.baseline, self.candidate)["status"])
        self.rewrite(self.candidate[0], "frame_ms", "0", lambda r: r["play_seconds"] == "4.0")
        self.assertEqual("invalid", compare(self.baseline, self.candidate)["status"])

    def test_hitch_crossing_the_end_of_the_window_is_included(self):
        file = self.candidate[0] / "recorder/frames.csv"
        with file.open(newline="") as stream:
            rows = list(csv.DictReader(stream))
        for row in rows:
            seconds = float(row["play_seconds"])
            if seconds == 30:
                row["frame_ms"] = "910"
            if seconds >= 30:
                row["play_seconds"] = str(seconds + .9)
        with file.open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
            writer.writeheader()
            writer.writerows(rows)
        self.assertIn("frame_max_ms", compare(self.baseline, self.candidate)["regressions"])

    def test_a_frozen_draw_clock_fails(self):
        self.rewrite(self.candidate[0], "render_tick", "0")
        self.assertEqual("fail", compare(self.baseline, self.candidate)["status"])

    def test_missing_evidence_or_errors_are_invalid(self):
        for name in ["manifest.txt", "screenshot.png", "Player.log", "player-arguments.txt"]:
            file = self.candidate[0] / name
            original = file.read_bytes()
            file.unlink()
            self.assertEqual("invalid", compare(self.baseline, self.candidate)["status"])
            file.write_bytes(original)
        (self.candidate[0] / "Player.log").write_text("NullReferenceException: bad\n")
        self.assertEqual("invalid", compare(self.baseline, self.candidate)["status"])

    def test_a_png_signature_without_an_image_is_invalid(self):
        (self.candidate[0] / "screenshot.png").write_bytes(b"\x89PNG\r\n\x1a\n" + bytes(2048))
        self.assertEqual("invalid", compare(self.baseline, self.candidate)["status"])

    def test_argument_paths_with_spaces_do_not_change_capture_settings(self):
        for path in self.baseline + self.candidate:
            tokens = ["-level", "level6a", "-seed", "1", "-recordFrames", str(path / "folder with spaces"),
                      "-logFile", str(path / "Player.log")]
            (path / "player-arguments.txt").write_text(" ".join(tokens))
            (path / "player-arguments.json").write_text(json.dumps(tokens))
        self.assertEqual("pass", compare(self.baseline, self.candidate)["status"])

    def test_desktop_screenshot_can_be_larger_than_the_game_window(self):
        file = self.candidate[0] / "screenshot.png"
        data = bytearray(file.read_bytes())
        # Preserve the row stride/payload and add one desktop row.
        pixels = b"\0\xff\0\0" + bytes(1919 * 3) + (b"\0" + bytes(1920 * 3)) * 1080
        header = struct.pack(">IIBBBBB", 1920, 1081, 8, 2, 0, 0, 0)
        def chunk(kind, payload):
            return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))
        file.write_bytes(bytes(data[:8]) + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b""))
        self.assertEqual("pass", compare(self.baseline, self.candidate)["status"])

    def test_repetition_cannot_be_faked_with_duplicate_paths(self):
        self.assertEqual("invalid", compare([self.baseline[0]] * 3, self.candidate)["status"])
        self.assertEqual("invalid", compare(self.baseline[:1], self.candidate)["status"])

    def test_cli_returns_machine_readable_failure(self):
        result = subprocess.run([sys.executable, str(Path(__file__).with_name("compare_captures.py")),
            "--baseline", *map(str, self.baseline), "--candidate", str(self.root / "missing")],
            capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertEqual("invalid", json.loads(result.stdout)["status"])

    @unittest.skipUnless(sys.platform == "win32" and shutil.which("powershell"), "Windows capture wrapper")
    def test_capture_wrapper_runs_three_times_and_preserves_the_comparison(self):
        package = self.root / "package with spaces"
        package.mkdir()
        # The external player's substitute records its invocation and supplies a controlled capture.
        fixture = str(self.baseline[0]).replace("'", "''")
        (package / "capture.ps1").write_text("""param([string]$Install, [string]$Level, [int]$Seed,
            [int]$Seconds, [int]$Width, [int]$Height, [switch]$Windowed,
            [int]$FrameCap, [int]$VSync, [string]$Output)
            New-Item -ItemType Directory -Path $Output | Out-Null
            Copy-Item -Path 'FIXTURE/*' -Destination $Output -Recurse
            $playerArguments = @('-level', 'level6a', '-seed', '1', '-recordFrames', (Join-Path $Output 'recorder'),
                '-logFile', (Join-Path $Output 'Player.log'))
            Set-Content -LiteralPath (Join-Path $Output 'player-arguments.txt') -Value ($playerArguments -join ' ')
            ConvertTo-Json -InputObject $playerArguments | Set-Content -LiteralPath (Join-Path $Output 'player-arguments.json') -Encoding UTF8
            $PSBoundParameters | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'invocation.json')
            """.replace("FIXTURE", fixture))
        script = Path(__file__).with_name("run-regression.ps1").resolve()
        quote = lambda path: "'" + str(path).replace("'", "''") + "'"
        output = self.root / "output with spaces"
        harness = self.root / "invoke.ps1"
        harness.write_text(f"& {quote(script)} -Package {quote(package)} -Install 'game path' "
            f"-Baseline @({','.join(map(quote, self.baseline))}) -OutputRoot {quote(output)}\nexit $LASTEXITCODE")
        run = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(harness)],
                             capture_output=True, text=True)
        self.assertEqual(0, run.returncode, run.stdout + run.stderr)
        reports = list(output.glob("*/comparison.json"))
        self.assertEqual(1, len(reports))
        self.assertEqual("pass", json.loads(reports[0].read_text(encoding="utf-8-sig"))["status"])
        invocations = list(output.glob("*/run-*/invocation.json"))
        self.assertEqual(3, len(invocations))
        for file in invocations:
            settings = json.loads(file.read_text(encoding="utf-8-sig"))
            self.assertEqual(("level6a", 1, 40, 1920, 1080, -1, 0),
                tuple(settings[key] for key in ("Level", "Seed", "Seconds", "Width", "Height", "FrameCap", "VSync")))


if __name__ == "__main__":
    unittest.main()

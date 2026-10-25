# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Fail a rendered arrival capture on every frame above the target, never its average."""

import argparse
import csv
import io
import json
import math
import re
import statistics
import struct
import zlib
from pathlib import Path


def read_capture_text(path):
    data = path.read_bytes()
    return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def verify_trace_log(text):
    if any(int(count) > 0 for count in re.findall(r"(\d+) ETW events were lost", text, re.IGNORECASE)):
        raise ValueError("Presentation trace lost ETW events")


def optional_number(value):
    try:
        return float(value)
    except (ValueError, TypeError):
        return math.nan


def verify_render_proof(path):
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("Missing PNG rendering proof")
    at, payload, header = 8, bytearray(), None
    while at + 12 <= len(data):
        size = struct.unpack(">I", data[at:at + 4])[0]
        kind, body = data[at + 4:at + 8], data[at + 8:at + 8 + size]
        if kind == b"IHDR":
            header = struct.unpack(">IIBBBBB", body)
        if kind == b"IDAT":
            payload.extend(body)
        at += size + 12
    if header is None or header[:3] != (1920, 1080, 8) or header[3] not in (2, 6) or header[6] != 0:
        raise ValueError("Rendering proof must be non-interlaced 1920x1080 RGB/RGBA8")
    width, height, _, colour, _, _, _ = header
    channels = 3 if colour == 2 else 4
    stride = width * channels
    raw = zlib.decompress(payload)
    if len(raw) != (stride + 1) * height:
        raise ValueError("Incomplete PNG rendering proof")
    previous, first = bytearray(stride), None
    for y in range(height):
        start = y * (stride + 1)
        kind, row = raw[start], bytearray(raw[start + 1:start + 1 + stride])
        if kind > 4:
            raise ValueError("Unsupported PNG row filter")
        for x in range(stride):
            a, b, c = row[x - channels] if x >= channels else 0, previous[x], previous[x - channels] if x >= channels else 0
            if kind == 1:
                predictor = a
            elif kind == 2:
                predictor = b
            elif kind == 3:
                predictor = (a + b) // 2
            elif kind == 4:
                p = a + b - c
                da, db, dc = abs(p - a), abs(p - b), abs(p - c)
                predictor = a if da <= db and da <= dc else b if db <= dc else c
            else:
                predictor = 0
            row[x] = (row[x] + predictor) & 255
        for x in range(0, stride, channels):
            rgb = row[x:x + 3]
            if first is None:
                first = rgb
            elif rgb != first:
                return
        previous = row
    raise ValueError("Blank rendering proof")


def metrics(values, budget):
    ordered = sorted(values)
    streak = longest = 0
    for value in values:
        streak = streak + 1 if value > budget else 0
        longest = max(longest, streak)
    return dict(samples=len(values), median_ms=statistics.median(values) if values else None,
                p99_ms=ordered[math.ceil(len(ordered) * .99) - 1] if values else None,
                max_ms=max(values) if values else None, over_budget=sum(v > budget for v in values),
                longest_over_budget_run=longest)


def evaluate(rows, metadata, fps=60, timings=None, presents=None, fps_qualification=False):
    if not math.isfinite(fps) or fps <= 0:
        raise ValueError("FPS must be finite and positive")
    budget = 1000 / fps
    invalid = []
    required = dict(Resolution="1920x1080", Development="False", RenderingVerified="True",
                    EffectRequests="1", PathRequests="1", EffectOmitted="False",
                    ScreenshotDuringMeasurement="False")
    if presents is None and not fps_qualification:
        required["FrameTimingEnabled"] = "True"
    for key, expected in required.items():
        if metadata.get(key) != expected:
            invalid.append(f"{key} must be {expected}, got {metadata.get(key)!r}")
    if any(not math.isfinite(float(r["intro_seconds"])) for r in rows):
        invalid.append("Unavailable introduction clock")
    selected = [r for r in rows if float(r["intro_seconds"]) >= 0]
    if not selected or float(selected[0]["intro_seconds"]) > .01 or float(selected[-1]["intro_seconds"]) < 14:
        invalid.append("Missing first-use or final introduction frames")
    if int(metadata.get("Samples", "0")) != len(rows):
        invalid.append("Recorded sample count disagrees with CSV")
    if int(metadata.get("RenderingCallbacks", "0")) < len(rows) - 1:
        invalid.append("Frames were not rendered")
    cpu, gpu, stamps = [], [], set()
    previous_frame = previous_time = None
    for row in selected:
        frame, elapsed = int(row["unity_frame"]), float(row["intro_seconds"])
        if previous_frame is not None and (frame != previous_frame + 1 or elapsed < previous_time):
            invalid.append("Missing or reordered gameplay frames")
        previous_frame, previous_time = frame, elapsed
        value = float(row["loop_ms"])
        if not math.isfinite(value) or value <= 0:
            invalid.append("Unavailable CPU frame interval")
        else:
            cpu.append(value)
    if timings is None:
        completed = selected
    else:
        start, end = int(metadata["IntroductionCpuTimestamp"]), int(metadata["EndCpuTimestamp"])
        frequency = int(metadata["CpuCounterFrequency"] if presents is not None or fps_qualification else metadata["CpuTimerFrequency"])
        if frequency <= 0 or (presents is None and not fps_qualification and frequency != int(metadata["CpuCounterFrequency"])) or metadata.get("CpuTimestampSource") != "WindowsQueryPerformanceCounter":
            invalid.append("CPU timing clocks are not comparable")
        elif not selected or (end - start) / frequency < 14 or abs((end - start) / frequency - float(selected[-1]["intro_seconds"])) > .1:
            invalid.append("GPU timing window does not match the measured introduction")
        if metadata.get("GpuTimingDrainFrames") != "8":
            invalid.append("Completed GPU timing drain missing")
        completed = sorted((r for r in timings if start <= int(r["timing_stamp"]) <= end),
                           key=lambda r: int(r["timing_stamp"]))
    for row in completed:
        stamp, value = int(row["timing_stamp"]), float(row["timing_gpu_ms"])
        if stamp > 0 and math.isfinite(value) and value > 0:
            if stamp in stamps:
                invalid.append("Repeated completed GPU timestamp")
            stamps.add(stamp)
            gpu.append(value)
    unity_gpu_samples = len(gpu)
    presentation = None
    if presents is not None:
        start, end = int(metadata["IntroductionCpuTimestamp"]), int(metadata["EndCpuTimestamp"])
        frequency = int(metadata["CpuCounterFrequency"])
        target_pid = metadata.get("PresentMonProcessId")
        if metadata.get("PresentMonExitCode") != "0":
            invalid.append("Presentation trace did not finish cleanly")
        native = sorted((r for r in presents if start <= int(r["TimeInQPC"]) <= end), key=lambda r: int(r["TimeInQPC"]))
        if not native or abs(len(native) - len(selected)) > 2:
            invalid.append("Presentation trace does not cover the rendered frame count")
        if len({r["SwapChainAddress"] for r in native}) != 1:
            invalid.append("Presentation trace needs exactly one game swap chain")
        intervals, gpu = [], []
        previous = None
        for row in native:
            stamp = int(row["TimeInQPC"])
            if row["ProcessID"] != target_pid:
                invalid.append("Presentation trace contains another process")
            interval, duration = optional_number(row["MsBetweenPresents"]), optional_number(row.get("MsGPUTime"))
            if not math.isfinite(interval) or interval <= 0:
                invalid.append("Presentation metrics unavailable")
                continue
            if previous is not None and (frequency <= 0 or stamp <= previous or abs((stamp - previous) * 1000 / frequency - interval) > .05):
                invalid.append("Missing or repeated presentation events")
            previous = stamp
            intervals.append(interval)
            if math.isfinite(duration) and duration > 0:
                gpu.append(duration)
        if len(gpu) != len(native) and not fps_qualification:
            invalid.append("Incomplete presentation GPU coverage")
        presentation = metrics(intervals, budget)
    elif (not selected or len(gpu) < len(selected) * .9) and not fps_qualification:
        invalid.append("Less than 90% completed GPU timing coverage")
    cpu_summary, gpu_summary = metrics(cpu, budget), metrics(gpu, budget)
    failed = cpu_summary["over_budget"] > 0 or (not fps_qualification and gpu_summary["over_budget"] > 0) or (presentation is not None and presentation["over_budget"] > 0)
    return dict(status="invalid" if invalid else "fail" if failed else "pass", target_fps=fps,
                budget_ms=budget, cpu=cpu_summary, gpu=gpu_summary,
                gpu_coverage=len(gpu) / len(selected) if selected else 0,
                gpu_source="not captured" if not gpu else "PresentMon ETW" if presents is not None else "Unity FrameTimingManager",
                unity_gpu_samples=unity_gpu_samples, presentation=presentation,
                invalid_reasons=sorted(set(invalid)),
                scope="Only this prepared Level2 introduction; not whole-game certification")


def qualify_fps(rows, metadata, presents, fps=60):
    result = evaluate(rows, metadata, fps, timings=[], presents=presents, fps_qualification=True)
    if presents is None:
        result["invalid_reasons"].append("FPS qualification requires a complete presentation trace")
    if metadata.get("CaptureMode") != "fps" or metadata.get("PresentationTraceMode") != "fps":
        result["invalid_reasons"].append("Diagnostic tracing cannot certify lightweight FPS acceptance")
    if metadata.get("FrameTimingEnabled") != "False":
        result["invalid_reasons"].append("Lightweight FPS acceptance requires Unity Frame Timing Stats disabled")
    if result["invalid_reasons"]:
        result["status"] = "invalid"
    result["mode"] = "fps"
    result["scope"] = "Application rendering/presentation cadence in this introduction; not display refresh or whole-game certification"
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    target = json.loads(Path(__file__).with_name("performance-target.json").read_text(encoding="utf-8"))
    parser.add_argument("--fps", type=float, default=target["minimum_fps"])
    parser.add_argument("--mode", choices=("fps", "diagnostic-gpu"), default="fps")
    args = parser.parse_args()
    try:
        metadata = dict(line.split("=", 1) for line in
                        read_capture_text(args.capture / "metadata.txt").splitlines() if "=" in line)
        with io.StringIO(read_capture_text(args.capture / "frames.csv")) as source:
            rows = list(csv.DictReader(source))
        with io.StringIO(read_capture_text(args.capture / "timings.csv")) as source:
            timings = list(csv.DictReader(source))
        native_path = args.capture / "presentmon.csv"
        presents = None
        if "PresentMonProcessId" in metadata:
            verify_trace_log(read_capture_text(args.capture / "presentmon-errors.log"))
            if not native_path.exists():
                raise ValueError("Requested presentation trace produced no frames")
        if native_path.exists():
            with io.StringIO(read_capture_text(native_path)) as source:
                presents = list(csv.DictReader(source))
        result = qualify_fps(rows, metadata, presents, args.fps) if args.mode == "fps" else evaluate(rows, metadata, args.fps, timings, presents)
        if args.mode == "diagnostic-gpu" and result["status"] != "invalid":
            result["status"] = "diagnostic"
            result["mode"] = "diagnostic-gpu"
        verify_render_proof(args.capture / "render-proof.png")
        for key, expected in target["metadata"].items():
            if metadata.get(key, "").strip() != expected:
                result["invalid_reasons"].append(f"Target {key} must be {expected!r}")
        if args.fps < target["minimum_fps"]:
            result["invalid_reasons"].append("Cannot lower the owner's minimum FPS for acceptance")
        if result["invalid_reasons"]:
            result["status"] = "invalid"
        result["target_profile"] = target["name"]
        result["api"] = metadata.get("API")
    except (ValueError, KeyError, OSError, OverflowError, struct.error, zlib.error) as error:
        result = dict(status="invalid", invalid_reasons=[str(error)])
    print(json.dumps(result, indent=2, allow_nan=False))
    return {"pass": 0, "fail": 1, "invalid": 2, "diagnostic": 3}[result["status"]]


if __name__ == "__main__":
    raise SystemExit(main())

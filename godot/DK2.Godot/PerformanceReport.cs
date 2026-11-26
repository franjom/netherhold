// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The performance harness (migration plan step 3o): with <c>--perf-report &lt;file&gt;</c> it samples every
/// frame after a warm-up into a preallocated <see cref="FrameRecording"/>, prints the summary at exit and writes the
/// JSON report; without it there is no node and no cost. It runs first in the frame (<see cref="PerformanceReportTail"/>
/// last), so each sample is the frame just drawn, and steers the focus along <c>--perf-camera</c>
/// (<see cref="CameraScript"/>) before the scene places the camera. Allocation-free per frame. The recipe and options
/// are in godot/README.md, <i>Measuring performance</i>.</summary>
public partial class PerformanceReport : Node
{
    private const int DefaultWarmUp = 120;
    private const double Mebibyte = 1024.0 * 1024.0;

    private FrameRecording _recording = null!;
    private PerformanceReportTail _tail = null!;
    private string _path = "";
    private int _warmUp, _frames, _frame, _mapWidth, _mapHeight, _selfMaxFrame = -1;
    private CameraScript? _camera;
    private WorldScene _world = null!;
    private string[] _arguments = [];
    private string _level = "";
    private ulong _seed;
    private Rid _viewport;
    private Camera3D? _drawn;
    private Vector2I? _size;
    private Transform3D _lastPose;
    private System.Numerics.Vector3 _origin;
    private bool _havePose, _written;
    private long _lastStamp, _lastAllocated, _selfBytes, _selfMaxBytes, _pendingSelfBytes, _allThreadsAtStart, _mainAtStart;
    private ulong _lastCompilations;
    private readonly int[] _lastCollections = new int[3];
    private readonly double[] _warmUpRow = new double[FrameRecording.MetricCount];

    /// <summary>Adds the harness under <paramref name="parent"/> when the arguments ask for it; null otherwise. Also
    /// applies <c>--perf-size WxH</c> (the scene rendered at that size, since the project's full screen ignores
    /// <c>--resolution</c>) and turns V-Sync off unless <c>--perf-vsync</c>. A malformed option quits with code 1: a
    /// silent default would measure something else, or never end.</summary>
    public static PerformanceReport? Attach(Node parent, string[] args, string level, ulong seed, WorldScene world,
        int mapWidth, int mapHeight)
    {
        string? path = LaunchArguments.Value(args, "--perf-report");
        if (path == null)
        {
            return null;
        }

        string? cameraText = LaunchArguments.Value(args, "--perf-camera");
        var camera = CameraScript.Parse(cameraText);
        if (!TryCount(args, "--perf-warmup", DefaultWarmUp, out int warmUp) || !TryCount(args, "--perf-frames", 0, out int frames)
            || !TrySize(LaunchArguments.Value(args, "--perf-size"), out Vector2I? size) || camera == null && cameraText != null)
        {
            GD.PushError("DK2.Godot: a --perf-warmup, --perf-frames, --perf-size or --perf-camera value is malformed; quitting.");
            parent.GetTree().Quit(1);
            return null;
        }
        var report = new PerformanceReport
        {
            Name = "PerformanceReport",
            ProcessPriority = int.MinValue,
            _path = path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
                ? ProjectSettings.GlobalizePath(path) : Path.GetFullPath(path),
            _warmUp = warmUp, _frames = frames, _camera = camera, _size = size,
            _world = world, _mapWidth = mapWidth, _mapHeight = mapHeight,
            _arguments = args, _level = level, _seed = seed,
            _recording = new FrameRecording(frames > 0 ? frames : 1 << 16),
            _tail = new PerformanceReportTail { Name = "PerformanceReportTail", ProcessPriority = int.MaxValue },
        };
        report.KeepRenderSize(parent.GetWindow());
        if (Array.IndexOf(args, "--perf-vsync") < 0)
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }

        parent.AddChild(report);
        parent.AddChild(report._tail);
        GD.Print("DK2.Godot performance report: ", warmUp, " warm-up frames, then ",
            frames > 0 ? frames.ToString(CultureInfo.InvariantCulture) + " frames" : "until exit", ", to ", report._path);
        return report;
    }

    public override void _Ready()
    {
        _world.MeasurePreviewRenderTime = true;
        _viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
    }

    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        ulong compilations = Compilations();
        // All threads, precisely (it is costly), once at the start of the measured window and once at its end.
        if (_frame == _warmUp + 1)
        {
            _allThreadsAtStart = GC.GetTotalAllocatedBytes(true);
            _mainAtStart = allocated;
        }
        // The pose this frame starts with is the one the last frame drew.
        if (_drawn == null || !IsInstanceValid(_drawn))
        {
            _drawn = GetViewport().GetCamera3D();
        }

        Transform3D pose = _drawn?.GlobalTransform ?? Transform3D.Identity;
        bool moved = _havePose && pose != _lastPose;
        // The warm-up samples into a discarded row, so the bindings' first-use allocations happen before measuring.
        if (_frame > 0 && !_written)
        {
            Sample(_frame > _warmUp ? _recording.Add(moved) : _warmUpRow, now, allocated, compilations);
        }

        _lastStamp = now;
        _lastAllocated = allocated;
        _lastCompilations = compilations;
        for (int generation = 0; generation < _lastCollections.Length; generation++)
        {
            _lastCollections[generation] = GC.CollectionCount(generation);
        }

        _lastPose = pose;
        _havePose = _drawn != null;
        if (_frame == 0)
        {
            _origin = _world.Focus;
        }

        if (_camera != null)
        {
            _world.Focus = _camera.FocusAt(_frame, _origin, _mapWidth, _mapHeight);
        }
        // What the harness allocated itself (expected 0); it lands in the next sample, which leaves it out.
        _pendingSelfBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (_frame > _warmUp && !_written)
        {
            _selfBytes += _pendingSelfBytes;
            if (_pendingSelfBytes > _selfMaxBytes)
            {
                (_selfMaxBytes, _selfMaxFrame) = (_pendingSelfBytes, _frame);
            }
        }
        _frame++;
        if (_frames > 0 && !_written && _recording.Count >= _frames)
        {
            Write();
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        if (!_written && _recording.Count > 0)
        {
            Write();
        }
    }

    private void Sample(Span<double> row, long now, long allocated, ulong compilations)
    {
        if (row.IsEmpty)
        {
            return;
        }

        row[(int)FrameMetric.FrameMs] = (now - _lastStamp) * 1000.0 / Stopwatch.Frequency;
        row[(int)FrameMetric.ProcessMs] = Math.Max(0, _tail.Stamp - _lastStamp) * 1000.0 / Stopwatch.Frequency;
        row[(int)FrameMetric.RenderCpuMs] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport);
        row[(int)FrameMetric.RenderGpuMs] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport);
        // A SubViewport is a separate timed render; include it while the full map draws it.
        if (_world.PreviewPose.HasValue && _world.PreviewRenderViewport is SubViewport preview)
        {
            Rid target = preview.GetViewportRid();
            row[(int)FrameMetric.RenderCpuMs] += RenderingServer.ViewportGetMeasuredRenderTimeCpu(target);
            row[(int)FrameMetric.RenderGpuMs] += RenderingServer.ViewportGetMeasuredRenderTimeGpu(target);
        }
        row[(int)FrameMetric.SetupCpuMs] = RenderingServer.GetFrameSetupTimeCpu();
        row[(int)FrameMetric.DrawCalls] = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        row[(int)FrameMetric.Objects] = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
        row[(int)FrameMetric.Primitives] = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        row[(int)FrameMetric.VideoMemoryMiB] = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / Mebibyte;
        row[(int)FrameMetric.TextureMemoryMiB] = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / Mebibyte;
        row[(int)FrameMetric.BufferMemoryMiB] = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) / Mebibyte;
        row[(int)FrameMetric.AllocatedBytes] = allocated - _lastAllocated - _pendingSelfBytes;
        row[(int)FrameMetric.Gen0Collections] = GC.CollectionCount(0) - _lastCollections[0];
        row[(int)FrameMetric.Gen1Collections] = GC.CollectionCount(1) - _lastCollections[1];
        row[(int)FrameMetric.Gen2Collections] = GC.CollectionCount(2) - _lastCollections[2];
        row[(int)FrameMetric.PipelineCompilations] = compilations >= _lastCompilations ? compilations - _lastCompilations : compilations;
    }

    /// <summary>A window of the asked size, and the scene rendered at that size whatever the window comes out as (a
    /// <see cref="Window.ContentScaleModeEnum.Viewport"/> stretch, the aspect ignored): the project's full screen ignores
    /// <c>--resolution</c>, an <c>override.cfg</c> may size the window, and Windows can trim a window by a few pixels.</summary>
    private void KeepRenderSize(Window window)
    {
        if (_size is not Vector2I size)
        {
            return;
        }

        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(size);
        window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
        window.ContentScaleSize = size;
    }

    private static ulong Compilations() =>
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsCanvas)
        + RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsMesh)
        + RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSurface)
        + RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsDraw)
        + RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSpecialization);

    private void Write()
    {
        _written = true;
        GD.Print("DK2.Godot performance (", _level, ", ", RenderingServer.GetVideoAdapterName(), "): ", _recording.Table());
        var counters = new List<KeyValuePair<string, double>>();
        if (_allThreadsAtStart > 0)
        {
            counters.Add(new("allocated.allThreadsBytes", GC.GetTotalAllocatedBytes(true) - _allThreadsAtStart));
            counters.Add(new("allocated.mainThreadBytes", GC.GetAllocatedBytesForCurrentThread() - _mainAtStart));
        }
        // What the harness itself allocated while measuring (expected 0; left out of allocatedBytes).
        counters.Add(new("harness.selfBytes", _selfBytes));
        counters.Add(new("harness.selfMaxBytes", _selfMaxBytes));
        counters.Add(new("harness.selfMaxFrame", _selfMaxFrame));
        if (_selfBytes != 0)
        {
            GD.PushWarning("DK2.Godot: the performance harness allocated ", _selfBytes, " B while measuring, at most ",
                _selfMaxBytes, " B on frame ", _selfMaxFrame, ".");
        }

        if (IsInstanceValid(_world))
        {
            _world.AddPerformanceCounters(counters);
        }

        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, _recording.Json(Info(), Abbreviated(_arguments), counters, _warmUp));
            GD.Print("DK2.Godot performance report written to ", _path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushError("DK2.Godot: the performance report could not be written to ", _path, ": ", exception.Message);
        }
    }

    private List<KeyValuePair<string, string>> Info()
    {
        Vector2I window = DisplayServer.WindowGetSize();
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2I screen = DisplayServer.ScreenGetSize();
        return new List<KeyValuePair<string, string>>
        {
            new("level", _level),
            new("seed", _seed.ToString(CultureInfo.InvariantCulture)),
            new("utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
            new("godot", Engine.GetVersionInfo()["string"].AsString()),
            new("gpu", RenderingServer.GetVideoAdapterName()),
            new("gpuVendor", RenderingServer.GetVideoAdapterVendor()),
            new("gpuType", RenderingServer.GetVideoAdapterType().ToString()),
            new("driver", string.Join(" ", OS.GetVideoAdapterDriverInfo())),
            new("graphicsApi", RenderingServer.GetCurrentRenderingDriverName() + " " + RenderingServer.GetVideoAdapterApiVersion()),
            new("renderer", RenderingServer.GetCurrentRenderingMethod()),
            new("resolution", FormattableString.Invariant($"{(int)viewport.X}x{(int)viewport.Y}")),
            new("window", FormattableString.Invariant($"{window.X}x{window.Y} {DisplayServer.WindowGetMode()}")),
            new("screen", FormattableString.Invariant($"{screen.X}x{screen.Y}")),
            new("vsync", DisplayServer.WindowGetVsyncMode().ToString()),
            new("cpu", OS.GetProcessorName() + " x" + OS.GetProcessorCount().ToString(CultureInfo.InvariantCulture)),
            new("engineBuild", OS.IsDebugBuild() ? "debug" : "release"),
#if DEBUG
            new("assemblyBuild", "Debug"),
#else
            new("assemblyBuild", "Release"),
#endif
            new("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription),
            new("cameraScript", _camera == null ? "" : LaunchArguments.Value(_arguments, "--perf-camera") ?? ""),
        };
    }

    /// <summary>The integer after <paramref name="key"/>, or <paramref name="fallback"/> when the key is absent; false when
    /// the value is missing or not a plain decimal.</summary>
    private static bool TryCount(string[] args, string key, int fallback, out int value)
    {
        value = fallback;
        if (Array.IndexOf(args, key) < 0)
        {
            return true;
        }

        return int.TryParse(LaunchArguments.Value(args, key), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>A <c>WxH</c> size, null when absent; false when malformed.</summary>
    private static bool TrySize(string? text, out Vector2I? size)
    {
        size = null;
        if (text == null)
        {
            return true;
        }

        int x = text.IndexOf('x');
        if (x <= 0 || !int.TryParse(text.Substring(0, x), NumberStyles.None, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(text.Substring(x + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int height)
            || width <= 0 || height <= 0)
        {
            return false;
        }

        size = new Vector2I(width, height);
        return true;
    }

    /// <summary>The arguments with the user's home folder shown as <c>~</c>, so a shared report names no local account.</summary>
    private static string[] Abbreviated(string[] args)
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile).Replace('\\', '/');
        string[] shown = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].Replace('\\', '/');
            shown[i] = home.Length > 0 && arg.StartsWith(home, StringComparison.OrdinalIgnoreCase)
                ? "~" + arg.Substring(home.Length) : args[i];
        }
        return shown;
    }
}

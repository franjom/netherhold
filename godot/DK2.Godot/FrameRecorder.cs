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

/// <summary>Records per-frame CSV telemetry with <see cref="FrameTelemetry"/>. The buffer is preallocated. <c>tools/perf/draw_clock.py</c> uses the timestamps to analyse the draw clock.</summary>
public partial class FrameRecorder : Node
{
    private FrameTelemetry _telemetry = null!;
    private string _directory = "";
    private string _level = "";
    private ulong _seed;
    private bool _written;
    private readonly List<KeyValuePair<string, string>> _engine = new List<KeyValuePair<string, string>>();

    /// <summary>Adds the recorder under <paramref name="parent"/> when the arguments ask for it; null otherwise, or when the
    /// directory is missing or already holds a capture, which is never written over.</summary>
    public static FrameRecorder? Attach(Node parent, string[] args, string level, ulong seed)
    {
        if (Array.IndexOf(args, "--record-frames") < 0)
        {
            return null;
        }

        string? directory = LaunchArguments.Value(args, "--record-frames");
        if (string.IsNullOrEmpty(directory) || directory.StartsWith("--", StringComparison.Ordinal))
        {
            GD.PushError("DK2.Godot: --record-frames needs a directory; no frames are recorded.");
            return null;
        }
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        if (FrameTelemetry.HoldsCapture(directory))
        {
            GD.PushWarning("DK2.Godot: the recording directory already holds a capture; choose a fresh one: ", directory);
            return null;
        }
        var recorder = new FrameRecorder
        {
            Name = "FrameRecorder",
            _telemetry = new FrameTelemetry(Stopwatch.Frequency),
            _directory = directory,
            _level = level,
            _seed = seed,
        };
        parent.AddChild(recorder);
        GD.Print("DK2.Godot: recording frames to ", directory);
        return recorder;
    }

    public override void _Ready()
    {
        // Record the machine and display metadata once at the start of the run.
        Vector2 size = GetViewport().GetVisibleRect().Size;
        Add("Godot", Engine.GetVersionInfo()["string"].AsString());
        Add("Backend", "Godot .NET");
        Add("Development", OS.IsDebugBuild() ? "True" : "False");
        Add("CPU", OS.GetProcessorName());
        Add("CPUCores", OS.GetProcessorCount().ToString(CultureInfo.InvariantCulture));
        Add("GPU", RenderingServer.GetVideoAdapterName());
        Add("GraphicsDriver", RenderingServer.GetVideoAdapterApiVersion());
        Add("API", RenderingServer.GetCurrentRenderingDriverName());
        Add("SystemMemoryMB", (OS.GetMemoryInfo()["physical"].AsInt64() / (1024 * 1024)).ToString(CultureInfo.InvariantCulture));
        Add("OS", OS.GetName() + " " + OS.GetVersion());
        Add("Resolution", ((int)size.X).ToString(CultureInfo.InvariantCulture) + "x" + ((int)size.Y).ToString(CultureInfo.InvariantCulture));
        Add("Fullscreen", DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen
            ? "True" : "False");
        Add("RefreshHz", DisplayServer.ScreenGetRefreshRate().ToString("F2", CultureInfo.InvariantCulture));
        Add("VSync", DisplayServer.WindowGetVsyncMode().ToString());
        Add("FrameCap", Engine.MaxFps.ToString(CultureInfo.InvariantCulture));
        Add("Hitches", "the Godot log (pass --log-file)");
    }

    private void Add(string key, string value)
    {
        _engine.Add(new KeyValuePair<string, string>(key, value));
    }

    public long Stamp()
    {
        return Stopwatch.GetTimestamp();
    }

    public double Elapsed(long started)
    {
        return _telemetry.Milliseconds(started, Stopwatch.GetTimestamp());
    }

    /// <summary>At the top of a frame with a running level.</summary>
    public void BeginFrame()
    {
        _telemetry.BeginFrame(Stopwatch.GetTimestamp(), GC.CollectionCount(0));
    }

    /// <summary>Adds the time since <paramref name="started"/> to one part of this frame.</summary>
    public void Spent(FrameTelemetryPart part, long started)
    {
        _telemetry.Spent(part, started, Stopwatch.GetTimestamp());
    }

    public void EndFrame(int ticks, double tickMs, double renderTick, System.Numerics.Vector3 camera, bool pathPlaying,
        double chunkRebuildMs)
    {
        _telemetry.EndFrame(Stopwatch.GetTimestamp(),
            new FrameTelemetryEnd(ticks, tickMs, renderTick, camera, pathPlaying, chunkRebuildMs, GC.CollectionCount(0)));
    }

    public override void _ExitTree()
    {
        if (_written)
        {
            return;
        }

        _written = true;
        using (var frames = new StreamWriter(Path.Combine(_directory, FrameTelemetry.FramesFile)))
        {
            _telemetry.WriteCsv(frames);
        }

        using (var metadata = new StreamWriter(Path.Combine(_directory, FrameTelemetry.MetadataFile)))
        {
            _telemetry.WriteMetadata(metadata, _engine, _level, _seed);
        }

        GD.Print("DK2.Godot: recorded ", _telemetry.Count, " frames (", _telemetry.Dropped, " dropped) to ", _directory);
    }
}

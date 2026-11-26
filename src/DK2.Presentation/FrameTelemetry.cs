// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.IO;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>The parts of a frame a <see cref="FrameTelemetry"/> row times, in its columns' order.</summary>
public enum FrameTelemetryPart
{
    Sound,
    /// <summary>The wait for a worker's ticks; zero for an adapter that ticks on the frame's thread.</summary>
    Wait,
    Script,
    Input,
    Present,
    Animation,
    Lights,
    Hud,
}

/// <summary>What a frame's end adds to its <see cref="FrameTelemetry"/> row: the ticks it ran and their time, the draw clock
/// (the drawn turn and its fraction toward the next), the camera in the renderers' frame, whether a camera path played,
/// the chunk upload inside the present, and the generation-0 collection count.</summary>
public readonly record struct FrameTelemetryEnd(int Ticks, double TickMs, double RenderTick, Vector3 Camera, bool PathPlaying,
    double ChunkRebuildMs, int Gen0Collections);

/// <summary>Per-frame telemetry in the CSV columns read by <c>tools/perf/draw_clock.py</c> and the tester
/// package's summarisers. Times are stopwatch timestamps at the constructor's
/// frequency. One preallocated buffer, so recording allocates nothing; frames past the capacity are counted in
/// <see cref="Dropped"/>.</summary>
public sealed class FrameTelemetry
{
    public const string FramesFile = "frames.csv";
    public const string MetadataFile = "metadata.txt";

    /// <summary>The recorder's CSV columns.</summary>
    public const string Header = "frame,play_seconds,frame_ms,update_ms,ticks,tick_ms,sound_ms,wait_tick_ms,script_ms,input_ms,"
        + "present_ms,animated_frame_ms,chunk_rebuild_ms,lights_ms,hud_ms,gc_collections,render_tick,cam_x,cam_y,cam_z,path";

    /// <summary>Capacity for ten minutes at 240 frames a second.</summary>
    public const int DefaultCapacity = 240 * 600;

    private const int PartCount = (int)FrameTelemetryPart.Hud + 1;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private struct Row
    {
        public double Seconds, FrameMs, UpdateMs, TickMs, RebuildMs, RenderTick;
        public Vector3 Camera;
        public int Ticks, Collections;
        public bool Path;
    }

    private readonly double _frequency;
    private readonly Row[] _rows;
    private readonly double[] _parts;
    private long _frameStart, _playStart = -1;
    private double _frameMs;
    private int _collections;

    public FrameTelemetry(long frequency, int capacity = DefaultCapacity)
    {
        if (frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _frequency = frequency;
        _rows = new Row[capacity];
        _parts = new double[capacity * PartCount];
    }

    public int Count { get; private set; }
    public int Dropped { get; private set; }

    /// <summary>Seconds from the first frame to the last recorded one.</summary>
    public double PlaySeconds => Count > 0 ? _rows[Count - 1].Seconds : 0;

    /// <summary>True when <paramref name="directory"/> already holds a capture: a recorder never writes over one.</summary>
    public static bool HoldsCapture(string directory) =>
        File.Exists(Path.Combine(directory, FramesFile)) || File.Exists(Path.Combine(directory, MetadataFile));

    public double Milliseconds(long started, long now) => (now - started) * 1000.0 / _frequency;

    /// <summary>At the top of a frame. The first call starts the play clock; the interval is from the previous call.</summary>
    public void BeginFrame(long now, int gen0Collections)
    {
        if (_playStart < 0)
        {
            _playStart = now;
            _collections = gen0Collections;
            _frameMs = 0;
        }
        else
        {
            _frameMs = Milliseconds(_frameStart, now);
        }

        _frameStart = now;
        if (Count < _rows.Length)
        {
            Array.Clear(_parts, Count * PartCount, PartCount);
        }
    }

    /// <summary>Adds the time from <paramref name="started"/> to <paramref name="now"/> to one part of this frame.</summary>
    public void Spent(FrameTelemetryPart part, long started, long now)
    {
        if (Count < _rows.Length)
        {
            _parts[Count * PartCount + (int)part] += Milliseconds(started, now);
        }
    }

    /// <summary>Ends the frame <see cref="BeginFrame"/> started: its row is kept, or counted in <see cref="Dropped"/>.</summary>
    public void EndFrame(long now, in FrameTelemetryEnd end)
    {
        if (_playStart < 0)
        {
            throw new InvalidOperationException("EndFrame before the first BeginFrame.");
        }

        if (Count == _rows.Length)
        {
            Dropped++;
            return;
        }
        _rows[Count++] = new Row
        {
            Seconds = (_frameStart - _playStart) / _frequency,
            FrameMs = _frameMs,
            UpdateMs = Milliseconds(_frameStart, now),
            TickMs = end.TickMs,
            RebuildMs = end.ChunkRebuildMs,
            RenderTick = end.RenderTick,
            Camera = end.Camera,
            Ticks = Math.Max(0, end.Ticks),
            Collections = Math.Max(0, end.Gen0Collections - _collections),
            Path = end.PathPlaying,
        };
        _collections = end.Gen0Collections;
    }

    /// <summary>The rows under <see cref="Header"/> (milliseconds to 3 places, the draw clock
    /// and the camera to 4). A frame's <c>frame_ms</c> is the interval ending as it began; its parts are its own work.</summary>
    public void WriteCsv(TextWriter writer)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        writer.Write(Header);
        writer.Write('\n');
        for (int i = 0; i < Count; i++)
        {
            ref Row row = ref _rows[i];
            writer.Write(i.ToString(Invariant));
            Write(writer, row.Seconds, "F3");
            Write(writer, row.FrameMs, "F3");
            Write(writer, row.UpdateMs, "F3");
            writer.Write(',');
            writer.Write(row.Ticks.ToString(Invariant));
            Write(writer, row.TickMs, "F3");
            for (int part = (int)FrameTelemetryPart.Sound; part <= (int)FrameTelemetryPart.Animation; part++)
            {
                Write(writer, _parts[i * PartCount + part], "F3");
            }

            Write(writer, row.RebuildMs, "F3");
            Write(writer, _parts[i * PartCount + (int)FrameTelemetryPart.Lights], "F3");
            Write(writer, _parts[i * PartCount + (int)FrameTelemetryPart.Hud], "F3");
            writer.Write(',');
            writer.Write(row.Collections.ToString(Invariant));
            Write(writer, row.RenderTick, "F4");
            Write(writer, row.Camera.X, "F4");
            Write(writer, row.Camera.Y, "F4");
            Write(writer, row.Camera.Z, "F4");
            writer.Write(row.Path ? ",1\n" : ",0\n");
        }
    }

    /// <summary><c>metadata.txt</c>: the adapter's <paramref name="engine"/> lines (machine, build, display), then the
    /// level, seed, sample count and play seconds, as <c>key=value</c> lines.</summary>
    public void WriteMetadata(TextWriter writer, IEnumerable<KeyValuePair<string, string>> engine, string level, ulong seed)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (engine is null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        foreach (KeyValuePair<string, string> pair in engine)
        {
            writer.Write(pair.Key + "=" + pair.Value + "\n");
        }

        writer.Write("Level=" + level + "\nSeed=" + seed.ToString(Invariant) + "\nSamples=" + Count.ToString(Invariant)
            + "\nDropped=" + Dropped.ToString(Invariant) + "\nPlaySeconds=" + PlaySeconds.ToString("F1", Invariant) + "\n");
    }

    private static void Write(TextWriter writer, double value, string format)
    {
        writer.Write(',');
        writer.Write(value.ToString(format, Invariant));
    }

    private static void Write(TextWriter writer, float value, string format)
    {
        writer.Write(',');
        writer.Write(value.ToString(format, Invariant));
    }
}

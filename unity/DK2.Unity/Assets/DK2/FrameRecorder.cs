// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DK2.Unity
{
    /// <summary>Per-frame telemetry a release player writes when asked (docs/PERFORMANCE.md, the
    /// desktop tier): <c>-recordFrames [directory]</c> on the command line, or a file named
    /// <c>record-frames</c> beside the executable. The profiler's recorders exist only in development
    /// players, so this reads stopwatches of its own. Nothing allocates on an ordinary frame; the rows
    /// go to <c>frames.csv</c>, in the game probe's columns, when the runner ends.</summary>
    internal sealed class FrameRecorder
    {
        public enum Part { Sound, Wait, Script, Input, Present, Animation, Lights, Hud, Count }

        private struct Row
        {
            public float Seconds, FrameMs, UpdateMs, TickMs, SoundMs, WaitMs, ScriptMs, InputMs, PresentMs, AnimationMs, RebuildMs, LightsMs, HudMs;
            public double RenderTick;
            public Vector3 Camera;
            public byte Ticks, Collections;
            public bool Path;
        }

        // Ten minutes at 240 frames a second; a longer run keeps its first ten minutes.
        private const int Capacity = 240 * 600;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private readonly Row[] _rows = new Row[Capacity];
        private readonly double[] _parts = new double[(int)Part.Count];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly string _directory;
        private int _count;
        private long _frameStart, _playStart = -1;
        private double _frameMs;
        private int _collections;
        private bool _written;

        private FrameRecorder(string directory) => _directory = directory;

        /// <summary>The recorder when the player was asked for one, else null.</summary>
        public static FrameRecorder TryCreate()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-recordFrames");
            string beside = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "record-frames");
            if (at < 0 && !File.Exists(beside)) return null;
            string directory = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("-", StringComparison.Ordinal)
                ? args[at + 1]
                : Path.Combine(Application.persistentDataPath, "frames", DateTime.Now.ToString("yyyyMMdd-HHmmss", Invariant));
            Directory.CreateDirectory(directory);
            Debug.Log($"DK2: recording frames to {directory}");
            return new FrameRecorder(directory);
        }

        public long Stamp() => _clock.ElapsedTicks;

        public double Elapsed(long started) => (_clock.ElapsedTicks - started) * 1000.0 / Stopwatch.Frequency;

        /// <summary>Adds the time since <paramref name="started"/> to one part of this frame.</summary>
        public void Spent(Part part, long started) => _parts[(int)part] += Elapsed(started);

        /// <summary>At the top of a frame with a running simulation. The first call starts the play
        /// clock; the frame interval is from the previous call.</summary>
        public void BeginFrame()
        {
            long now = _clock.ElapsedTicks;
            if (_playStart < 0)
            {
                _playStart = now;
                _collections = GC.CollectionCount(0);
            }
            _frameMs = _frameStart > 0 ? (now - _frameStart) * 1000.0 / Stopwatch.Frequency : 0;
            _frameStart = now;
            Array.Clear(_parts, 0, _parts.Length);
        }

        /// <param name="tickMs">The worker's time in the ticks this frame joined, if any.</param>
        public void EndFrame(int ticks, double tickMs, double renderTick, Vector3 camera, bool pathPlaying, double rebuildMs)
        {
            if (_count == Capacity) return;
            int collections = GC.CollectionCount(0);
            _rows[_count++] = new Row
            {
                Seconds = (float)((_frameStart - _playStart) / (double)Stopwatch.Frequency),
                FrameMs = (float)_frameMs,
                UpdateMs = (float)Elapsed(_frameStart),
                TickMs = (float)tickMs,
                SoundMs = (float)_parts[(int)Part.Sound],
                WaitMs = (float)_parts[(int)Part.Wait],
                ScriptMs = (float)_parts[(int)Part.Script],
                InputMs = (float)_parts[(int)Part.Input],
                PresentMs = (float)_parts[(int)Part.Present],
                AnimationMs = (float)_parts[(int)Part.Animation],
                RebuildMs = (float)rebuildMs,
                LightsMs = (float)_parts[(int)Part.Lights],
                HudMs = (float)_parts[(int)Part.Hud],
                RenderTick = renderTick,
                Camera = camera,
                Ticks = (byte)Math.Min(ticks, byte.MaxValue),
                Collections = (byte)Math.Min(collections - _collections, byte.MaxValue),
                Path = pathPlaying,
            };
            _collections = collections;
        }

        /// <summary>Writes <c>frames.csv</c> and <c>metadata.txt</c>; once.</summary>
        public void Write(string level, ulong seed)
        {
            if (_written) return;
            _written = true;
            var csv = new StringBuilder(_count * 100 + 200);
            // update_ms is the runner's Update from its first line to its last; the rest of frame_ms is
            // the engine's: rendering, presenting, other components, and whatever the driver does.
            csv.Append("frame,play_seconds,frame_ms,update_ms,ticks,tick_ms,sound_ms,wait_tick_ms,script_ms,input_ms,present_ms,")
               .Append("animated_frame_ms,chunk_rebuild_ms,lights_ms,hud_ms,gc_collections,render_tick,cam_x,cam_y,cam_z,path\n");
            for (int i = 0; i < _count; i++)
            {
                ref Row row = ref _rows[i];
                csv.Append(i).Append(',')
                   .Append(row.Seconds.ToString("F3", Invariant)).Append(',')
                   .Append(row.FrameMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.UpdateMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.Ticks).Append(',')
                   .Append(row.TickMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.SoundMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.WaitMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.ScriptMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.InputMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.PresentMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.AnimationMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.RebuildMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.LightsMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.HudMs.ToString("F3", Invariant)).Append(',')
                   .Append(row.Collections).Append(',')
                   .Append(row.RenderTick.ToString("F4", Invariant)).Append(',')
                   .Append(row.Camera.x.ToString("F4", Invariant)).Append(',')
                   .Append(row.Camera.y.ToString("F4", Invariant)).Append(',')
                   .Append(row.Camera.z.ToString("F4", Invariant)).Append(',')
                   .Append(row.Path ? '1' : '0').Append('\n');
            }
            File.WriteAllText(Path.Combine(_directory, "frames.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(_directory, "metadata.txt"), Metadata(level, seed));
            Debug.Log($"DK2: recorded {_count} frames to {_directory}");
        }

        private string Metadata(string level, ulong seed) =>
            $"Unity={Application.unityVersion}\n" +
#if ENABLE_IL2CPP
            "Backend=IL2CPP\n" +
#else
            "Backend=Mono\n" +
#endif
            $"Development={Debug.isDebugBuild}\nCPU={SystemInfo.processorType}\nCPUCores={SystemInfo.processorCount}\n" +
            $"GPU={SystemInfo.graphicsDeviceName}\nGraphicsDriver={SystemInfo.graphicsDeviceVersion}\nAPI={SystemInfo.graphicsDeviceType}\n" +
            $"GraphicsMemoryMB={SystemInfo.graphicsMemorySize}\nSystemMemoryMB={SystemInfo.systemMemorySize}\nOS={SystemInfo.operatingSystem}\n" +
            $"Resolution={Screen.width}x{Screen.height}\nFullscreen={Screen.fullScreen}\nRefreshHz={Screen.currentResolution.refreshRateRatio.value.ToString("F2", Invariant)}\n" +
            $"VSync={QualitySettings.vSyncCount}\nFrameCap={Application.targetFrameRate}\nLevel={level}\nSeed={seed}\n" +
            $"Samples={_count}\nPlaySeconds={(_count > 0 ? _rows[_count - 1].Seconds : 0f).ToString("F1", Invariant)}\n" +
            "Hitches=Player.log (the DK2 hitch lines name first-use art by kind)\n";
    }
}

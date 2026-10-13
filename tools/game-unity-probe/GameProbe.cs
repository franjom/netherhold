// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace DK2.Unity
{
    /// <summary>
    /// Plays a campaign level through the production <see cref="DungeonRunner"/> at 60 frames a
    /// second for a fixed time, standing in for a player who tags walls to dig, and records each
    /// frame from the Unity Profiler's recorders: frame time, ticks run, the tick, the presenter,
    /// animation, chunk rebuilds, the heaviest simulation markers and managed allocation. Every
    /// time is play time, from the first frame with a running simulation, so loading the level
    /// moves nothing. Rows go into preallocated storage and are formatted only at the end, so the
    /// probe allocates nothing in the frames it measures. Copied into an isolated scratch project
    /// by <c>run.ps1</c>; never part of the game.
    /// </summary>
    // First, so frameMs (Update to Update) spans the same frame as the recorders' previous frame.
    [DefaultExecutionOrder(-1000)]
    public sealed class GameProbe : MonoBehaviour
    {
        // name in the CSV, Profiler marker, category
        private static readonly (string Column, string Marker, ProfilerCategory Category)[] Recorded =
        {
            ("main_thread_ms", "Main Thread", ProfilerCategory.Internal),
            ("tick_ms", "DK2.Simulation.Tick", ProfilerCategory.Scripts),
            ("present_ms", "DK2.Scene.Present", ProfilerCategory.Scripts),
            ("animated_frame_ms", "DK2.Scene.AnimatedFrame", ProfilerCategory.Scripts),
            ("chunk_rebuild_ms", "DK2.TerrainChunks.Rebuild", ProfilerCategory.Scripts),
            ("imp_task_ms", "DK2.Sim.ImpTask", ProfilerCategory.Scripts),
            ("grid_nearest_ms", "DK2.Sim.grid.nearest", ProfilerCategory.Scripts),
            ("nav_refresh_ms", "DK2.Sim.nav.refresh", ProfilerCategory.Scripts),
            ("nav_build_ms", "DK2.Sim.nav.build", ProfilerCategory.Scripts),
            ("movement_ms", "DK2.Sim.CreatureMovement", ProfilerCategory.Scripts),
            ("triggers_ms", "DK2.Sim.triggers", ProfilerCategory.Scripts),
            ("chunk_geometry_ms", "DK2.TerrainChunks.Geometry", ProfilerCategory.Scripts),
            ("chunk_upload_ms", "DK2.TerrainChunks.Upload", ProfilerCategory.Scripts),
            ("transparent_set_ms", "DK2.TransparentTerrain.SetChunk", ProfilerCategory.Scripts),
            ("transparent_sort_ms", "DK2.TransparentTerrain.Sort", ProfilerCategory.Scripts),
        };

        private const int Tick = 1, Present = 2, Animated = 3, Chunk = 4, ImpTask = 5, Grid = 6, Nav = 7;

        // Frames in the first seconds of play are left out of the summary: the first full present.
        private const float WarmUpSeconds = 2;

        private readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        private ProfilerRecorder _gcAllocated;
        private readonly Stopwatch _clock = new Stopwatch();
        private Row[] _rows;
        private double[] _values;
        private int _count;
        private DungeonRunner _runner;
        private List<PlayerCommand> _pending;
        private string _output;
        private float _duration, _captureFrom, _captureTo;
        private int _frame, _gcCount, _errors;
        private string _firstError;
        private long _lastFrameTicks, _playStart = -1;
        private bool _wave1, _wave2, _capturing, _captured;

        private struct Row
        {
            public int Frame, Collections;
            public float Seconds;
            public double FrameMs;
            public long Ticks, GridCalls, GcBytes;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("DK2 game probe").AddComponent<GameProbe>();

        private static string Arg(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        private static float Seconds(string key, float fallback) =>
            float.Parse(Arg(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

        private void Awake()
        {
            // An error the game logs makes the run's timings worthless (logging an exception every
            // frame is itself a spike), so the summary leads with it and the exit code says so.
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                if (_errors++ == 0) _firstError = message;
            };
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            _output = Arg("-probeOutput", Application.persistentDataPath);
            Directory.CreateDirectory(_output);
            _duration = Seconds("-probeSeconds", 150);
            // Off unless asked: a full Profiler capture adds overhead to the frames it records.
            _captureFrom = Seconds("-captureFrom", 0);
            _captureTo = Seconds("-captureTo", 0);

            // Room for every frame at well over the frame cap, made now rather than while measuring.
            int capacity = (int)(_duration * 120) + 1000;
            _rows = new Row[capacity];
            _values = new double[capacity * Recorded.Length];

            // The production runner, configured as its Inspector would be, before its Start runs.
            _runner = gameObject.AddComponent<DungeonRunner>();
            Set("installDirectory", Arg("-dk2Install", "E:/GogGames/Dungeon Keeper 2"));
            Set("levelName", Arg("-level", "Level1"));
            Set("seed", ulong.Parse(Arg("-seed", "1"), CultureInfo.InvariantCulture));
            _pending = (List<PlayerCommand>)typeof(DungeonRunner)
                .GetField("_pendingCommands", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_runner);

            foreach (var (_, marker, category) in Recorded)
                _recorders.Add(ProfilerRecorder.StartNew(category, marker, 1));
            _gcAllocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            _clock.Start();
        }

        private void Set(string field, object value) =>
            typeof(DungeonRunner).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_runner, value);

        private void Update()
        {
            try
            {
                long now = _clock.ElapsedTicks;
                double frameMs = (now - _lastFrameTicks) * 1000.0 / Stopwatch.Frequency;
                _lastFrameTicks = now;

                if (_runner.Simulation == null)
                {
                    // The runner reports a failed start as a logged error; waiting longer shows nothing.
                    if (_errors > 0 || _clock.Elapsed.TotalSeconds > 120)
                        throw new InvalidOperationException($"the runner did not start: {_firstError ?? "timed out"}");
                    return;
                }
                if (_playStart < 0)
                {
                    _playStart = now;
                    _gcCount = GC.CollectionCount(0);
                }
                float seconds = (float)((now - _playStart) / (double)Stopwatch.Frequency);

                // Recorder values are the previous frame's, as is frameMs.
                if (_frame > 0) Record(_frame - 1, seconds, frameMs);
                _frame++;

                // A player tagging walls round the heart, twice: the imps dig, the mesh rebuilds and
                // the chunks redraw, which is what play does.
                if (!_wave1 && seconds >= 5) { _wave1 = true; TagAroundHeart(8); }
                if (!_wave2 && seconds >= 60) { _wave2 = true; TagAroundHeart(16); }

                if (_captureTo > _captureFrom && !_capturing && !_captured && seconds >= _captureFrom)
                {
                    Profiler.logFile = Path.Combine(_output, "capture");
                    Profiler.enableBinaryLog = true;
                    Profiler.enabled = true;
                    _capturing = true;
                }
                if (_capturing && seconds >= _captureTo)
                {
                    Profiler.enabled = false;
                    Profiler.enableBinaryLog = false;
                    Profiler.logFile = "";
                    _capturing = false;
                    _captured = true;
                }

                if (seconds >= _duration || _count == _rows.Length) Finish();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                enabled = false;
                Application.Quit(1);
            }
        }

        private void Record(int frame, float seconds, double frameMs)
        {
            int collections = GC.CollectionCount(0) - _gcCount;
            _gcCount += collections;
            int at = _count * Recorded.Length;
            for (int i = 0; i < _recorders.Count; i++)
                _values[at + i] = _recorders[i].Valid ? _recorders[i].LastValue / 1e6 : double.NaN;
            _rows[_count++] = new Row
            {
                Frame = frame, Seconds = seconds, FrameMs = frameMs, Collections = collections,
                Ticks = _recorders[Tick].Valid ? _recorders[Tick].GetSample(0).Count : 0,
                GridCalls = _recorders[Grid].Valid ? _recorders[Grid].GetSample(0).Count : 0,
                GcBytes = _gcAllocated.Valid ? _gcAllocated.LastValue : -1,
            };
        }

        private double Value(int row, int marker) => _values[row * Recorded.Length + marker];

        private void TagAroundHeart(int radius)
        {
            WorldState world = _runner.Simulation.World;
            GameDatabase database = _runner.Simulation.Database;
            byte heart = database.Room(RoomTypeId.DungeonHeart).TerrainId;
            var hearts = new List<TilePos>();
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if (world.Tile(x, y).TerrainId == heart && world.Tile(x, y).OwnerPlayerId == PlayerIds.FirstKeeper)
                        hearts.Add(new TilePos(x, y));
            if (hearts.Count == 0) throw new InvalidOperationException("no heart for the first keeper");
            int cx = (int)hearts.Average(t => t.X), cy = (int)hearts.Average(t => t.Y);

            // Every solid tile in the square; the tag system keeps the taggable ones.
            var tiles = new List<TilePos>();
            for (int y = Math.Max(0, cy - radius); y <= Math.Min(world.Height - 1, cy + radius); y++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(world.Width - 1, cx + radius); x++)
                    if (database.TerrainById(world.Tile(x, y).TerrainId).IsSolid) tiles.Add(new TilePos(x, y));
            _pending.Add(new TagTilesCommand(new PlayerId(PlayerIds.FirstKeeper), tiles, true));
            Debug.Log($"DK2 game probe: tagged {tiles.Count} solid tiles within {radius} of the heart at ({cx},{cy})");
        }

        private void Finish()
        {
            enabled = false;
            WriteCsv();
            string chunkCheck = CheckChunksAgainstFullRebuild();

            int[] rows = Enumerable.Range(0, _count).Where(i => _rows[i].Seconds >= WarmUpSeconds).ToArray();
            double[] frames = rows.Select(i => _rows[i].FrameMs).OrderBy(v => v).ToArray();
            double P(double p) => frames.Length == 0 ? double.NaN : frames[Math.Min(frames.Length - 1, (int)Math.Ceiling(frames.Length * p) - 1)];
            double MeanOf(Func<int, double> f, bool ticked) =>
                rows.Where(i => (_rows[i].Ticks > 0) == ticked).Select(f).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Average();

            var text = new StringBuilder();
            if (_errors > 0)
                text.AppendLine($"INVALID RUN: the game logged {_errors} errors; first: {_firstError}");
            text.AppendLine(chunkCheck);
            string[] neverValid = Recorded.Where((r, i) => !_recorders[i].Valid).Select(r => r.Marker).ToArray();
            if (neverValid.Length > 0)
                text.AppendLine("markers never recorded (NaN in the CSV): " + string.Join(", ", neverValid));
            text.AppendLine(FormattableString.Invariant(
                $"{rows.Length} frames after {WarmUpSeconds} s of play: frame p50 {P(.5):F2} ms, p95 {P(.95):F2}, p99 {P(.99):F2}, max {frames.LastOrDefault():F2}"));
            text.AppendLine(FormattableString.Invariant(
                $"frames over 33 ms: {rows.Count(i => _rows[i].FrameMs > 33)}; with a tick: {rows.Count(i => _rows[i].Ticks > 0)}; gen-0 collections: {rows.Sum(i => _rows[i].Collections)}; GC allocated {rows.Where(i => _rows[i].GcBytes > 0).Sum(i => _rows[i].GcBytes) / 1048576.0:F1} MB"));
            text.AppendLine(FormattableString.Invariant(
                $"mean frame without a tick {MeanOf(i => _rows[i].FrameMs, false):F2} ms (present {MeanOf(i => Value(i, Present), false):F2}, animation {MeanOf(i => Value(i, Animated), false):F2}); with a tick {MeanOf(i => _rows[i].FrameMs, true):F2} ms (tick {MeanOf(i => Value(i, Tick), true):F2})"));
            text.AppendLine("worst frames: frame, play s, frame_ms, ticks, tick, present, animation, chunk rebuild, ImpTask, grid.nearest (calls), nav.refresh, GC KB, collections");
            foreach (int i in rows.OrderByDescending(i => _rows[i].FrameMs).Take(15))
            {
                Row r = _rows[i];
                text.AppendLine(FormattableString.Invariant(
                    $"  {r.Frame,6} {r.Seconds,7:F1} {r.FrameMs,8:F2} {r.Ticks,3} {Value(i, Tick),8:F2} {Value(i, Present),8:F2} {Value(i, Animated),8:F2} {Value(i, Chunk),8:F2} {Value(i, ImpTask),8:F2} {Value(i, Grid),8:F2} ({r.GridCalls}) {Value(i, Nav),8:F2} {r.GcBytes / 1024.0,9:F1} {r.Collections}"));
            }
            File.WriteAllText(Path.Combine(_output, "summary.txt"), text.ToString());
            Debug.Log("DK2 game probe: done\n" + text);
            foreach (ProfilerRecorder recorder in _recorders) recorder.Dispose();
            _gcAllocated.Dispose();
            Application.Quit(_errors > 0 ? 2 : 0);
        }

        /// <summary>
        /// The terrain's chunks are assembled from tiles cached since they last changed. After the
        /// run, every chunk mesh is snapshotted, every drawn tile marked stale and the terrain
        /// flushed, which rebuilds each tile from scratch; the two must agree bit for bit, or the
        /// cache kept a tile whose inputs had changed. A mismatch counts as an error. It compares
        /// the cache against this build's own full rebuild, so it proves no stale tile survived,
        /// for the changes this run made (digging and tagging, with the lattice on); that the
        /// assembly matches the old in-place build is by construction, not tested here.
        /// </summary>
        private string CheckChunksAgainstFullRebuild()
        {
            const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
            object scene = typeof(DungeonRunner).GetField("_scene", Private).GetValue(_runner);
            object terrain = scene?.GetType().GetField("_terrain", Private).GetValue(scene);
            if (terrain == null) return "chunk cache check: no classic terrain";
            Type type = terrain.GetType();
            var chunks = (Dictionary<int, GameObject>)type.GetField("_chunks", Private).GetValue(terrain);
            var drawn = (bool[])type.GetField("_drawn", Private).GetValue(terrain);

            // A flush rebuilds within a frame budget (ADR-0457); the check needs every chunk done,
            // before the snapshot and after the forced rebuild.
            type.GetProperty("FlushBudgetMilliseconds").SetValue(terrain, 0.0);
            MethodInfo flush = type.GetMethod("Flush");
            flush.Invoke(terrain, null);
            Dictionary<int, string> before = chunks.ToDictionary(c => c.Key, c => Describe(c.Value));
            MethodInfo markStale = type.GetMethod("MarkStale", Private);
            for (int i = 0; i < drawn.Length; i++)
                if (drawn[i]) markStale.Invoke(terrain, new object[] { i });
            flush.Invoke(terrain, null);
            Dictionary<int, string> after = chunks.ToDictionary(c => c.Key, c => Describe(c.Value));

            int[] differ = before.Keys.Union(after.Keys)
                .Where(k => !before.TryGetValue(k, out string a) || !after.TryGetValue(k, out string b) || a != b)
                .OrderBy(k => k).ToArray();
            if (differ.Length == 0) return $"chunk cache check: all {before.Count} chunks match a full rebuild";
            Debug.LogError($"DK2 game probe: {differ.Length} chunks differ from a full rebuild: {string.Join(", ", differ)}");
            return $"chunk cache check: {differ.Length} of {before.Count} chunks DIFFER from a full rebuild";
        }

        private static string Describe(GameObject chunk)
        {
            if (chunk == null) return "";
            Mesh mesh = chunk.GetComponent<MeshFilter>().sharedMesh;
            var text = new StringBuilder();
            // Every float by its bits, so equal strings mean equal meshes.
            foreach (Vector3 v in mesh.vertices) Bits(Bits(Bits(text, v.x), v.y), v.z);
            text.Append('|');
            foreach (Vector3 n in mesh.normals) Bits(Bits(Bits(text, n.x), n.y), n.z);
            text.Append('|');
            foreach (Vector2 uv in mesh.uv) Bits(Bits(text, uv.x), uv.y);
            for (int s = 0; s < mesh.subMeshCount; s++)
                text.Append('|').Append(string.Join(",", mesh.GetTriangles(s)));
            foreach (Material material in chunk.GetComponent<MeshRenderer>().sharedMaterials)
                text.Append('|').Append(material == null ? "null" : material.name).Append('#').Append(material == null ? 0 : material.GetHashCode());
            return text.ToString();
        }

        private static StringBuilder Bits(StringBuilder text, float value) =>
            text.Append(BitConverter.ToInt32(BitConverter.GetBytes(value), 0)).Append(',');

        private void WriteCsv()
        {
            var csv = new StringBuilder();
            csv.AppendLine("frame,play_seconds,frame_ms,ticks,grid_nearest_calls,gc_alloc_bytes,gc_collections," +
                           string.Join(",", Recorded.Select(r => r.Column)));
            for (int i = 0; i < _count; i++)
            {
                Row r = _rows[i];
                csv.Append(r.Frame).Append(',').Append(r.Seconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(r.FrameMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(r.Ticks).Append(',')
                    .Append(r.GridCalls).Append(',').Append(r.GcBytes).Append(',').Append(r.Collections);
                for (int m = 0; m < Recorded.Length; m++)
                    csv.Append(',').Append(Value(i, m).ToString("F3", CultureInfo.InvariantCulture));
                csv.AppendLine();
            }
            File.WriteAllText(Path.Combine(_output, "frames.csv"), csv.ToString());
        }
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using DK2.Simulation;

namespace DK2.Harness;

/// <summary>Where a scenario run spent its wall-clock time and its allocations, marker by marker
/// (<see cref="SimulationMarker"/>): each system, the spans below them, the keeper AI and the
/// per-tick state hash. Per tick as well as in total, because a frame drop is the worst tick, not
/// the mean one. Observation only: nothing in the run reads it, and it is kept out of the written
/// report so two runs' reports still compare equal.</summary>
public sealed class RunTimings
{
    internal RunTimings(TimeSpan total, int ticks, TimeSpan ticking, long allocated,
        TickDistribution perTick, IReadOnlyList<MarkerTiming> markers, WorstTick worst)
    {
        Total = total;
        Ticks = ticks;
        Ticking = ticking;
        Allocated = allocated;
        PerTick = perTick;
        Markers = markers;
        Worst = worst;
    }

    /// <summary>The whole run, setup included.</summary>
    public TimeSpan Total { get; }

    public int Ticks { get; }

    /// <summary>The ticks alone: simulation, state hash and keeper AI.</summary>
    public TimeSpan Ticking { get; }

    /// <summary>Building the world and everything else outside a tick.</summary>
    public TimeSpan Setup => Total - Ticking;

    /// <summary>Bytes allocated on the run's thread during ticks.</summary>
    public long Allocated { get; }

    /// <summary>How long one tick took: median, 99th percentile, worst.</summary>
    public TickDistribution PerTick { get; }

    /// <summary>Every marker that ran, most self time first.</summary>
    public IReadOnlyList<MarkerTiming> Markers { get; }

    /// <summary>Tick time no marker covers.</summary>
    public TimeSpan Unmarked => Ticking - TimeSpan.FromTicks(Markers.Sum(m => m.Self.Ticks));

    /// <summary>The slowest tick, marker by marker.</summary>
    public WorstTick Worst { get; }

    /// <summary>A few lines for a test's output: the per-tick distribution, the markers with the
    /// most self time, then the worst tick's breakdown.</summary>
    public string Summarise(int top = 12)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"{Total.TotalSeconds:F2} s, {Ticks} ticks: tick p50 {Ms(PerTick.P50)}, p99 {Ms(PerTick.P99)}, " +
            $"max {Ms(PerTick.Max)} (tick {Worst.Tick}); setup {Setup.TotalSeconds:F2} s; " +
            $"{Allocated / 1048576.0:F1} MB allocated ({Kb(Allocated, Ticks)} KB/tick)");
        text.AppendLine();
        text.Append("     self/tick    total/tick      p99/tick      max/tick  calls/tick  KB/tick  marker");
        foreach (MarkerTiming m in Markers.Take(top))
        {
            text.AppendLine();
            text.Append(CultureInfo.InvariantCulture,
                $"  {Ms(PerTickOf(m.Self)),12}  {Ms(PerTickOf(m.Total)),12}  {Ms(m.P99),12}  {Ms(m.Max),12}  " +
                $"{(double)m.Calls / Math.Max(1, Ticks),10:F1}  {Kb(m.AllocatedSelf, Ticks),7}  {m.Name}");
        }
        text.AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"  {Ms(PerTickOf(Unmarked)),12}  unmarked");

        text.AppendLine();
        text.Append(CultureInfo.InvariantCulture,
            $"worst tick {Worst.Tick}: {Ms(Worst.Time)}, {Kb(Worst.Allocated, 1)} KB allocated");
        text.AppendLine();
        text.Append("          self         total   calls       KB  marker");
        foreach (MarkerTick m in Worst.Markers.Take(top))
        {
            text.AppendLine();
            text.Append(CultureInfo.InvariantCulture,
                $"  {Ms(m.Self),12}  {Ms(m.Total),12}  {m.Calls,6}  {Kb(m.AllocatedSelf, 1),7}  {m.Name}");
        }
        return text.ToString();
    }

    private TimeSpan PerTickOf(TimeSpan time) => Ticks == 0 ? TimeSpan.Zero : time / Ticks;

    private static string Ms(TimeSpan time) => string.Create(CultureInfo.InvariantCulture, $"{time.TotalMilliseconds:F3} ms");

    private static string Kb(long bytes, int ticks) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / Math.Max(1, ticks):F1}");

}

public readonly record struct TickDistribution(TimeSpan P50, TimeSpan P99, TimeSpan Max);

/// <summary>One marker over the whole run. <see cref="Total"/> includes the markers opened inside
/// it; <see cref="Self"/> does not, so the selves add up to the marked time. The percentiles are
/// of its total per tick, over every tick, counting a tick it did not run in as zero.</summary>
public sealed record MarkerTiming(
    string Name, long Calls, TimeSpan Total, TimeSpan Self, long AllocatedSelf,
    TimeSpan P50, TimeSpan P99, TimeSpan Max);

/// <summary>One marker within one tick.</summary>
public sealed record MarkerTick(string Name, long Calls, TimeSpan Total, TimeSpan Self, long AllocatedSelf);

public sealed record WorstTick(int Tick, TimeSpan Time, long Allocated, IReadOnlyList<MarkerTick> Markers);

/// <summary>
/// The harness's <see cref="ISimulationProfiler"/>. Built on <see cref="Stopwatch.GetTimestamp"/>
/// and <see cref="GC.GetAllocatedBytesForCurrentThread"/>, each tens of nanoseconds, so it stays on
/// for every run. The caller brackets each tick with <see cref="BeginTick"/> and <see cref="EndTick"/>;
/// a span outside a tick, or an <see cref="End"/> that is not the innermost open span, throws,
/// because a profile with a mismatched span would be attributing time to the wrong place.
/// </summary>
internal sealed class RunClock : ISimulationProfiler
{
    public static readonly SimulationMarker KeeperAi = SimulationMarker.Named("keeper AI");
    public static readonly SimulationMarker Controllers = SimulationMarker.Named("test controllers");
    public static readonly SimulationMarker StateHash = SimulationMarker.Named("state hash");

    private readonly long _start = Stopwatch.GetTimestamp();

    // This tick, indexed by marker id; _touched lists the ids to read and clear at its end.
    private long[] _total = [];
    private long[] _self = [];
    private long[] _allocated = [];
    private long[] _calls = [];
    private readonly List<int> _touched = [];

    private Frame[] _stack = new Frame[16];
    private int _depth;

    private bool _inTick;
    private int _tick;
    private long _tickStart;
    private long _tickAllocStart;

    private readonly List<TickRecord> _ticks = [];
    private long _ticking;
    private long _tickAllocated;
    private int _worst = -1;

    public void BeginTick(int tick)
    {
        if (_inTick) throw new InvalidOperationException($"tick {_tick} is still open");
        _inTick = true;
        _tick = tick;
        _tickAllocStart = GC.GetAllocatedBytesForCurrentThread();
        _tickStart = Stopwatch.GetTimestamp();
    }

    public void EndTick()
    {
        long elapsed = Stopwatch.GetTimestamp() - _tickStart;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - _tickAllocStart;
        if (!_inTick) throw new InvalidOperationException("no tick is open");
        if (_depth != 0) throw new InvalidOperationException($"{_stack[_depth - 1].Marker} is still open at the end of tick {_tick}");
        _inTick = false;

        var spans = new MarkerSpan[_touched.Count];
        for (int i = 0; i < _touched.Count; i++)
        {
            int id = _touched[i];
            spans[i] = new MarkerSpan(id, _calls[id], _total[id], _self[id], _allocated[id]);
            _calls[id] = _total[id] = _self[id] = _allocated[id] = 0;
        }
        _touched.Clear();

        _ticks.Add(new TickRecord(_tick, elapsed, allocated, spans));
        _ticking += elapsed;
        _tickAllocated += allocated;
        if (_worst < 0 || elapsed > _ticks[_worst].Time) _worst = _ticks.Count - 1;
    }

    public void Begin(SimulationMarker marker)
    {
        if (!_inTick) throw new InvalidOperationException($"{marker} began outside a tick");
        if (_depth == _stack.Length) Array.Resize(ref _stack, _stack.Length * 2);
        _stack[_depth++] = new Frame(marker, GC.GetAllocatedBytesForCurrentThread(), Stopwatch.GetTimestamp());
    }

    public void End(SimulationMarker marker)
    {
        long now = Stopwatch.GetTimestamp();
        long allocNow = GC.GetAllocatedBytesForCurrentThread();
        if (_depth == 0 || !ReferenceEquals(_stack[_depth - 1].Marker, marker))
            throw new InvalidOperationException(
                $"{marker} ended, but the innermost open span is {(_depth == 0 ? "none" : _stack[_depth - 1].Marker.Name)}");

        Frame frame = _stack[--_depth];
        long elapsed = now - frame.Start;
        long allocated = allocNow - frame.AllocStart;

        int id = marker.Id;
        if (id >= _total.Length) Grow(id);
        if (_calls[id] == 0) _touched.Add(id);
        _calls[id]++;
        _total[id] += elapsed;
        _self[id] += elapsed - frame.ChildTime;
        _allocated[id] += allocated - frame.ChildAllocated;

        if (_depth > 0)
        {
            _stack[_depth - 1].ChildTime += elapsed;
            _stack[_depth - 1].ChildAllocated += allocated;
        }
    }

    public RunTimings Stop()
    {
        long total = Stopwatch.GetTimestamp() - _start;
        int ticks = _ticks.Count;

        // Every marker's total per tick, dense, zero where it did not run.
        var perTick = new Dictionary<int, long[]>();
        var sums = new Dictionary<int, MarkerSpan>();
        for (int t = 0; t < ticks; t++)
        {
            foreach (MarkerSpan span in _ticks[t].Spans)
            {
                if (!perTick.TryGetValue(span.Id, out long[]? series)) perTick[span.Id] = series = new long[ticks];
                series[t] = span.Total;
                sums[span.Id] = sums.TryGetValue(span.Id, out MarkerSpan sum) ? sum.Plus(span) : span;
            }
        }

        List<MarkerTiming> markers = [.. sums.Values
            .Select(s =>
            {
                long[] series = perTick[s.Id];
                Array.Sort(series);
                return new MarkerTiming(SimulationMarker.ForId(s.Id).Name, s.Calls, Elapsed(s.Total), Elapsed(s.Self),
                    s.Allocated, Elapsed(Percentile(series, 50)), Elapsed(Percentile(series, 99)), Elapsed(series[^1]));
            })
            .OrderByDescending(m => m.Self)
            .ThenBy(m => m.Name, StringComparer.Ordinal)];

        long[] times = [.. _ticks.Select(t => t.Time)];
        Array.Sort(times);
        TickDistribution distribution = ticks == 0
            ? default
            : new TickDistribution(Elapsed(Percentile(times, 50)), Elapsed(Percentile(times, 99)), Elapsed(times[^1]));

        WorstTick worst = _worst < 0
            ? new WorstTick(-1, TimeSpan.Zero, 0, [])
            : new WorstTick(_ticks[_worst].Tick, Elapsed(_ticks[_worst].Time), _ticks[_worst].Allocated,
                [.. _ticks[_worst].Spans
                    .Select(s => new MarkerTick(SimulationMarker.ForId(s.Id).Name, s.Calls, Elapsed(s.Total), Elapsed(s.Self), s.Allocated))
                    .OrderByDescending(m => m.Self)
                    .ThenBy(m => m.Name, StringComparer.Ordinal)]);

        return new RunTimings(Elapsed(total), ticks, Elapsed(_ticking), _tickAllocated, distribution, markers, worst);
    }

    /// <summary>Nearest rank over a sorted, non-empty series.</summary>
    private static long Percentile(long[] sorted, int percent) =>
        sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * percent / 100.0) - 1, 0, sorted.Length - 1)];

    private void Grow(int id)
    {
        int size = Math.Max(id + 1, Math.Max(64, _total.Length * 2));
        Array.Resize(ref _total, size);
        Array.Resize(ref _self, size);
        Array.Resize(ref _allocated, size);
        Array.Resize(ref _calls, size);
    }

    private static TimeSpan Elapsed(long timestamps) =>
        TimeSpan.FromSeconds((double)timestamps / Stopwatch.Frequency);

    private struct Frame(SimulationMarker marker, long allocStart, long start)
    {
        public readonly SimulationMarker Marker = marker;
        public readonly long AllocStart = allocStart;
        public readonly long Start = start;
        public long ChildTime;
        public long ChildAllocated;
    }

    private readonly record struct MarkerSpan(int Id, long Calls, long Total, long Self, long Allocated)
    {
        public MarkerSpan Plus(MarkerSpan other) =>
            new(Id, Calls + other.Calls, Total + other.Total, Self + other.Self, Allocated + other.Allocated);
    }

    private sealed record TickRecord(int Tick, long Time, long Allocated, MarkerSpan[] Spans);
}

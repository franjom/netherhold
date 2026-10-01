// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using DK2.Simulation;

namespace DK2.Harness;

/// <summary>Where a scenario run spent its wall-clock time: the systems one by one, the keeper
/// AI, the per-tick state hash, and everything else. Observation only: nothing in the run reads
/// it, and it is kept out of the written report so two runs' reports still compare equal.</summary>
public sealed class RunTimings
{
    internal RunTimings(TimeSpan total, TimeSpan keepers, TimeSpan hashing, int ticks,
        IReadOnlyList<SystemTiming> systems)
    {
        Total = total;
        Keepers = keepers;
        Hashing = hashing;
        Ticks = ticks;
        Systems = systems;
    }

    public TimeSpan Total { get; }
    public TimeSpan Keepers { get; }
    public TimeSpan Hashing { get; }
    public int Ticks { get; }

    /// <summary>Each system's time over the run, slowest first.</summary>
    public IReadOnlyList<SystemTiming> Systems { get; }

    public TimeSpan Simulation => TimeSpan.FromTicks(Systems.Sum(s => s.Time.Ticks));

    /// <summary>Setup, triggers, input and rendering: the total less what is itemised.</summary>
    public TimeSpan Other => Total - Simulation - Keepers - Hashing;

    /// <summary>A few lines for a test's output: the split, then the slowest systems.</summary>
    public string Summarise(int top = 8)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"{Total.TotalSeconds:F2} s over {Ticks} ticks ({PerTick(Total)}/tick): " +
            $"systems {Seconds(Simulation)}, keeper AI {Seconds(Keepers)}, state hash {Seconds(Hashing)}, " +
            $"other {Seconds(Other)}");
        foreach (SystemTiming system in Systems.Take(top))
        {
            text.AppendLine();
            text.Append(CultureInfo.InvariantCulture,
                $"  {Seconds(system.Time),9}  {PerTick(system.Time),10}/tick  {system.Name}");
        }
        return text.ToString();
    }

    private static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s";

    private string PerTick(TimeSpan time) => Ticks == 0
        ? "-"
        : (time.TotalMilliseconds / Ticks).ToString("F3", CultureInfo.InvariantCulture) + " ms";
}

public readonly record struct SystemTiming(string Name, TimeSpan Time);

/// <summary>Accumulates elapsed time for one run. Built on <see cref="Stopwatch.GetTimestamp"/>,
/// which costs tens of nanoseconds, so it stays on for every run.</summary>
internal sealed class RunClock
{
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly List<TimedSystem> _systems = [];
    private long _keepers;
    private long _hashing;

    /// <summary>Wraps each system so its <c>Execute</c> is timed. The order and the systems
    /// themselves are unchanged, so the run is the same run.</summary>
    public IEnumerable<ISimulationSystem> Wrap(IEnumerable<ISimulationSystem> systems)
    {
        foreach (ISimulationSystem system in systems)
        {
            var timed = new TimedSystem(system);
            _systems.Add(timed);
            yield return timed;
        }
    }

    public static long Now() => Stopwatch.GetTimestamp();

    public void AddKeepers(long since) => _keepers += Stopwatch.GetTimestamp() - since;

    public void AddHashing(long since) => _hashing += Stopwatch.GetTimestamp() - since;

    public RunTimings Stop(int ticks) => new(
        Elapsed(Stopwatch.GetTimestamp() - _start), Elapsed(_keepers), Elapsed(_hashing), ticks,
        [.. _systems.Select(s => new SystemTiming(s.Name, Elapsed(s.Elapsed))).OrderByDescending(s => s.Time)]);

    private static TimeSpan Elapsed(long timestamps) =>
        TimeSpan.FromSeconds((double)timestamps / Stopwatch.Frequency);

    private sealed class TimedSystem(ISimulationSystem inner) : ISimulationSystem
    {
        public long Elapsed;

        public string Name => inner.Name;

        public void Execute(SimulationContext context)
        {
            long start = Stopwatch.GetTimestamp();
            inner.Execute(context);
            Elapsed += Stopwatch.GetTimestamp() - start;
        }
    }
}

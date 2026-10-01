// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using System.Diagnostics;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Which state ids <see cref="CreatureStateMachine"/> has written since the process
/// started, for the coverage tests that hold <see cref="CreatureStateCoverage"/> to what the
/// code does. Off unless the runtime switch <see cref="SwitchName"/> is set, as the test
/// projects set it.</summary>
/// <remarks>Free when off: <see cref="Enabled"/> is a <c>static readonly</c> read once, which
/// the tiered JIT folds to a constant, so the guarded call is removed from the state write.
/// Thread-safe when on, because xUnit runs test classes in parallel.</remarks>
public static class StateEntryLog
{
    /// <summary>The <c>runtimeconfig.json</c> switch that turns recording on.</summary>
    public const string SwitchName = "DK2.Simulation.RecordStateEntries";

    /// <summary>Whether this process records.</summary>
    public static readonly bool Enabled =
        AppContext.TryGetSwitch(SwitchName, out bool on) && on;

    private static readonly long[] Bits = new long[(CreatureStateTable.Count + 63) / 64];
    private static readonly ConcurrentDictionary<int, string> FirstCaller = new();

    // Per thread, because a suspended test and a recorded one may run at the same time.
    [ThreadStatic] private static int _suspended;

    /// <summary>Stops recording on this thread until the result is disposed: for a test that
    /// drives the transition gate with arbitrary ids, which is not the game entering them.</summary>
    public static Suspension Unrecorded()
    {
        _suspended++;
        return default;
    }

    /// <summary>The scope <see cref="Unrecorded"/> opens.</summary>
    public readonly struct Suspension : IDisposable
    {
        public void Dispose() => _suspended--;
    }

    // netstandard2.1 has no Interlocked.Or; a set bit is never cleared, so the loop ends.
    internal static void Record(int stateId)
    {
        if (_suspended > 0) return;
        ref long word = ref Bits[stateId >> 6];
        long bit = 1L << (stateId & 63);
        long seen = Interlocked.Read(ref word);
        if ((seen & bit) == 0) FirstCaller.TryAdd(stateId, OutsideCaller());
        while ((seen & bit) == 0)
        {
            long was = Interlocked.CompareExchange(ref word, seen | bit, seen);
            if (was == seen) return;
            seen = was;
        }
    }

    /// <summary>Every state id written so far, ascending.</summary>
    public static IReadOnlyList<int> Entered()
    {
        var ids = new List<int>();
        for (int id = 0; id < CreatureStateTable.Count; id++)
        {
            if ((Interlocked.Read(ref Bits[id >> 6]) & (1L << (id & 63))) != 0) ids.Add(id);
        }

        return ids;
    }

    /// <summary>The first method outside this assembly that led to the state's first entry,
    /// such as the test that ran it, or null if the state was never entered.</summary>
    public static string? FirstEnteredBy(int stateId) =>
        FirstCaller.TryGetValue(stateId, out string? who) ? who : null;

    private static string OutsideCaller()
    {
        foreach (StackFrame frame in new StackTrace().GetFrames())
        {
            Type? type = frame.GetMethod()?.DeclaringType;
            if (type is not null && type.Assembly != typeof(StateEntryLog).Assembly
                && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true)
                return $"{type.FullName}.{frame.GetMethod()!.Name}";
        }

        return "(no caller outside DK2.Simulation)";
    }
}

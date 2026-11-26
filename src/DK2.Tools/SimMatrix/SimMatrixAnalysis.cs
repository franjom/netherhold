// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DK2.Tools.SimMatrix;

/// <summary>A hub: code so many rows reach that it would paint every row the same. The core view stops at it.</summary>
internal sealed record Hub(string Name, Func<IMethodSymbol, bool> Contains);

/// <summary>One row seen through everything it reaches, or through the core that stops at the hubs.</summary>
/// <param name="Cells">Per column: "W" written, "R" read and not written, "" neither.</param>
internal sealed record RowView(
    int Methods,
    IReadOnlyList<string> Cells,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Writes,
    IReadOnlyList<string> SystemOwnedWrites,
    int DelegateCalls,
    IReadOnlyList<string> Hubs);

internal sealed record RowResult(SimRow Row, RowView Core, RowView Full);

/// <summary>A field outside the columns, and the rows that write it.</summary>
internal sealed record WrittenField(string Name, string Column, IReadOnlyList<int> Rows);

internal sealed record HubResult(string Name, IReadOnlyList<int> Rows, int Methods);

/// <summary>Two neighbouring rows with no state one writes and the other reads or writes.</summary>
internal sealed record FreePair(int First, int Second, bool Core, bool Full);

internal sealed record SimMatrixResult(
    int Files,
    int Methods,
    IReadOnlyList<RowResult> Rows,
    IReadOnlyDictionary<string, string> StateTypes,
    IReadOnlyList<string> SharedTypes,
    IReadOnlyList<string> Unmapped,
    IReadOnlyList<HubResult> Hubs,
    IReadOnlyList<WrittenField> SystemOwned,
    IReadOnlyList<WrittenField> StaticState,
    IReadOnlyList<FreePair> FreePairs);

/// <summary>Builds the matrix: the rows, what each reaches, and where what it reads and writes sits.</summary>
internal static class SimMatrixAnalysis
{
    internal const string TriggerExecutorType = "DK2.Simulation.TriggerExecutor";
    internal const string WorldSteeringType = "DK2.Simulation.Navigation.WorldSteering";
    internal const string EventSinkType = "DK2.Simulation.ISimulationEventSink";

    internal static SimMatrixResult Run(string root) => Run(SimCompilation.Load(root));

    internal static SimMatrixResult Run(SimCompilation source)
    {
        var index = new SourceIndex(source);
        Compilation compilation = index.Compilation;
        IReadOnlyList<SimRow> rows = SimRows.Read(source);
        var model = new StateModel(index);

        INamedTypeSymbol[] entryFaces = [.. new[] { SimRows.SystemInterface, SimRows.CreatureStepInterface, SimRows.RoomStepInterface }.Select(n => SimRows.Type(compilation, n))];
        INamedTypeSymbol sink = SimRows.Type(compilation, EventSinkType);
        IMethodSymbol sinkKey = sink.GetMembers().OfType<IMethodSymbol>().First();
        INamedTypeSymbol triggers = SimRows.Type(compilation, TriggerExecutorType);
        INamedTypeSymbol steering = SimRows.Type(compilation, WorldSteeringType);
        Hub[] hubs =
        [
            new("TriggerExecutor", m => Within(m.ContainingType, triggers)),
            new("WorldSteering.Refresh", m => m.Name == "Refresh" && SymbolEqualityComparer.Default.Equals(m.ContainingType, steering)),
        ];

        bool Stop(IMethodSymbol m) =>
            entryFaces.Any(face => SymbolEqualityComparer.Default.Equals(m.ContainingType, face))
            || model.IsExcludedType(m.ContainingType);
        ISymbol? Sink(IMethodSymbol m) =>
            SymbolEqualityComparer.Default.Equals(m.ContainingType, sink)
            || m.ContainingType.AllInterfaces.Contains(sink, SymbolEqualityComparer.Default) ? sinkKey : null;

        var summaries = new ConcurrentDictionary<IMethodSymbol, MethodSummary>(SymbolEqualityComparer.Default);
        MethodSummary Summary(IMethodSymbol m) => summaries.GetOrAdd(m, x => MethodWalker.Summarise(x, index, Stop, Sink));

        HashSet<IMethodSymbol> Reach(IEnumerable<IMethodSymbol> entries, Func<IMethodSymbol, bool> stopAt)
        {
            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var queue = new Queue<IMethodSymbol>();
            foreach (IMethodSymbol e in entries)
            {
                if (seen.Add(e))
                {
                    queue.Enqueue(e);
                }
            }

            while (queue.TryDequeue(out IMethodSymbol? m))
            {
                foreach (IMethodSymbol call in Summary(m).Calls)
                {
                    foreach (IMethodSymbol target in index.Targets(call))
                    {
                        if (!stopAt(target) && seen.Add(target))
                        {
                            queue.Enqueue(target);
                        }
                    }
                }
            }
            return seen;
        }

        // Summaries are independent; build them in parallel before the walks read them.
        var full = new HashSet<IMethodSymbol>[rows.Count];
        var core = new HashSet<IMethodSymbol>[rows.Count];
        Parallel.For(0, rows.Count, i =>
        {
            full[i] = Reach(rows[i].Entries, _ => false);
            core[i] = Reach(rows[i].Entries, m => hubs.Any(h => h.Contains(m)));
        });

        var allMethods = new HashSet<IMethodSymbol>(full.SelectMany(s => s), SymbolEqualityComparer.Default);
        var writtenStatics = new HashSet<ISymbol>(
            allMethods.SelectMany(m => Summary(m).Writes).Where(w => w.IsStatic && w is not IMethodSymbol),
            SymbolEqualityComparer.Default);

        var places = new Dictionary<ISymbol, (FieldPlace Place, int Column)>(SymbolEqualityComparer.Default);
        (FieldPlace Place, int Column) Place(ISymbol key)
        {
            if (!places.TryGetValue(key, out (FieldPlace Place, int Column) place))
            {
                places[key] = place = SymbolEqualityComparer.Default.Equals(key, sinkKey)
                    ? (FieldPlace.Column, StateModel.EventsOut)
                    : model.Place(key, writtenStatics);
            }

            return place;
        }

        RowView View(HashSet<IMethodSymbol> reach)
        {
            var reads = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var writes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            int delegates = 0;
            foreach (IMethodSymbol m in reach)
            {
                MethodSummary s = Summary(m);
                reads.UnionWith(s.Reads);
                writes.UnionWith(s.Writes);
                // An event or callback raised is an emit: it counts as a write only of an output.
                writes.UnionWith(s.Raises.Where(k => Place(k) == (FieldPlace.Column, StateModel.EventsOut)));
                delegates += s.DelegateCalls;
            }
            // Commands are input: what a system "writes" there is the command index's per-tick memo.
            ISymbol[] memo = [.. writes.Where(k => Place(k) == (FieldPlace.Column, StateModel.CommandsIn))];
            writes.ExceptWith(memo);
            reads.UnionWith(memo);
            string[] cells = new string[StateModel.Columns.Length];
            Array.Fill(cells, "");
            foreach (ISymbol key in reads)
            {
                if (Place(key) is (FieldPlace.Column, var c))
                {
                    cells[StateModel.ReadColumn(key, c)] = "R";
                }
            }

            foreach (ISymbol key in writes)
            {
                if (Place(key) is (FieldPlace.Column, var c))
                {
                    cells[c] = "W";
                }
            }

            return new RowView(
                reach.Count,
                cells,
                Names(reads.Where(k => Place(k).Place == FieldPlace.Column && !writes.Contains(k))),
                Names(writes.Where(k => Place(k).Place == FieldPlace.Column)),
                Names(writes.Where(k => Place(k).Place == FieldPlace.SystemOwned)),
                delegates,
                hubs.Where(h => reach.Any(h.Contains)).Select(h => h.Name).ToArray());
        }

        string Name(ISymbol key) => SymbolEqualityComparer.Default.Equals(key, sinkKey)
            ? "events"
            : $"{SimRows.Display(key.ContainingType)}.{key.Name}";
        IReadOnlyList<string> Names(IEnumerable<ISymbol> keys) => keys.Select(Name).Distinct().Order(StringComparer.Ordinal).ToArray();

        RowResult[] results = [.. rows.Select((row, i) => new RowResult(row, View(core[i]), View(full[i])))];

        HubResult[] hubResults = [.. hubs.Select(h =>
        {
            IMethodSymbol[] reached = [.. allMethods.Where(h.Contains)];
            int methods = Reach(reached, _ => false).Count;
            return new HubResult(h.Name, results.Where(r => r.Full.Hubs.Contains(h.Name)).Select(r => r.Row.Number).ToArray(), methods);
        })];

        var writers = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var columnOf = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            foreach (IMethodSymbol m in full[i])
            {
                foreach (ISymbol key in Summary(m).Writes)
            {
                (FieldPlace place, int column) = Place(key);
                bool listed = place == FieldPlace.SystemOwned || (place == FieldPlace.Column && key.IsStatic && key is not IMethodSymbol);
                if (!listed)
                    {
                        continue;
                    }

                    string name = Name(key);
                if (!writers.TryGetValue(name, out SortedSet<int>? set))
                    {
                        writers[name] = set = [];
                    }

                    set.Add(rows[i].Number);
                columnOf[name] = place == FieldPlace.SystemOwned ? "" : StateModel.Columns[column];
            }
            }
        }
        WrittenField[] Listed(bool systemOwned) => [.. writers
            .Where(p => (columnOf[p.Key] == "") == systemOwned)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new WrittenField(p.Key, columnOf[p.Key], p.Value.ToArray()))];

        return new SimMatrixResult(
            source.Files,
            allMethods.Count,
            results,
            model.TypeColumn
                .Where(p => p.Value != StateModel.Excluded)
                .ToDictionary(p => p.Key.ToDisplayString(), p => model.IsShared(p.Key) ? "shared" : StateModel.Columns[p.Value], StringComparer.Ordinal),
            model.MultiColumn
                .Select(m => $"{m.Type.ToDisplayString()}: {string.Join(", ", m.Columns.Select(c => c == StateModel.Excluded ? "excluded" : StateModel.Columns[c]))}")
                .Order(StringComparer.Ordinal).ToArray(),
            [.. model.Unmapped.Order(StringComparer.Ordinal)],
            hubResults,
            Listed(systemOwned: true),
            Listed(systemOwned: false),
            FreePairs(results));
    }

    private static bool Within(INamedTypeSymbol? type, INamedTypeSymbol outer)
    {
        for (; type is not null; type = type.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, outer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Neighbours: consecutive systems, or consecutive steps of one container. A container is compared
    /// with its neighbours as itself and all its steps together, which is what runs in its slot.</summary>
    private static FreePair[] FreePairs(IReadOnlyList<RowResult> rows)
    {
        (HashSet<string> Writes, HashSet<string> Touched) Data(RowResult row, bool core)
        {
            RowView[] members = [.. rows.Where(r => r.Row.Number == row.Row.Number || r.Row.Parent == row.Row.Number).Select(r => core ? r.Core : r.Full)];
            var writes = members.SelectMany(v => v.Writes).Where(n => n != "events").ToHashSet(StringComparer.Ordinal);
            var touched = members.SelectMany(v => v.Writes.Concat(v.Reads)).Where(n => n != "events").ToHashSet(StringComparer.Ordinal);
            return (writes, touched);
        }
        bool Free(RowResult a, RowResult b, bool core)
        {
            (HashSet<string>? aw, HashSet<string>? at) = Data(a, core);
            (HashSet<string>? bw, HashSet<string>? bt) = Data(b, core);
            return !aw.Overlaps(bt) && !bw.Overlaps(at);
        }

        var pairs = new List<FreePair>();
        foreach (IGrouping<int, RowResult> group in rows.GroupBy(r => r.Row.Parent))
        {
            RowResult[] siblings = [.. group];
            for (int i = 1; i < siblings.Length; i++)
            {
                bool core = Free(siblings[i - 1], siblings[i], core: true);
                bool full = Free(siblings[i - 1], siblings[i], core: false);
                if (core || full)
                {
                    pairs.Add(new FreePair(siblings[i - 1].Row.Number, siblings[i].Row.Number, core, full));
                }
            }
        }
        return [.. pairs.OrderBy(p => p.First)];
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.Tools.SimMatrix;
using Microsoft.CodeAnalysis;

namespace DK2.Tools.Tests;

/// <summary>The simulation read/write matrix (issue 9, WP6): its rows against the running order, its
/// column tables against the world's members, its write rules, and its bytes.</summary>
public class SimMatrixTests
{
    static string Root => Repo.FindRepoRoot() ?? throw new InvalidOperationException("no repository above the test");

    // One analysis of the repository for the tests that read it (about five seconds).
    static readonly Lazy<SimMatrixResult> Repository = new(() => SimMatrixAnalysis.Run(Root));

    [Fact]
    public void RowsAreTheSystemsAndStepsDefaultRuns()
    {
        var expected = new List<string>();
        foreach (ISimulationSystem system in SimulationSystems.Default())
        {
            expected.Add(Name(system));
            if (system is CreatureUpdateSystem creatures) expected.AddRange(creatures.Steps.Select(Name));
            if (system is RoomTickSystem rooms) expected.AddRange(rooms.Steps.Select(Name));
        }

        var rows = Repository.Value.Rows.Select(r => r.Row.Type.ToDisplayString()).ToArray();

        Assert.Equal(expected, rows);
        Assert.All(Repository.Value.Rows.Where(r => r.Row.Parent > 0),
            r => Assert.Contains(Repository.Value.Rows[r.Row.Parent - 1].Row.Name, new[] { "CreatureUpdateSystem", "RoomTickSystem" }));

        static string Name(object o) => o.GetType().FullName!.Replace('+', '.');
    }

    [Fact]
    public void EveryWorldAndContextMemberHasAColumn()
    {
        // A new WorldState or SimulationContext member needs a line in StateModel's tables.
        Assert.Empty(Repository.Value.Unmapped);
    }

    [Fact]
    public void BothHubsAreReachedAndTheCoreViewStopsAtThem()
    {
        var result = Repository.Value;
        Assert.All(result.Hubs, h => Assert.NotEmpty(h.Rows));
        var triggers = result.Rows.Single(r => r.Row.Name == "ActionPointTriggerSystem");
        Assert.Contains("TriggerExecutor", triggers.Full.Hubs);
        Assert.Empty(triggers.Core.Hubs);
        Assert.All(result.Rows, r => Assert.True(r.Core.Methods <= r.Full.Methods, r.Row.Name));
    }

    [Fact]
    public void TwoRunsWriteTheSameBytes()
    {
        var second = SimMatrixAnalysis.Run(Root);
        Assert.Equal(SimMatrixReport.Markdown(Repository.Value), SimMatrixReport.Markdown(second));
        Assert.Equal(SimMatrixReport.Json(Repository.Value), SimMatrixReport.Json(second));
        Assert.DoesNotContain('\r', SimMatrixReport.Markdown(second));
    }

    const string Sample = """
        using System.Collections.Generic;
        namespace S;
        public struct Spot { public int X; public void Move() => X++; }
        public sealed class Holder
        {
            public int Count;
            public Spot Spot;
            public readonly List<int> Items = new();
            public Spot[] Spots = new Spot[2];
            public event System.Action? Changed;
            public System.Action? Callback;
            public Holder? Next;
            public Kind Flags;
            public int[] Dst = new int[4];
            public int Index;
            public List<int> Raw => Items;
            public Holder() { Count = 1; Spot.X = 2; Spots[1].X = 3; }
            public Holder(Holder other) { other.Count = 2; other.Items.Add(1); }
            public void Plain() => Count = 3;
            public void IntoStruct() => Spot.X = 4;
            public void StructCall() => Spot.Move();
            public void IntoArray() => Spots[0].X = 5;
            public void Mutate() => Items.Add(6);
            public void ReadOnly() { int n = Items.Count + Count; }
            public void Fresh() { var h = new Holder(); h.Count = 7; h.Items.Add(8); }
            public void Initialiser() { var h = new Holder { Count = 9 }; }
            public void Raise() => Changed?.Invoke();
            public void CallBack() => Callback?.Invoke();
            public void Chain() => Next?.Next?.Items.Clear();
            public void Predicate() { bool b = Flags.HasFlag(Kind.A); string t = Flags.ToString(); }
            public void ThroughAccessor() => Raw.Add(1);
            public void CopyFive(int[] src) => System.Array.Copy(src, Index, Dst, 0, 2);
            public void Reassigned() { var h = new Holder(); h = Next!; h.Count = 1; }
        }
        [System.Flags] public enum Kind { A = 1, B = 2 }
        public interface IStep { void Run(Holder h); }
        public sealed class A : IStep { public void Run(Holder h) => h.Count = 1; }
        public sealed class B : IStep { public void Run(Holder h) => h.Items.Clear(); }
        public static class Caller { public static void Call(IStep step, Holder h) => step.Run(h); }
        """;

    static SourceIndex Compile() => new(SimCompilation.FromText(Sample));

    static MethodSummary Summary(SourceIndex index, string type, string method)
    {
        var symbol = index.Compilation.GetTypeByMetadataName(type)!.GetMembers(method).OfType<IMethodSymbol>().Single();
        return MethodWalker.Summarise(symbol, index, _ => false, _ => null);
    }

    static string[] Names(IEnumerable<ISymbol> keys) =>
        keys.Select(k => $"{k.ContainingType.Name}.{k.Name}").Order(StringComparer.Ordinal).ToArray();

    [Theory]
    [InlineData("Plain", "Holder.Count")]
    [InlineData("IntoStruct", "Holder.Spot,Spot.X")]
    [InlineData("StructCall", "Holder.Spot")]
    [InlineData("IntoArray", "Holder.Spots,Spot.X")]
    [InlineData("Mutate", "Holder.Items")]
    [InlineData("ReadOnly", "")]
    [InlineData("Fresh", "")]
    [InlineData("Initialiser", "")]
    [InlineData("Raise", "")]
    [InlineData("CallBack", "")]
    [InlineData("Chain", "Holder.Items")]
    [InlineData("Predicate", "")]
    [InlineData("ThroughAccessor", "Holder.Items")]
    [InlineData("CopyFive", "Holder.Dst")]
    [InlineData("Reassigned", "Holder.Count")]
    public void WritesFollowTheRules(string method, string written)
    {
        var index = Compile();
        var summary = Summary(index, "S.Holder", method);
        Assert.Equal(written.Length == 0 ? [] : written.Split(','), Names(summary.Writes));
    }

    [Fact]
    public void AConstructorWritingItsOwnInstanceIsNotAWrite()
    {
        var index = Compile();
        var constructors = index.Compilation.GetTypeByMetadataName("S.Holder")!.InstanceConstructors;
        var own = constructors.Single(c => c.Parameters.Length == 0);
        var other = constructors.Single(c => c.Parameters.Length == 1);
        Assert.Empty(MethodWalker.Summarise(own, index, _ => false, _ => null).Writes);
        Assert.Equal(["Holder.Count", "Holder.Items"], Names(MethodWalker.Summarise(other, index, _ => false, _ => null).Writes));
    }

    [Fact]
    public void RaisingIsRecordedApartFromWrites()
    {
        var index = Compile();
        Assert.Equal(["Holder.Changed"], Names(Summary(index, "S.Holder", "Raise").Raises));
        Assert.Equal(["Holder.Callback"], Names(Summary(index, "S.Holder", "CallBack").Raises));
    }

    [Fact]
    public void PhasesAndLabelsAreTheSource()
    {
        var rows = Repository.Value.Rows.Select(r => r.Row).ToArray();
        Assert.Equal(["Commands", "RoomLifecycle", "KeeperActions", "ThingPass", "Work", "RoomTicks", "PlayerFrame", "Needs", "Verdict"],
            rows.Select(r => r.Phase).Distinct());
        Assert.Contains(rows, r => r.Name == "HatcherySystem(lays: false, stepObjects: false)");
        Assert.Contains(rows, r => r.Name == "DungeonHeartSystem(frames: false)");
        Assert.All(rows.Where(r => r.Kind == RowKind.CreatureStep), r => Assert.Equal(["BeginTurn", "Update", "EndTurn"], r.Entries.Select(e => e.Name)));
    }

    [Fact]
    public void ReadingARoomInstanceIsReadingRoomsAndRebuildingItIsNot()
    {
        // RoomTickSystem walks every RoomInstance and keeps no room state of its own.
        var rooms = Array.IndexOf(StateModel.Columns, "Rooms");
        var walk = Repository.Value.Rows.Single(r => r.Row.Name == "RoomTickSystem");
        Assert.Equal("R", walk.Core.Cells[rooms]);
        Assert.All(Repository.Value.Rows.Where(r => r.Core.Writes.All(w => !w.StartsWith("TileState.", StringComparison.Ordinal)
                                                                         && !w.StartsWith("WorldState.", StringComparison.Ordinal)
                                                                         && !w.StartsWith("RoomBuildWaveState.", StringComparison.Ordinal)
                                                                         && !w.StartsWith("TempleSacrificeState.", StringComparison.Ordinal)
                                                                         && !w.StartsWith("GraveyardState.", StringComparison.Ordinal)
                                                                         && !w.StartsWith("SacrificeIngredient.", StringComparison.Ordinal)
                                                                         && !w.StartsWith("RoomBorderCursor.", StringComparison.Ordinal))),
            r => Assert.NotEqual("W", r.Core.Cells[rooms]));
    }

    [Fact]
    public void AWorldAccessorReturningAListCarriesItsWrites()
    {
        // TempleSacrificeRules removes through WorldState.RawTempleSacrifices => _templeSacrifices.
        var temple = Repository.Value.Rows.Single(r => r.Row.Name == "TempleSacrificeSystem");
        Assert.Contains("WorldState._templeSacrifices", temple.Core.Writes);
    }

    [Fact]
    public void AnInterfaceCallReachesEveryImplementation()
    {
        var index = Compile();
        var call = Assert.Single(Summary(index, "S.Caller", "Call").Calls);
        var targets = index.Targets(call).Select(t => t.ContainingType.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["A", "B"], targets);
    }

    [Fact]
    public void ADelegateCallIsCountedNotFollowed()
    {
        var index = Compile();
        var summary = Summary(index, "S.Holder", "Raise");
        Assert.Equal(1, summary.DelegateCalls);
        Assert.Empty(summary.Calls);
    }
}

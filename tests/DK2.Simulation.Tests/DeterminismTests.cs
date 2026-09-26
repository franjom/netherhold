// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Linq;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// The determinism gate: the same seed and the same inputs must produce the
/// same state hash, tick for tick. Must stay green on every platform and must
/// never require a DK2 install.
/// </summary>
public class DeterminismTests
{
    private const int Ticks = 1000;

    [Fact]
    public void SameSeedAndInput_ProduceIdenticalHashAtEveryTick()
    {
        GameSimulation a = TestWorld.Simulation();
        GameSimulation b = TestWorld.Simulation();

        Assert.Equal(a.StateHash(), b.StateHash());

        for (int t = 0; t < Ticks; t++)
        {
            InputFrame input = InputAt(t);
            a.Tick(input);
            b.Tick(InputAt(t)); // freshly built each time — value equality, not identity

            Assert.Equal(a.CurrentTick, b.CurrentTick);
            Assert.True(a.StateHash() == b.StateHash(),
                $"runs diverged at {a.CurrentTick}");
        }
    }

    [Fact]
    public void DifferentSeed_Diverges()
    {
        // A gate that cannot fail proves nothing: the hash has to discriminate.
        GameSimulation a = TestWorld.Simulation(seed: 1);
        GameSimulation b = TestWorld.Simulation(seed: 2);

        a.Run(50);
        b.Run(50);

        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void DifferentInput_Diverges()
    {
        GameSimulation a = TestWorld.Simulation();
        GameSimulation b = TestWorld.Simulation();

        a.Tick(InputFrame.Of(new AddManaCommand(1, 100)));
        b.Tick(InputFrame.Empty);

        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void RngPosition_IsPartOfTheHash()
    {
        // Two worlds identical in every visible way, but one has consumed a
        // draw. They must not compare equal: they are about to diverge.
        GameSimulation a = TestWorld.Simulation();
        GameSimulation b = TestWorld.Simulation();

        b.Rng.NextUInt64();

        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void EventRecording_DoesNotAffectState()
    {
        GameSimulation quiet = TestWorld.Simulation();
        GameSimulation observed = TestWorld.Simulation();
        var sink = new ListEventSink();
        observed.ObserveWith(sink);

        quiet.Run(100);
        observed.Run(100);

        Assert.Equal(quiet.StateHash(), observed.StateHash());
        Assert.NotEmpty(sink.Events);
    }

    [Fact]
    public void StateHash_IsStableAcrossBuildsAndMachines()
    {
        // A pinned literal. It catches what comparing two runs in one process
        // cannot — string GetHashCode, dictionary iteration order, struct
        // layout drift. A deliberate change to simulation state or to the hash
        // bumps GameSimulation.HashVersion, bumps WorldSnapshot.FormatVersion
        // to match and updates this literal, all in the same commit. Any other
        // failure means determinism is broken and the change must not ship.
        GameSimulation sim = TestWorld.Simulation(seed: 0xDEADBEEF);
        sim.Run(500);

        // HashVersion 121: a door's orientation (ADR-0233).
        // Earlier pins and their reasons live in docs/research/hash-versions.md.
        Assert.Equal(0x954849F97CFC0296UL, sim.StateHash());
    }

    /// <summary>The pin above only guards creature state while there is creature state
    /// in it. For four days there was none and nobody noticed, because an empty
    /// population makes every creature assertion pass by vacuity (Q206) — so the
    /// population is asserted here rather than trusted.</summary>
    [Fact]
    public void ThePinnedWorldIsPopulated_OrItGuardsNoCreatureField()
    {
        GameSimulation sim = TestWorld.Simulation(seed: 0xDEADBEEF);

        Assert.True(sim.World.Creatures.Count >= 2,
            "The gate's world needs at least two creatures: one so CreatureState.HashInto "
            + "runs at all, a second so the order of WorldState.Creatures is hashed too.");
        Assert.Equal(
            sim.World.Creatures.Count,
            sim.World.Creatures.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void FreshWorld_MatchesFreshWorld()
    {
        GameDatabase database = TestWorld.Database();
        WorldState one = TestWorld.World(database);
        WorldState two = TestWorld.World(database);

        var a = new GameSimulation(database, one, seed: 7);
        var b = new GameSimulation(database, two, seed: 7);

        Assert.Equal(a.StateHash(), b.StateHash());
    }

    private static InputFrame InputAt(int tick) => tick % 7 == 0
        ? InputFrame.Of(new AddManaCommand((byte)(tick % 3), tick))
        : InputFrame.Empty;
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// The determinism gate. This is the project's load-bearing test: every claim
/// of behavioural fidelity made from M3 onwards is only meaningful if a run is
/// reproducible in the first place.
///
/// It must stay green on every platform and must never require a DK2 install.
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
        // A gate that cannot fail proves nothing. This asserts the hash really
        // does discriminate between runs.
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
        // A pinned value. Its purpose is to catch the silent killers of
        // cross-machine reproducibility — string GetHashCode, dictionary
        // iteration order, struct layout drift — which every other test here
        // would happily pass.
        //
        // If this fails after a deliberate change to simulation state or to the
        // hash itself, bump GameSimulation.HashVersion and update the value in
        // the same commit. If it fails for any other reason, determinism is
        // broken and the change must not ship.
        GameSimulation sim = TestWorld.Simulation(seed: 0xDEADBEEF);
        sim.Run(500);

        // Bumped with HashVersion 10 -> 11 (ADR-0048): CreatureState gained
        // Health, HungerTicks and ChickensNeeded, so the hash composition
        // changed. This fixture does spawn imps, so the value moved for two
        // reasons at once: three new fields in the composition, and health
        // actually being non-zero on every creature. Snapshot FormatVersion
        // went 10 -> 11 in the same commit. Previous values: 0x06F71648486D0FEC
        // (HashVersion 10), 0x4F824667CDA7EF25 (HashVersion 9).
        Assert.Equal(0x12E4F45E48ACEB9BUL, sim.StateHash());
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

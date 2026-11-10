// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The fall heading a collapse picks (death-fall-heading.md, ADR-0732).</summary>
public class DeathFallTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;
    private static readonly TilePos At = new(5, 5);

    // Level 5 seed 1, turn 1720: DK2 logs 29 draws at Creature.cpp line 6461 from this seed when the
    // Wizard collapses. The layout (only the south open) is fitted to the sixteenths the log rejects;
    // with it the first pick ends on 1041 after 18 draws and the second on 1036, its +0x102 that frame.
    [Fact]
    public void ThePicksReplayTheLoggedDrawsForALayoutFittedToTheLog()
    {
        (GameSimulation sim, CreatureState goblin, Dk2Lcg lcg) = Setup(1, open: [(0, 1)]);
        var expected = new Dk2Lcg(4010985998);
        int last = 0, first = 0;
        for (int i = 0; i < 29; i++)
        {
            last = expected.NextInt(0x800);
            if (i == 17) first = last;
        }
        Assert.Equal((1041, 1036), (first, last));

        uint after = 0;
        InTick(sim, context =>
        {
            lcg.Reseed(4010985998);
            DeathFallRules.Enter(context, goblin);
            after = lcg.Seed;
        });

        Assert.Equal(1041, goblin.Heading);
        Assert.Equal(expected.Seed, after);
    }

    [Fact]
    public void WithAllFourSidesBlockedTheHeadingStaysAndNothingIsDrawn()
    {
        (GameSimulation sim, CreatureState goblin, Dk2Lcg lcg) = Setup(77, open: []);
        goblin.Heading = 555;
        uint before = 0, after = 0;
        InTick(sim, context =>
        {
            before = lcg.Seed;
            DeathFallRules.Enter(context, goblin);
            after = lcg.Seed;
        });

        Assert.Equal(555, goblin.Heading);
        Assert.Equal(before, after);
    }

    // DK2 collapses inside the damage call; ours in the thing pass's next collapse step.
    [Fact]
    public void TheCollapseStepFellsAMarkedCreatureAndPicksItsHeading()
    {
        (GameSimulation sim, CreatureState goblin, _) = Setup(5, open: [(0, 1)]);
        goblin.Heading = 0;
        goblin.Health = 0;
        goblin.CollapseDue = true;
        sim.Tick();

        Assert.Equal(CreatureDeathRules.FallingState, goblin.Dk2StateId);
        Assert.False(goblin.CollapseDue);
        Assert.Equal(8, ((goblin.Heading + 0x20) & 0x7FF) >> 7);   // the south, the only open side
    }

    // A diagonal is open only when its own tile and both orthogonal tiles beside it are.
    [Fact]
    public void ADiagonalNeedsBothOfItsOrthogonalNeighboursOpen()
    {
        (GameSimulation sim, CreatureState goblin, _) = Setup(1, open: [(1, 0), (1, 1)]);
        var headings = new List<int>();
        for (int i = 0; i < 200; i++) InTick(sim, context => headings.Add(DeathFallRules.Pick(context, goblin)));
        Assert.Equal(200, headings.Count);
        Assert.All(headings, h => Assert.Equal(4, ((h + 0x20) & 0x7FF) >> 7));   // east only: the south-east needs the south
    }

    // Damage that empties the health collapses the creature in the same tick's thing pass, as DK2's
    // health routine does inside the damage call, and is carried by a snapshot until it has.
    [Fact]
    public void DamageMarksTheCollapseAndASnapshotCarriesTheMark()
    {
        (GameSimulation sim, CreatureState goblin, _) = Setup(9, open: [(0, 1)]);
        StatusEffectRules.Hurt(sim.World, sim.Database, sim.Database.CreatureById(GoblinId), goblin, 1_000_000, Keeper);
        Assert.True(goblin.CollapseDue);
        ulong marked = sim.StateHash();
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database);
        Assert.True(restored.World.CreatureById(goblin.Id)!.CollapseDue);
        Assert.Equal(marked, restored.StateHash());
        goblin.CollapseDue = false;
        Assert.NotEqual(marked, sim.StateHash());
    }

    private static void InTick(GameSimulation sim, Action<SimulationContext> body) =>
        new GameSimulation(sim.Database, sim.World, sim.Rng, [new Picker(body)]).Tick();

    private sealed class Picker(Action<SimulationContext> body) : ISimulationSystem
    {
        public string Name => "death fall picker";
        public void Execute(SimulationContext context) => body(context);
    }

    private static (GameSimulation, CreatureState, Dk2Lcg) Setup(uint seed, (int Dx, int Dy)[] open)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            world.Tile(At.X + dx, At.Y + dy).TerrainId = (dx, dy) == (0, 0) || open.Contains((dx, dy))
                ? Dk2TerrainFixture.ClaimedPath : Dk2TerrainFixture.Rock;
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, At);
        var lcg = new Dk2Lcg(seed);
        var sim = new GameSimulation(db, world, new SimRandom(lcg), [new CollapseSystem()]);
        return (sim, goblin, lcg);
    }
}

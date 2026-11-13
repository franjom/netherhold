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

    // Neither death entry nor the damage call stops the body (no 0x0048ECE0): it keeps its last step's
    // 16.16 point and velocity, which neighbours are pushed off (Q795).
    [Fact]
    public void TheCollapseLeavesTheBodyWhereItsLastStepPutIt()
    {
        (GameSimulation sim, CreatureState goblin, _) = Setup(5, open: [(0, 1)]);
        var body = new DK2.Simulation.Steering.Fixed16Vector((goblin.PosX << 4) + 4, (goblin.PosY << 4) + 13);
        var velocity = new DK2.Simulation.Steering.Fixed16Vector(-1596, -13075);
        goblin.Body.Pos = body;
        goblin.Body.Vel = velocity;
        goblin.PhysicalVelocityX = 500;
        goblin.PhysicalVelocityY = -700;
        goblin.Health = 0;
        goblin.CollapseDue = true;
        sim.Tick();

        Assert.Equal(CreatureDeathRules.FallingState, goblin.Dk2StateId);
        Assert.Equal((body, velocity), (goblin.Body.Pos, goblin.Body.Vel));
        // The entries clear the persistent and temporary vectors (0x00459635-0x00459658).
        Assert.Equal((0, 0, 0), (goblin.PhysicalVelocityX, goblin.PhysicalVelocityY, goblin.CreationVelocityZ));
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

    // 0x005119F0 as the hit calls it (0x004AC05B): Level 5, turns 1718 to 1720, three Fireballs heading
    // 5, 29 and 131 leave the Wizard heading 1019, 995 and 893.
    [Theory]
    [InlineData(5, 1019)]
    [InlineData(29, 995)]
    [InlineData(131, 893)]
    public void AHitHeadsTheCreatureByTheShotsAngleDistanceFromHalfATurn_ConfirmedInCapture(int shotHeading, int creatureHeading) =>
        Assert.Equal(creatureHeading, DK2.Simulation.Steering.Bearing.Distance(shotHeading, 0x400));

    // The same arithmetic past half a turn and on it; no capture has these.
    [Theory]
    [InlineData(2035, 1011)]
    [InlineData(1024, 0)]
    public void TheAngleDistanceFromHalfATurnNeverPassesHalfATurn(int shotHeading, int creatureHeading) =>
        Assert.Equal(creatureHeading, DK2.Simulation.Steering.Bearing.Distance(shotHeading, 0x400));

    // 0x00459850: the Wizard's vector 3 y, 3223 in the shipped record, gives its captured step of 1261
    // and 2966 at heading 893. The two refusals are read from the handler; the captured Thief stays
    // where it fell, by one of them.
    [Theory]
    [InlineData(Dk2TerrainFixture.DirtPath, true)]
    [InlineData(Dk2TerrainFixture.Rock, false)]
    [InlineData(Dk2TerrainFixture.Water, false)]
    public void TheFallStepsBackAlongTheHeadingWhenItsActionEnds_UnlessTheTileBehindIsSolidOrOfAnotherNavigationByte(
        byte behind, bool steps)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int ty = 1; ty < world.Height - 1; ty++)
        for (int tx = 1; tx < world.Width - 1; tx++)
            world.Tile(tx, ty).TerrainId = Dk2TerrainFixture.DirtPath;
        world.Tile(16, 9).TerrainId = behind;
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(16, 10));
        CreatureDef stepping = db.CreatureById(goblin.CreatureTypeId) with { DeathFallStepY = 3223 };
        // The Wizard's point within its tile, twenty-three rows up.
        goblin.PosX = 67803;
        goblin.PosY = 135760 - (23 << 12);
        goblin.Heading = 893;
        DK2.Simulation.Navigation.WorldSteering.SyncBody(goblin);
        int x = goblin.PosX, y = goblin.PosY;
        var sim = new GameSimulation(db, world, 1, [new Run(context =>
        {
            if (goblin.Dk2StateId != CreatureDeathRules.FallingState)
            {
                CreatureStateMachine.TrySetState(context.Hooks, goblin, CreatureDeathRules.FallingState, context.Tick.Value);
                goblin.ActionUntilTick = context.Tick.Value + 2;
            }
            DeathFallRules.Fall(context, goblin, stepping);
        })]);

        sim.Tick();
        sim.Tick();
        Assert.Equal((CreatureDeathRules.FallingState, x, y), (goblin.Dk2StateId, goblin.PosX, goblin.PosY));
        sim.Tick();
        Assert.Equal(CreatureDeathRules.DyingState, goblin.Dk2StateId);
        Assert.Equal(steps ? (x - 1261, y - 2966) : (x, y), (goblin.PosX, goblin.PosY));
        if (steps) Assert.Equal((goblin.PosX, goblin.PosY), (goblin.LastPosX, goblin.LastPosY));
    }

    // Level 5: the Wizard's entity steps back on turn 1726 and its body is where it fell until the
    // capture ends; a Warlock walking through that place on turn 1740 is pushed off it.
    [Fact]
    public void TheFallenCreaturesBodyStaysWhereItFell_ConfirmedInCapture()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int ty = 1; ty < world.Height - 1; ty++)
        for (int tx = 1; tx < world.Width - 1; tx++)
            world.Tile(tx, ty).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(16, 10));
        CreatureDef stepping = db.CreatureById(goblin.CreatureTypeId) with { DeathFallStepY = 3223 };
        goblin.Heading = 893;
        DK2.Simulation.Navigation.WorldSteering.SyncBody(goblin);
        var fell = goblin.Body.Pos;
        int x = goblin.PosX, y = goblin.PosY;
        var sim = new GameSimulation(db, world, 1, [new Run(context =>
        {
            if (goblin.Dk2StateId == CreatureDeathRules.DyingState) return;
            CreatureStateMachine.TrySetState(context.Hooks, goblin, CreatureDeathRules.FallingState, context.Tick.Value);
            DeathFallRules.Fall(context, goblin, stepping);
        }), new DK2.Simulation.Systems.CreatureMovementSystem()]);

        sim.Tick();
        sim.Tick();
        sim.Tick();

        Assert.Equal((CreatureDeathRules.DyingState, x - 1261, y - 2966), (goblin.Dk2StateId, goblin.PosX, goblin.PosY));
        Assert.Equal(fell, goblin.Body.Pos);
    }

    private sealed class Run(Action<SimulationContext> body) : ISimulationSystem
    {
        public string Name => "test step";
        public void Execute(SimulationContext context) => body(context);
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

/// <summary>The step back <c>0x00460DA0</c> and its two vectors (combat-fear.md §3 step 8,
/// combat-spells.md §10): along the creature's own heading, twenty-four ticks of shuffle when
/// backing off and sixteen when a spell cannot be cast, with the three-tile axis fall-backs.</summary>
public class StepBackTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void TheLengthsAreTwentyFourAndSixteenTicksOfShuffle()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        CreatureDef vampire = db.CreatureById(Dk2TerrainFixture.VampireCreatureId);
        // ShuffleSpeed 1638 over four ticks a second is 409 a tick (def+0x5BC).
        Assert.Equal(-((409 * 24) & ~15), FearRules.FleeStepBackLength(vampire));
        Assert.Equal(-(409 << 4), FearRules.CastStepBackLength(vampire));
    }

    [Theory]
    [InlineData(0, 0, 9808)]       // facing north (y falling): the step is south
    [InlineData(512, -9808, 0)]    // facing east: the step is west
    [InlineData(1024, 0, -9808)]   // facing south: the step is north
    public void TheVectorPointsBehindTheHeading(int heading, int x, int y)
    {
        var me = new CreatureState { Heading = heading };
        Assert.Equal((x, y), FearRules.StepBackVector(me, -9808));
    }

    /// <summary>A wall three tiles behind but not two and a half: ours used to step three tiles
    /// straight away from its target and fail; DK2's twenty-four ticks of shuffle stop short of it. The
    /// step is an impulse the physical pass (<c>0x004B2030</c>) spends in one turn at a sixteenth: the
    /// creature slides 9808 / 16 = 613 back and keeps its heading.</summary>
    [Fact]
    public void AStepShortOfTheWallBehindIsTaken_AndSlidesASixteenthOfIt()
    {
        (GameDatabase db, WorldState world) = Open(wallRow: 8);
        CreatureState vampire = Facing(db, world, heading: 0);
        int y = vampire.PosY;
        Assert.True(Run(db, world, vampire));
        Assert.Equal(y + 613, vampire.PosY);
        Assert.Equal(0, vampire.Heading);
    }

    /// <summary>Stepping back turn after turn keeps going the same way: the heading never turns
    /// toward the step, as a walked route would turn it (ours at hash 339 stood still).</summary>
    [Fact]
    public void RepeatedStepsKeepSlidingBack()
    {
        (GameDatabase db, WorldState world) = Open(wallRow: 10);
        CreatureState vampire = Facing(db, world, heading: 0);
        int y = vampire.PosY;
        for (int i = 0; i < 4; i++) Assert.True(Run(db, world, vampire));
        Assert.Equal(y + 4 * 613, vampire.PosY);
    }

    /// <summary>A wall inside the step: no route reaches its point and <c>(x, y+vy)</c> is in the wall,
    /// but <c>(x+vx, y)</c> is my own tile, so DK2 steps along x, west for a zero component
    /// (<c>0x00460EA9</c>-<c>0x00460F47</c>): 0x3000 / 16 a turn.</summary>
    [Fact]
    public void AWallInsideTheStepSendsItSideways()
    {
        (GameDatabase db, WorldState world) = Open(wallRow: 7);
        CreatureState vampire = Facing(db, world, heading: 0);
        int x = vampire.PosX;
        Assert.True(Run(db, world, vampire));
        Assert.Equal(x - 0x3000 / 16, vampire.PosX);
    }

    /// <summary>The slide stops short of a solid tile (ours: DK2's pass reflects and halves, Q373).</summary>
    [Fact]
    public void TheSlideStopsAtASolidTile()
    {
        (GameDatabase db, WorldState world) = Open(wallRow: 6);
        CreatureState vampire = Facing(db, world, heading: 0);
        vampire.PosY = CreatureState.CentreOf(5) + 1800;   // 248 short of the wall row
        int y = vampire.PosY;
        var sim = new GameSimulation(db, world, 1, [new Probe(context => FearRules.Slide(context, vampire, 0, 9808))]);
        sim.Tick();
        Assert.Equal(y, vampire.PosY);
    }

    /// <summary>A routed point is not enough: with both <c>(x, y+vy)</c> and <c>(x+vx, y)</c> solid the
    /// step is refused, with no axis fall-back (<c>0x00460E99</c> to <c>0x00460F4F</c>, the result word
    /// still 0). The fall-backs would test the same two solid tiles, so this pins the result, not the arm.</summary>
    [Fact]
    public void ARoutedPointBetweenTwoSolidCornersIsRefused()
    {
        (GameDatabase db, WorldState world) = Open(wallRow: -1);
        CreatureState vampire = Facing(db, world, heading: 256);   // facing north-east: the step is south-west
        (int vx, int vy) = FearRules.StepBackVector(vampire, FearRules.FleeStepBackLength(db.CreatureById(vampire.CreatureTypeId)));
        Assert.True(vx < 0 && vy > 0);
        world.Tile(vampire.Tile.X, (vampire.PosY + vy) / Fp32.One).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile((vampire.PosX + vx) / Fp32.One, vampire.Tile.Y).TerrainId = Dk2TerrainFixture.Rock;
        Assert.False(Run(db, world, vampire));
    }

    private static (GameDatabase, WorldState) Open(int wallRow)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 12), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = y == wallRow ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static CreatureState Facing(GameDatabase db, WorldState world, int heading)
    {
        CreatureState vampire = world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId, Keeper, new TilePos(10, 5));
        vampire.Heading = heading;
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 4));
        return vampire;
    }

    private static bool Run(GameDatabase db, WorldState world, CreatureState me, bool step = true)
    {
        bool taken = false;
        var sim = new GameSimulation(db, world, 1, [new Probe(context =>
        {
            if (step)
                taken = FearRules.StepBack(context, me, db.CreatureById(me.CreatureTypeId),
                    FearRules.FleeStepBackLength(db.CreatureById(me.CreatureTypeId)), []);
        }), new Systems.CreatureMovementSystem()]);
        sim.Tick();
        return taken;
    }

    private sealed class Probe(Action<SimulationContext> run) : ISimulationSystem
    {
        public string Name => "probe";
        public void Execute(SimulationContext context) => run(context);
    }
}

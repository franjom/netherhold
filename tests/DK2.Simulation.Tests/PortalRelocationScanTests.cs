// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public class PortalRelocationScanTests
{
    private static (WorldState World, CreatureState Creature, CreatureDef Definition) Setup(int offsetX, int offsetY)
    {
        CreatureDef definition = Dk2TerrainFixture.Database().CreatureById(7) with
        {
            CreatureId = 91, EntranceOffsetX = offsetX, EntranceOffsetY = offsetY,
            EntranceFrames = 0, EntranceFramesPerSecond = 0,
        };
        GameDatabase database = Dk2TerrainFixture.Database(definition);
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [0, 1, 2, 3, 4]);
        CreatureState creature = world.SpawnCreature(database, 91, 3, new TilePos(6, 6));
        creature.Dk2StateId = PortalRules.JoiningState;
        creature.StateEnteredTick = 0;
        creature.FightProcessCounter = 7;
        creature.LastFightScanTileIndex = 6 * world.Width + 6;
        new GameSimulation(database, world, seed: 0, systems: []).Tick();
        return (world, creature, definition);
    }

    // 00459D13-00459D27 copies the displaced XYZ to the +F6 reference that
    // 0045E9F0 reads through 004B5D40; the next stationary scan keeps its cadence.
    [Theory]
    [InlineData(2929, 0)]
    [InlineData(0, 2929)]
    public void CompletedPortalStepDoesNotForceTheNextStationaryScan(int offsetX, int offsetY)
    {
        var (world, creature, definition) = Setup(offsetX, offsetY);
        Assert.False(PortalRules.StillJoining(world, creature, definition));
        bool moved = creature.LastFightScanTileIndex != creature.Tile.Y * world.Width + creature.Tile.X;
        Assert.False(CombatRules.ScanIsDue(creature, moved));
        Assert.Equal((byte)8, creature.FightProcessCounter);
    }

    [Fact]
    public void WaitingForEntranceCompletionDoesNotRelocateOrChangeTheReference()
    {
        var (world, creature, definition) = Setup(0, 2929);
        definition = definition with { EntranceFrames = 147, EntranceFramesPerSecond = 30 };
        Assert.True(PortalRules.StillJoining(world, creature, definition));
        Assert.Equal(new TilePos(6, 6), creature.Tile);
        Assert.Equal(6 * world.Width + 6, creature.LastFightScanTileIndex);
        Assert.Equal((byte)7, creature.FightProcessCounter);
    }

    [Fact]
    public void RelocationWithinTheSameTileDoesNotForceAScan()
    {
        var (world, creature, definition) = Setup(0, 1);
        Assert.False(PortalRules.StillJoining(world, creature, definition));
        bool moved = creature.LastFightScanTileIndex != creature.Tile.Y * world.Width + creature.Tile.X;
        Assert.False(CombatRules.ScanIsDue(creature, moved));
        Assert.Equal((byte)8, creature.FightProcessCounter);
    }

    [Fact]
    public void SubsequentOrdinaryTileMovementStillForcesAScan()
    {
        var (world, creature, definition) = Setup(0, 2929);
        Assert.False(PortalRules.StillJoining(world, creature, definition));
        creature.PosY += Fp32.One;
        bool moved = creature.LastFightScanTileIndex != creature.Tile.Y * world.Width + creature.Tile.X;
        Assert.True(CombatRules.ScanIsDue(creature, moved));
        Assert.Equal((byte)0, creature.FightProcessCounter);
    }
}

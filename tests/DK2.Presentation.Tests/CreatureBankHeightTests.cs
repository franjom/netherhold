// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class CreatureBankHeightTests
{
    // A created worker rises from just above its floor over the creation turn in its ordinary pose (HYPOTHESIS:
    // construction leaves the previous height there), then plays its slot-32 jump for the one update the chooser
    // sees it in 214, the one whose physical pass drops it (0x0048BE50's 214 arm, imp-create-position.md).
    [InstallFact]
    public void ACreatedWorkerRisesThenJumpsForTheOneUpdateThatDropsIt()
    {
        var (database, world, creature) = WaterPatch();
        world.Tile(1, 1).TerrainId = database.Room(RoomTypeId.Lair).TerrainId;
        new GameSimulation(database, world, 1, Array.Empty<ISimulationSystem>()).Tick();   // tick 1: creation at a real tick
        creature.Dk2StateId = ImpCreationRules.FallingState;
        creature.StateEnteredTick = world.CurrentTick.Value;
        creature.PosZ = creature.LastPosZ = ImpCreationRules.BirthHeightAboveFloor;
        var view = new WorldView(database);
        AssetRef jump = AssetRef.ForCreature("Dwarf", AssetKind.Animation, "creation_jump");
        CreatureView born = view.Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal((6144, 4096 + 0x20), (born.Height, born.PreviousHeight));
        Assert.NotEqual(jump, born.Animation);

        // The next update: the physical pass has dropped it, still in 214.
        new GameSimulation(database, world, 1, Array.Empty<ISimulationSystem>()).Tick();
        creature.LastPosZ = ImpCreationRules.BirthHeightAboveFloor;
        creature.PosZ = 0;
        CreatureView falling = view.Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal((4096, 6144), (falling.Height, falling.PreviousHeight));
        Assert.Equal(jump, falling.Animation);
        Assert.False(falling.AnimationLoops);
        Assert.Equal(world.CurrentTick, falling.AnimationStartedAt);

        // Landed: state 11 takes the standing pose at once.
        creature.Dk2StateId = 11;
        Assert.NotEqual(jump, view.Creatures(world).Single(c => c.Id == creature.Id).Animation);
        // The Imp's slot 32 is imp-jump(iw): a type-6 clip of 20 frames at 20 fps.
        CreatureAnimationTiming clip = database.CreatureByName("Imp")!.ArtSlot(32);
        Assert.Equal((6, 20, 20), ((int)clip.ResourceType, (int)clip.FrameCount, (int)clip.FramesPerSecond));
    }

    [InstallFact]
    public void ACreationFallPreservesBothHeightsAboveTheGroundForInterpolation()
    {
        var (database, world, creature) = WaterPatch("Troll");
        world.Tile(1, 1).TerrainId = database.Room(RoomTypeId.Lair).TerrainId;
        creature.Dk2StateId = ImpCreationRules.FallingState;
        creature.PosZ = creature.LastPosZ = ImpCreationRules.BirthHeightAboveFloor;
        var view = new WorldView(database);
        CreatureView born = view.Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal(6144, born.Height);
        Assert.Equal(6144, born.PreviousHeight);
        creature.PosZ = 0;
        CreatureView fallen = view.Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal(4096, fallen.Height);
        Assert.Equal(6144, fallen.PreviousHeight);
    }

    [InstallTheory]
    [InlineData(2, 1, 0xC00, 0x800, 0xD33)]
    [InlineData(0, 1, 0x400, 0x800, 0xD33)]
    [InlineData(1, 0, 0x800, 0x400, 0xD33)]
    [InlineData(1, 2, 0x800, 0xC00, 0xD33)]
    [InlineData(2, 0, 0xC00, 0x600, 0xBCC)]
    [InlineData(2, 1, 0xC00, 0x600, 0xD33)]
    [InlineData(1, 0, 0x800, 0xC00, 0xA66)]
    public void GroundedCreatureRisesOnlyTowardItsBank(int landX, int landY,
        int offsetX, int offsetY, int expectedHeight)
    {
        var (database, world, creature) = WaterPatch();
        world.Tile(landX, landY).TerrainId = database.Terrain.Values.First(t => !t.IsSolid && !t.IsWater && !t.IsLava
            && !database.RoomsByTerrain.ContainsKey(t.TerrainId)).TerrainId;
        creature.PosX = Fp32.One + offsetX;
        creature.PosY = Fp32.One + offsetY;
        CreatureView rendered = new WorldView(database).Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal(expectedHeight, rendered.Height);
    }

    [InstallFact]
    public void ShoreCrossingRetainsBothGroundHeightsForInterpolation()
    {
        var (database, world, creature) = WaterPatch();
        world.Tile(2, 1).TerrainId = database.Room(RoomTypeId.Lair).TerrainId;
        creature.LastPosX = Fp32.One + 0xC00;
        creature.LastPosY = Fp32.One + 0x800;
        creature.PosX = 2 * Fp32.One + 0x400;
        creature.PosY = creature.LastPosY;
        CreatureView rendered = new WorldView(database).Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal(0xD33, rendered.PreviousHeight);
        Assert.Equal(Fp32.One, rendered.Height);
    }

    [InstallFact]
    public void ABridgeOverTheSameLiquidDoesNotBecomeAShoreBank()
    {
        var (database, world, creature) = WaterPatch();
        world.Tile(2, 1).TerrainId = database.Room(RoomTypeId.StoneBridge).TerrainId;
        world.Tile(2, 1).Flags = BridgeRules.SpansWater;
        creature.PosX = Fp32.One + 0xC00;
        Assert.Equal(0xA66, new WorldView(database).Creatures(world).Single(c => c.Id == creature.Id).Height);
    }

    private static (GameDatabase, WorldState, CreatureState) WaterPatch(string creatureName = "Dwarf")
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        WorldState world = WorldState.FromMap(database,
            GameDatabaseLoader.LoadCampaignMap(install, "Level8")!, [2, 3]);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                world.Tile(x, y).TerrainId = database.WaterTerrain!.TerrainId;
        CreatureState creature = world.SpawnCreature(database,
            database.CreatureByName(creatureName)!.CreatureId, 2, new TilePos(1, 1));
        return (database, world, creature);
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>What a drawn creature carries for its footsteps: the definition's marks of the requested walk (art slot 1) or run
/// (slot 2) clip, its sound category, and the grounded-water flag (docs/research/sound-effects-continuous.md section 1).</summary>
public sealed class CreatureFootstepViewTests
{
    private static (GameDatabase Database, WorldState World, CreatureState Creature, WorldView View) Walker(
        string name, bool water = false)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        WorldState world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(install, "Level8")!, [2, 3]);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                world.Tile(x, y).TerrainId = water ? database.WaterTerrain!.TerrainId
                    : database.Terrain.Values.First(t => !t.IsSolid && !t.IsWater && !t.IsLava
                        && !database.RoomsByTerrain.ContainsKey(t.TerrainId)).TerrainId;
        CreatureState creature = world.SpawnCreature(database, database.CreatureByName(name)!.CreatureId, 2, new TilePos(1, 1));
        CreatureStateMachine.BeginTravel(new StateHookContext(world, database), creature, 6, ImpStates.For(TileWorkKind.Claim).Entry);
        creature.Body.PathTotalCount = 1;
        creature.Body.MaxSpeed = 768 << 4;
        creature.Body.Vel = new DK2.Simulation.Steering.Fixed16Vector(128 << 4, 0);
        return (database, world, creature, new WorldView(database));
    }

    [InstallFact]
    public void AWalkingCreatureCarriesItsDefinitionsSlotOneMarksAndItsCategory()
    {
        var (database, world, creature, view) = Walker("Imp");
        CreatureView walking = view.Creatures(world).Single(c => c.Id == creature.Id);
        CreatureDef imp = database.CreatureByName("Imp")!;
        Assert.Equal(new FootstepMarkPair(2, 8), imp.FootstepMarksOf(1));
        Assert.Equal(imp.FootstepMarksOf(1), walking.FootstepMarks);
        Assert.Equal(imp.SoundCategory, walking.SoundCategory);
        Assert.False(walking.Wading);
    }

    [InstallFact]
    public void ARunningCreatureCarriesItsSlotTwoMarks()
    {
        var (database, world, creature, view) = Walker("Knight");
        creature.Running = true;
        CreatureView running = view.Creatures(world).Single(c => c.Id == creature.Id);
        CreatureDef knight = database.CreatureByName("Knight")!;
        Assert.Equal(new FootstepMarkPair(11, 24), knight.FootstepMarksOf(2));
        Assert.Equal(knight.FootstepMarksOf(2), running.FootstepMarks);
    }

    [InstallFact]
    public void AStandingCreatureCarriesNoMarks()
    {
        var (_, world, creature, view) = Walker("Imp");
        creature.Body.Vel = default;
        CreatureView standing = view.Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal(default, standing.FootstepMarks);
    }

    [InstallFact]
    public void AGroundedCreatureOnWaterWadesAndAFlyingOneDoesNot()
    {
        var (database, world, dwarf, view) = Walker("Dwarf", water: true);
        Assert.True(view.Creatures(world).Single(c => c.Id == dwarf.Id).Wading);
        CreatureDef flyer = database.Creatures.Values.First(c => c.Flags.HasFlag(CreatureFlags.CanFly)
            && !c.Flags.HasFlag(CreatureFlags.IsWorker));
        CreatureState fly = world.SpawnCreature(database, flyer.CreatureId, 2, new TilePos(1, 1));
        Assert.False(view.Creatures(world).Single(c => c.Id == fly.Id).Wading);
    }
}

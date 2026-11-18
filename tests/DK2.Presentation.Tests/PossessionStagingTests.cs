// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The <c>possess-first-creature</c> check's staging (<see cref="PossessionStaging"/>).</summary>
public sealed class PossessionStagingTests
{
    private static readonly PlayerId Viewer = new(PlayerIds.FirstKeeper);

    private static (GameDatabase Database, WorldState World) Level1()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        return (db, WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!, [1, 3, 4]));
    }

    [InstallFact]
    public void TheViewersFirstCreatureIsPossessedAsTheStrikeLeavesIt()
    {
        (GameDatabase db, WorldState world) = Level1();
        byte goblin = db.Creatures.Values.Single(c => c.Name == "Goblin").CreatureId;
        CreatureState first = world.SpawnCreature(db, goblin, Viewer, new TilePos(3, 3));
        CreatureState second = world.SpawnCreature(db, goblin, Viewer, new TilePos(4, 3));
        CreatureState taken = world.OwnedCreatures(Viewer).First();
        taken.FollowerCount = 2;
        taken.NextPossessionInfluenceTick = new GameTick(9);

        CreatureState? staged = PossessionStaging.PossessFirst(world, Viewer);

        Assert.Same(taken, staged);
        Assert.True(staged!.Possessed);
        Assert.Equal(0, staged.FollowerCount);
        Assert.Equal(new GameTick(0), staged.NextPossessionInfluenceTick);
        Assert.Equal(staged.Id, world.Player(Viewer).PossessedCreatureId);
        Assert.True(PossessionScene.Latched(world, Viewer));
        Assert.Single(new[] { first, second }, c => c.Possessed);

        // A second call takes no other creature.
        Assert.Null(PossessionStaging.PossessFirst(world, Viewer));
        Assert.Equal(staged.Id, world.Player(Viewer).PossessedCreatureId);
        Assert.Single(new[] { first, second }, c => c.Possessed);
    }

    [InstallFact]
    public void AViewerWithNoCreaturePossessesNothing()
    {
        (_, WorldState world) = Level1();
        foreach (CreatureState creature in world.OwnedCreatures(Viewer).ToArray()) world.RemoveCreature(creature.Id);

        Assert.Null(PossessionStaging.PossessFirst(world, Viewer));
        Assert.Equal(0, world.Player(Viewer).PossessedCreatureId);
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The <c>possess-first-creature</c> check's staging (<see cref="PossessionStaging"/>).</summary>
public sealed class PossessionStagingTests
{
    [InstallFact]
    public void OrdinaryCameraFocusMovesOverTheTargetBeforePossessionIsLatched()
    {
        (GameDatabase db, WorldState world) = Level1();
        byte goblin = db.Creatures.Values.Single(c => c.Name == "Goblin").CreatureId;
        CreatureState target = world.SpawnCreature(db, goblin, Viewer, new TilePos(4, 3));
        target.PosX += Fp32.One / 4;
        target.PosY -= Fp32.One / 8;
        var focus = new Vector3(40, 5, -30);
        bool prepared = false;

        CreatureState? staged = PossessionStaging.PossessFirst(world, Viewer, focus, (selected, aligned) =>
        {
            Assert.Same(target, selected);
            Assert.False(selected.Possessed);
            Assert.Equal(0, world.Player(Viewer).PossessedCreatureId);
            focus = aligned;
            prepared = true;
        });

        Assert.True(prepared);
        Assert.Same(target, staged);
        Assert.Equal(new Vector3(4.75f, 5, -3.375f), focus);
        Assert.True(target.Possessed);
        Assert.Equal(target.Id, world.Player(Viewer).PossessedCreatureId);
    }

    [InstallFact]
    public void AnAlreadyPossessedViewerDoesNotRepositionTheOrdinaryCamera()
    {
        (GameDatabase db, WorldState world) = Level1();
        byte goblin = db.Creatures.Values.Single(c => c.Name == "Goblin").CreatureId;
        world.SpawnCreature(db, goblin, Viewer, new TilePos(4, 3));
        Assert.NotNull(PossessionStaging.PossessFirst(world, Viewer));
        bool prepared = false;

        Assert.Null(PossessionStaging.PossessFirst(world, Viewer, new Vector3(40, 5, -30), (_, _) => prepared = true));

        Assert.False(prepared);
    }

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

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI.Tests;

public class KeeperStagingTests
{
    private static readonly PlayerId Keeper = new(KeeperWorld.Keeper);
    private static readonly PlayerId Other = new(4);

    [Fact]
    public void AViewThroughStagingEqualsOneBuiltAlone()
    {
        (GameDatabase db, WorldState world) = Battle();
        var staging = new KeeperStaging();
        AssertSameView(db, world, Keeper, staging);
    }

    [Fact]
    public void TheSecondKeeperThroughOneStagingEqualsItsViewBuiltAlone()
    {
        (GameDatabase db, WorldState world) = Battle();
        var staging = new KeeperStaging();
        AssertSameView(db, world, Keeper, staging);
        AssertSameView(db, world, Other, staging);
        Assert.Equal(1, staging.Builds);
    }

    [Fact]
    public void AfterARoutingByteChangesTheStagingRebuildsWholeAndStillEqualsAFreshView()
    {
        (GameDatabase db, WorldState world) = Battle();
        var staging = new KeeperStaging();
        AssertSameView(db, world, Keeper, staging);
        KeeperWorld.Set(world, db, new TilePos(13, 14), KeeperWorld.Rock);

        AssertSameView(db, world, Keeper, staging);

        Assert.Equal(2, staging.Builds);
    }

    [Fact]
    public void WhileTheBytesAreUnchangedOneBuildServesEveryView()
    {
        (GameDatabase db, WorldState world) = Battle();
        var staging = new KeeperStaging();
        // More than 255 searches exercises the reused router's generation wrap.
        for (int i = 0; i < 140; i++) AssertSameView(db, world, Keeper, staging);
        Assert.Equal(1, staging.Builds);
    }

    private static void AssertSameView(GameDatabase db, WorldState world, PlayerId keeper, KeeperStaging staging)
    {
        var alone = new KeeperView(db, world, keeper);
        var shared = new KeeperView(db, world, keeper, staging: staging);
        Assert.Contains(alone.Creatures, c => c.HeartRouteExists);
        Assert.Contains(shared.Creatures, c => c.HeartRouteExists);
        Assert.Equal(KeeperViewSnapshot.ToBytes(alone), KeeperViewSnapshot.ToBytes(shared));
    }

    private static (GameDatabase, WorldState) Battle()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db, morePlayers: [Other.Value]);
        for (int y = 12; y <= 18; y++)
            for (int x = 8; x <= 12; x++)
                KeeperWorld.Set(world, db, new TilePos(x, y), KeeperWorld.ClaimedPath, Other.Value);
        KeeperWorld.Set(world, db, new TilePos(9, 15), KeeperWorld.HeartFloor, Other.Value);
        CreatureState a = world.SpawnCreature(db, KeeperWorld.FighterCreature, Keeper, new TilePos(13, 15));
        CreatureState b = world.SpawnCreature(db, KeeperWorld.FighterCreature, Other, new TilePos(11, 15));
        var hooks = new StateHookContext(world, db);
        Assert.True(CreatureStateMachine.TrySetState(hooks, a, 199));
        Assert.True(CreatureStateMachine.TrySetState(hooks, b, 199));
        return (db, world);
    }
}

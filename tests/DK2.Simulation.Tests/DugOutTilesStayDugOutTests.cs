// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>The dungeon only ever opens up, and two terrains never open at all.
/// CONFIRMED (owner, 2026-09-04, volunteered): DK2 has no mechanic for filling a
/// dug-out tile back to rock, and neither solid rock nor gems can be dug through
/// — gems are an infinite gold source, solid rock the map boundary.</summary>
/// <remarks>Gems destroy to Gems and Impenetrable Rock is not taggable, so both
/// rules fall out of the terrain record; the fill-in reading that sits against
/// them (<c>FillInHealth</c> = 5, <see cref="TerrainFlags.FillInable"/> on five
/// floor terrains, Claimed Path's <see cref="TerrainDef.MaxHealthTypeTerrainId"/>
/// = Rock) is Q18, Q22 and Q219, and <c>TileWorkKind.FillIn</c> was removed as
/// unreachable — docs/research/tile-work.md. Scope: these guard workers, not the
/// engine; two <c>Secret3</c> tiles do close a tile by
/// <c>ALTER_TERRAIN_TYPE</c> (Q74).</remarks>
public class DugOutTilesStayDugOutTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    /// <summary>
    /// Over a long dig with several imps, no tile ever goes from walkable back
    /// to solid.
    ///
    /// The strongest available statement of "there is no fill-in": not that the
    /// job is unlisted, but that the outcome never happens.
    /// </summary>
    [Fact]
    public void NoTileEverGoesFromOpenBackToSolid()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        for (int i = 0; i < 4; i++)
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        var tiles = new List<TilePos>();
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                TerrainDef terrain = database.TerrainById(world.Tile(x, y).TerrainId);
                if (terrain.IsSolid && terrain.IsTaggable && !terrain.IsImpenetrable)
                    tiles.Add(new TilePos(x, y));
            }

        var sim = new GameSimulation(database, world, 2468, Dk2TerrainFixture.DefaultSystems());
        sim.Tick(InputFrame.Of(new TagTilesCommand(Keeper, tiles, true)));

        bool[] wasOpen = new bool[world.TileCount];
        for (int tick = 0; tick < 4000; tick++)
        {
            sim.Tick(InputFrame.Empty);

            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    int index = (y * world.Width) + x;
                    bool solid = database.TerrainById(world.Tile(x, y).TerrainId).IsSolid;

                    Assert.False(
                        wasOpen[index] && solid,
                        $"({x},{y}) was dug out and closed again by tick {tick} — " +
                        "DK2 has no fill-in (owner, 2026-09-04)");

                    wasOpen[index] |= !solid;
                }
        }
    }

    /// <summary>
    /// A gem seam is mined forever and never runs out.
    ///
    /// Applied straight at the rules rather than through an imp, because the
    /// point is the terrain transition and not who performs it: a thousand
    /// applications, all of them yielding gold, and the tile is still gems.
    /// </summary>
    [Fact]
    public void AGemSeamIsNeverExhausted()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        var at = new TilePos(6, 5);
        Assert.Equal(Dk2TerrainFixture.Gems, world.Tile(at).TerrainId);

        long mined = 0;
        for (int i = 0; i < 1000; i++)
        {
            TileWorkResult step = TileWorkRules.Apply(
                database, ref world.Tile(at), TileWorkKind.MineGold, Keeper);
            mined += step.GoldReleased;
            Assert.Equal(Dk2TerrainFixture.Gems, world.Tile(at).TerrainId);
        }

        Assert.True(mined > 0, "a gem seam yielded no gold at all");
    }

    /// <summary>
    /// The two indestructible categories, stated as the owner stated them, and
    /// distinguished: impenetrable rock cannot even be marked, while gems can be
    /// marked and worked but never removed.
    /// </summary>
    [Fact]
    public void ImpenetrableRockCannotBeMarkedAndGemsCanNeverBeDugThrough()
    {
        GameDatabase database = Dk2TerrainFixture.Database();

        TerrainDef rock = database.TerrainById(Dk2TerrainFixture.ImpenetrableRock);
        Assert.True(rock.IsSolid);
        Assert.True(rock.IsImpenetrable);
        Assert.False(rock.IsTaggable);

        TerrainDef gems = database.TerrainById(Dk2TerrainFixture.Gems);
        Assert.True(gems.IsSolid);
        Assert.True(gems.IsImpenetrable);
        Assert.True(gems.IsTaggable);
        Assert.True(gems.GoldValue > 0);

        // The whole of "never dug through", in one field: what a gem tile becomes
        // when it is destroyed is a gem tile.
        Assert.Equal(Dk2TerrainFixture.Gems, gems.DestroyedTypeTerrainId);

        // And the plain seam is the contrast that makes the point non-vacuous.
        TerrainDef gold = database.TerrainById(Dk2TerrainFixture.Gold);
        Assert.False(gold.IsImpenetrable);
        Assert.NotEqual(Dk2TerrainFixture.Gold, gold.DestroyedTypeTerrainId);
    }

    /// <summary>
    /// Watches the work queue between the workers filling it and the work system
    /// draining it — the only moment in a tick when it is non-empty.
    /// </summary>
    private sealed class WorkSpy : ISimulationSystem
    {
        public readonly List<TileWorkKind> Seen = [];

        public string Name => "WorkSpy";

        public void Execute(SimulationContext context)
        {
            foreach (TileWorkOrder order in context.World.PendingWork)
                Seen.Add(order.Kind);
        }
    }

    /// <summary>
    /// No worker ever selects filling in as a job.
    ///
    /// Checked through behaviour rather than by reading the priority list, so
    /// that a future job priority could not satisfy it by being spelled
    /// differently: over a long dig, every order that reaches the queue is
    /// inspected and none of them is a fill-in.
    /// </summary>
    [Fact]
    public void NoWorkerEverQueuesAFillIn()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        for (int i = 0; i < 4; i++)
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        var tiles = new List<TilePos>();
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                TerrainDef terrain = database.TerrainById(world.Tile(x, y).TerrainId);
                if (terrain.IsSolid && terrain.IsTaggable && !terrain.IsImpenetrable)
                    tiles.Add(new TilePos(x, y));
            }

        // The declared order with the spy spliced in ahead of the work system,
        // which drains the queue within the same tick.
        var spy = new WorkSpy();
        var systems = new List<ISimulationSystem>();
        foreach (ISimulationSystem system in Dk2TerrainFixture.DefaultSystems())
        {
            if (system is Systems.TileWorkSystem) systems.Add(spy);
            systems.Add(system);
        }

        Assert.Contains(spy, systems);

        var sim = new GameSimulation(database, world, 1357, [.. systems]);
        sim.Tick(InputFrame.Of(new TagTilesCommand(Keeper, tiles, true)));

        for (int tick = 0; tick < 4000; tick++) sim.Tick(InputFrame.Empty);

        Assert.True(spy.Seen.Count > 0, "no work was queued at all, so this proved nothing");

        // Every kind an imp chose, and NOTHING ELSE. Stronger than the old
        // "does not contain FillIn": a new work kind that closed a tile would
        // fail here even if nobody thought to name it.
        Assert.All(spy.Seen, kind => Assert.Contains(kind, new[]
        {
            TileWorkKind.Dig, TileWorkKind.MineGold, TileWorkKind.DigOwnWall,
            TileWorkKind.DigEnemyWall, TileWorkKind.Claim, TileWorkKind.ConvertRoom,
            TileWorkKind.Reinforce,
        }));

        // Non-vacuous the other way too: the kinds an imp does choose did show up.
        Assert.Contains(TileWorkKind.Dig, spy.Seen);
        Assert.Contains(TileWorkKind.Claim, spy.Seen);
    }
}

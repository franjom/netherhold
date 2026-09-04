// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// Creatures cannot pass through walls.
///
/// <para>CONFIRMED (owner, 2026-09-04): <i>"creatures CAN NOT pass through
/// walls, they can only move through dug out tiles, not through solid rocks"</i>.
/// Volunteered rather than asked for, after watching an OpenKeeper gameplay
/// video in which imps walked through rock.</para>
///
/// <para>The rule was already implemented — <see cref="WorldPathGrid"/> refuses
/// every solid terrain, and has since the first worker — but it was implemented
/// in one place and asserted in none. The pathfinding tests prove the
/// <i>search</i> will not route through a wall on a hand-drawn grid; nothing
/// proved that the simulation, running the real systems on real terrain over
/// many ticks, never puts a creature inside one. A rule that only one line of
/// code knows is a rule that can be deleted by accident, which is what these
/// tests exist to prevent.</para>
///
/// <para>They are also the reason <see cref="Systems.CreatureMovementSystem"/>
/// now re-checks each waypoint: a route is planned once, and a wooden bridge
/// over lava burns out from under it thirty seconds after it is built (Q67).</para>
/// </summary>
public class WallsBlockMovementTests
{
    private const byte Keeper = 1;

    /// <summary>Every tile of the fixture map a keeper could ever mark for work.</summary>
    private static TilePos[] EverythingDiggable(WorldState world, GameDatabase database)
    {
        var tiles = new List<TilePos>();
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                TerrainDef terrain = database.TerrainById(world.Tile(x, y).TerrainId);
                if (terrain.IsSolid && terrain.IsTaggable && !terrain.IsImpenetrable)
                    tiles.Add(new TilePos(x, y));
            }

        return [.. tiles];
    }

    private static bool IsSolidAt(GameDatabase database, WorldState world, TilePos p) =>
        world.Contains(p) && database.TerrainById(world.Tile(p).TerrainId).IsSolid;

    /// <summary>
    /// The headline invariant, checked after every tick of a dungeon being dug
    /// out from the inside: no creature is ever standing in rock.
    ///
    /// Four imps rather than one, and the whole map marked, so the run contains
    /// crowding, re-planning, gold carrying and abandoned jobs — the situations
    /// where a route outlives the world it was planned against.
    /// </summary>
    [Fact]
    public void NoCreatureIsEverStandingOnASolidTile()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, Keeper, (byte)2]);

        for (int i = 0; i < 4; i++)
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        var sim = new GameSimulation(database, world, 7331, Dk2TerrainFixture.DefaultSystems());
        sim.Tick(InputFrame.Of(new TagTilesCommand(Keeper, EverythingDiggable(world, database), true)));

        for (int tick = 0; tick < 4000; tick++)
        {
            sim.Tick(InputFrame.Empty);

            foreach (CreatureState creature in world.Creatures)
            {
                TilePos at = creature.Tile;
                Assert.True(
                    world.Contains(at),
                    $"creature {creature.Id} left the map at ({at.X},{at.Y}) on tick {tick}");
                Assert.False(
                    IsSolidAt(database, world, at),
                    $"creature {creature.Id} stood inside " +
                    $"{database.TerrainById(world.Tile(at).TerrainId).Name} " +
                    $"at ({at.X},{at.Y}) on tick {tick}");
            }
        }
    }

    /// <summary>
    /// The same run, checked one step earlier: a creature is never even
    /// <i>intending</i> to cross a wall.
    ///
    /// Separate from the test above because the two fail differently. A route
    /// through rock that is never walked is still a bug — it means the search
    /// and the world disagree — and it would be invisible to a test that only
    /// looks at where creatures ended up.
    /// </summary>
    [Fact]
    public void NoRouteACreatureIsWalkingCrossesASolidTile()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, Keeper, (byte)2]);

        for (int i = 0; i < 4; i++)
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        var sim = new GameSimulation(database, world, 7331, Dk2TerrainFixture.DefaultSystems());
        sim.Tick(InputFrame.Of(new TagTilesCommand(Keeper, EverythingDiggable(world, database), true)));

        for (int tick = 0; tick < 4000; tick++)
        {
            sim.Tick(InputFrame.Empty);

            foreach (CreatureState creature in world.Creatures)
                for (int i = creature.PathIndex; i < creature.Path.Count; i++)
                    Assert.False(
                        IsSolidAt(database, world, creature.Path[i]),
                        $"creature {creature.Id} was routed through " +
                        $"({creature.Path[i].X},{creature.Path[i].Y}) on tick {tick}");
        }
    }

    /// <summary>
    /// Ground that stops being walkable under a creature stops it, rather than
    /// being walked over.
    ///
    /// The invariants above cannot produce this on their own — digging only ever
    /// makes the world <i>more</i> walkable — so it is staged. **Lava, not rock**,
    /// because lava is the case DK2 actually has: a wooden bridge over lava burns
    /// out thirty seconds after it is built and the tile reverts (Q67,
    /// <see cref="Systems.BridgeBurnSystem"/>), while nothing in DK2 ever turns
    /// floor back into rock at all (owner, 2026-09-04 — see
    /// <see cref="DugOutTilesStayDugOutTests"/>). An imp carries
    /// <see cref="CreatureFlags.CanWalkOnWater"/> and not
    /// <see cref="CreatureFlags.CanWalkOnLava"/>, so lava stops it.
    /// </summary>
    [Fact]
    public void GroundThatStopsBeingWalkableUnderAWalkStopsTheWalk()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, Keeper, (byte)2]);

        // A corridor of claimed floor from the starting room out along row 2.
        for (int x = 4; x <= 10; x++)
        {
            ref TileState tile = ref world.Tile(x, 2);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
            tile.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        }

        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        for (int x = 4; x <= 10; x++)
            imp.Path.Add(new TilePos(x, 2));

        var sim = new GameSimulation(database, world, 11, [new Systems.CreatureMovementSystem()]);

        // Walk until it is well into the corridor, then wall it off ahead.
        while (imp.Tile.X < 6) sim.Tick(InputFrame.Empty);

        int blockAt = imp.Tile.X + 2;
        ref TileState blocked = ref world.Tile(blockAt, 2);
        blocked.TerrainId = Dk2TerrainFixture.Lava;
        blocked.OwnerPlayerId = 0;
        blocked.Health = database.TerrainById(Dk2TerrainFixture.Lava).StartingHealth;

        for (int tick = 0; tick < 200; tick++)
        {
            sim.Tick(InputFrame.Empty);
            Assert.True(
                imp.Tile.X < blockAt,
                $"imp reached ({imp.Tile.X},{imp.Tile.Y}), past the lava at ({blockAt},2)");
        }

        // And it did not simply freeze mid-route: the route is dropped, which is
        // what lets the task system give it something else to do.
        Assert.Empty(imp.Path);
    }

    /// <summary>
    /// The grid's own answer, terrain type by terrain type, for a mover with no
    /// hazard-crossing flags: solid means unwalkable, and nothing else does.
    ///
    /// Pinned separately from the runs above because those can only exercise the
    /// terrain the fixture map happens to contain, and because this is the line
    /// the whole rule rests on.
    /// </summary>
    [Fact]
    public void TheGridRefusesEverySolidTerrainAndAcceptsEveryOpenOne()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, Keeper, (byte)2]);

        CreatureDef walker = Dk2TerrainFixture.Imp() with
        {
            Flags = Dk2TerrainFixture.Imp().Flags
                & ~CreatureFlags.CanWalkOnWater
                & ~CreatureFlags.CanWalkOnLava,
        };

        var grid = new WorldPathGrid(database, world, walker);

        byte[] solid =
        [
            Dk2TerrainFixture.ImpenetrableRock,
            Dk2TerrainFixture.Rock,
            Dk2TerrainFixture.Gold,
            Dk2TerrainFixture.Gems,
            Dk2TerrainFixture.ReinforcedWall,
        ];

        byte[] open =
        [
            Dk2TerrainFixture.DirtPath,
            Dk2TerrainFixture.ClaimedPath,
            Dk2TerrainFixture.TreasuryTerrain,
            Dk2TerrainFixture.LairTerrain,
        ];

        foreach (byte terrainId in solid)
        {
            Assert.True(database.TerrainById(terrainId).IsSolid);
            world.Tile(8, 8).TerrainId = terrainId;
            Assert.False(grid.IsWalkable(8, 8), database.TerrainById(terrainId).Name);
        }

        foreach (byte terrainId in open)
        {
            Assert.False(database.TerrainById(terrainId).IsSolid);
            world.Tile(8, 8).TerrainId = terrainId;
            Assert.True(grid.IsWalkable(8, 8), database.TerrainById(terrainId).Name);
        }
    }
}

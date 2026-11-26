// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>What the possessed view asks of the world (possession-client.md §2, §2a, §2b).</summary>
public sealed class PossessionWorldTests
{
    private const int One = 65536;
    private static readonly Fixed16Vector East = new(One, 0);

    [Theory]
    [InlineData(2 * One, 0, One / 2, 3 * One / 2)]          // the near edge of a circle ahead
    [InlineData(-2 * One, 0, One / 2, -5 * One / 2)]        // behind: the smaller root is the far edge
    [InlineData(2 * One, One, One / 2, int.MaxValue)]       // a miss
    [InlineData(2 * One, One / 2, One / 2, int.MaxValue)]   // a graze counts as a miss
    public void TheTouchDistanceIsTheSmallerRoot_StronglyInferredFromBinary(int centreX, int centreY, int radius, int expected)
    {
        Assert.Equal(expected, PossessionWorld.TouchDistance(default, East, new Fixed16Vector(centreX, centreY), radius));
    }

    private static (GameDatabase Db, WorldState World, PossessionWorld View, CreatureState Me, TilePos Open, TilePos Wall) Level1()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        // An open tile with open ground to its west and solid terrain to its east.
        TilePos open = default, wall = default;
        for (int y = 1; y < world.Height - 1 && wall == default; y++)
            for (int x = 2; x < world.Width - 1; x++)
                if (!Solid(x, y) && !Solid(x - 1, y) && Solid(x + 1, y) && Plain(x, y) && Plain(x - 1, y))
                {
                    (open, wall) = (new TilePos(x, y), new TilePos(x + 1, y));
                    break;
                }
        byte goblin = db.Creatures.Values.Single(c => c.Name == "Goblin").CreatureId;
        CreatureState me = Spawn(db, world, goblin, open);
        me.Possessed = true;
        var view = new PossessionWorld(db);
        view.Bind(world, me);
        return (db, world, view, me, open, wall);

        bool Solid(int x, int y) => db.TerrainById(world.Tile(x, y).TerrainId).IsSolid;
        bool Plain(int x, int y) => db.RoomByTerrain(world.Tile(x, y).TerrainId) is null
            && !db.TerrainById(world.Tile(x, y).TerrainId).IsWater && !db.TerrainById(world.Tile(x, y).TerrainId).IsLava;
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, TilePos at)
    {
        CreatureState c = world.SpawnCreature(db, type, new PlayerId(3), at);
        c.Body.Radius = SteeringMover.CrowdWordOf(db.CreatureById(type).FootprintRadius.Raw);
        WorldSteering.SyncBody(c);
        return c;
    }

    [InstallFact]
    public void AStepIsAllowedOntoOpenGroundAndRefusedIntoRock_StronglyInferredFromBinary()
    {
        (GameDatabase _, WorldState _, PossessionWorld? view, CreatureState _, TilePos open, TilePos wall) = Level1();

        Assert.True(view.CanStepTo(open.X - 1, open.Y));
        Assert.False(view.CanStepTo(wall.X, wall.Y));
        Assert.False(view.CanStepTo(-1, open.Y));
        Assert.False(view.Blocked((open.X << 12) + 0x800, (open.Y << 12) + 0x800));
        Assert.True(view.Blocked((wall.X << 12) + 0x800, (wall.Y << 12) + 0x800));
    }

    [InstallFact]
    public void ThePossessedSpeedsAreTheDefinitionsWithThePossessionStat_StronglyInferredFromBinary()
    {
        (GameDatabase? db, WorldState _, PossessionWorld? view, CreatureState? me, TilePos _, TilePos _) = Level1();
        CreatureDef def = db.CreatureById(me.CreatureTypeId);
        int stat = db.FirstPersonStat(CreatureStatId.Speed) + 100;

        PossessedBody body = view.Body();

        Assert.Equal(SimulationTiming.LoaderRatePerTick(def.Speed.Raw) * stat / 100, body.WalkSpeed);
        Assert.Equal(SimulationTiming.LoaderRatePerTick(def.RunSpeed.Raw) * stat / 100, body.RunSpeed);
        Assert.Equal(SimulationTiming.LoaderRatePerTick(def.ShuffleSpeed.Raw) * stat / 100, body.ShuffleSpeed);
        Assert.Equal((def.EyeHeight.Raw, def.FootprintRadius.Raw), (body.EyeHeight, body.BodyWidth));
        Assert.True(body.WalkSpeed > 0);
    }

    [InstallFact]
    public void AWeakNonWorkerMovesAtAFifth_StronglyInferredFromBinary()
    {
        (GameDatabase _, WorldState? world, PossessionWorld? view, CreatureState? me, TilePos _, TilePos _) = Level1();
        int healthy = view.Body().WalkSpeed;
        me.Health = 1;
        view.Bind(world, me);

        Assert.True(view.Body().WalkSpeed < healthy);
        Assert.InRange(view.Body().WalkSpeed, (healthy / 5) - 2, (healthy / 5) + 2);
    }

    /// <summary>Another creature half a tile ahead: the step toward it ends on the edge where the two bodies touch.</summary>
    [InstallFact]
    public void AStepIntoAnotherCreatureStopsWhereTheBodiesTouch_StronglyInferredFromBinary()
    {
        (GameDatabase? db, WorldState? world, PossessionWorld? view, CreatureState? me, TilePos open, TilePos _) = Level1();
        int startX = me.PosX, y = me.PosY;
        CreatureState other = Spawn(db, world, me.CreatureTypeId, new TilePos(open.X - 1, open.Y));
        other.PosX = startX - 0x800;
        other.PosY = y;
        WorldSteering.SyncBody(other);
        view.Bind(world, me);
        int reach = (me.Body.Radius + other.Body.Radius) >> 4;

        (int x, int resolvedY) = view.ResolveAgainstThings(startX, y, startX - 0x700, y);

        Assert.InRange(x, other.PosX + reach - 2, other.PosX + reach + 2);
        Assert.Equal(y, resolvedY);
        Assert.Equal((startX - 0x700, y - 0x4000), view.ResolveAgainstThings(startX, y - 0x4000, startX - 0x700, y - 0x4000));
    }
}

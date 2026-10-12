// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public class ImpClaimPositionTests
{
    [Theory]
    [InlineData(Dk2TerrainFixture.DirtPath, PlayerIds.Unowned, TileWorkKind.Claim)]
    [InlineData(Dk2TerrainFixture.ClaimedPath, PlayerIds.FirstKeeper + 1, TileWorkKind.AttackTile)]
    [InlineData(Dk2TerrainFixture.TreasuryTerrain, PlayerIds.FirstKeeper + 1, TileWorkKind.ConvertRoom)]
    public void AnImpAlreadyOnTheTargetTileWalksToItsCentreBeforeFloorWork(
        byte terrain, byte owner, TileWorkKind work)
    {
        GameSimulation sim = Start(terrain, owner, new TilePos(4, 2));
        CreatureState imp = Assert.Single(sim.World.Creatures);
        imp.PosX = 16896; // (4.125, 2.5), three eighths of a tile from the centre.
        int initialHealth = TargetHealth(sim);

        TakeJob(sim, imp);

        Assert.Equal(work, imp.TargetWork);
        Assert.Equal(6, imp.Dk2StateId);
        Assert.Equal(16896, imp.PosX);
        Assert.Equal(initialHealth, TargetHealth(sim));
        ReachFirstApplication(sim, imp, initialHealth);
    }

    [Fact]
    public void AClaimFromANeighbouringTileReachesTheCentreBeforeItsFirstApplication()
    {
        GameSimulation sim = Start(Dk2TerrainFixture.DirtPath, PlayerIds.Unowned, new TilePos(3, 2));
        CreatureState imp = Assert.Single(sim.World.Creatures);
        int initialHealth = TargetHealth(sim);

        TakeJob(sim, imp);
        ReachFirstApplication(sim, imp, initialHealth);
    }

    private static GameSimulation Start(byte terrain, byte owner, TilePos at)
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Neutral, PlayerIds.FirstKeeper, (byte)(PlayerIds.FirstKeeper + 1)]);
        ref TileState target = ref world.Tile(4, 2);
        target.TerrainId = terrain;
        target.OwnerPlayerId = owner;
        target.Health = database.TerrainById(terrain).StartingHealth;
        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, PlayerIds.FirstKeeper, at);
        return new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
    }

    private static void TakeJob(GameSimulation sim, CreatureState imp)
    {
        for (int i = 0; i < 4 && imp.TargetTileIndex < 0; i++) sim.Tick(InputFrame.Empty);
        Assert.Equal(2 * sim.World.Width + 4, imp.TargetTileIndex);
    }

    private static int TargetHealth(GameSimulation sim)
    {
        TileState tile = sim.World.Tile(4, 2);
        // The fixture has one room tile; room conversion changes its deficit, not tile health.
        return tile.Health - tile.RoomHealthLost;
    }

    private static void ReachFirstApplication(GameSimulation sim, CreatureState imp, int initialHealth)
    {
        for (int i = 0; i < 30 && TargetHealth(sim) == initialHealth; i++)
            sim.Tick(InputFrame.Empty);

        Assert.NotEqual(initialHealth, TargetHealth(sim));
        // Native travel progress permits a short landing; do not require a teleport to the point.
        Assert.InRange(Math.Abs(imp.PosX - 18432), 0, 409);
        Assert.InRange(Math.Abs(imp.PosY - 10240), 0, 409);
    }
}

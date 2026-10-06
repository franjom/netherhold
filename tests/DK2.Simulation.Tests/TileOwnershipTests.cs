// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>Ownership after tile work and room removal (Q95, ADR-0045).</summary>
public class TileOwnershipTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static WorldState World() =>
        WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static void Set(WorldState world, TilePos at, byte terrainId, byte owner)
    {
        ref TileState tile = ref world.Tile(at);
        tile = default;
        tile.TerrainId = terrainId;
        tile.OwnerPlayerId = owner;
        tile.Health = Db.TerrainById(terrainId).StartingHealth;
    }

    private static void WorkUntilChanged(WorldState world, TilePos at, TileWorkKind kind, byte by)
    {
        byte was = world.Tile(at).TerrainId;
        ref TileState tile = ref world.Tile(at);
        for (int i = 0; i < 2000 && tile.TerrainId == was; i++)
            TileWorkRules.Apply(Db, ref tile, kind, by);

        Assert.NotEqual(was, world.Tile(at).TerrainId);
    }

    // ---- the invariant ----------------------------------------------------

    [Fact]
    public void TileWorkAssignsNeutralOwnershipToGroundThatCannotBeOwned()
    {
        WorldState world = World();

        // Every transition the fixture can express that ends somewhere an owner
        // is meaningless, driven to completion, with the tile owned going in.
        //
        // ConvertRoom is deliberately absent: a takeover leaves the terrain
        // exactly as it found it, so it never ends on unownable ground. That it
        // must not be broken by this rule is TakingARoomStillTakesIt.
        (byte Terrain, byte Owner, TileWorkKind Kind)[] cases =
        [
            (Dk2TerrainFixture.ReinforcedWall, Keeper, TileWorkKind.DigOwnWall),
            (Dk2TerrainFixture.ReinforcedWall, Rival, TileWorkKind.DigEnemyWall),
            (Dk2TerrainFixture.ClaimedPath, Rival, TileWorkKind.AttackTile),
            (Dk2TerrainFixture.ClaimedVaultTerrain, Rival, TileWorkKind.AttackTile),
        ];

        int checkedCases = 0;
        foreach ((byte terrain, byte owner, TileWorkKind kind) in cases)
        {
            var at = new TilePos(9, 9);
            Set(world, at, terrain, owner);

            WorkUntilChanged(world, at, kind, Keeper);

            TerrainDef after = Db.TerrainById(world.Tile(at).TerrainId);
            Assert.False(after.IsOwnable,
                $"{terrain} under {kind} was expected to end on unownable ground, " +
                $"and ended on {after.Name} — so this case tests nothing");

            Assert.Equal(PlayerIds.Neutral, world.Tile(at).OwnerPlayerId);
            checkedCases++;
        }

        Assert.Equal(cases.Length, checkedCases);
    }

    /// <summary>The defect, stated as the two facts that were both false of the same tile.</summary>
    [Fact]
    public void GroundDugOutOfAKeepersOwnWallIsNeitherTheirsNorStuck()
    {
        WorldState world = World();
        var at = new TilePos(9, 9);
        Set(world, at, Dk2TerrainFixture.ReinforcedWall, Keeper);

        // Their own wall, so DigOwnWall. It leaves Rock, then Rock is dug to
        // Dirt Path — neither of which is ownable terrain.
        WorkUntilChanged(world, at, TileWorkKind.DigOwnWall, Keeper);
        Assert.Equal(Dk2TerrainFixture.Rock, world.Tile(at).TerrainId);
        Assert.Equal(PlayerIds.Neutral, world.Tile(at).OwnerPlayerId);

        WorkUntilChanged(world, at, TileWorkKind.Dig, Keeper);
        Assert.Equal(Dk2TerrainFixture.DirtPath, world.Tile(at).TerrainId);
        Assert.Equal(PlayerIds.Neutral, world.Tile(at).OwnerPlayerId);

        // And the point of all of it: the keeper can now claim what they opened.
        // With the stale byte this was false, and there was no other way through.
        Assert.True(TileWorkRules.Applies(Db, world.Tile(at), TileWorkKind.Claim, Keeper));
    }

    /// <summary>
    /// The end-to-end shape of the defect, through the imp AI: a keeper boxed in
    /// by their own reinforced wall digs out and their territory grows.
    ///
    /// <para>This is the test that would have caught it. Every existing claiming
    /// test opened <i>unowned</i> rock, which never carried the stale byte — the
    /// gap was specifically a wall the keeper already owned, and nothing dug one
    /// and then tried to grow past it.</para>
    /// </summary>
    [Fact]
    public void AKeepersTerritoryGrowsThroughAWallItDugItself()
    {
        WorldState world = World();

        // The fixture's keeper room is (1..3, 1..3). Wall the keeper's own
        // reinforced wall at (4,2), with dirt beyond it at (5,2).
        Set(world, new TilePos(4, 2), Dk2TerrainFixture.ReinforcedWall, Keeper);
        Set(world, new TilePos(5, 2), Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);

        world.SpawnCreature(Db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        var sim = new GameSimulation(Db, world, seed: 4, Dk2TerrainFixture.DefaultSystems());
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, new TilePos(4, 2))));
        for (int i = 0; i < 600; i++) sim.Tick(InputFrame.Empty);

        // The wall came down, the floor it left is the keeper's, and the dirt
        // beyond it has been claimed too — which is only possible if the dug
        // tile counted as own land.
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, world.Tile(4, 2).TerrainId);
        Assert.Equal(Keeper, world.Tile(4, 2).OwnerPlayerId);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, world.Tile(5, 2).TerrainId);
        Assert.Equal(Keeper, world.Tile(5, 2).OwnerPlayerId);
    }

    // ---- and what the invariant must NOT break ---------------------------

    [Fact]
    public void SellingYourOwnTreasuryKeepsTheFloorItStoodOn()
    {
        // The original reason for keeping the byte, and it still holds: a
        // treasury's destroyed type is Claimed Path, which IS ownable, so
        // nothing is cleared. Selling a room does not hand the ground to nobody.
        WorldState world = World();
        var at = new TilePos(9, 9);
        Set(world, at, Dk2TerrainFixture.TreasuryTerrain, Keeper);

        RoomRules.Unmake(Db, ref world.Tile(at));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, world.Tile(at).TerrainId);
        Assert.Equal(Keeper, world.Tile(at).OwnerPlayerId);
    }

    [Fact]
    public void SellingABridgeOverWaterDoesNotLeaveYouOwningTheWater()
    {
        // The other side of the same rule, and the case that shows it is not
        // merely a fix for digging: water cannot be owned, so the neutral player has it (Q514).
        WorldState world = World();
        var at = new TilePos(9, 9);
        Set(world, at, Dk2TerrainFixture.Water, PlayerIds.Unowned);
        RoomRules.Place(Db, ref world.Tile(at), Db.Room(RoomTypeId.WoodenBridge), Keeper);
        Assert.Equal(Keeper, world.Tile(at).OwnerPlayerId);

        RoomRules.Unmake(Db, ref world.Tile(at));

        Assert.Equal(Dk2TerrainFixture.Water, world.Tile(at).TerrainId);
        Assert.Equal(PlayerIds.Neutral, world.Tile(at).OwnerPlayerId);
    }

    [Fact]
    public void TakingARoomStillTakesIt()
    {
        // A takeover writes owners only, never terrain (RoomHealthRules), so the
        // ownership invariant has nothing to undo: the owner it was given survives.
        WorldState world = World();
        var at = new TilePos(9, 9);
        Set(world, at, Dk2TerrainFixture.TreasuryTerrain, Rival);

        ref TileState tile = ref world.Tile(at);
        for (int i = 0; i < 100 && tile.OwnerPlayerId != Keeper; i++)
            RoomHealthRules.Convert(world, Db, (at.Y * world.Width) + at.X, Keeper);

        Assert.Equal(Keeper, world.Tile(at).OwnerPlayerId);
        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, world.Tile(at).TerrainId);
    }

    [Fact]
    public void ClaimingStillTakesGround()
    {
        WorldState world = World();
        var at = new TilePos(9, 9);
        Set(world, at, Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);

        WorkUntilChanged(world, at, TileWorkKind.Claim, Keeper);

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, world.Tile(at).TerrainId);
        Assert.Equal(Keeper, world.Tile(at).OwnerPlayerId);
    }
}

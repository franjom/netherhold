// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// One invariant: <b>a tile whose terrain cannot be owned does not carry an
/// owner.</b>
///
/// <para><b>The defect this exists to stop coming back</b> (Q95, ADR-0045). The
/// owner byte used to survive every conversion, on a justification recorded in
/// two places: <i>"which owner id means 'nobody' is UNKNOWN (Q17), and every
/// rule that cares asks <c>TerrainDef.IsOwnable</c> first, so a stale byte on
/// unownable terrain is inert."</i> Both halves expired.</para>
///
/// <list type="number">
/// <item><b>Q17 is answered.</b> Player 0 is unowned — confirmed three ways
/// across all 66 shipped levels (ADR-0043). So there is now an id that means
/// nobody, and the reason for not writing one is gone.</item>
/// <item><b>The byte was never inert.</b>
/// <see cref="TileWorkKind.Claim"/> tests <c>OwnerPlayerId != playerId</c> and
/// does <i>not</i> ask <c>IsOwnable</c> first. So a keeper who dug through their
/// own reinforced wall got Dirt Path still stamped with their id — which is
/// neither claimable by them (the owner already matches) nor their own land (the
/// terrain is not ownable). <b>Their territory could not grow through it,
/// ever.</b></item>
/// </list>
///
/// <para><b>It walled a level-1 keeper in permanently</b>, because the only route
/// out of that dungeon heart is through the keeper's own reinforced wall. Four
/// months, and what finally showed it was a scenario that dug toward the portal
/// and then sat there for 130 game-seconds doing nothing.</para>
///
/// <para>The general lesson is the project's own: a justification can rot when
/// the thing it rests on is answered elsewhere. "Q17 is UNKNOWN" was true when
/// written and false the day ADR-0043 landed, and nothing connected the two.</para>
/// </summary>
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
    public void NoTerrainThatCannotBeOwnedEverCarriesAnOwner()
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

            Assert.Equal(PlayerIds.Unowned, world.Tile(at).OwnerPlayerId);
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
        Assert.Equal(PlayerIds.Unowned, world.Tile(at).OwnerPlayerId);

        WorkUntilChanged(world, at, TileWorkKind.Dig, Keeper);
        Assert.Equal(Dk2TerrainFixture.DirtPath, world.Tile(at).TerrainId);
        Assert.Equal(PlayerIds.Unowned, world.Tile(at).OwnerPlayerId);

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
        // merely a fix for digging: water cannot be owned, so nobody owns it.
        WorldState world = World();
        var at = new TilePos(9, 9);
        Set(world, at, Dk2TerrainFixture.Water, PlayerIds.Unowned);
        RoomRules.Place(Db, ref world.Tile(at), Db.Room(RoomTypeId.WoodenBridge), Keeper);
        Assert.Equal(Keeper, world.Tile(at).OwnerPlayerId);

        RoomRules.Unmake(Db, ref world.Tile(at));

        Assert.Equal(Dk2TerrainFixture.Water, world.Tile(at).TerrainId);
        Assert.Equal(PlayerIds.Unowned, world.Tile(at).OwnerPlayerId);
    }

    [Fact]
    public void TakingARoomStillTakesIt()
    {
        // The invariant runs inside the same Convert() a takeover uses, so it
        // has to not undo one: a converted room stays ownable terrain, so the
        // owner it was just given survives.
        WorldState world = World();
        var at = new TilePos(9, 9);
        Set(world, at, Dk2TerrainFixture.TreasuryTerrain, Rival);

        ref TileState tile = ref world.Tile(at);
        for (int i = 0; i < 100 && tile.OwnerPlayerId != Keeper; i++)
            TileWorkRules.Apply(Db, ref tile, TileWorkKind.ConvertRoom, Keeper);

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

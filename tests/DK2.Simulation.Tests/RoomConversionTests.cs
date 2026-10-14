// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>Taking a room that belongs to somebody else — a rival's, or a neutral
/// one, which is what an unclaimed portal is. CONFIRMED (owner, 2026-09-04):
/// "once an imp claims a portal then the entire portal gets claimed, all tiles
/// that are assigned to that portal are immediately claimed … that is a game
/// mechanic that works on claiming enemy/neutral rooms too, rooms are instantly
/// claimed by imps, not tile by tile" (Q92, ADR-0044).</summary>
/// <remarks>Before that, <see cref="TileWorkKind.Claim"/> applied to room terrain
/// — it is <c>Ownable</c> and not <c>Solid</c>, which is all the claim rule asked
/// for — so a rival's treasury came over a tile at a time at
/// <c>ClaimTileHealth</c>'s rate and a half-taken portal was reachable. The first
/// two tests here are the ones that would have caught it.</remarks>
public class RoomConversionTests
{
    private const byte Keeper = 3;
    private const byte Rival = 4;

    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static WorldState World() => WorldState.FromMap(
        Db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static void Paint(
        WorldState world, RoomTypeId type, int x0, int y0, int w, int h, byte owner)
    {
        RoomDef room = Db.Room(type);
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                RoomRules.Place(Db, ref tile, room, owner);
            }
        }
    }

    /// <summary>
    /// Works one tile until the room it is in changes hands, and says how many
    /// applications it took, at the 80% every worker is born with. Bounded so a
    /// rule that never completes fails rather than hangs.
    /// </summary>
    private static int WorkUntilTaken(WorldState world, TilePos at, byte playerId)
    {
        for (int i = 1; i <= 500; i++)
        {
            TileWorkResult result = RoomHealthRules.Convert(world, Db, at, playerId);
            if (result.Outcome == TileWorkOutcome.Transformed) return i;
            if (result.Outcome == TileWorkOutcome.Rejected)
                throw new InvalidOperationException($"ConvertRoom rejected at {at}");
        }
        throw new InvalidOperationException($"ConvertRoom never completed at {at}");
    }

    private static RoomInstance RoomAt(WorldState world, int x, int y) =>
        RoomIndex.RoomAt(world, Db, (y * world.Width) + x)
        ?? throw new InvalidOperationException($"no room at ({x},{y})");

    // ---- the two tests that would have caught the defect ------------------

    /// <summary>The headline fact, and the whole reason this file exists.</summary>
    [Fact]
    public void ClaimingOneTileOfAPortalClaimsAllNineAtOnce()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Portal, 5, 5, 3, 3, PlayerIds.Neutral);

        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        // One tile, one corner, and not the middle — so "the tile that happened
        // to be worked" cannot be mistaken for "the tile the room is keyed on".
        world.QueueWork(5, 5, TileWorkKind.ConvertRoom, Keeper);
        for (int i = 0; i < 30; i++)
        {
            sim.Tick(InputFrame.Empty);
            world.QueueWork(5, 5, TileWorkKind.ConvertRoom, Keeper);
        }

        for (int y = 5; y <= 7; y++)
        {
            for (int x = 5; x <= 7; x++)
            {
                Assert.Equal(Keeper, world.Tile(x, y).OwnerPlayerId);
                Assert.Equal(Dk2TerrainFixture.PortalTerrain, world.Tile(x, y).TerrainId);
            }
        }

        Assert.Contains(sink.Events, e => e.Kind == "room_converted");
    }

    /// <summary>
    /// <b>A half-taken room is not a state this can reach.</b> The room is one
    /// instance of nine tiles before and one instance of nine tiles after, and at
    /// no point two.
    ///
    /// <para>That matters beyond tidiness: <see cref="PortalRules.CanProduce"/>
    /// refuses a portal that does not contain its own centre tile, which is a
    /// guard against a state the original cannot be in — and this is what says
    /// so.</para>
    /// </summary>
    [Fact]
    public void APortalIsNeverHalfTaken()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Portal, 5, 5, 3, 3, PlayerIds.Neutral);

        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);

        var seen = new List<(byte Owner, int Tiles)>();
        for (int i = 0; i < 40; i++)
        {
            world.QueueWork(6, 5, TileWorkKind.ConvertRoom, Keeper);
            sim.Tick(InputFrame.Empty);

            RoomInstance room = RoomAt(world, 6, 5);
            seen.Add((room.OwnerPlayerId, room.TileCount));
        }

        // Every observation is a whole nine-tile room, and both owners appear —
        // so the run really did cross the takeover rather than never reaching it.
        Assert.All(seen, s => Assert.Equal(9, s.Tiles));
        Assert.Contains(seen, s => s.Owner == PlayerIds.Neutral);
        Assert.Contains(seen, s => s.Owner == Keeper);
    }

    // ---- what the two work kinds are for ---------------------------------

    [Fact]
    public void ClaimIsNotWhatTakesARoomAndConvertRoomIsNotWhatTakesFloor()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 1, 1, Rival);
        ref TileState roomTile = ref world.Tile(5, 5);

        // The fix: a room tile is convertible and NOT claimable. Before
        // 2026-09-04 both were true and Claim won.
        Assert.False(TileWorkRules.Applies(Db, roomTile, TileWorkKind.Claim, Keeper));
        Assert.True(TileWorkRules.Applies(Db, roomTile, TileWorkKind.ConvertRoom, Keeper));

        // And the partition holds the other way, so nothing falls between them.
        ref TileState floor = ref world.Tile(9, 9);
        floor.TerrainId = Dk2TerrainFixture.ClaimedPath;
        floor.OwnerPlayerId = Rival;
        floor.Health = Db.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;

        // A rival's floor is attacked before it is claimed (Q68), so it is
        // AttackTile's, and a room is never AttackTile's.
        Assert.False(TileWorkRules.Applies(Db, floor, TileWorkKind.Claim, Keeper));
        Assert.True(TileWorkRules.Applies(Db, floor, TileWorkKind.AttackTile, Keeper));
        Assert.False(TileWorkRules.Applies(Db, floor, TileWorkKind.ConvertRoom, Keeper));
        Assert.False(TileWorkRules.Applies(Db, roomTile, TileWorkKind.AttackTile, Keeper));
    }

    [Fact]
    public void EveryAttackableRoomTerrainIsConvertible_AGateOnlyOnceRuined_AndNoNonRoomTerrainIs()
    {
        WorldState world = World();
        byte heart = Db.Room(RoomTypeId.DungeonHeart).TerrainId;

        foreach (TerrainDef terrain in Db.Terrain.Values)
        {
            ref TileState tile = ref world.Tile(9, 9);
            tile = default;
            tile.TerrainId = terrain.TerrainId;
            tile.OwnerPlayerId = Rival;
            tile.Health = terrain.StartingHealth;

            bool room = terrain.IsRoom && !terrain.IsSolid && terrain.TerrainId != heart;
            Assert.Equal(
                room && terrain.IsAttackable,
                TileWorkRules.Applies(Db, tile, TileWorkKind.ConvertRoom, Keeper));

            // A gate's terrain is not Attackable: its ruin is what opens it (0x00454040, ADR-0320).
            tile.RoomRuined = true;
            Assert.Equal(room, TileWorkRules.Applies(Db, tile, TileWorkKind.ConvertRoom, Keeper));
        }
    }

    [Fact]
    public void ARoomYouAlreadyHoldIsNotConvertible()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 1, 1, Keeper);

        Assert.False(TileWorkRules.Applies(
            Db, world.Tile(5, 5), TileWorkKind.ConvertRoom, Keeper));
    }

    /// <summary>
    /// A dungeon heart is beaten down, not taken.
    ///
    /// <para>STRONGLY INFERRED rather than observed, and the argument is Q40's:
    /// on defeat <i>rivals claim the rooms and gold</i>, which happens after the
    /// heart falls. Letting conversion apply to a heart would send imps to steal
    /// one. Q93 — worth asking, because it is the one place this rule is ours
    /// rather than the owner's.</para>
    /// </summary>
    [Fact]
    public void ADungeonHeartCannotBeTakenThisWay()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.DungeonHeart, 5, 5, 5, 5, Rival);

        Assert.False(TileWorkRules.Applies(
            Db, world.Tile(7, 7), TileWorkKind.ConvertRoom, Keeper));
        Assert.False(TileWorkRules.Applies(
            Db, world.Tile(7, 7), TileWorkKind.Claim, Keeper));
    }

    // ---- what survives the takeover --------------------------------------

    [Fact]
    public void ATakenRoomStaysTheSameRoomRatherThanBeingDemolished()
    {
        // ConvertRoomHealth is negative and every room terrain in the data claims to
        // leave Claimed Path when destroyed. A conversion runs the ROOM's health
        // down, never a tile's, so nothing follows that field: demolishing is
        // AttackRoomHealth's job (TileWorkKind.AttackRoom).
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 2, Rival);

        WorkUntilTaken(world, new TilePos(5, 5), Keeper);

        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, world.Tile(5, 5).TerrainId);
        Assert.Equal(Keeper, world.Tile(5, 5).OwnerPlayerId);

        // And the worked tile's own health was never touched: the room's was.
        Assert.Equal(
            Db.TerrainById(Dk2TerrainFixture.TreasuryTerrain).StartingHealth,
            world.Tile(5, 5).Health);
    }

    [Fact]
    public void ATreasuryChangesHandsWithItsGoldStillInIt()
    {
        // Q40, observed on defeat: rivals claim the rooms AND gold. Nothing
        // spills, because a tile's capacity is a property of its terrain and the
        // terrain did not change.
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 2, Rival);
        GoldStore.Deposit(world, Db, Rival, 5000);
        Assert.Equal(5000, GoldStore.Total(world, Rival));

        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        for (int i = 0; i < 60; i++)
        {
            world.QueueWork(5, 5, TileWorkKind.ConvertRoom, Keeper);
            sim.Tick(InputFrame.Empty);
        }

        Assert.Equal(5000, GoldStore.Total(world, Keeper));
        Assert.Equal(0, GoldStore.Total(world, Rival));
    }

    /// <summary>
    /// The tiles that came over with the room keep whatever damage they had: a
    /// conversion writes owners and the room's health, never a tile's
    /// (<c>0x00497BF0</c>), as a claimed burning bridge keeps its fire (Q68).
    /// </summary>
    [Fact]
    public void TilesThatCameOverWithTheRoomKeepTheirDamage()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 2, Rival);

        // Rough up a tile the worker is not standing on.
        ref TileState bruised = ref world.Tile(6, 6);
        bruised.Health = 42;

        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        for (int i = 0; i < 60; i++)
        {
            world.QueueWork(5, 5, TileWorkKind.ConvertRoom, Keeper);
            sim.Tick(InputFrame.Empty);
        }

        Assert.Equal(Keeper, world.Tile(6, 6).OwnerPlayerId);
        Assert.Equal(42, world.Tile(6, 6).Health);
    }

    // ---- how long it takes ------------------------------------------------

    /// <summary>
    /// The room's whole health is beaten down, so time grows with its size
    /// (<c>0x00497BF0</c>, ADR-0336): StartingHealth 10000 a tile against
    /// <c>ConvertRoomHealth</c> −4000 / 4 × 80% = −800 a tick for a rival's room,
    /// and <c>ConvertNeutralRoomHealth</c> −20000 / 4 × 80% = −4000 for a neutral
    /// one. A neutral 3×3 taking 23 is what the Level 1 capture shows for its
    /// portal (room <c>+0x35</c>: 90000 down by 4000 a tick, Q500).
    /// </summary>
    [Theory]
    [InlineData(1, 1, Rival, 13)]
    [InlineData(2, 1, Rival, 25)]
    [InlineData(3, 3, Rival, 113)]
    [InlineData(1, 1, PlayerIds.Neutral, 3)]
    [InlineData(2, 1, PlayerIds.Neutral, 5)]
    [InlineData(3, 3, PlayerIds.Neutral, 23)]
    public void ConversionTimeScalesWithTheRoomsSize(int width, int height, byte owner, int applications)
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, width, height, owner);

        Assert.Equal(applications, WorkUntilTaken(world, new TilePos(5, 5), Keeper));
        for (int y = 5; y < 5 + height; y++)
            for (int x = 5; x < 5 + width; x++)
                Assert.Equal(Keeper, world.Tile(x, y).OwnerPlayerId);
    }

    [Fact]
    public void ATakenRoomIsResetToItsStartingHealthTimesItsSize()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 2, Rival);

        WorkUntilTaken(world, new TilePos(5, 5), Keeper);

        RoomInstance room = RoomAt(world, 5, 5);
        Assert.Equal(Keeper, room.OwnerPlayerId);
        Assert.Equal(
            4 * Db.TerrainById(Dk2TerrainFixture.TreasuryTerrain).StartingHealth,
            RoomHealthRules.Health(world, Db, room));
    }

    [Fact]
    public void TheNeutralRateIsChosenByTheWorkedTilesOwner()
    {
        Assert.Equal(-800, RoomHealthRules.ConvertPerApplication(Db, Rival, EfficiencyRules.BaseModifier));
        Assert.Equal(-4000, RoomHealthRules.ConvertPerApplication(Db, PlayerIds.Neutral, EfficiencyRules.BaseModifier));

        // A slapped or dropped imp, 0xCCC + 0x199: the 4499 of the Level 2 capture.
        Assert.Equal(-4499, RoomHealthRules.ConvertPerApplication(
            Db, PlayerIds.Neutral, EfficiencyRules.BaseModifier + EfficiencyRules.BonusModifier));
    }

    [Fact]
    public void ADamagedRoomStaysDamagedWhenTheWorkStops_AndTheNextWorkerPicksItUp()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 1, Rival);
        for (int i = 0; i < 10; i++) RoomHealthRules.Convert(world, Db, new TilePos(5, 5), Keeper);

        Assert.Equal(20000 - (10 * 800), RoomHealthRules.Health(world, Db, RoomAt(world, 5, 5)));
        Assert.Equal(Rival, world.Tile(6, 5).OwnerPlayerId);

        // From the other tile, the same room: 12000 left is 15 more.
        Assert.Equal(15, WorkUntilTaken(world, new TilePos(6, 5), Keeper));
    }

    /// <summary>
    /// A worker takes a room by standing <b>in</b> it.
    ///
    /// <para>Nothing was written to decide this: the stance rule asks what the
    /// tile will be when the job finishes, a takeover leaves it exactly what it
    /// was, and room terrain is walkable — so the answer falls out (ADR-0028).
    /// It is also where the original puts an imp claiming a room.</para>
    /// </summary>
    [Fact]
    public void AWorkerTakesARoomByStandingInIt()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Portal, 5, 5, 3, 3, PlayerIds.Neutral);

        var grid = new WorldPathGrid(
            Db, world, Db.CreatureById(Dk2TerrainFixture.ImpCreatureId));

        Assert.True(WorkStance.IsOnTheTile(
            Db, world, grid, new TilePos(5, 5), TileWorkKind.ConvertRoom));
    }

    /// <summary>Bridges convert through the room's aggregate health, with the same
    /// neutral/rival rates as other rooms (room-health.md section 3; Q94).</summary>
    [Theory]
    [InlineData(Rival, 10)]
    [InlineData(PlayerIds.Neutral, 2)]
    public void AMultiTileBridgeConvertsWholeAtTheRoomRate(byte owner, int applications)
    {
        WorldState world = World();

        // Four bridge tiles in a row over water: 8000 aggregate health.
        for (int x = 5; x <= 8; x++)
        {
            ref TileState tile = ref world.Tile(x, 9);
            tile.TerrainId = Dk2TerrainFixture.Water;
            RoomRules.Place(Db, ref tile, Db.Room(RoomTypeId.WoodenBridge), owner);
        }

        Assert.Equal(4, RoomAt(world, 5, 9).TileCount);

        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        for (int i = 1; i < applications; i++)
        {
            world.QueueWork(5, 9, TileWorkKind.ConvertRoom, Keeper);
            sim.Tick(InputFrame.Empty);
            for (int x = 5; x <= 8; x++)
                Assert.Equal(owner, world.Tile(x, 9).OwnerPlayerId);
        }

        world.QueueWork(5, 9, TileWorkKind.ConvertRoom, Keeper);
        sim.Tick(InputFrame.Empty);

        for (int x = 5; x <= 8; x++)
        {
            Assert.Equal(Keeper, world.Tile(x, 9).OwnerPlayerId);
            Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, world.Tile(x, 9).TerrainId);
            Assert.Equal(BridgeRules.SpansWater, world.Tile(x, 9).Flags);
        }
    }

    // ---- and an imp doing it by itself ------------------------------------

    /// <summary>
    /// The whole thing through the imp AI: nobody queues the work, nobody names
    /// the tile.
    ///
    /// <para>Taking a room sits at the claiming priority, so an imp with nothing
    /// marked goes and takes the neutral portal next to its keeper's land — which
    /// is the sequence that makes a portal reachable in a real game and is what
    /// Q92 was blocking.</para>
    /// </summary>
    [Fact]
    public void AnImpTakesANeutralPortalNextToItsKeepersLandByItself()
    {
        WorldState world = World();

        // Keeper floor at (4,2) joining the fixture's claimed room to a portal at
        // (5,1). Claiming spreads from land the keeper already holds (Q42), so
        // the portal has to touch it.
        ref TileState join = ref world.Tile(4, 2);
        join.TerrainId = Dk2TerrainFixture.ClaimedPath;
        join.OwnerPlayerId = Keeper;
        join.Health = Db.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;

        Paint(world, RoomTypeId.Portal, 5, 1, 3, 3, PlayerIds.Neutral);
        world.SpawnCreature(Db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        var sim = new GameSimulation(Db, world, seed: 7, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        for (int i = 0; i < 400; i++) sim.Tick(InputFrame.Empty);

        for (int y = 1; y <= 3; y++)
            for (int x = 5; x <= 7; x++)
                Assert.Equal(Keeper, world.Tile(x, y).OwnerPlayerId);

        Assert.Contains(sink.Events, e => e.Kind == "room_converted");
    }
}

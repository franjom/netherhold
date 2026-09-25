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
    /// Works one tile until the conversion lands, and says how many applications
    /// it took. Bounded so a rule that never completes fails rather than hangs.
    ///
    /// <para><b>This exercises the pure rule and NOT the spread to the rest of
    /// the room</b> — that is <see cref="Systems.TileWorkSystem"/>'s, because it
    /// needs the world. A test about the other tiles has to go through the
    /// system.</para>
    /// </summary>
    private static int WorkUntilTaken(WorldState world, TilePos at, byte playerId)
    {
        ref TileState tile = ref world.Tile(at);
        for (int i = 1; i <= 500; i++)
        {
            TileWorkResult result = TileWorkRules.Apply(
                Db, ref tile, TileWorkKind.ConvertRoom, playerId);
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
        for (int i = 0; i < 20; i++)
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
    public void EveryRoomTerrainIsConvertibleAndNoNonRoomTerrainIs()
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

            bool expected = terrain.IsRoom && !terrain.IsSolid && terrain.TerrainId != heart;
            Assert.Equal(
                expected,
                TileWorkRules.Applies(Db, tile, TileWorkKind.ConvertRoom, Keeper));
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
        // ConvertRoomHealth is negative, so the worked tile runs DOWN to zero —
        // and every room terrain in the data claims to leave Claimed Path when
        // destroyed. Following that field would demolish the room instead of
        // taking it, which is AttackRoomHealth's job and a work kind that does
        // not exist.
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 2, Rival);

        WorkUntilTaken(world, new TilePos(5, 5), Keeper);

        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, world.Tile(5, 5).TerrainId);
        Assert.Equal(Keeper, world.Tile(5, 5).OwnerPlayerId);

        // And the worked tile is at full health rather than the zero it was
        // driven to: a room taken is a room, not a ruin.
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
        for (int i = 0; i < 40; i++)
        {
            world.QueueWork(5, 5, TileWorkKind.ConvertRoom, Keeper);
            sim.Tick(InputFrame.Empty);
        }

        Assert.Equal(5000, GoldStore.Total(world, Keeper));
        Assert.Equal(0, GoldStore.Total(world, Rival));
    }

    /// <summary>
    /// The tiles that came over with the room keep whatever damage they had.
    ///
    /// <para>Only the owner is written on them. Resetting their health would mean
    /// repairing a room by taking it, and a rival who has been chipping at your
    /// treasury for a minute should not be rewarded with a fresh one. Same
    /// reasoning as a claimed burning bridge keeping its fire (Q68).</para>
    /// </summary>
    [Fact]
    public void TilesThatCameOverWithTheRoomKeepTheirDamage()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 2, Rival);

        // Rough up a tile the worker is not standing on.
        ref TileState bruised = ref world.Tile(6, 6);
        bruised.Health = 42;

        // Through the system, not WorkUntilTaken: the spread to the rest of the
        // room is TileWorkSystem's, because it needs the world.
        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        for (int i = 0; i < 40; i++)
        {
            world.QueueWork(5, 5, TileWorkKind.ConvertRoom, Keeper);
            sim.Tick(InputFrame.Empty);
        }

        Assert.Equal(Keeper, world.Tile(6, 6).OwnerPlayerId);
        Assert.Equal(42, world.Tile(6, 6).Health);
    }

    // ---- how long it takes ------------------------------------------------

    [Fact]
    public void TakingARoomCostsItsWholeHealthAtFourThousandASecond()
    {
        // ConvertRoomHealth is -4000 a second and work lands five times a second,
        // so 800 a swing against a room's 10000 health: 13 applications, which is
        // 2.6 seconds. That is the "instantly" the owner describes — not zero
        // time, but fast enough that the propagation is what a player notices.
        WorldState world = World();
        Paint(world, RoomTypeId.Portal, 5, 5, 3, 3, PlayerIds.Neutral);

        int applications = WorkUntilTaken(world, new TilePos(5, 5), Keeper);

        Assert.Equal(13, applications);
        Assert.Equal(-4000, TileWorkRules.PerSecond(Db, TileWorkKind.ConvertRoom));
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

    // ---- the extrapolation, flagged --------------------------------------

    /// <summary>
    /// <b>A multi-tile bridge converts whole, and this is the one place the
    /// owner's statement is being stretched.</b>
    ///
    /// <para>They said "rooms". A wooden bridge is a room in every way the data
    /// can tell — room terrain, its own <c>RoomDef</c>, built and sold like one —
    /// so it is treated like one, and one tile of a rival's ten-tile bridge hands
    /// over all ten. That is a strategically large consequence drawn from a
    /// sentence about portals, so it is pinned here as behaviour rather than
    /// buried as an assumption. Q94: claim one end of a long bridge in the
    /// original and see whether the far end changes colour.</para>
    /// </summary>
    [Fact]
    public void AMultiTileBridgeConvertsWholeAndThatIsAnExtrapolation()
    {
        WorldState world = World();

        // Four bridge tiles in a row over water, the rival's.
        for (int x = 5; x <= 8; x++)
        {
            ref TileState tile = ref world.Tile(x, 9);
            tile.TerrainId = Dk2TerrainFixture.Water;
            RoomRules.Place(Db, ref tile, Db.Room(RoomTypeId.WoodenBridge), Rival);
        }

        Assert.Equal(4, RoomAt(world, 5, 9).TileCount);

        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        for (int i = 0; i < 20; i++)
        {
            world.QueueWork(5, 9, TileWorkKind.ConvertRoom, Keeper);
            sim.Tick(InputFrame.Empty);
        }

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

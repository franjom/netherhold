// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// Rooms, and where a keeper's gold actually is.
///
/// The fixture map is a claimed-path room at (1..3, 1..3) owned by player 1,
/// surrounded by rock — see <see cref="Dk2TerrainFixture"/>. That block of
/// claimed floor is the only ground on it a room can be built on, which is the
/// point: unclaimed rock and unclaimed dirt are not somewhere a keeper may
/// build.
/// </summary>
public class RoomTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;

        public void Tick(params PlayerCommand[] commands) =>
            Sim.Tick(commands.Length == 0 ? InputFrame.Empty : InputFrame.Of(commands));

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);
    }

    private static Run Start(ulong seed = 7)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

        var sim = new GameSimulation(database, world, seed, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    private static byte TreasuryId => (byte)RoomTypeId.Treasury;
    private static byte LairId => (byte)RoomTypeId.Lair;

    /// <summary>Gives the keeper somewhere to keep money, without going through the build rules.</summary>
    private static void GiveTreasury(Run run, byte player, params TilePos[] tiles)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        RoomDef treasury = database.Room(RoomTypeId.Treasury);
        foreach (TilePos p in tiles)
        {
            ref TileState tile = ref run.World.Tile(p);
            RoomRules.Place(database, ref tile, treasury, player);
        }
    }

    /// <summary>A dungeon heart holding gold — the only way a keeper can afford a first treasury.</summary>
    private static void GiveHeart(Run run, byte player, TilePos at, int gold)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        ref TileState tile = ref run.World.Tile(at);
        RoomRules.Place(database, ref tile, database.Room(RoomTypeId.DungeonHeart), player);
        GoldStore.Deposit(run.World, database, player, gold);
    }

    // ---- where a room may go ----------------------------------------------

    [Fact]
    public void ARoomGoesOnItsOwnersClaimedFloor()
    {
        Run run = Start();
        GiveHeart(run, Keeper, new TilePos(1, 1), 1000);

        run.Tick(BuildRoomCommand.At(Keeper, TreasuryId, new TilePos(2, 2)));

        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, run.World.Tile(2, 2).TerrainId);
        Assert.Equal(Keeper, run.World.Tile(2, 2).OwnerPlayerId);
    }

    [Fact]
    public void TheFirstTreasuryIsPaidForOutOfTheDungeonHeart()
    {
        // A keeper's first treasury cannot come out of a treasury. The heart is
        // the other place the data gives gold a per-tile limit, and it is what
        // makes the opening move of a level possible at all.
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();
        GiveHeart(run, Keeper, new TilePos(1, 1), 1000);

        int cost = database.Room(RoomTypeId.Treasury).Cost;
        run.Tick(BuildRoomCommand.At(Keeper, TreasuryId, new TilePos(2, 2)));

        Assert.Equal(1000 - cost, GoldStore.Total(run.World, Keeper));
        Assert.Equal(1000 - cost, run.World.Tile(1, 1).GoldHeld);
        Assert.Equal(0, run.World.Tile(2, 2).GoldHeld);
    }

    [Fact]
    public void ARoomDoesNotGoOnSomebodyElsesFloor()
    {
        Run run = Start();
        run.Tick(BuildRoomCommand.At(Rival, TreasuryId, new TilePos(2, 2)));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(2, 2).TerrainId);
        Assert.Contains(run.Of("built"), e => e.Detail.Contains("NotYourLand=1", StringComparison.Ordinal));
    }

    [Fact]
    public void ARoomDoesNotGoOnRockThatHasNotBeenDugOut()
    {
        Run run = Start();
        run.Tick(BuildRoomCommand.At(Keeper, TreasuryId, new TilePos(8, 8)));

        Assert.Equal(Dk2TerrainFixture.Rock, run.World.Tile(8, 8).TerrainId);
        Assert.Contains(run.Of("built"), e => e.Detail.Contains("WrongGround=1", StringComparison.Ordinal));
    }

    [Fact]
    public void ARoomDoesNotGoOnUnclaimedDirt()
    {
        // Dug-out floor nobody owns. The ground is right; the ownership is not,
        // and DK2 makes a keeper claim a tile before building on it.
        Run run = Start();
        ref TileState dirt = ref run.World.Tile(8, 8);
        dirt.TerrainId = Dk2TerrainFixture.DirtPath;
        dirt.OwnerPlayerId = 0;

        run.Tick(BuildRoomCommand.At(Keeper, TreasuryId, new TilePos(8, 8)));

        Assert.Equal(Dk2TerrainFixture.DirtPath, run.World.Tile(8, 8).TerrainId);
        Assert.Contains(run.Of("built"), e => e.Detail.Contains("NotYourLand=1", StringComparison.Ordinal));
    }

    [Fact]
    public void ARoomDoesNotGoOnTopOfAnotherRoom()
    {
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(2, 2));

        run.Tick(BuildRoomCommand.At(Keeper, LairId, new TilePos(2, 2)));

        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, run.World.Tile(2, 2).TerrainId);
        Assert.Contains(run.Of("built"), e => e.Detail.Contains("Occupied=1", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyABridgeGoesOnWater_AndOnlyOnWater()
    {
        // The rule is not "the room is a bridge": it is the room's own
        // PlaceableOnWater flag, which in the shipped data only the two bridges
        // carry. Nothing in the rules names a bridge.
        GameDatabase database = Dk2TerrainFixture.Database();
        var water = new TileState { TerrainId = Dk2TerrainFixture.Water };
        var floor = new TileState { TerrainId = Dk2TerrainFixture.ClaimedPath, OwnerPlayerId = Keeper };

        RoomDef bridge = database.Room(RoomTypeId.WoodenBridge);
        RoomDef treasury = database.Room(RoomTypeId.Treasury);

        Assert.Equal(RoomPlacement.Allowed, RoomRules.CanBuild(database, water, bridge, Keeper));
        Assert.Equal(RoomPlacement.WrongGround, RoomRules.CanBuild(database, water, treasury, Keeper));
        Assert.Equal(RoomPlacement.WrongGround, RoomRules.CanBuild(database, floor, bridge, Keeper));
    }

    [Fact]
    public void ARoomAPlayerMayNotBuildIsRefusedEvenOnPerfectGround()
    {
        // The dungeon heart is not Buildable. It exists on levels because the
        // level put it there, not because a keeper bought one.
        GameDatabase database = Dk2TerrainFixture.Database();
        var floor = new TileState { TerrainId = Dk2TerrainFixture.ClaimedPath, OwnerPlayerId = Keeper };

        Assert.Equal(
            RoomPlacement.NotBuildable,
            RoomRules.CanBuild(database, floor, database.Room(RoomTypeId.DungeonHeart), Keeper));
    }

    [Fact]
    public void BuildingAnUnknownRoomIsRefusedRatherThanThrowing()
    {
        Run run = Start();
        run.Tick(new BuildRoomCommand(Keeper, 200, [new TilePos(2, 2)]));

        Assert.Contains(run.Of("build_refused"), e => e.Detail.Contains("no_such_room", StringComparison.Ordinal));
    }

    // ---- what a room costs -------------------------------------------------

    [Fact]
    public void ARoomIsPaidForOutOfTheTreasury_ATileAtATime()
    {
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(1, 1));
        GoldStore.Deposit(run.World, Dk2TerrainFixture.Database(), Keeper, 1000);

        int cost = Dk2TerrainFixture.Database().Room(RoomTypeId.Lair).Cost;
        run.Tick(BuildRoomCommand.At(Keeper, LairId, new TilePos(2, 2), new TilePos(3, 2)));

        Assert.Equal(1000 - (2 * cost), GoldStore.Total(run.World, Keeper));
    }

    [Fact]
    public void AKeeperWhoCanAffordSomeTilesGetsSomeTiles()
    {
        // 300 buys one lair tile of the two asked for. The command is applied in
        // the order it names its tiles, so the first is the one that lands.
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(1, 1));
        GoldStore.Deposit(run.World, Dk2TerrainFixture.Database(), Keeper, 300);

        run.Tick(BuildRoomCommand.At(Keeper, LairId, new TilePos(2, 2), new TilePos(3, 2)));

        Assert.Equal(Dk2TerrainFixture.LairTerrain, run.World.Tile(2, 2).TerrainId);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(3, 2).TerrainId);
        Assert.Equal(0, GoldStore.Total(run.World, Keeper));
        Assert.Contains(run.Of("built"), e => e.Detail.Contains("NotEnoughGold=1", StringComparison.Ordinal));
    }

    [Fact]
    public void AKeeperWithNoTreasuryCannotBuyAnything()
    {
        Run run = Start();
        run.Tick(BuildRoomCommand.At(Keeper, LairId, new TilePos(2, 2)));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(2, 2).TerrainId);
        Assert.Contains(run.Of("built"), e => e.Detail.Contains("NotEnoughGold=1", StringComparison.Ordinal));
    }

    [Fact]
    public void AFreeRoomNeedsNoTreasuryAtAll()
    {
        // The bridge costs 200, the treasury 200, the dungeon heart nothing. A
        // room whose Cost is zero must not be blocked by an empty treasury, or
        // no level could ever place its own free rooms through the same rules.
        GameDatabase database = Dk2TerrainFixture.Database();
        Assert.Equal(0, RoomRules.CostPerTile(database.Room(RoomTypeId.DungeonHeart)));

        Run run = Start();
        Assert.True(GoldStore.TryWithdraw(run.World, Keeper, 0));
    }

    // ---- where the gold is -------------------------------------------------

    [Fact]
    public void ATreasuryTileHoldsWhatTheDataSaysAndNoMore()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(1, 1));

        int perTile = database[VariableTypeId.MaxGoldPerTreasuryTile];
        int stored = GoldStore.Deposit(run.World, database, Keeper, perTile + 5000);

        Assert.Equal(perTile, stored);
        Assert.Equal(perTile, run.World.Tile(1, 1).GoldHeld);
        Assert.Equal(perTile, GoldStore.Total(run.World, Keeper));
    }

    [Fact]
    public void ADungeonHeartTileHoldsLessThanATreasuryTile()
    {
        // Two separate variables, and the heart's is the smaller. A keeper with
        // only a heart has somewhere to put gold, but not much of it.
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();

        ref TileState heart = ref run.World.Tile(1, 1);
        RoomRules.Place(database, ref heart, database.Room(RoomTypeId.DungeonHeart), Keeper);

        int stored = GoldStore.Deposit(run.World, database, Keeper, 999_999);

        Assert.Equal(database[VariableTypeId.MaxGoldPerDungeonHeartTile], stored);
        Assert.True(stored < database[VariableTypeId.MaxGoldPerTreasuryTile]);
    }

    [Fact]
    public void ARoomThatIsNotForKeepingGoldInKeepsNone()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();

        ref TileState lair = ref run.World.Tile(1, 1);
        RoomRules.Place(database, ref lair, database.Room(RoomTypeId.Lair), Keeper);

        Assert.Equal(0, GoldStore.Capacity(database, run.World.Tile(1, 1)));
        Assert.Equal(0, GoldStore.Deposit(run.World, database, Keeper, 5000));
    }

    [Fact]
    public void PlainFloorKeepsNoGoldEither_BecauseAPileIsNotAStore()
    {
        // Still zero now that floor piles exist, and for a better reason than
        // before: a pile is an object standing on the floor, not capacity the
        // floor has. GoldStore answers "where can this keeper KEEP gold", and
        // bare floor is not an answer -- FloorGold.Drop is.
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();

        Assert.Equal(0, GoldStore.Capacity(database, run.World.Tile(2, 2)));
        Assert.Equal(0, GoldStore.Deposit(run.World, database, Keeper, 500));
    }

    [Fact]
    public void GoldSpreadsAcrossTreasuryTilesAndStopsWhenTheyAreFull()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(1, 1), new TilePos(2, 1));

        int perTile = database[VariableTypeId.MaxGoldPerTreasuryTile];
        Assert.Equal(2 * perTile, GoldStore.SpaceFor(run.World, database, Keeper));

        int stored = GoldStore.Deposit(run.World, database, Keeper, perTile + 100);

        Assert.Equal(perTile + 100, stored);
        Assert.Equal(perTile, run.World.Tile(1, 1).GoldHeld);
        Assert.Equal(100, run.World.Tile(2, 1).GoldHeld);
    }

    [Fact]
    public void OneKeepersTreasuryIsNotAnothersMoney()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(1, 1));
        GoldStore.Deposit(run.World, database, Keeper, 500);

        Assert.Equal(500, GoldStore.Total(run.World, Keeper));
        Assert.Equal(0, GoldStore.Total(run.World, Rival));
        Assert.False(GoldStore.TryWithdraw(run.World, Rival, 1));
    }

    [Fact]
    public void SpendingTakesAllOfItOrNoneOfIt()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(1, 1));
        GoldStore.Deposit(run.World, database, Keeper, 500);

        Assert.False(GoldStore.TryWithdraw(run.World, Keeper, 501));
        Assert.Equal(500, GoldStore.Total(run.World, Keeper));

        Assert.True(GoldStore.TryWithdraw(run.World, Keeper, 500));
        Assert.Equal(0, GoldStore.Total(run.World, Keeper));
    }

    [Fact]
    public void AFloorAttackDoesNotTouchATreasuryOrItsGold()
    {
        // A room tile is not hostile floor (0x00454000 refuses Room), so the
        // floor attack leaves a rival's treasury and its gold alone. Rooms fall
        // to AttackRoomHealth, which is unbuilt, and with it the gold spill a
        // destroyed treasury would make (Q68).
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(2, 2));
        GoldStore.Deposit(run.World, database, Keeper, 2500);

        ref TileState tile = ref run.World.Tile(2, 2);
        tile.Health = 1;
        run.World.QueueWork(2, 2, TileWorkKind.AttackTile, Rival);
        run.Tick();

        Assert.Equal(1, run.World.Tile(2, 2).Health);
        Assert.Equal(2500, GoldStore.Total(run.World, Keeper));
        Assert.Empty(run.Of("gold_spilled"));
    }

    // ---- the gate ----------------------------------------------------------

    [Fact]
    public void GoldSurvivesASnapshot()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start();
        GiveTreasury(run, Keeper, new TilePos(1, 1), new TilePos(2, 1));
        GoldStore.Deposit(run.World, database, Keeper, 4000);
        run.Tick();

        byte[] bytes = WorldSnapshot.ToBytes(run.Sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            bytes, database, Dk2TerrainFixture.DefaultSystems());

        Assert.Equal(4000, GoldStore.Total(restored.World, Keeper));
        Assert.Equal(run.Sim.StateHash(), restored.StateHash());
    }

    [Fact]
    public void BuildingIsDeterministicUnderIdenticalInput()
    {
        static ulong RunOne()
        {
            Run run = Start();
            GiveTreasury(run, Keeper, new TilePos(1, 1));
            GoldStore.Deposit(run.World, Dk2TerrainFixture.Database(), Keeper, 5000);

            run.Tick(BuildRoomCommand.Rect(Keeper, LairId, 1, 2, 3, 3));
            for (int i = 0; i < 20; i++) run.Tick();
            return run.Sim.StateHash();
        }

        Assert.Equal(RunOne(), RunOne());
    }

    [Fact]
    public void ABuildCommandChangesTheStateHash()
    {
        // The command has to reach the world, not just the log. Two runs that
        // differ only in whether the keeper built something must not agree.
        Run built = Start();
        GiveTreasury(built, Keeper, new TilePos(1, 1));
        GoldStore.Deposit(built.World, Dk2TerrainFixture.Database(), Keeper, 5000);
        built.Tick(BuildRoomCommand.At(Keeper, LairId, new TilePos(2, 2)));

        Run idle = Start();
        GiveTreasury(idle, Keeper, new TilePos(1, 1));
        GoldStore.Deposit(idle.World, Dk2TerrainFixture.Database(), Keeper, 5000);
        idle.Tick();

        Assert.NotEqual(built.Sim.StateHash(), idle.Sim.StateHash());
    }
}

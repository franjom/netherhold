using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// What a bridge is built over, and what is left when it goes (Q64).
///
/// <para>The behaviour is CONFIRMED — the owner: a sold bridge reverts to the
/// water or lava it spanned, not to floor. The terrain table alone cannot produce
/// that, because every room terrain in the shipped data claims to leave Claimed
/// Path behind. What supplies the missing fact is the MAP record's third byte per
/// tile, which turns out to mark a bridge over lava; see
/// <see cref="BridgeRules"/>.</para>
/// </summary>
public class BridgeTests
{
    private const byte Keeper = 1;

    private static byte BridgeId => (byte)RoomTypeId.WoodenBridge;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;

        public void Tick(params PlayerCommand[] commands) =>
            Sim.Tick(commands.Length == 0 ? InputFrame.Empty : InputFrame.Of(commands));

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);
    }

    private static Run Start(byte hazardTerrainId, TilePos at)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, Keeper, (byte)2]);

        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = hazardTerrainId;
        tile.Health = database.TerrainById(hazardTerrainId).StartingHealth;

        var sim = new GameSimulation(database, world, 3, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    /// <summary>Gives the keeper somewhere to pay from and refund into.</summary>
    private static void GiveHeart(Run run, TilePos at, int gold)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        ref TileState tile = ref run.World.Tile(at);
        RoomRules.Place(database, ref tile, database.Room(RoomTypeId.DungeonHeart), Keeper);
        GoldStore.Deposit(run.World, database, Keeper, gold);
    }

    // ---- the round trip, both hazards ------------------------------------

    [Theory]
    [InlineData(Dk2TerrainFixture.Water)]
    [InlineData(Dk2TerrainFixture.Lava)]
    public void ABridgeSoldRevertsToTheHazardItSpanned(byte hazard)
    {
        var span = new TilePos(6, 6);
        Run run = Start(hazard, span);
        GiveHeart(run, new TilePos(1, 1), 1000);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));
        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, run.World.Tile(span).TerrainId);

        run.Tick(SellRoomCommand.At(Keeper, span));

        // Not Claimed Path, which is what the terrain record on its own says.
        Assert.Equal(hazard, run.World.Tile(span).TerrainId);
    }

    /// <summary>
    /// The bug this fixes, stated as a test: the terrain table's own
    /// destroyed-type field would have left floor over a lava lake.
    /// </summary>
    [Fact]
    public void TheTerrainTableAloneWouldHaveLeftFloorOverLava()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        TerrainDef bridge = database.TerrainById(Dk2TerrainFixture.WoodenBridgeTerrain);

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, bridge.DestroyedTypeTerrainId);
        Assert.False(database.TerrainById(bridge.DestroyedTypeTerrainId).IsLava);
    }

    [Fact]
    public void ABridgeBuiltOverLavaIsMarkedAndOneOverWaterIsNot()
    {
        GameDatabase database = Dk2TerrainFixture.Database();

        var overLava = new TilePos(6, 6);
        Run lava = Start(Dk2TerrainFixture.Lava, overLava);
        GiveHeart(lava, new TilePos(1, 1), 1000);
        lava.Tick(BuildRoomCommand.At(Keeper, BridgeId, overLava));

        Assert.Equal(BridgeRules.SpansLava, lava.World.Tile(overLava).Flags);
        Assert.Equal(
            Dk2TerrainFixture.Lava,
            BridgeRules.SpannedTerrain(database, lava.World.Tile(overLava))!.TerrainId);

        var overWater = new TilePos(7, 6);
        Run water = Start(Dk2TerrainFixture.Water, overWater);
        GiveHeart(water, new TilePos(1, 1), 1000);
        water.Tick(BuildRoomCommand.At(Keeper, BridgeId, overWater));

        Assert.Equal(BridgeRules.SpansWater, water.World.Tile(overWater).Flags);
        Assert.Equal(
            Dk2TerrainFixture.Water,
            BridgeRules.SpannedTerrain(database, water.World.Tile(overWater))!.TerrainId);
    }

    /// <summary>
    /// Once the bridge is gone the marker goes with it, so a tile that becomes a
    /// bridge again later cannot inherit the wrong hazard.
    /// </summary>
    [Fact]
    public void SellingClearsTheMarkerSoTheTileCanBeBridgedAgain()
    {
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));
        run.Tick(SellRoomCommand.At(Keeper, span));

        Assert.Equal(BridgeRules.SpansWater, run.World.Tile(span).Flags);
        Assert.Equal(Dk2TerrainFixture.Lava, run.World.Tile(span).TerrainId);

        // And rebuilding re-marks it, rather than trusting the stale byte.
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));
        Assert.Equal(BridgeRules.SpansLava, run.World.Tile(span).Flags);
    }

    // ---- the rule is asked of the room, not of a name --------------------

    [Fact]
    public void OnlyBridgesSpanAnything()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(Dk2TerrainFixture.Water, new TilePos(6, 6));

        // A treasury on ordinary floor spans nothing, whatever its Flags byte
        // happens to say - which is what stops the marker being read on the 38
        // terrain types it means nothing for.
        ref TileState tile = ref run.World.Tile(2, 2);
        RoomRules.Place(database, ref tile, database.Room(RoomTypeId.Treasury), Keeper);
        tile.Flags = BridgeRules.SpansLava;

        Assert.Null(BridgeRules.SpannedTerrain(database, tile));
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, BridgeRules.RevertsTo(database, tile));
    }

    [Fact]
    public void IsBridgeAsksThePlacementFlagsRatherThanTheName()
    {
        GameDatabase database = Dk2TerrainFixture.Database();

        Assert.True(BridgeRules.IsBridge(database.Room(RoomTypeId.WoodenBridge)));
        Assert.False(BridgeRules.IsBridge(database.Room(RoomTypeId.Treasury)));
        Assert.False(BridgeRules.IsBridge(database.Room(RoomTypeId.Lair)));
    }

    // ---- destruction and selling agree ----------------------------------

    /// <summary>
    /// A bridge beaten down by a rival leaves the same hazard selling it does.
    /// The two used to go through the destroyed-type field separately, and both
    /// were wrong the same way; there is one rule now.
    /// </summary>
    [Fact]
    public void ABridgeBeatenDownLeavesTheHazardToo()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        // A rival attacks the tile until it falls. AttackTile applies to
        // somebody else's non-solid ownable terrain, which a bridge is.
        ref TileState tile = ref run.World.Tile(span);
        for (int i = 0; i < 5000 && tile.TerrainId == Dk2TerrainFixture.WoodenBridgeTerrain; i++)
            TileWorkRules.Apply(database, ref tile, TileWorkKind.AttackTile, playerId: 2);

        Assert.Equal(Dk2TerrainFixture.Lava, tile.TerrainId);
    }
}

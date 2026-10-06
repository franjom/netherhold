// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

    private static RoomTypeId BridgeId => RoomTypeId.WoodenBridge;

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
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = hazardTerrainId;
        tile.Health = database.TerrainById(hazardTerrainId).StartingHealth;

        // The keeper's own floor to the west, which a span must touch (0x0044F630, Q33).
        ref TileState shore = ref world.Tile(new TilePos(at.X - 1, at.Y));
        shore.TerrainId = Dk2TerrainFixture.ClaimedPath;
        shore.OwnerPlayerId = Keeper;
        shore.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;

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

    private static void Lay(Run run, byte terrainId, params TilePos[] tiles)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        foreach (TilePos at in tiles)
        {
            ref TileState tile = ref run.World.Tile(at);
            tile.TerrainId = terrainId;
            tile.Health = database.TerrainById(terrainId).StartingHealth;
        }
    }

    // ---- where a span may start (0x0044F630, 0x004C57F0; Q33) --------------

    [Fact]
    public void ASpanTouchingNoOwnFloorIsRefused_AHumanOrderIncluded()
    {
        Run run = Start(Dk2TerrainFixture.Water, new TilePos(6, 6));
        GiveHeart(run, new TilePos(1, 1), 1000);
        var island = new TilePos(9, 6);
        Lay(run, Dk2TerrainFixture.Water, island);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, island));

        Assert.Equal(Dk2TerrainFixture.Water, run.World.Tile(island).TerrainId);
        Assert.Contains(run.Of("built"), e => e.Detail.Contains("refused=NotTouchingOwnLand=1"));
    }

    /// <summary>DK2 marks a dragged bridge rectangle before laying it: the tiles touching own floor,
    /// then the hazard joined to them inside the rectangle (0x004C57F0), so the wave's corner does
    /// not matter.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADraggedSpanGrowsFromTheShoreWhicheverEndItIsLaidFrom(bool fromTheShore)
    {
        TilePos near = new(6, 6), mid = new(7, 6), far = new(8, 6);
        Run run = Start(Dk2TerrainFixture.Water, near);
        GiveHeart(run, new TilePos(1, 1), 1000);
        Lay(run, Dk2TerrainFixture.Water, mid, far);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, fromTheShore ? [near, mid, far] : [far, mid, near]));

        Assert.All([near, mid, far], t => Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, run.World.Tile(t).TerrainId));
    }

    /// <summary>The mark spreads only through tiles of the order (0x004C57F0 stays inside its
    /// rectangle): water the order leaves out is not a bridge to the tile beyond it.</summary>
    [Fact]
    public void ADraggedSpanDoesNotSpreadThroughWaterOutsideTheOrder()
    {
        TilePos near = new(6, 6), skipped = new(7, 6), far = new(8, 6);
        Run run = Start(Dk2TerrainFixture.Water, near);
        GiveHeart(run, new TilePos(1, 1), 1000);
        Lay(run, Dk2TerrainFixture.Water, skipped, far);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, far, near));

        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, run.World.Tile(near).TerrainId);
        Assert.Equal(Dk2TerrainFixture.Water, run.World.Tile(skipped).TerrainId);
        Assert.Equal(Dk2TerrainFixture.Water, run.World.Tile(far).TerrainId);
    }

    [Fact]
    public void ADraggedSpanStopsWhereItsHazardIsBroken()
    {
        TilePos near = new(6, 6), far = new(8, 6); // (7,6) stays rock
        Run run = Start(Dk2TerrainFixture.Water, near);
        GiveHeart(run, new TilePos(1, 1), 1000);
        Lay(run, Dk2TerrainFixture.Water, far);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, far, new TilePos(7, 6), near));

        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, run.World.Tile(near).TerrainId);
        Assert.Equal(Dk2TerrainFixture.Water, run.World.Tile(far).TerrainId);
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

    /// <summary>Whatever removes a bridge tile leaves the hazard: the destroyed path
    /// and the selling path both resolve through <see cref="BridgeRules.RevertsTo"/>,
    /// having once consulted the destroyed-type field separately and been wrong the
    /// same way. No built work kind destroys a bridge — a rival's imps take it as a
    /// room (Q68), and <c>AttackTile</c> refuses rooms (<c>0x00454000</c>). The
    /// room-attack handler (<c>AttackRoomHealth</c>) is unbuilt (Q68).</summary>
    [Fact]
    public void AnythingThatRemovesABridgeTileLeavesTheHazard()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        ref TileState tile = ref run.World.Tile(span);
        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, tile.TerrainId);
        Assert.False(TileWorkRules.Applies(database, tile, TileWorkKind.AttackTile, playerId: 2));
        Assert.Equal(Dk2TerrainFixture.Lava, BridgeRules.RevertsTo(database, tile));
    }

    // ---- burning (Q67) ---------------------------------------------------

    /// <summary>
    /// <b>A wooden bridge over lava burns away</b>, and the clock starts when it
    /// is built — CONFIRMED (owner, 2026-09-03). It is the start-at-build detail
    /// that made this implementable: the variable's name fits three readings and
    /// only an observation picks one.
    /// </summary>
    [Fact]
    public void AWoodenBridgeOverLavaBurnsAwayAfterTheTimeTheDataGives()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        int life = SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.WoodBridgeLifeOnLavaSeconds]);
        Assert.True(life > 1);

        // Still there just before the clock runs out...
        for (int i = 0; i < life - 2; i++) run.Tick();
        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, run.World.Tile(span).TerrainId);

        // ...and gone, back to the lava it spanned, once it does.
        run.Tick();
        run.Tick();
        Assert.Equal(Dk2TerrainFixture.Lava, run.World.Tile(span).TerrainId);
        // Lava cannot be owned: the neutral player has it, as Tile_SetTerrain gives it (ADR-0362).
        Assert.Equal(PlayerIds.Neutral, run.World.Tile(span).OwnerPlayerId);
        Assert.Contains(run.Of("bridge_burnt"), e => e.Detail.Contains("(6,6)"));
    }

    /// <summary>
    /// A wooden bridge over <b>water</b> lasts for ever. The variable is
    /// <c>WoodBridgeLifeOnLava</c>Seconds, and the hazard is the whole of the
    /// difference.
    /// </summary>
    [Fact]
    public void AWoodenBridgeOverWaterNeverBurns()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Water, span);
        GiveHeart(run, new TilePos(1, 1), 1000);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));
        Assert.Equal(0, run.World.Tile(span).BurnTicksRemaining);

        int life = SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.WoodBridgeLifeOnLavaSeconds]);
        for (int i = 0; i < life * 2; i++) run.Tick();

        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, run.World.Tile(span).TerrainId);
        Assert.Empty(run.Of("bridge_burnt"));
    }

    /// <summary>
    /// The clock is set at build, not on the first footstep, and it is visible in
    /// the tile the moment the bridge lands.
    /// </summary>
    [Fact]
    public void TheClockIsRunningFromTheTickTheBridgeIsBuilt()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);

        Assert.Equal(0, run.World.Tile(span).BurnTicksRemaining);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        int life = SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.WoodBridgeLifeOnLavaSeconds]);

        // Set on the build tick, and already one tick down because the burn
        // system runs after the build system in the same tick.
        Assert.InRange(run.World.Tile(span).BurnTicksRemaining, life - 1, life);
    }

    /// <summary>
    /// Selling a burning bridge stops the clock, so a tile bridged again later
    /// does not inherit a countdown from the bridge before it.
    /// </summary>
    [Fact]
    public void SellingABurningBridgeStopsTheClock()
    {
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);

        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));
        Assert.True(run.World.Tile(span).BurnTicksRemaining > 0);

        run.Tick(SellRoomCommand.At(Keeper, span));

        Assert.Equal(0, run.World.Tile(span).BurnTicksRemaining);
        Assert.Equal(Dk2TerrainFixture.Lava, run.World.Tile(span).TerrainId);
    }

    /// <summary>
    /// The rule reads a variable that names the wooden bridge, so a stone bridge
    /// over the same lava is permanent. That is the one place the bridge rules
    /// name a room type, and DK2's own variable name is why.
    /// </summary>
    [Fact]
    public void OnlyTheWoodenBridgeBurns()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        TerrainDef lava = database.TerrainById(Dk2TerrainFixture.Lava);

        Assert.True(
            BridgeRules.LifetimeTicks(database, database.Room(RoomTypeId.WoodenBridge), lava) > 0);
        Assert.Equal(
            0, BridgeRules.LifetimeTicks(database, database.Room(RoomTypeId.DungeonHeart), lava));

        // And nothing burns over water, whichever bridge it is.
        TerrainDef water = database.TerrainById(Dk2TerrainFixture.Water);
        Assert.Equal(
            0, BridgeRules.LifetimeTicks(database, database.Room(RoomTypeId.WoodenBridge), water));
    }

    /// <summary>
    /// A creature caught on a burning bridge is <b>unharmed here and not
    /// stranded</b>. Unharmed is a known gap — creature health does not exist, so
    /// the burning the owner described cannot be modelled (Q67). Not stranded is
    /// the part that matters for correctness: the pathfinder enters a mover's
    /// starting tile without testing walkability, so a creature standing in lava
    /// can still walk out.
    /// </summary>
    [Fact]
    public void ACreatureOnABurntBridgeCanStillLeaveTheTileItIsOn()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        // Somewhere to escape TO. The fixture's rock body surrounds (6,6), so
        // without this the imp is encased and the test would be asserting that
        // rock is impassable rather than anything about lava.
        ref TileState escape = ref run.World.Tile(6, 7);
        escape.TerrainId = Dk2TerrainFixture.DirtPath;
        escape.Health = database.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;

        // Put an imp on the bridge and let it burn out from under it.
        run.World.SpawnCreature(run.Sim.Database, Dk2TerrainFixture.ImpCreatureId, Keeper, span);
        int life = SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.WoodBridgeLifeOnLavaSeconds]);
        for (int i = 0; i < life + 2; i++) run.Tick();

        Assert.Equal(Dk2TerrainFixture.Lava, run.World.Tile(span).TerrainId);

        // The imp is standing on lava it could never have walked onto, and a
        // route out of it is still findable.
        CreatureState imp = run.World.Creatures[0];
        var grid = new WorldPathGrid(database, run.World, database.CreatureById(imp.CreatureTypeId));
        Assert.False(grid.IsWalkable(span.X, span.Y));

        var route = new List<TilePos>();
        Assert.True(new Pathfinding.GridPathfinder().TryFindNearest(
            grid, span, at => grid.IsWalkable(at.X, at.Y), route));
    }

    // ---- a rival taking a bridge (Q68) -----------------------------------

    /// <summary>A rival keeper's imps take a bridge by jumping on it and claiming
    /// it, if it is adjacent to their keeper's territory (CONFIRMED, owner
    /// 2026-09-03, Q68). Both halves are rules this project already had from other
    /// evidence: jumping on it is <see cref="WorkStance"/>'s on-the-tile stance,
    /// which follows from "standable while working and standable when the job
    /// finishes" (ADR-0028), and adjacency is the ownership condition on the
    /// claimed tile's neighbour (Q42, ADR-0028). The claim itself is
    /// <see cref="ARivalClaimsABridgeDirectlyAndItStaysABridge"/>.</summary>
    [Fact]
    public void TheStanceRuleAlreadySaysABridgeIsClaimedByStandingOnIt()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Water, span);
        GiveHeart(run, new TilePos(1, 1), 1000);
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        var grid = new WorldPathGrid(
            database, run.World, database.CreatureById(Dk2TerrainFixture.ImpCreatureId));

        Assert.True(WorkStance.IsOnTheTile(
            database, run.World, grid, span, TileWorkKind.ConvertRoom));
    }

    /// <summary>A rival claims a bridge and it stays a bridge, with no neutral
    /// step — the tile is claimed directly. CONFIRMED (owner 2026-09-03, Q68).
    /// The destination is the subtlety: claiming normally converts a tile to its
    /// <c>MaxHealthTypeTerrainId</c>, which for a bridge is the bridge itself but
    /// for a rival's Claimed Path is Rock, the fill-in destination (Q18), so
    /// following it would turn their floor into a wall. Hence
    /// <c>UpwardDestination</c>: claiming ground that is already the claimable
    /// kind changes the owner and leaves the terrain alone.</summary>
    [Fact]
    public void ARivalClaimsABridgeDirectlyAndItStaysABridge()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Water, span);
        GiveHeart(run, new TilePos(1, 1), 1000);
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        Assert.True(TileWorkRules.Applies(
            database, run.World.Tile(span), TileWorkKind.ConvertRoom, playerId: Rival));

        ref TileState tile = ref run.World.Tile(span);
        int index = (span.Y * run.World.Width) + span.X;
        for (int i = 0; i < 5000 && tile.OwnerPlayerId != Rival; i++)
            RoomHealthRules.Convert(run.World, database, index, Rival);

        Assert.Equal(Rival, tile.OwnerPlayerId);
        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, tile.TerrainId);

        // And the hazard marker survives, so the rival's bridge still knows what
        // it is standing over.
        Assert.Equal(BridgeRules.SpansWater, tile.Flags);
    }

    /// <summary>
    /// A rival claiming a <b>burning</b> wooden bridge takes the fire with it.
    /// Putting the fire out by taking the tile would be a reward for taking it,
    /// and the terrain never changed, so nothing has restarted (Q67, Q68).
    /// </summary>
    [Fact]
    public void ClaimingABurningBridgeDoesNotPutTheFireOut()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        ref TileState tile = ref run.World.Tile(span);
        int burningBefore = tile.BurnTicksRemaining;
        Assert.True(burningBefore > 0);

        int index = (span.Y * run.World.Width) + span.X;
        for (int i = 0; i < 5000 && tile.OwnerPlayerId != Rival; i++)
            RoomHealthRules.Convert(run.World, database, index, Rival);

        Assert.Equal(Rival, tile.OwnerPlayerId);
        Assert.Equal(burningBefore, tile.BurnTicksRemaining);
    }
}

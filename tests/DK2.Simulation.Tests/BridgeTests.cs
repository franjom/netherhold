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
    private const byte Keeper = 1;
    private const byte Rival = 2;

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
    /// <b>Whatever removes a bridge tile leaves the hazard</b> — asserted through
    /// the rules rather than through a mechanic, because the original has no
    /// mechanic that does this.
    ///
    /// <para>The owner (2026-09-03): a bridge can only be "attacked" by a rival
    /// keeper's imps, who <i>claim</i> it rather than beat it down. So this is not
    /// a test that DK2 destroys bridges by damage — it does not (Q68). It is a
    /// test that the destroyed path and the selling path cannot disagree, since
    /// both used to consult the destroyed-type field separately and both were
    /// wrong the same way. `AttackTile` is the only work kind that will currently
    /// take an enemy bridge tile to zero, and nothing in the simulation ever
    /// chooses it; the injection here stands in for any future path that
    /// might.</para>
    /// </summary>
    [Fact]
    public void AnythingThatRemovesABridgeTileLeavesTheHazard()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Lava, span);
        GiveHeart(run, new TilePos(1, 1), 1000);
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        // Driven straight through the rules. Nothing in the simulation chooses
        // AttackTile, and in the original nothing attacks a bridge at all.
        ref TileState tile = ref run.World.Tile(span);
        for (int i = 0; i < 5000 && tile.TerrainId == Dk2TerrainFixture.WoodenBridgeTerrain; i++)
            TileWorkRules.Apply(database, ref tile, TileWorkKind.AttackTile, playerId: 2);

        Assert.Equal(Dk2TerrainFixture.Lava, tile.TerrainId);
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
        run.World.SpawnCreature(Dk2TerrainFixture.ImpCreatureId, Keeper, span);
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

    /// <summary>
    /// <b>The derived stance rule already predicts what the owner described, for
    /// a case it was never designed for.</b>
    ///
    /// <para>The owner (2026-09-03): a rival keeper's imps take a bridge by
    /// <i>jumping on it and claiming it, if it is adjacent to their keeper's
    /// territory</i>. Both halves of that sentence are rules this project already
    /// has, arrived at from other evidence entirely:</para>
    /// <list type="bullet">
    /// <item><b>jumping on it</b> is <see cref="WorkStance"/>'s on-the-tile
    /// stance, which falls out of "standable while working and standable when the
    /// job finishes" — a bridge is walkable and stays a bridge, so the rule says
    /// stand on it without anyone having considered bridges (ADR-0028);</item>
    /// <item><b>adjacent to their territory</b> is the ownership condition on the
    /// claimed tile's neighbour (Q42, ADR-0028).</item>
    /// </list>
    ///
    /// <para>Two derived rules predicting an observation neither was built from is
    /// the strongest evidence either has had. The gap is elsewhere — see
    /// <see cref="ARivalCannotYetClaimABridgeBecauseClaimingRefusesOwnedGround"/>.</para>
    /// </summary>
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
            database, run.World, grid, span, TileWorkKind.Claim));
    }

    /// <summary>
    /// <b>A rival claims a bridge, and it stays a bridge.</b> CONFIRMED (Q68,
    /// owner 2026-09-03): there is no neutral step — the tile is claimed
    /// directly.
    ///
    /// <para>The subtlety is the destination. Claiming normally converts a tile
    /// to its <c>MaxHealthTypeTerrainId</c>, and for a bridge that is the bridge
    /// itself, so it happens to work. For a rival's <i>Claimed Path</i> the same
    /// field says <b>Rock</b> — it is the fill-in destination (Q18) — so
    /// following it would turn their floor into a wall. Hence
    /// <c>UpwardDestination</c>: claiming ground that is already the claimable
    /// kind changes the owner and leaves the terrain alone.</para>
    /// </summary>
    [Fact]
    public void ARivalClaimsABridgeDirectlyAndItStaysABridge()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        var span = new TilePos(6, 6);
        Run run = Start(Dk2TerrainFixture.Water, span);
        GiveHeart(run, new TilePos(1, 1), 1000);
        run.Tick(BuildRoomCommand.At(Keeper, BridgeId, span));

        Assert.True(TileWorkRules.Applies(
            database, run.World.Tile(span), TileWorkKind.Claim, playerId: Rival));

        ref TileState tile = ref run.World.Tile(span);
        for (int i = 0; i < 5000 && tile.OwnerPlayerId != Rival; i++)
            TileWorkRules.Apply(database, ref tile, TileWorkKind.Claim, Rival);

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

        for (int i = 0; i < 5000 && tile.OwnerPlayerId != Rival; i++)
            TileWorkRules.Apply(database, ref tile, TileWorkKind.Claim, Rival);

        Assert.Equal(Rival, tile.OwnerPlayerId);
        Assert.Equal(burningBefore, tile.BurnTicksRemaining);
    }
}

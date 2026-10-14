// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// Mana: the income a keeper gets for holding land.
///
/// The fixture map is a 3×3 block of claimed path at (1..3, 1..3) owned by
/// player 1, surrounded by rock — see <see cref="Dk2TerrainFixture"/>. Claimed
/// path is worth one mana a second in DK2, so the keeper's whole income on this
/// map is nine, and every test below is some way of moving that number.
/// </summary>
public class ManaTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;
    private const int ClaimedTilesOnTheFixtureMap = 9;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;

        public int Mana(byte player) => World.Player(player).Mana;

        public void Seconds(int count) =>
            Sim.Run(count * SimulationTiming.TicksPerSecond);

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);
    }

    private static Run Start(ulong seed = 7)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

        // Without the imp supply, deliberately: these tests measure what land and
        // a dungeon heart pay a keeper, to the mana. The free-imp supply keeps a
        // deadline rather than a countdown, so handing a keeper a heart hands them
        // an imp on the same tick and the imp's upkeep comes off the figure being
        // asserted. ManaUpkeepTests is where imps and mana meet.
        var sim = new GameSimulation(
            database, world, seed,
            Dk2TerrainFixture.SystemsWithout<Systems.ImpSupplySystem>());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    // ---- income comes from land ----------------------------------------

    [Fact]
    public void ClaimedLandPaysItsKeeper()
    {
        Run run = Start();
        run.Seconds(1);

        Assert.Equal(ClaimedTilesOnTheFixtureMap, run.Mana(Keeper));
    }

    [Fact]
    public void ItPaysEverySecond_NotOnce()
    {
        Run run = Start();
        run.Seconds(10);

        Assert.Equal(10 * ClaimedTilesOnTheFixtureMap, run.Mana(Keeper));
    }

    /// <summary>
    /// A level does not start by handing out a second of mana that has not
    /// elapsed. The first payment lands on the last tick of the first second.
    /// </summary>
    [Fact]
    public void TheFirstPaymentIsAtTheEndOfTheFirstSecond()
    {
        Run run = Start();

        run.Sim.Run(SimulationTiming.TicksPerSecond - 1);
        Assert.Equal(0, run.Mana(Keeper));

        run.Sim.Run(1);
        Assert.Equal(ClaimedTilesOnTheFixtureMap, run.Mana(Keeper));
    }

    /// <summary>
    /// Rock, dirt and the map edge are worth nothing — and neither is a keeper
    /// who holds none of the claimed block. Player 0 nominally owns every
    /// unclaimed tile on this map and still earns nothing from them.
    /// </summary>
    [Fact]
    public void UnclaimedGroundPaysNobody()
    {
        Run run = Start();
        run.Seconds(5);

        Assert.Equal(0, run.Mana(0));
        Assert.Equal(0, run.Mana(Rival));
    }

    [Fact]
    public void LosingLandLosesTheIncomeWithIt()
    {
        Run run = Start();
        run.Seconds(1);
        Assert.Equal(9, run.Mana(Keeper));

        // The rival takes one tile of the block.
        run.World.Tile(1, 1).OwnerPlayerId = Rival;

        run.Seconds(1);
        Assert.Equal(9 + 8, run.Mana(Keeper));
        Assert.Equal(1, run.Mana(Rival));
    }

    /// <summary>
    /// Claiming ground is the only thing in the simulation today that raises a
    /// keeper's income, and it does it through the tile rules rather than
    /// through anything mana knows about.
    /// </summary>
    [Fact]
    public void ClaimingGroundRaisesTheIncome()
    {
        Run run = Start();

        // A patch of unclaimed dirt inside the block's reach.
        run.World.Tile(4, 1).TerrainId = Dk2TerrainFixture.DirtPath;
        run.World.Tile(4, 1).Health = 1;
        run.World.Tile(4, 1).OwnerPlayerId = 0;

        run.Seconds(1);
        Assert.Equal(9, run.Mana(Keeper));

        // Five applications to claim one tile (ADR-0133), queued together so the
        // claim completes inside the same game second the mana is paid for.
        for (int i = 0; i < 5; i++) run.World.QueueWork(4, 1, TileWorkKind.Claim, Keeper);
        run.Seconds(1);

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 1).TerrainId);
        Assert.Equal(9 + 10, run.Mana(Keeper));
    }

    /// <summary>
    /// <b>Building on your own floor does not cost you income.</b> Room terrain has a
    /// mana gain of zero in every one of the game's 26 rooms, but DK2 keeps the rate as a
    /// running word: <c>Tile_SetTerrain</c>'s adjustment (<c>0x00455B90</c>) is skipped
    /// whenever the old or the new terrain is a Room, so the claimed floor's 1 stays on the
    /// books under the treasury (ADR-0334; the Level 3 capture's rate word counts every room
    /// tile the keeper built). A room tile owned from the start pays nothing.
    /// </summary>
    [Fact]
    public void ARoomBuiltOnClaimedFloorKeepsTheFloorsIncome_AndOneOwnedFromTheStartPaysNothing()
    {
        Run run = Start();
        GameDatabase database = Dk2TerrainFixture.Database();
        RoomDef treasury = database.Room(RoomTypeId.Treasury);

        RoomRules.Place(database, ref run.World.Tile(1, 1), treasury, Keeper);
        RoomRules.Place(database, ref run.World.Tile(2, 1), treasury, Keeper);
        // Laid in the map rather than built: no floor was ever paid for under it.
        run.World.Tile(5, 1).TerrainId = treasury.TerrainId;
        run.World.Tile(5, 1).OwnerPlayerId = Keeper;

        run.Seconds(1);
        Assert.Equal(ClaimedTilesOnTheFixtureMap, run.Mana(Keeper));
    }

    /// <summary>A rival who takes a room tile gets the room terrain's 0 (<c>Tile_SetOwner</c>
    /// moves the current terrain's gain, <c>0x004559D0</c>). Ours drops the kept floor gain on
    /// the owner change; DK2's old owner keeps paying for it, which ours does not mirror (Q521).</summary>
    [Fact]
    public void ARoomTileTakenByARivalPaysTheRivalNothing()
    {
        Run run = Start();
        GameDatabase database = Dk2TerrainFixture.Database();
        RoomRules.Place(database, ref run.World.Tile(1, 1), database.Room(RoomTypeId.Treasury), Keeper);
        Assert.Equal(1, ManaRules.GainPerSecond(database, run.World.Tile(1, 1)));

        // The rival's imps convert the tile: applications until the takeover completes.
        for (int i = 0; i < 5000 && run.World.Tile(1, 1).OwnerPlayerId == Keeper; i++)
            RoomHealthRules.Convert(run.World, database, new TilePos(1, 1), Rival);
        Assert.Equal(Rival, run.World.Tile(1, 1).OwnerPlayerId);
        Assert.Equal(0, ManaRules.GainPerSecond(database, run.World.Tile(1, 1)));
    }

    /// <summary>
    /// A claimed vault is worth a hundred times a claimed path. It is the only
    /// terrain in the game for which that is true, and it is why the mana rules
    /// read the terrain's own number rather than counting tiles.
    /// </summary>
    [Fact]
    public void AClaimedVaultIsWorthAHundredClaimedPaths()
    {
        Run run = Start();
        run.World.Tile(5, 1).TerrainId = Dk2TerrainFixture.ClaimedVaultTerrain;
        run.World.Tile(5, 1).OwnerPlayerId = Keeper;

        run.Seconds(1);
        Assert.Equal(9 + 100, run.Mana(Keeper));
    }

    /// <summary>An unclaimed vault is worth nothing, which is what claiming one is for.</summary>
    [Fact]
    public void AnUnclaimedVaultIsWorthNothing()
    {
        Run run = Start();
        run.World.Tile(5, 1).TerrainId = Dk2TerrainFixture.ManaVaultTerrain;
        run.World.Tile(5, 1).OwnerPlayerId = Keeper;

        run.Seconds(1);
        Assert.Equal(9, run.Mana(Keeper));
    }

    // ---- the dungeon heart ---------------------------------------------

    /// <summary>
    /// A keeper's heart pays a flat 30 a second on top of their land, which is
    /// why a keeper with a heart and nothing else still has an income.
    /// </summary>
    [Fact]
    public void ADungeonHeartPaysOnTopOfTheLand()
    {
        Run run = Start();

        // Off the claimed block, so the land's nine is untouched — putting the
        // heart *on* claimed floor costs a tile of income, which is
        // <see cref="HeartTerrainItselfPaysNothing"/>'s business.
        GiveHeart(run, Keeper, new TilePos(5, 1));

        run.Seconds(1);
        Assert.Equal(9 + 30, run.Mana(Keeper));
    }

    /// <summary>
    /// <b>Once per keeper, not once per tile.</b> A dungeon heart is 5×5 in the
    /// shipped data, and a keeper has at most one of them ever — losing it is
    /// what defeat is. So 25 heart tiles are worth 30 a second between them, and
    /// counting tiles would have been wrong by a factor of 25.
    /// </summary>
    [Fact]
    public void AWholeHeartIsWorthOneHeart_NotOnePerTile()
    {
        Run run = Start();
        GiveHeart(run, Keeper, new TilePos(5, 1), new TilePos(6, 1), new TilePos(7, 1));

        run.Seconds(1);
        Assert.Equal(9 + 30, run.Mana(Keeper));
    }

    [Fact]
    public void AKeeperWithNoHeartGetsNothingFromOne()
    {
        Run run = Start();
        GiveHeart(run, Rival, new TilePos(5, 1));

        run.Seconds(1);
        Assert.Equal(9, run.Mana(Keeper));
        Assert.Equal(30, run.Mana(Rival));
    }

    /// <summary>
    /// Heart terrain generates no mana of its own — the 30 is the keeper's, not
    /// the tile's — and a heart is laid by the map, never built over paid floor,
    /// so its tiles keep nothing (ADR-0334).
    /// </summary>
    [Fact]
    public void HeartTerrainItselfPaysNothing()
    {
        Run run = Start();
        GiveHeart(run, Keeper, new TilePos(1, 1), new TilePos(2, 1));

        run.Seconds(1);
        Assert.Equal(7 + 30, run.Mana(Keeper));
    }

    /// <summary>Somewhere for the heart's 30 a second to come from.</summary>
    private static void GiveHeart(Run run, byte player, params TilePos[] tiles)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        RoomDef heart = database.Room(RoomTypeId.DungeonHeart);
        foreach (TilePos p in tiles)
        {
            RoomRules.Place(database, ref run.World.Tile(p), heart, player);
            // A heart is laid in the map, never built over paid floor: nothing kept (ADR-0334).
            run.World.Tile(p).KeptManaGain = 0;
        }
    }

    // ---- the two limits ------------------------------------------------

    [Fact]
    public void IncomeIsCappedAtMaximumManaGainPerSecond()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        int cap = database[VariableTypeId.MaximumManaGainPerSecond];

        // More claimed path than the cap pays for: 576 tiles at 1 against 500.
        WorldState world = AllOneTerrain(database, 24, 24, Dk2TerrainFixture.ClaimedPath, Keeper);

        int[] gain = ManaRules.GainPerSecondByPlayerIndex(world, database);
        Assert.Equal(cap, gain[0]);
        Assert.True(576 > cap, "the fixture must actually exceed the cap");

        // A Claimed Vault carries MaxManaGain 100 beside its 100 of gain, and Tile_SetOwner
        // (0x004559D0) adds it to the owner's cap word (+0x10DE) as it adds the gain to the rate
        // word: 144 vaults raise the cap to 500 + 14400, so their whole 14400 is paid (ADR-0334).
        WorldState vaults = AllOneTerrain(database, 12, 12, Dk2TerrainFixture.ClaimedVaultTerrain, Keeper);
        Assert.Equal(144 * 100, ManaRules.GainPerSecondByPlayerIndex(vaults, database)[0]);
    }

    [Fact]
    public void ManaStopsAtTheThreshold()
    {
        Run run = Start();
        GameDatabase database = Dk2TerrainFixture.Database();
        int threshold = database[VariableTypeId.MaximumManaThreshold];

        run.World.Player(Keeper).Mana = threshold - 4;
        run.Seconds(1);

        Assert.Equal(threshold, run.Mana(Keeper));
    }

    /// <summary>
    /// The ceiling is a clamp (Q545): a second whose net is not negative cuts mana above it back
    /// in one step (<c>0x004C10E0</c>, <c>0x004C11DD</c>–<c>0x004C11E7</c>). Ours drained the
    /// excess at <c>ExcessManaDecreaseRatePerSecond</c>, 5 a second, which the binary never reads.
    /// </summary>
    [Fact]
    public void ManaAboveTheCeiling_IsCutBackInOneStep_ByASecondThatEarns()
    {
        Run run = Start();
        int threshold = Dk2TerrainFixture.Database()[VariableTypeId.MaximumManaThreshold];

        run.World.Player(Keeper).Mana = threshold + 50000;
        run.Seconds(1);

        Assert.Equal(threshold, run.Mana(Keeper));
    }

    /// <summary>A negative net only floors mana at zero, so above the ceiling it falls by the
    /// upkeep alone and stays above (<c>0x004C11CB</c>–<c>0x004C11D1</c>).</summary>
    [Fact]
    public void ManaAboveTheCeiling_FallsOnlyByTheNet_OnASecondThatCosts()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        int threshold = database[VariableTypeId.MaximumManaThreshold];
        var player = new PlayerState { PlayerId = Keeper, Mana = threshold + 1000 };

        ManaRules.ApplyOneSecond(database, ref player, gainPerSecond: 10, upkeepPerSecond: 38);

        Assert.Equal(threshold + 972, player.Mana);
    }

    // ---- reporting and persistence -------------------------------------

    [Fact]
    public void EveryPaymentIsReported()
    {
        Run run = Start();
        run.Seconds(3);

        List<SimEvent> gained = [.. run.Of("mana_gained")];
        Assert.Equal(3, gained.Count);
        Assert.All(gained, e => Assert.Contains($"player={Keeper} amount=9 rate=9/s", e.Detail));
    }

    /// <summary>A keeper earning nothing does not fill the log with saying so.</summary>
    [Fact]
    public void APlayerWithNoLandIsNotReported()
    {
        Run run = Start();
        run.Seconds(3);

        Assert.All(run.Of("mana_gained"), e => Assert.Contains($"player={Keeper}", e.Detail));
    }

    [Fact]
    public void ManaSurvivesASnapshot()
    {
        Run run = Start();
        run.Seconds(4);
        int before = run.Mana(Keeper);

        byte[] saved = WorldSnapshot.ToBytes(run.Sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            saved, Dk2TerrainFixture.Database(), Dk2TerrainFixture.DefaultSystems());

        Assert.Equal(before, restored.World.Player(Keeper).Mana);

        // And it keeps earning at the same rate on the other side of the save.
        restored.Run(2 * SimulationTiming.TicksPerSecond);
        run.Seconds(2);

        Assert.Equal(run.Mana(Keeper), restored.World.Player(Keeper).Mana);
    }

    /// <summary>
    /// A map of one terrain, wholly owned. Built by hand rather than through
    /// <see cref="Dk2TerrainFixture.Map"/> because the only thing being measured
    /// is a total large enough to hit a cap.
    /// </summary>
    private static WorldState AllOneTerrain(
        GameDatabase database, int width, int height, byte terrainId, byte owner)
    {
        var tiles = new MapTile[width * height];
        for (int i = 0; i < tiles.Length; i++) tiles[i] = new MapTile(terrainId, owner, 0, 0);
        return WorldState.FromMap(database, new LevelMap(width, height, tiles), [owner]);
    }
}

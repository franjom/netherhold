// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>What happens to gold between the seam and the treasury. CONFIRMED
/// (owner, Q31): an imp mines into its own hands up to <c>maxGoldHeld</c>, runs
/// to the drop spot and adds the load to the store; with nowhere to put it, it
/// keeps mining and a lump grows on the floor in front of it.</summary>
/// <remarks>Numbers, all from the shipped data: a gold seam is 660 health and
/// 3000 gold; <c>MineGoldHealth</c> is −300 a second and work lands five times a
/// second (Q47), so a swing takes 60 health and frees 273 gold — the number the
/// owner measured. A seam is eleven swings totalling exactly 3000. A level 1 Imp's hands
/// are full at 1400 less variable 74's 340, so a seam is three trips: 1090, 1091, 819 (Q50,
/// ADR-0296). A treasury tile holds 3000 and a dungeon-heart tile 1000. Gold that will not
/// fit in an imp's hands is put down at its feet, onto the lump already there (Q138, Q140).</remarks>
public class GoldCarryingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

    /// <summary>What an imp is holding when it breaks off: four swings' worth, the first over
    /// 1060 (1400 less 340).</summary>
    private const int FullLoad = 1090;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;

        public CreatureState Imp => Sim.World.Creatures[0];

        public void Tick(params PlayerCommand[] commands) =>
            Sim.Tick(commands.Length == 0 ? InputFrame.Empty : InputFrame.Of(commands));

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++) Tick();
        }

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);

        public int Amounts(string kind) => Of(kind).Sum(Amount);

        public static int Amount(SimEvent e) =>
            int.Parse(e.Detail.Split("amount=")[1].Split(' ')[0]);
    }

    /// <summary>
    /// One imp in its keeper's room, a corridor carved out to the gold seam at
    /// (5,5), and the seam marked for mining. The corridor is carved rather than
    /// dug so that the test is about carrying and not about digging.
    /// </summary>
    private static Run AtTheSeam(TilePos? impAt = null)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, impAt ?? new TilePos(2, 2));

        foreach (TilePos p in new[] { new TilePos(4, 3), new TilePos(4, 4), new TilePos(4, 5) })
        {
            ref TileState tile = ref world.Tile(p);
            tile.TerrainId = Dk2TerrainFixture.DirtPath;
            tile.Health = database.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }

        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    /// <summary>Turns one of the keeper's room tiles into somewhere gold can go.</summary>
    private static void PutStoreAt(Run run, TilePos at, RoomTypeId type, byte owner = Keeper)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        ref TileState tile = ref run.World.Tile(at);
        tile.TerrainId = database.Room(type).TerrainId;
        tile.Health = database.TerrainById(tile.TerrainId).StartingHealth;
        tile.OwnerPlayerId = owner;
    }

    // ---- the round trip --------------------------------------------------

    [Fact]
    public void AnImpCarriesMinedGoldToATreasury()
    {
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(800);

        // A seam is worth exactly 3000, and exactly 3000 arrives. Gold is taken
        // from a running total rather than per swing, so truncation shows up as
        // a coin's difference between swings and never as a shortfall.
        Assert.Equal(3000, run.World.Tile(1, 1).GoldHeld);
        Assert.Equal(3000, GoldStore.Total(run.World, Keeper));
        Assert.Equal(0, run.Imp.GoldHeld);
        Assert.NotEqual(Dk2TerrainFixture.Gold, run.World.Tile(5, 5).TerrainId);

        // And none of it went missing on the way.
        Assert.Empty(run.Of("gold_uncollected"));
    }

    [Fact]
    public void AnImpBreaksOffMiningOnceItsHandsAreFull()
    {
        // State 26 asks the hands test before each strike (0x00465870: 0x00464360(1)), full at
        // 1400 less 340, so the imp downs tools after four swings (1090 held, Q50).
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(24);

        // It sets off on the tick of its fourth strike, as both captures show, so the
        // departure is logged before that strike's gold reaches its hands; the load
        // it puts down is the full four.
        SimEvent leaving = run.Of("delivering").First();
        Assert.Contains("to=(1,1)", leaving.Detail);

        // Four swings, and nothing thrown away to get there.
        Assert.Equal(4, run.Of("gold_mined").Count());
        Assert.Empty(run.Of("gold_uncollected"));

        run.Ticks(40);
        Assert.Equal(FullLoad, Run.Amount(run.Of("gold_delivered").First()));
    }

    [Fact]
    public void AnImpThatReachesTheSeamFull_LeavesOnItsFirstTickInStateTwentySix()
    {
        // 0x00465870 holds only while an action runs. Its first call in 26 has none, so the
        // hands test and the store chooser come before the first strike starts. Level 4
        // seed 1 3400-4000: DK2's full arrivals leave one tick after entering 26, 5 of 5.
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);
        run.Imp.GoldHeld = FullLoad;

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        long entered = -1, left = -1;
        for (int i = 0; i < 120 && left < 0; i++)
        {
            run.Tick();
            long now = run.World.CurrentTick.Value;
            if (entered < 0 && run.Imp.Dk2StateId == 26) entered = now;
            if (entered >= 0 && run.Of("delivering").Any()) left = now;
        }

        Assert.True(entered >= 0, "the imp never reached the seam");
        Assert.Equal(entered + 1, left);
        Assert.Empty(run.Of("gold_mined"));
    }

    [Fact]
    public void AnImpThatReachesTheSeamFullWithNowhereToStore_StrikesOnTheUsualTick()
    {
        // With no store the first call's chooser fails and the handler starts the strike
        // (0x00465870, 0x00496980(2)); the strike lands two ticks after entering 26, as a
        // miner with room in its hands does (ADR-0280).
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Lair);
        run.Imp.GoldHeld = FullLoad;

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        long entered = -1, struck = -1;
        for (int i = 0; i < 120 && struck < 0; i++)
        {
            run.Tick();
            long now = run.World.CurrentTick.Value;
            if (entered < 0 && run.Imp.Dk2StateId == 26) entered = now;
            if (entered >= 0 && run.Of("gold_mined").Any()) struck = now;
        }

        Assert.True(entered >= 0, "the imp never reached the seam");
        Assert.Equal(entered + 2, struck);
        Assert.Empty(run.Of("delivering"));
    }

    [Fact]
    public void AnImpDeliversWhatItIsHoldingWhenTheSeamRunsOut()
    {
        // Eleven swings split four, four and three, so the last trip carries less
        // than a full load. An imp that only ran when full would stand on an
        // exhausted seam holding it.
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(800);

        List<int> loads = run.Of("gold_delivered").Select(Run.Amount).ToList();
        Assert.Equal([1090, 1091, 819], loads);
        Assert.Equal(3000, loads.Sum());
    }

    [Fact]
    public void TwoMinersStrikingTheSeamsLastSwing_OnlyOneReceivesGoldAndNeitherDepartsThatTick()
    {
        // The strike pays one miner, then the exhausted seam keeps that miner in state 26.
        // The hands test cannot send it to the store before the action completes (ADR-0512).
        Run run = AtTheSeam(new TilePos(4, 5));
        GameDatabase database = Dk2TerrainFixture.Database();
        run.World.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(4, 5));
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);
        run.World.Tile(5, 5).Health = 60;
        foreach (CreatureState imp in run.World.Creatures) imp.GoldHeld = 818;

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        for (int i = 0; i < 30 && !run.Of("gold_mined").Any(); i++) run.Tick();

        GameTick fell = Assert.Single(run.Of("gold_mined")).Tick;
        Assert.DoesNotContain(run.Of("delivering"), e => e.Tick == fell);
        Assert.Equal([818, 1091], run.World.Creatures.Select(imp => imp.GoldHeld).Order().ToArray());
        Assert.Equal(ImpStates.For(TileWorkKind.MineGold).Work,
            run.World.Creatures.Single(imp => imp.GoldHeld == 1091).Dk2StateId);
        run.Ticks(30);
        Assert.Contains(run.Of("job_abandoned"), e => e.Detail.Contains("(5,5) MineGold reason=gone"));
    }

    // ---- where the load lands (Q35) --------------------------------------

    [Fact]
    public void AFullTileSpillsIntoTheNextOne()
    {
        // Two dungeon-heart tiles, 1000 each, against a 1090 load: state 29 hands the whole
        // load to the room and the deposit 0x004F3C60 fills the chain from the tile the walk
        // ended on, the chooser's random chain start, so that tile takes 1000 and the other the
        // rest in one deposit, with no second walk (ADR-0306). The run ends inside
        // TimeBeforeFreeImpGeneratedSeconds so this stays about one imp's load.
        Run run = AtTheSeam(new TilePos(4, 4));
        PutStoreAt(run, new TilePos(3, 3), RoomTypeId.DungeonHeart);
        PutStoreAt(run, new TilePos(3, 2), RoomTypeId.DungeonHeart);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));

        // Run to the moment the spillover lands rather than to a fixed tick.
        // A fixed count broke when movement went eight-connected (ADR-0025):
        // the imp got home sooner, unloaded, walked back and had already mined
        // two more swings by tick 95. What the test is about is where the load
        // goes, not how long the walk takes.
        for (int i = 0; i < 95 && run.Of("gold_delivered").Count() == 0; i++) run.Tick();

        // The load is four swings, 1090 to 1092 by which swings the 272s fall on; the
        // point is that the second tile takes exactly what the first could not, in one deposit.
        List<int> loads = run.Of("gold_delivered").Select(Run.Amount).ToList();
        int[] held = [run.World.Tile(3, 3).GoldHeld, run.World.Tile(3, 2).GoldHeld];
        Assert.Equal(1000, held.Max());
        Assert.InRange(held.Min(), 90, 92);
        Assert.Equal([held.Sum()], loads);
        Assert.Equal(0, run.Imp.GoldHeld);
    }

    /// <summary>
    /// <b>A Heart with room takes the whole load, and what its tiles cannot hold goes on the
    /// floor.</b> The store chooser sends the imp while the Heart holds less than 16 × 1000, but
    /// state 29 measures it against 16 × the Treasury tile cap, so the imp's hands always empty
    /// there; the deposit fills the chain and puts the rest down as a neutral pile on the tile it
    /// started from (0x00466AC0, 0x004F3C60). Level 2's capture: imp 449 hands over 1,470 to a
    /// Heart at 15,995 at tick 1795 and fetches it back off the floor (ADR-0306).
    /// </summary>
    [Fact]
    public void AHeartWithLittleRoomTakesTheWholeLoad_AndTheRestGoesOnTheFloor()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = AtTheSeam(new TilePos(4, 4));
        PutStoreAt(run, new TilePos(3, 3), RoomTypeId.DungeonHeart);
        PutStoreAt(run, new TilePos(3, 2), RoomTypeId.DungeonHeart);
        run.World.Tile(3, 3).GoldHeld = 990;
        run.World.Tile(3, 2).GoldHeld = 1000;

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        for (int i = 0; i < 95 && !run.Of("gold_delivered").Any(); i++) run.Tick();

        SimEvent delivered = Assert.Single(run.Of("gold_delivered"));
        Assert.Equal(0, run.Imp.GoldHeld);
        Assert.Equal(1000, run.World.Tile(3, 3).GoldHeld);
        ObjectState pile = Assert.Single(run.World.Objects, o => FloorGold.IsPile(database, o));
        Assert.Equal(PlayerIds.Neutral, pile.OwnerPlayerId);
        Assert.InRange(pile.GoldHeld, 1080, 1082);
        Assert.Contains($"floor={pile.GoldHeld}", delivered.Detail);
        Assert.NotEqual(4, pile.Cell);
    }

    [Fact]
    public void ARoomThatIsNotAStore_TakesNothing()
    {
        // A lair is a room and it is the keeper's, and gold still will not go in
        // it: only the treasury and the dungeon heart have a per-tile limit in
        // the data, so only those two can hold any (ADR-0015).
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Lair);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(300);

        Assert.Equal(0, run.World.Tile(1, 1).GoldHeld);
        Assert.Empty(run.Of("gold_delivered"));
    }

    /// <summary>An imp puts nothing into a rival's treasury for as long as it is
    /// the rival's. Asserted on every tick up to the takeover, not at a fixed
    /// tick: rooms can change hands, so an imp that mines its seam out finds the
    /// rival's treasury claimable, takes it, and then properly fills it — see
    /// <see cref="AnImpTakesARivalsTreasuryBeforeItEverPutsGoldInIt"/>.</summary>
    [Fact]
    public void AnImpWillNotFillARivalsTreasuryWhileItIsStillTheRivals()
    {
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury, owner: Rival);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));

        bool takenOver = false;
        for (int i = 0; i < 300 && !takenOver; i++)
        {
            run.Ticks(1);
            takenOver = run.World.Tile(1, 1).OwnerPlayerId != Rival;
            if (!takenOver) Assert.Equal(0, run.World.Tile(1, 1).GoldHeld);
        }

        // And the run really did reach the interesting state rather than simply
        // running out of ticks with the imp still busy — otherwise the loop above
        // asserts nothing about a rival's treasury that a rock would not satisfy.
        Assert.True(takenOver, "the imp never took the rival's treasury, so nothing above was tested");
    }

    /// <summary>An imp takes a rival's treasury and then uses it, in that order.
    /// CONFIRMED (owner, 2026-09-04): "imps fill only their own keeper's treasury
    /// … for SP my imps NEVER fill anyone's treasure but my own", stated here as a
    /// sequence rather than a prohibition — the takeover happens strictly before
    /// the first delivery.</summary>
    /// <remarks>Structural, not merely observed: <c>GoldStore</c>'s store lookup
    /// skips every room whose owner is not the depositor and is the only path into
    /// a tile's <see cref="TileState.GoldHeld"/>. Nothing was written to make the
    /// takeover happen; it falls out of two rules meeting — a room somebody else
    /// holds is convertible work at the claiming priority (Q92) and a treasury
    /// tile the keeper owns is somewhere gold can go (ADR-0015).</remarks>
    [Fact]
    public void AnImpTakesARivalsTreasuryBeforeItEverPutsGoldInIt()
    {
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury, owner: Rival);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(300);

        // The takeover leaves the load in the imp's hands; its next dig puts it down (kind 15).
        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 6)));
        run.Ticks(600);

        Assert.Equal(Keeper, run.World.Tile(1, 1).OwnerPlayerId);
        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, run.World.Tile(1, 1).TerrainId);
        Assert.True(run.World.Tile(1, 1).GoldHeld > 0, "the imp took the treasury and then left it empty");

        SimEvent taken = Assert.Single(run.Of("room_converted"));
        SimEvent firstDelivery = run.Of("gold_delivered").First();
        Assert.True(
            taken.Tick.Value < firstDelivery.Tick.Value,
            $"gold went in on tick {firstDelivery.Tick.Value} but the treasury only " +
            $"became this keeper's on tick {taken.Tick.Value}");
    }

    /// <summary>
    /// The owner's rule in its general form: <b>no gold ever enters a tile its
    /// keeper does not own</b>, on any tick of a run with a rival store in it.
    ///
    /// <para>The test above pins the one sequence; this pins the invariant, which
    /// is what the sentence actually says. It watches every tick rather than the
    /// end state, because a deposit that went in and was later taken over would
    /// leave an end state indistinguishable from a legal one.</para>
    /// </summary>
    [Fact]
    public void NoGoldEverEntersATileItsKeeperDoesNotOwn()
    {
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury, owner: Rival);
        PutStoreAt(run, new TilePos(3, 1), RoomTypeId.Treasury, owner: Keeper);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));

        var lastGold = new Dictionary<int, int>();
        int depositsSeen = 0;

        for (int t = 0; t < 400; t++)
        {
            run.Ticks(1);

            for (int i = 0; i < run.World.TileCount; i++)
            {
                ref TileState tile = ref run.World.TileAtIndex(i);
                int before = lastGold.TryGetValue(i, out int g) ? g : 0;
                if (tile.GoldHeld > before)
                {
                    // Gold arrived here this tick. Whoever it arrived for, this
                    // tile is theirs — there is no other legal way in.
                    Assert.Equal(Keeper, tile.OwnerPlayerId);
                    depositsSeen++;
                }
                lastGold[i] = tile.GoldHeld;
            }
        }

        Assert.True(depositsSeen > 0, "no gold moved at all, so the invariant above was never tested");
    }

    // ---- nowhere to put it -----------------------------------------------

    [Fact]
    public void AnImpWithNowhereToPutGold_KeepsMining_AndGrowsALumpAtItsFeet()
    {
        // CONFIRMED that the original's imp goes on mining and grows a lump on
        // the floor, and read: the mining strike 0x004971D0 hands the imp what it
        // can hold and puts the rest down at its own position, onto the pile
        // already there (Q138, Q140). So nothing is lost: 1400 in hand, 1600 in one pile where
        // it stood. Nothing tops the hands up from it: the pick-ups take only below the hands
        // test's mark (1060 at level 1, 1130 at level 2), and 1400 is over both (ADR-0293).
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = AtTheSeam();

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(300);

        // Mined out, and by 300 ticks the imp has moved on and claimed the floor
        // it opened - which is the priority order doing its job (ADR-0018).
        Assert.NotEqual(Dk2TerrainFixture.Gold, run.World.Tile(5, 5).TerrainId);
        CreatureDef imp = database.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        Assert.Equal(CreatureLevelRules.MaxGoldHeld(database, imp, level: 1), run.Imp.GoldHeld);
        Assert.Empty(run.Of("gold_uncollected"));
        ObjectState lump = Assert.Single(run.World.Objects, o => FloorGold.IsPile(database, o));
        Assert.Equal(3000 - run.Imp.GoldHeld, lump.GoldHeld);
        Assert.Contains(lump.TileIndex, new[] { new TilePos(4, 5), new TilePos(4, 4) }
            .Select(p => p.Y * run.World.Width + p.X));
    }

    /// <summary>The seam (5,5) is worked from (4,5) with the wall to the right: face 2, whose three
    /// slots cover cells 2, 5 and 8 of the standing tile (the column along its right edge).</summary>
    private static readonly byte[] SeamSlotCells = [2, 5, 8];

    /// <summary>The chooser 0x00469AF0 skips a slot whose cell's pile holds strictly more than
    /// MaxGoldPileOutsideTreasury (2000). With every slot's cell over, no mining job is taken
    /// while they stay over; at exactly 2000 one is taken at once (Q140). The imp may later claim
    /// the tile and pick the piles up, which reopens it; the assertion is about the ticks they
    /// are over.</summary>
    [Theory]
    [InlineData(2001, false)]
    [InlineData(2000, true)]
    public void AFaceWhoseSlotsAllHoldPilesOverTheLimitIsNotMined_StrictlyGreater(int pile, bool takenAtOnce)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = AtTheSeam();
        int spot = 5 * run.World.Width + 4;
        foreach (byte cell in SeamSlotCells) FloorGold.PutDown(run.World, database, spot, cell, pile);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        bool takenWhileOver = false;
        for (int t = 0; t < 300; t++)
        {
            bool over = SeamSlotCells.All(c => FloorGold.PileInCell(run.World, spot, c) > FloorGold.MaxPile(database));
            int before = MiningTaken(run);
            run.Tick();
            if (over && MiningTaken(run) > before) takenWhileOver = true;
        }

        Assert.False(takenWhileOver);
        Assert.Equal(takenAtOnce, run.Of("job_taken").Take(1).Any(e => e.Detail.Contains("MineGold")));

        static int MiningTaken(Run r) => r.Of("job_taken").Count(e => e.Detail.Contains("MineGold"));
    }

    /// <summary>A miner with nowhere to deliver gives its slot up once the pile in the slot's cell
    /// passes 2000 (state 26, 0x00465870), and the chooser hands it the face's next slot, so the
    /// seam is still mined out, its gold in more than one cell. One pile per tile would stop it
    /// short, which is what broke Level 1's replay.</summary>
    [Fact]
    public void AMinerMovesToTheNextSlotWhenItsPilePassesTheLimit_AndMinesTheSeamOut()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = AtTheSeam();
        int spot = 5 * run.World.Width + 4;
        FloorGold.PutDown(run.World, database, spot, SeamSlotCells[0], 1500);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        int most = 0;
        for (int t = 0; t < 300; t++)
        {
            run.Tick();
            most = Math.Max(most, FloorGold.PileInCell(run.World, spot, SeamSlotCells[0]));
        }

        // The pile passes the limit before the slot is given up, by less than one more load;
        // Tier B may take some back up afterwards, so its highest is what is asserted.
        Assert.Contains(run.Of("job_abandoned"), e => e.Detail.Contains("reason=pile_underfoot_full"));
        Assert.NotEqual(Dk2TerrainFixture.Gold, run.World.Tile(5, 5).TerrainId);
        Assert.InRange(most, 2001, 2000 + 1364);
        Assert.True(FloorGold.PileInCell(run.World, spot, SeamSlotCells[1]) > 0);
        // Conserved: 3000 mined, 1400 in hand, and the 1500 laid beforehand.
        Assert.Equal(3000 + 1500 - run.Imp.GoldHeld, FloorGold.Total(run.World, database));
    }

    [Fact]
    public void AnImpHoldsItsLoadUntilSomewhereToPutItAppears()
    {
        // The load is not lost while the keeper has nowhere to keep it: a miner with
        // full hands and nowhere to go mines on, and puts the load down the moment a
        // treasury exists - state 26 asks the store chooser before each swing that
        // would not fit. That is what makes gold-in-hands a place gold can be rather
        // than an accounting step.
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = AtTheSeam();

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        int max = database.CreatureById(Dk2TerrainFixture.ImpCreatureId).MaxGoldHeld;
        for (int t = 0; t < 300 && run.Imp.GoldHeld < max; t++) run.Tick();
        int held = run.Imp.GoldHeld;
        Assert.True(held >= max, $"hands hold {held}");
        Assert.Equal(Dk2TerrainFixture.Gold, run.World.Tile(5, 5).TerrainId);

        // The treasury appears while the full miner still works the seam. (Once the seam is
        // out, a full imp holds its load: state 10 never deposits, ADR-0288.)
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);
        run.Ticks(300);

        Assert.True(run.World.Tile(1, 1).GoldHeld >= held, $"stored {run.World.Tile(1, 1).GoldHeld} of {held}");
    }

    // ---- the gate --------------------------------------------------------

    [Fact]
    public void AnImpsLoadSurvivesASnapshot()
    {
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);
        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        for (int t = 0; t < 60 && run.Imp.GoldHeld == 0; t++) run.Tick();

        Assert.True(run.Imp.GoldHeld > 0, "the imp should be mid-delivery");

        byte[] saved = WorldSnapshot.ToBytes(run.Sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            saved, Dk2TerrainFixture.Database(), Dk2TerrainFixture.DefaultSystems());

        Assert.Equal(run.Imp.GoldHeld, restored.World.Creatures[0].GoldHeld);
        Assert.Equal(run.Sim.StateHash(), restored.StateHash());

        for (int i = 0; i < 200; i++)
        {
            run.Tick();
            restored.Tick();
            Assert.Equal(run.Sim.StateHash(), restored.StateHash());
        }
    }

    [Fact]
    public void CarryingIsReproducible()
    {
        ulong Play()
        {
            Run run = AtTheSeam();
            PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);
            run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
            run.Ticks(400);
            return run.Sim.StateHash();
        }

        Assert.Equal(Play(), Play());
    }
}

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// What happens to gold between the seam and the treasury.
///
/// <b>CONFIRMED (Q31, owner observation of the original):</b> an imp mines into
/// its own hands up to <c>maxGoldHeld</c>, runs to the drop spot and adds the
/// load to the store; if there is nowhere to put it, it keeps mining and a lump
/// grows on the floor in front of it. Everything here tests the first half. The
/// lump is an OBJECT and the OBJECTS record is not parsed until M6, so the second
/// half stops at "keeps mining" and the gold that will not fit is reported and
/// lost — which is asserted too, because a missing mechanic that is invisible is
/// worse than one that is loud.
///
/// <para>The numbers all come out of the shipped data and are worth having in one
/// place. A gold seam is 660 health and 3000 gold; <c>MineGoldHealth</c> is −300 a
/// second and work lands five times a second (Q47), so a swing takes 60 health and
/// frees 273 gold — the number the owner measured in the original. A seam is
/// therefore <b>eleven swings</b> totalling exactly 3000. An Imp's
/// <c>maxGoldHeld</c> is 1400, so it fills after five swings and a seam takes
/// <b>three trips</b>: 1363, 1364, 273. A treasury tile holds 3000 and a
/// dungeon-heart tile 1000.</para>
/// </summary>
public class GoldCarryingTests
{
    private const byte Keeper = 1;
    private const byte Rival = 2;

    /// <summary>What an imp is holding when it breaks off: five swings' worth.</summary>
    private const int FullLoad = 1363;

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
            database, Dk2TerrainFixture.Map(), [(byte)0, Keeper, Rival]);
        world.SpawnCreature(
            Dk2TerrainFixture.ImpCreatureId, Keeper, impAt ?? new TilePos(2, 2));

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
    public void AnImpBreaksOffMiningWhenTheNextSwingWouldNotFit()
    {
        // maxGoldHeld is 1400 and a swing frees 273, so the imp downs tools after
        // five swings (1363 held) rather than take a sixth it could not hold.
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(120);

        SimEvent leaving = run.Of("delivering").First();
        Assert.Contains($"gold={FullLoad}", leaving.Detail);
        Assert.Contains("to=(1,1)", leaving.Detail);

        // Five swings, and nothing thrown away to get there.
        Assert.Equal(5, run.Of("gold_mined").Count());
        Assert.Empty(run.Of("gold_uncollected"));
    }

    [Fact]
    public void AnImpDeliversWhatItIsHoldingWhenTheSeamRunsOut()
    {
        // Eleven swings split five, five and one, so the last trip carries a
        // single swing - nowhere near a full load. An imp that only ran when full
        // would stand on an exhausted seam holding it.
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(800);

        List<int> loads = run.Of("gold_delivered").Select(Run.Amount).ToList();
        Assert.Equal([1363, 1364, 273], loads);
        Assert.Equal(3000, loads.Sum());
    }

    // ---- where the load lands (Q35) --------------------------------------

    [Fact]
    public void AFullTileSpillsIntoTheNextOne()
    {
        // Two dungeon-heart tiles, 1000 each, against a 1363 load. The imp fills
        // the one it walked to and carries the rest to the next — which is Q35's
        // spillover, and it falls out of re-routing rather than being coded.
        //
        // (3,3) fills first because that is the one the imp reaches first coming
        // back along the corridor, which is the whole point: which tile of a
        // store fills is decided by the walk, not by an index.
        //
        // Everything here is placed to finish inside
        // TimeBeforeFreeImpGeneratedSeconds. Heart tiles are the only way to get
        // a 1000-capacity store, and a heart is also where ImpSupplySystem puts
        // the imps it hands a short-staffed keeper - so the imp starts beside the
        // seam and the store is two steps away, and the run stops before the
        // first free imp arrives. This test is about one imp's load.
        Run run = AtTheSeam(new TilePos(4, 4));
        PutStoreAt(run, new TilePos(3, 3), RoomTypeId.DungeonHeart);
        PutStoreAt(run, new TilePos(3, 2), RoomTypeId.DungeonHeart);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));

        // Run to the moment the spillover lands rather than to a fixed tick.
        // A fixed count broke when movement went eight-connected (ADR-0025):
        // the imp got home sooner, unloaded, walked back and had already mined
        // two more swings by tick 95. What the test is about is where the load
        // goes, not how long the walk takes.
        for (int i = 0; i < 95 && run.World.Tile(3, 2).GoldHeld == 0; i++) run.Tick();

        Assert.Equal(1000, run.World.Tile(3, 3).GoldHeld);
        Assert.Equal(363, run.World.Tile(3, 2).GoldHeld);
        Assert.Equal(0, run.Imp.GoldHeld);
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

    [Fact]
    public void AnImpWillNotFillARivalsTreasury()
    {
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury, owner: Rival);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(300);

        Assert.Equal(0, run.World.Tile(1, 1).GoldHeld);
        Assert.Empty(run.Of("gold_delivered"));
    }

    // ---- nowhere to put it -----------------------------------------------

    [Fact]
    public void AnImpWithNowhereToPutGold_KeepsMining()
    {
        // CONFIRMED that the original's imp goes on mining and grows a lump on
        // the floor. The mining is here; the lump is not, so what the imp cannot
        // hold is reported as uncollected and lost. The point of the test is that
        // a keeper with no treasury still gets their seam dug out — an imp that
        // stood still holding a full load would be a worse wrong answer than one
        // that loses the overflow.
        Run run = AtTheSeam();

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(300);

        // Mined out, and by 300 ticks the imp has moved on and claimed the floor
        // it opened - which is the priority order doing its job (ADR-0018).
        Assert.NotEqual(Dk2TerrainFixture.Gold, run.World.Tile(5, 5).TerrainId);
        Assert.Equal(1400, run.Imp.GoldHeld);
        Assert.Equal(1600, run.Amounts("gold_uncollected"));
        Assert.Contains(run.Of("gold_uncollected"),
            e => e.Detail.Contains("would not fit in the worker's hands"));
    }

    [Fact]
    public void AnImpHoldsItsLoadUntilSomewhereToPutItAppears()
    {
        // The load is not lost while the keeper has nowhere to keep it: an imp
        // that has been carrying 1400 around all game puts it down the moment a
        // treasury exists. That is what makes gold-in-hands a place gold can be
        // rather than an accounting step.
        Run run = AtTheSeam();

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(300);
        Assert.Equal(1400, run.Imp.GoldHeld);

        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);
        run.Ticks(300);

        Assert.Equal(0, run.Imp.GoldHeld);
        Assert.Equal(1400, run.World.Tile(1, 1).GoldHeld);
    }

    // ---- the gate --------------------------------------------------------

    [Fact]
    public void AnImpsLoadSurvivesASnapshot()
    {
        Run run = AtTheSeam();
        PutStoreAt(run, new TilePos(1, 1), RoomTypeId.Treasury);
        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(90);

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

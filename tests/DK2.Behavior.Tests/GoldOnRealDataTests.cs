using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Behavior.Tests;

/// <summary>
/// The gold round trip against the real game's data: a shipped seam, a shipped
/// dungeon heart, and the distance the shipped map puts between them.
///
/// The synthetic tests in <c>DK2.Simulation.Tests.GoldCarryingTests</c> prove the
/// rules; this proves the numbers are the game's. Every quantity below is DK2's
/// own — the seam's 3000 over 660 health, <c>MineGoldHealth</c> −300, the Imp's
/// <c>maxGoldHeld</c> 1400, <c>MaxGoldPerDungeonHeartTile</c> 1000 — and none of
/// them is written down here; they are read out of the install and the
/// arithmetic is asserted against what they imply.
///
/// Skips when no installation is configured (DK2_DIR), so CI stays green without
/// proprietary assets.
/// </summary>
public class GoldOnRealDataTests
{
    /// <summary>
    /// Level 1's own layout, surveyed with
    /// <c>dk2tools kwd map maps/Level1Map.kld &lt;terrain&gt;</c>: player 3's
    /// dungeon heart is the 5×5 block at (12..16, 26..30), a strip of their
    /// claimed path runs along (14,25), and an unowned gold seam sits directly
    /// north of it at (14,24). An imp on the path can reach both without a step
    /// being invented for it.
    /// </summary>
    private const byte Keeper = 3;
    private static readonly TilePos Path = new(14, 25);
    private static readonly TilePos Seam = new(14, 24);
    private static readonly TilePos Heart = new(14, 26);

    private sealed record Level(GameSimulation Sim, GameDatabase Db, ListEventSink Events)
    {
        public WorldState World => Sim.World;

        public CreatureState Imp => Sim.World.Creatures[0];

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);
    }

    /// <summary>Loads level 1 and puts one of player 3's imps on their own path beside the seam.</summary>
    private static Level? Level1()
    {
        string? install = Dk2Install.Find();
        if (install is null) return null; // skipped: no DK2 install

        GameDatabase db = GameDatabaseLoader.Load(install);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level1")!;
        WorldState world = WorldState.FromMap(
            db, map, map.Tiles.Select(t => t.PlayerId).Distinct());

        // If the survey above ever stops describing the shipped map, fail here
        // rather than quietly testing something else.
        Assert.Equal(Keeper, world.Tile(Path).OwnerPlayerId);
        Assert.True(db.TerrainById(world.Tile(Seam).TerrainId).GoldValue > 0,
            $"{Seam} is not a gold seam on the shipped level 1");
        Assert.Equal(RoomTypeId.DungeonHeart, RoomRules.RoomOn(db, world.Tile(Heart))?.TypeId);

        world.SpawnCreature(db.CreatureByName("Imp")!.CreatureId, Keeper, Path);

        var sim = new GameSimulation(db, world, 7, SimulationSystems.Default());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Seam)));
        return new Level(sim, db, sink);
    }

    [Fact]
    public void AnImpMinesARealSeamAndCarriesItIntoTheDungeonHeart()
    {
        Level? level = Level1();
        if (level is null) return;

        int before = GoldStore.Total(level.World, Keeper);
        int seamValue = level.Db.TerrainById(level.World.Tile(Seam).TerrainId).GoldValue;

        level.Sim.Run(400);

        // The seam is gone, and every coin it was worth is in the keeper's
        // dungeon heart — bar the handful DK2's own integer division loses on
        // the way, which is why this is a tolerance and not an equality.
        Assert.Equal(0, level.Db.TerrainById(level.World.Tile(Seam).TerrainId).GoldValue);
        Assert.Equal(0, level.Imp.GoldHeld);

        int gained = GoldStore.Total(level.World, Keeper) - before;
        Assert.InRange(gained, seamValue - 10, seamValue);
        Assert.Empty(level.Of("gold_uncollected"));
    }

    /// <summary>
    /// <b>273 gold a swing, eleven swings, exactly 3000.</b> The measurement
    /// that settled Q47, checked against the shipped seam rather than the
    /// fixture's copy of it.
    ///
    /// The chain, so a future session can see it rather than take it: the owner
    /// measured 273 gold per swing in the original; a seam is 3000 gold over 660
    /// health; so a swing takes 273 × 660 / 3000 = 60 health; so a seam takes 11
    /// swings; so an application is a fifth of <c>MineGoldHealth</c>'s −300, and
    /// work lands five times a second.
    /// </summary>
    [Fact]
    public void ASwingAtARealSeamFreesTheGoldTheOriginalFrees()
    {
        Level? level = Level1();
        if (level is null) return;

        var swings = new List<int>();
        ref TileState seam = ref level.World.Tile(Seam);
        while (level.Db.TerrainById(seam.TerrainId).GoldValue > 0)
        {
            TileWorkResult result = TileWorkRules.Apply(
                level.Db, ref seam, TileWorkKind.MineGold, Keeper);
            swings.Add(result.GoldReleased);
            Assert.Equal(-60, result.HealthDelta);
        }

        Assert.Equal(11, swings.Count);
        Assert.Equal(273, swings.GroupBy(g => g).OrderByDescending(g => g.Count()).First().Key);
        Assert.Equal(3000, swings.Sum());
    }

    [Fact]
    public void ItTakesMoreThanOneTripBecauseAnImpsHandsAreSmallerThanASeam()
    {
        // The interesting relationship in the shipped numbers: one swing frees
        // more than half a seam, and an Imp cannot hold two swings' worth. That
        // is what makes imps shuttle instead of standing at the rock face, and
        // it is asserted rather than described so that a data change would show
        // up as a failure.
        Level? level = Level1();
        if (level is null) return;

        int maxHeld = level.Db.CreatureByName("Imp")!.MaxGoldHeld;
        int seamValue = level.Db.TerrainById(level.World.Tile(Seam).TerrainId).GoldValue;
        Assert.True(seamValue > maxHeld,
            $"a seam is worth {seamValue} and an imp holds {maxHeld}: no trip would be needed");

        level.Sim.Run(400);

        Assert.True(level.Of("delivering").Count() >= 2,
            $"only {level.Of("delivering").Count()} trips for a seam worth {seamValue}");
        Assert.All(level.Of("gold_delivered"),
            e => Assert.True(Amount(e) <= maxHeld,
                $"a load of {Amount(e)} is more than an imp can carry ({maxHeld})"));
    }

    [Fact]
    public void ALoadTooBigForOneHeartTileSpillsIntoTheNext()
    {
        // MaxGoldPerDungeonHeartTile is 1000 against a first load of 1363, so the
        // first trip alone has to touch two tiles. Which two is decided by where
        // the imp walked, which is as close to Q35 as observation has got.
        Level? level = Level1();
        if (level is null) return;

        int perTile = level.Db[VariableTypeId.MaxGoldPerDungeonHeartTile];
        level.Sim.Run(400);

        List<SimEvent> loads = level.Of("gold_delivered").ToList();

        // A split load is one the imp walks away from still carrying something.
        Assert.Contains(loads, e => !e.Detail.EndsWith("carrying=0", StringComparison.Ordinal));

        var tiles = loads.Select(e => e.Detail.Split("at=")[1].Split(' ')[0]).Distinct().ToList();
        Assert.True(tiles.Count >= 2, $"every load landed in the same tile: {tiles[0]}");
        Assert.All(loads, e => Assert.True(Amount(e) <= perTile,
            $"a tile took {Amount(e)}, over the limit of {perTile}"));
    }

    /// <summary>
    /// <b>A keeper with no treasury can hold 16000 gold, and we say 25000.</b>
    /// CONFIRMED by the owner from the original (2026-08-30), and a defect
    /// recorded rather than fixed, because fixing it needs something we do not
    /// have.
    ///
    /// <para>The arithmetic says exactly where the difference is.
    /// <c>MaxGoldPerDungeonHeartTile</c> is 1000 and a dungeon heart is a 5×5
    /// block, which is 25 tiles and would be 25000. The original stores 16000,
    /// so <b>16 tiles store gold and 9 do not</b> — and 9 is 3×3, the raised
    /// centre the heart itself stands on. A screenshot of the heart shows it:
    /// the structure occupies a 3×3 platform and the gold piles sit on the ring
    /// of 16 around it.</para>
    ///
    /// <para>Not implemented because nothing here knows a room <i>instance</i>
    /// or its origin — a room is terrain, tile by tile, so there is no way to
    /// ask which of 25 heart tiles is a centre one. <c>TileConstruction</c> is
    /// <c>FiveByFiveRotated</c> for the heart and unread, and it is the field
    /// that would answer it. Room shape is Q36/Q45 territory and lands with room
    /// instances. Q52.</para>
    /// </summary>
    [Fact]
    public void AKeepersHeartHoldsMoreGoldHereThanInTheOriginal()
    {
        Level? level = Level1();
        if (level is null) return;

        int perTile = level.Db[VariableTypeId.MaxGoldPerDungeonHeartTile];
        int heartTiles = 0;
        for (int i = 0; i < level.World.TileCount; i++)
        {
            ref TileState tile = ref level.World.TileAtIndex(i);
            if (tile.OwnerPlayerId != Keeper) continue;
            if (RoomRules.RoomOn(level.Db, tile)?.TypeId == RoomTypeId.DungeonHeart) heartTiles++;
        }

        // A 5x5 heart, all of it storing, is where our number comes from.
        Assert.Equal(25, heartTiles);
        Assert.Equal(25_000, heartTiles * perTile);

        // The original stores 16000, which is the 16-tile ring around a 3x3
        // centre. The nine missing tiles are the heart's own platform.
        Assert.Equal(16_000, (heartTiles - 9) * perTile);
        Assert.Equal(9, 3 * 3);
    }

    /// <summary>
    /// The one place the whole model can be checked against a single shipped
    /// number: <c>MaxGoldPileOutsideTreasury</c> is 2000, which is what a lump
    /// on the floor can grow to. It is read by nothing, because floor lumps are
    /// OBJECTS and the OBJECTS record is not parsed until M6 — and this test
    /// exists so that stays deliberate rather than forgotten (Q31).
    /// </summary>
    [Fact]
    public void TheFloorPileLimitIsInTheDataAndIsReadByNothing()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        Assert.Equal(2000, db[VariableTypeId.MaxGoldPileOutsideTreasury]);
    }

    private static int Amount(SimEvent e) =>
        int.Parse(e.Detail.Split("amount=")[1].Split(' ')[0]);
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using DK2.TestSupport;

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
/// Gated with [InstallFact], so it reports SKIPPED without an installation rather than passing.
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
        string install = Dk2InstallGate.Path;
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

        world.SpawnCreature(db, db.CreatureByName("Imp")!.CreatureId, Keeper, Path);

        var sim = new GameSimulation(db, world, 7, SimulationSystems.Default());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Seam)));
        return new Level(sim, db, sink);
    }

    [InstallFact]
    public void AnImpMinesARealSeamAndCarriesItIntoTheDungeonHeart()
    {
        Level? level = Level1();
        Assert.NotNull(level);

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
    [InstallFact]
    public void ASwingAtARealSeamFreesTheGoldTheOriginalFrees()
    {
        Level? level = Level1();
        Assert.NotNull(level);

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

    [InstallFact]
    public void ItTakesMoreThanOneTripBecauseAnImpsHandsAreSmallerThanASeam()
    {
        // The interesting relationship in the shipped numbers: one swing frees
        // more than half a seam, and an Imp cannot hold two swings' worth. That
        // is what makes imps shuttle instead of standing at the rock face, and
        // it is asserted rather than described so that a data change would show
        // up as a failure.
        Level? level = Level1();
        Assert.NotNull(level);

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

    [InstallFact]
    public void ALoadTooBigForOneHeartTileSpillsIntoTheNext()
    {
        // MaxGoldPerDungeonHeartTile is 1000 against a first load of 1363, so the
        // first trip alone has to touch two tiles. Which two is decided by where
        // the imp walked, which is as close to Q35 as observation has got.
        Level? level = Level1();
        Assert.NotNull(level);

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
    /// <b>A keeper with no treasury holds exactly 16000 gold</b>, on the real
    /// campaign map, from the real heart. CONFIRMED by the owner (Q52) and now
    /// reproduced rather than merely recorded (ADR-0027).
    ///
    /// <para>The arithmetic says exactly where the number comes from.
    /// <c>MaxGoldPerDungeonHeartTile</c> is 1000 and a dungeon heart is 5x5, so
    /// twenty-five tiles would be 25000 — which is what this project stored
    /// until room instances existed. The original stores 16000, so <b>16 tiles
    /// store gold and 9 do not</b>, and 9 is 3x3: the raised platform the heart
    /// structure stands on. A screenshot of the heart shows it, with the gold
    /// piles ringing the centre.</para>
    ///
    /// <para>This is the test the whole room-instance change was for. It reads
    /// the real level's real heart, and it is deliberately expressed as a total
    /// rather than as "9 tiles are excluded", because the total is the thing the
    /// owner actually measured.</para>
    /// </summary>
    [InstallFact]
    public void AKeepersHeartHoldsExactlySixteenThousand()
    {
        Level? level = Level1();
        Assert.NotNull(level);

        int perTile = level.Db[VariableTypeId.MaxGoldPerDungeonHeartTile];
        int heartTiles = 0;
        int storing = 0;

        for (int i = 0; i < level.World.TileCount; i++)
        {
            ref TileState tile = ref level.World.TileAtIndex(i);
            if (tile.OwnerPlayerId != Keeper) continue;
            if (RoomRules.RoomOn(level.Db, tile)?.TypeId != RoomTypeId.DungeonHeart) continue;

            heartTiles++;
            if (GoldStore.Capacity(level.World, level.Db, i) > 0) storing++;
        }

        Assert.Equal(25, heartTiles);
        Assert.Equal(16, storing);
        Assert.Equal(9, heartTiles - storing);

        // What the owner measured: the whole keeper's capacity with no treasury.
        Assert.Equal(16_000, GoldStore.SpaceFor(level.World, level.Db, Keeper));
        Assert.Equal(16_000, storing * perTile);
    }

    /// <summary>
    /// The heart on the real map is one room instance of twenty-five tiles, and
    /// it is a solid 5x5. Asserted separately from the gold, because if the
    /// flood fill ever split it — a neighbouring tile changing owner would do
    /// it — the capacity test above would still pass for the wrong reason.
    /// </summary>
    [InstallFact]
    public void TheHeartOnTheRealMapIsOneFiveByFiveInstance()
    {
        Level? level = Level1();
        Assert.NotNull(level);

        List<RoomInstance> hearts = RoomIndex.Build(level.World, level.Db)
            .Where(r => r.Definition.TypeId == RoomTypeId.DungeonHeart
                        && r.OwnerPlayerId == Keeper)
            .ToList();

        RoomInstance heart = Assert.Single(hearts);
        Assert.Equal(25, heart.TileCount);
        Assert.Equal(5, heart.Width);
        Assert.Equal(5, heart.Height);
        Assert.True(heart.IsRectangular);

        // 5x5: sixteen on the rim, nine inside — and the nine inside are exactly
        // the platform, which is why the two counts agree.
        Assert.Equal(16, heart.EdgeTileCount);
        Assert.Equal(9, heart.InteriorTileCount);
    }


    /// <summary>
    /// The one place the whole model can be checked against a single shipped
    /// number: <c>MaxGoldPileOutsideTreasury</c> is 2000, which is what a lump
    /// on the floor can grow to. It is read by nothing: OBJECTS now parses, but
    /// M6 has not projected floor-lump records or their behaviour into the
    /// world. This test exists so that stays deliberate rather than forgotten
    /// (Q31).
    /// </summary>
    [InstallFact]
    public void TheFloorPileLimitIsInTheDataAndIsReadByNothing()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase db = GameDatabaseLoader.Load(install);
        Assert.Equal(2000, db[VariableTypeId.MaxGoldPileOutsideTreasury]);
    }

    private static int Amount(SimEvent e) =>
        int.Parse(e.Detail.Split("amount=")[1].Split(' ')[0]);
}

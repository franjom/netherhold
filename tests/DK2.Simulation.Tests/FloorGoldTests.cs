// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// Gold lying on the floor. Every number checked here came out of
/// <c>DKII.EXE</c> on 2026-09-05 — see <c>docs/research/gold-on-the-floor.md</c>.
/// </summary>
public sealed class FloorGoldTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = 7;

    private static (WorldState World, GameDatabase Db) Empty()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        return (WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [PlayerIds.Unowned, PlayerIds.Neutral, Keeper]), db);
    }

    // ---- what a pile is ---------------------------------------------------

    [Fact]
    public void APileIsObjectOneAndCarriesItsAmount()
    {
        (WorldState world, GameDatabase db) = Empty();

        ObjectState? pile = FloorGold.Drop(world, db, 12, 750);

        Assert.NotNull(pile);
        Assert.Equal(FloorGold.FloorGoldObjectId, pile!.ObjectTypeId);
        Assert.Equal(750, pile.GoldHeld);
        Assert.Equal(12, pile.TileIndex);
        Assert.Equal("Gold", db.ObjectById(pile.ObjectTypeId)!.Name);
    }

    /// <summary>
    /// All three gold records carry <see cref="ObjectFlags.TypeGold"/>, so the
    /// flag identifies gold and the id identifies <i>which</i> gold — the
    /// precedence objects.md §7 read out of the original's own dispatch.
    /// </summary>
    [Fact]
    public void EveryGoldRecordIsAPileAndNothingElseIs()
    {
        (WorldState world, GameDatabase db) = Empty();

        foreach (byte id in new byte[] { 1, 2, FloorGold.TreasuryPileObjectId })
        {
            ObjectState gold = world.SpawnObject(db, id, Keeper, 0);
            Assert.True(FloorGold.IsPile(db, gold), $"object {id} should be gold");
        }

        ObjectState bed = world.SpawnObject(db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, 1);
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, 2);
        Assert.False(FloorGold.IsPile(db, bed));
        Assert.False(FloorGold.IsPile(db, chicken));
    }

    [Fact]
    public void DroppingNothingCreatesNoPile()
    {
        (WorldState world, GameDatabase db) = Empty();

        Assert.Null(FloorGold.Drop(world, db, 5, 0));
        Assert.Null(FloorGold.Drop(world, db, 5, -100));
        Assert.Empty(world.Objects);
    }

    /// <summary>
    /// Nothing merges. Two drops on one tile are two objects, because that is
    /// what the original does everywhere except when an imp is choosing a spot
    /// to put a load down.
    /// </summary>
    [Fact]
    public void TwoDropsOnOneTileAreTwoPiles()
    {
        (WorldState world, GameDatabase db) = Empty();

        FloorGold.Drop(world, db, 9, 300);
        FloorGold.Drop(world, db, 9, 400);

        var piles = new List<ObjectState>();
        FloorGold.PilesOn(world, db, 9, piles);

        Assert.Equal(2, piles.Count);
        Assert.Equal(700, FloorGold.OnTile(world, db, 9));
        Assert.Equal(700, FloorGold.Total(world, db));
    }

    [Fact]
    public void GoldOnOtherTilesIsNotOnThisOne()
    {
        (WorldState world, GameDatabase db) = Empty();

        FloorGold.Drop(world, db, 9, 300);
        FloorGold.Drop(world, db, 10, 400);

        Assert.Equal(300, FloorGold.OnTile(world, db, 9));
        Assert.Equal(700, FloorGold.Total(world, db));
    }

    // ---- taking gold back off the floor ------------------------------------

    [Fact]
    public void TakingPartOfAPileLeavesTheRest()
    {
        (WorldState world, GameDatabase db) = Empty();
        ObjectState pile = FloorGold.Drop(world, db, 4, 1000)!;

        Assert.Equal(400, FloorGold.Take(world, db, pile, 400));
        Assert.Equal(600, pile.GoldHeld);
        Assert.Single(world.Objects);
    }

    /// <summary>An emptied pile is destroyed — the original has no zero pile.</summary>
    [Fact]
    public void AnEmptiedPileIsGone()
    {
        (WorldState world, GameDatabase db) = Empty();
        ObjectState pile = FloorGold.Drop(world, db, 4, 1000)!;

        Assert.Equal(1000, FloorGold.Take(world, db, pile, 5000));
        Assert.Empty(world.Objects);
        Assert.Equal(0, FloorGold.Total(world, db));
    }

    [Fact]
    public void TakingFromSomethingThatIsNotGoldTakesNothing()
    {
        (WorldState world, GameDatabase db) = Empty();
        ObjectState bed = world.SpawnObject(db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, 3);

        Assert.Equal(0, FloorGold.Take(world, db, bed, 100));
        Assert.Single(world.Objects);
    }

    // ---- the cap that is not a cap -----------------------------------------

    /// <summary>
    /// <c>MaxGoldPileOutsideTreasury</c> is 2000, and the check the original
    /// makes is <b>strictly greater</b>: a pile sitting exactly on the number is
    /// still somewhere an imp will put gold.
    /// </summary>
    [Fact]
    public void APileOnTheLimitStillAcceptsMoreAndOneOverDoesNot()
    {
        (WorldState world, GameDatabase db) = Empty();
        Assert.Equal(2000, FloorGold.MaxPile(db));

        Assert.True(FloorGold.AcceptsMore(db, FloorGold.Drop(world, db, 1, 1999)!));
        Assert.True(FloorGold.AcceptsMore(db, FloorGold.Drop(world, db, 2, 2000)!));
        Assert.False(FloorGold.AcceptsMore(db, FloorGold.Drop(world, db, 3, 2001)!));
    }

    /// <summary>
    /// <b>Nothing clamps.</b> The paths that make piles in the original — a sold
    /// store, a full treasury, a dead creature — all make them as big as the
    /// gold they were given, and two of the three routinely exceed the limit.
    /// This is the test that would fail if someone "fixed" the cap into an
    /// invariant, which is exactly what nearly happened to the hatchery's flock.
    /// </summary>
    [Fact]
    public void APileMayBeBiggerThanTheLimit()
    {
        (WorldState world, GameDatabase db) = Empty();

        ObjectState pile = FloorGold.Drop(world, db, 7, 3000)!;

        Assert.Equal(3000, pile.GoldHeld);
        Assert.True(pile.GoldHeld > FloorGold.MaxPile(db));
        Assert.Equal(3000, FloorGold.OnTile(world, db, 7));
    }

    // ---- a creature drops what it carried ----------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(999, 1)]
    [InlineData(1000, 2)]
    [InlineData(2999, 2)]
    [InlineData(3000, 3)]
    [InlineData(100000, 3)]
    public void OneTwoOrThreePiles(int gold, int expected) =>
        Assert.Equal(expected, FloorGold.PileCount(gold));

    /// <summary>
    /// The original divides by the pile count with integer division and hands
    /// every pile that share, so <b>the remainder ceases to exist</b>. 2999
    /// leaves two piles of 1499 and one gold nobody ever sees again.
    /// </summary>
    [Fact]
    public void TheRemainderIsDestroyed()
    {
        (WorldState world, GameDatabase db) = Empty();
        CreatureState imp = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 2));
        imp.GoldHeld = 2999;

        int dropped = FloorGold.DropFrom(world, db, imp);

        Assert.Equal(2998, dropped);
        Assert.Equal(0, imp.GoldHeld);

        int tile = (2 * world.Width) + 3;
        var piles = new List<ObjectState>();
        FloorGold.PilesOn(world, db, tile, piles);

        Assert.Equal(2, piles.Count);
        Assert.All(piles, p => Assert.Equal(1499, p.GoldHeld));
        Assert.Equal(2998, FloorGold.Total(world, db));
    }

    [Fact]
    public void ThreePilesAtThreeThousand()
    {
        (WorldState world, GameDatabase db) = Empty();
        CreatureState imp = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));
        imp.GoldHeld = 3000;

        Assert.Equal(3000, FloorGold.DropFrom(world, db, imp));

        var piles = new List<ObjectState>();
        FloorGold.PilesOn(world, db, (1 * world.Width) + 1, piles);
        Assert.Equal(3, piles.Count);
        Assert.All(piles, p => Assert.Equal(1000, p.GoldHeld));
    }

    [Fact]
    public void EmptyHandsLeaveNothingBehind()
    {
        (WorldState world, GameDatabase db) = Empty();
        CreatureState imp = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));

        Assert.Equal(0, FloorGold.DropFrom(world, db, imp));
        Assert.Empty(world.Objects);
    }

    /// <summary>
    /// Floor gold is nobody's until it is carried in. The keeper's total counts
    /// tiles, so a pile is not on anybody's books — which is the original's own
    /// accounting: making a floor pile out of a treasury tile <i>subtracts</i>
    /// the amount from the player.
    /// </summary>
    [Fact]
    public void APileIsNotPartOfAnyKeepersTotal()
    {
        (WorldState world, GameDatabase db) = Empty();

        FloorGold.Drop(world, db, 5, 2500);

        Assert.Equal(0, GoldStore.Total(world, Keeper));
        Assert.Equal(2500, FloorGold.Total(world, db));
        Assert.Equal(PlayerIds.Neutral, world.Objects[0].OwnerPlayerId);
    }

    /// <summary>
    /// The pile survives a save, because <see cref="ObjectState.GoldHeld"/> is
    /// in the snapshot and in the hash. A field that round-tripped as zero would
    /// make a loaded game quietly poorer.
    /// </summary>
    [Fact]
    public void APileSurvivesASnapshot()
    {
        (WorldState world, GameDatabase db) = Empty();
        var sim = new GameSimulation(db, world, new SimRandom(7), SimulationSystems.Default());
        FloorGold.Drop(world, db, 11, 1234);

        ulong before = sim.StateHash();
        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(sim), db, SimulationSystems.Default());

        Assert.Equal(before, restored.StateHash());
        Assert.Equal(1234, FloorGold.Total(restored.World, db));
    }
}

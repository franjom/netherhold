// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The gold carrier's store tile (<c>0x00464400</c>), the store deposits
/// (<c>0x004F3C60</c>, <c>0x004F4090</c>) and the store sweep (<c>0x004F47E0</c>) as the
/// 2026-10-05 verification audit's blind re-read found them (ADR-0355): which tile takes a
/// carrier, and the world-stream draws a new store pile and a swept leftover take.</summary>
public class StoreChooserAndDepositTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static (GameDatabase Db, WorldState World) World()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        return (database, world);
    }

    private static RoomInstance Room(GameDatabase db, WorldState world, byte terrain, int x0, int y0, int x1, int y1)
    {
        TerrainDef def = db.TerrainById(terrain);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = def.TerrainId;
                tile.OwnerPlayerId = Keeper;
                tile.Health = def.StartingHealth;
            }
        return RoomIndex.RoomAt(world, db, (y0 * world.Width) + x0)!;
    }

    /// <summary>Counts the draws by bound and hands back the low end of each.</summary>
    private sealed class Counting : IRandomAlgorithm
    {
        public readonly Dictionary<int, int> ByBound = [];
        public int NextInt(int exclusiveMax)
        {
            ByBound[exclusiveMax] = ByBound.GetValueOrDefault(exclusiveMax) + 1;
            return 0;
        }
        public void HashInto(ref Hash64 hash) { }
    }

    /// <summary>The chooser compares a tile's store pile with the <b>Treasury</b> tile cap, 3000,
    /// on a Heart too, so a Heart rim tile already at its own 1000 still takes a carrier. Ours
    /// passed it over for the next tile with room (<c>SpaceIn &gt; 0</c>).</summary>
    [Fact]
    public void AFullHeartRimTile_StillTakesACarrier_SinceTheChooserTestsTheTreasuryCap()
    {
        (GameDatabase db, WorldState world) = World();
        RoomInstance heart = Room(db, world, Dk2TerrainFixture.DungeonHeartTerrain, 5, 5, 9, 9);
        int rim = (5 * world.Width) + 5;
        world.TileAtIndex(rim).GoldHeld = db[VariableTypeId.MaxGoldPerDungeonHeartTile];

        Assert.Equal(rim, GoldStore.DepositTileAlongChain(world, db, heart, rim));
    }

    /// <summary>The Heart's builder stands its Arch Pillars on chain tiles 7, 16 and 18 and the heart
    /// on 12 (<c>0x004DB700</c>); ours keeps none of them as objects, but the chooser must still pass
    /// those tiles over, as DK2's does on finding a thing there.</summary>
    [Theory]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(18)]
    public void AHeartTileTheBuilderStoodAnObjectOn_IsPassedOver(int position)
    {
        (GameDatabase db, WorldState world) = World();
        RoomInstance heart = Room(db, world, Dk2TerrainFixture.DungeonHeartTerrain, 5, 5, 9, 9);
        int[] chain = SlabChain.Order(world, heart);

        Assert.Equal(chain[position + 1], GoldStore.DepositTileAlongChain(world, db, heart, chain[position]));
    }

    /// <summary>A tile with no store pile takes a carrier only when nothing at all lies on it: a
    /// floor pile on an empty Treasury tile turns the carrier on to the next tile.</summary>
    [Fact]
    public void AnEmptyTreasuryTileWithSomethingOnIt_IsPassedOver()
    {
        (GameDatabase db, WorldState world) = World();
        RoomInstance treasury = Room(db, world, Dk2TerrainFixture.TreasuryTerrain, 5, 5, 7, 5);
        int[] chain = SlabChain.Order(world, treasury);
        FloorGold.Drop(world, db, chain[0], 50);

        Assert.Equal(chain[1], GoldStore.DepositTileAlongChain(world, db, treasury, chain[0]));
    }

    /// <summary>Each store pile a deposit creates draws rand(0x800) on the world stream; a tile
    /// that already holds gold has its pile and draws nothing. 4000 from an empty three-tile
    /// Treasury's first tile makes two piles; ours drew nothing.</summary>
    [Fact]
    public void ADeposit_DrawsOnceForEachStorePileItCreates()
    {
        (GameDatabase db, WorldState world) = World();
        RoomInstance treasury = Room(db, world, Dk2TerrainFixture.TreasuryTerrain, 5, 5, 7, 5);
        int[] chain = SlabChain.Order(world, treasury);
        var counting = new Counting();

        int left = GoldStore.FillAlongChainFrom(world, db, treasury, chain[0], 4000, new SimRandom(counting));

        Assert.Equal(0, left);
        Assert.Equal(2, counting.ByBound.GetValueOrDefault(0x800));

        GoldStore.FillAlongChainFrom(world, db, treasury, chain[0], 500, new SimRandom(counting));
        Assert.Equal(2, counting.ByBound.GetValueOrDefault(0x800));
    }

    /// <summary>A live Heart sweeps every 16th turn full or not, and the sweep destroys each loose
    /// pile it deposits, so a pile on a full Heart goes back down as a new pile at one of eight
    /// points, a rand(8) each time. Ours left a pile nothing could take where it lay.</summary>
    [Fact]
    public void ALoosepileOnAFullHeart_IsPutBackDownAsANewPile_EverySweep()
    {
        (GameDatabase db, WorldState world) = World();
        RoomInstance heart = Room(db, world, Dk2TerrainFixture.DungeonHeartTerrain, 5, 5, 9, 9);
        foreach (int tile in heart.TileIndices)
            world.TileAtIndex(tile).GoldHeld = GoldStore.Capacity(world, db, tile, heart);
        int rim = (5 * world.Width) + 5;
        ObjectState pile = FloorGold.Drop(world, db, rim, 700)!;
        var counting = new Counting();
        var sim = new GameSimulation(db, world, new SimRandom(counting), [new StoreSweepSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        for (int i = 0; i < StoreSweepSystem.SweepTurns; i++) sim.Tick(InputFrame.Empty);

        SimEvent swept = Assert.Single(sink.Events, e => e.Kind == "swept");
        Assert.Contains("stored=0 left=700", swept.Detail);
        Assert.DoesNotContain(pile, world.Objects);
        ObjectState again = Assert.Single(world.Objects, o => o.ObjectTypeId == FloorGold.FloorGoldObjectId);
        Assert.Equal((rim, 700), (again.TileIndex, again.GoldHeld));
        Assert.Equal(1, counting.ByBound.GetValueOrDefault(8));
    }

    /// <summary>A full Treasury is not swept (<c>0x004F39A0</c> tests its tally against its
    /// capacity first), so its pile stays and nothing is drawn.</summary>
    [Fact]
    public void AFullTreasury_IsNotSwept()
    {
        (GameDatabase db, WorldState world) = World();
        RoomInstance treasury = Room(db, world, Dk2TerrainFixture.TreasuryTerrain, 5, 5, 7, 5);
        foreach (int tile in treasury.TileIndices)
            world.TileAtIndex(tile).GoldHeld = db[VariableTypeId.MaxGoldPerTreasuryTile];
        ObjectState pile = FloorGold.Drop(world, db, treasury.TileIndices[0], 700)!;
        var counting = new Counting();
        var sim = new GameSimulation(db, world, new SimRandom(counting), [new StoreSweepSystem()]);

        for (int i = 0; i <= StoreSweepSystem.SweepTurns; i++) sim.Tick(InputFrame.Empty);

        Assert.Contains(pile, world.Objects);
        Assert.Empty(counting.ByBound);
    }

    /// <summary>A failed aimed claim retry falls through to the per-kind record (<c>0x00463EEC</c>),
    /// so the claim search runs a second time and draws a second rand(4) (<c>0x00468D54</c>). Two
    /// imps alike but for the aim, on a map with nothing to claim, differ by exactly that draw.
    /// Ours searched once (<c>else if</c>).</summary>
    [Fact]
    public void AFailedAimedClaimRetry_SearchesForAClaimTwice()
    {
        int FourDraws(bool aimed)
        {
            GameDatabase db = Dk2TerrainFixture.Database();
            WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
            CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
            imp.LastTaskKind = ImpTaskKind.Claim;
            imp.DroppedTask = aimed ? ImpTaskKind.Claim : ImpTaskKind.None;
            var counting = new Counting();
            var sim = new GameSimulation(db, world, new SimRandom(counting), Dk2TerrainFixture.DefaultSystems());
            sim.Tick(InputFrame.Empty);
            sim.Tick(InputFrame.Empty); // the choice is made on the second tick
            return counting.ByBound.GetValueOrDefault(4);
        }

        Assert.Equal(FourDraws(aimed: false) + 1, FourDraws(aimed: true));
    }
}

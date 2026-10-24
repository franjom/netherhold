// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A store's gold as DK2's Gold Pile 1: the sparkle <c>0x004F3A00</c> and the Hand's lift
/// <c>0x004C0CB0</c> (gold-hover-and-sparkle.md §3, §4).</summary>
public sealed class StoreGoldTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    /// <summary>Hands out the queued draws, then 99, and records every bound asked for.</summary>
    private sealed class Scripted(params int[] draws) : IRandomAlgorithm
    {
        private readonly Queue<int> _draws = new(draws);
        public readonly List<int> Bounds = [];
        public int NextInt(int exclusiveMax)
        {
            Bounds.Add(exclusiveMax);
            return _draws.Count > 0 ? _draws.Dequeue() : Math.Min(99, exclusiveMax - 1);
        }
        public void HashInto(ref Hash64 hash) { }
    }

    private static (GameDatabase Db, WorldState World) World()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        return (database, WorldState.FromMap(database, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]));
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

    private static (GameSimulation Sim, ListEventSink Sink) Run(GameDatabase db, WorldState world, Scripted rng, PlayerCommand? command = null)
    {
        var sim = new GameSimulation(db, world, new SimRandom(rng), [new StoreSweepSystem(), new HandSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick(command is null ? InputFrame.Empty : InputFrame.Of(command));
        return (sim, sink);
    }

    [Fact]
    public void ATreasury_DrawsOnceATurn_AndItsTilesOnlyBelow30()
    {
        (GameDatabase db, WorldState world) = World();
        RoomInstance treasury = Room(db, world, Dk2TerrainFixture.TreasuryTerrain, 5, 5, 7, 5);
        foreach (int tile in treasury.TileIndices) world.TileAtIndex(tile).GoldHeld = 500;

        var high = new Scripted(30);
        Run(db, world, high);
        Assert.Equal([100], high.Bounds);

        var low = new Scripted(29);
        Run(db, world, low);
        Assert.Equal([100, 100, 100, 100], low.Bounds);   // the room's, then one per pile
    }

    [Fact]
    public void OnlyTilesHoldingGold_Draw_AndAPileBelow10_AsksForGoldSparkle_AtAnInferredHeight()
    {
        (GameDatabase db, WorldState world) = World();
        RoomInstance treasury = Room(db, world, Dk2TerrainFixture.TreasuryTerrain, 5, 5, 7, 5);
        int[] chain = SlabChain.Order(world, treasury);
        world.TileAtIndex(chain[0]).GoldHeld = 3000;
        world.TileAtIndex(chain[2]).GoldHeld = 1;

        var rng = new Scripted(0, 10, 9);
        (_, ListEventSink sink) = Run(db, world, rng);

        Assert.Equal(3, rng.Bounds.Count);
        ViewEffectRequest effect = Assert.Single(sink.Events, e => e.EffectRequest is not null).EffectRequest!.Value;
        Assert.Equal(34, effect.EffectId.Value);
        Assert.Equal(Keeper, effect.Owner.Value);
        Assert.Equal(world.TilePosition(chain[2]), effect.Tile);
        Assert.Equal(CreatureState.CentreOf(world.TilePosition(chain[2]).X), effect.Position.X.Raw);
        Assert.Equal(Fp32.One + 1068, effect.Height.Raw);   // floor plus Gold Pile 1's Height
        Assert.Equal(0, effect.Heading.Raw);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void AHeart_Sparkles_UnlessRuined(bool ruined, int draws)
    {
        (GameDatabase db, WorldState world) = World();
        Room(db, world, Dk2TerrainFixture.DungeonHeartTerrain, 5, 5, 9, 9);
        world.Player(Keeper).HeartRuined = ruined;

        var rng = new Scripted(30);
        Run(db, world, rng);

        Assert.Equal(draws, rng.Bounds.Count);
    }

    [Fact]
    public void OtherRooms_DoNotSparkle()
    {
        (GameDatabase db, WorldState world) = World();
        Room(db, world, Dk2TerrainFixture.LairTerrain, 5, 5, 7, 5);

        var rng = new Scripted();
        Run(db, world, rng);

        Assert.Empty(rng.Bounds);
    }

    [Fact]
    public void ALift_TakesTheTilesWholePile_IntoTheHand()
    {
        (GameDatabase db, WorldState world) = World();
        Room(db, world, Dk2TerrainFixture.TreasuryTerrain, 5, 5, 7, 5);
        world.Tile(6, 5).GoldHeld = 2500;
        world.Tile(7, 5).GoldHeld = 700;

        (_, ListEventSink sink) = Run(db, world, new Scripted(), new PickUpStoreGoldCommand(Keeper, new TilePos(6, 5)));

        HandItem held = Assert.Single(world.Player(Keeper).Hand);
        ObjectState pile = world.ObjectById(held.Id)!;
        Assert.Equal((FloorGold.TreasuryPileObjectId, 2500, true), (pile.ObjectTypeId, pile.GoldHeld, pile.InHand));
        Assert.Equal(0, world.Tile(6, 5).GoldHeld);
        Assert.Equal(700, GoldStore.Total(world, Keeper));
        Assert.Contains(sink.Events, e => e.Kind == "store_gold_picked_up");
    }

    [Theory]
    [InlineData(0)]    // nothing stored on the tile
    [InlineData(-1)]   // gold on a tile that is not a store
    [InlineData(64)]   // a full hand
    public void ALift_IsRefused_WithoutAStorePile_OrAFreeSlot(int setup)
    {
        (GameDatabase db, WorldState world) = World();
        Room(db, world, setup == -1 ? Dk2TerrainFixture.LairTerrain : Dk2TerrainFixture.TreasuryTerrain, 5, 5, 7, 5);
        world.Tile(6, 5).GoldHeld = setup == 0 ? 0 : 900;
        if (setup == 64)
        {
            ObjectState filler = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, 0);
            for (int i = 0; i < HandRules.Capacity(db); i++) world.Player(Keeper).Hand.Add(HandItem.Of(filler));
        }
        int held = world.Player(Keeper).Hand.Count, objects = world.Objects.Count;

        Run(db, world, new Scripted(), new PickUpStoreGoldCommand(Keeper, new TilePos(6, 5)));

        Assert.Equal(held, world.Player(Keeper).Hand.Count);
        Assert.Equal(objects, world.Objects.Count);
        Assert.Equal(setup == 0 ? 0 : 900, world.Tile(6, 5).GoldHeld);
    }

    [Fact]
    public void TheCursorOffersAStorePile_OnlyToItsOwner()
    {
        (GameDatabase db, WorldState world) = World();
        Room(db, world, Dk2TerrainFixture.TreasuryTerrain, 5, 5, 7, 5);
        world.Tile(6, 5).GoldHeld = 900;

        Assert.True(HandRules.MayReachStoreGold(world, new TilePos(6, 5), Keeper));
        Assert.False(HandRules.MayReachStoreGold(world, new TilePos(6, 5), PlayerIds.Neutral));
    }
}

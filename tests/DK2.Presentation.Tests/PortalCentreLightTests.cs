// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class PortalCentreLightTests
{
    private static readonly TilePos Centre = new(5, 5);

    [Fact]
    public void InitialSourceHasNativeInputsAndCheapClassification()
    {
        var sources = new PortalCentreLights(new EffectTime(0));
        sources.Create(Centre);
        ClassicPointLight light = Assert.Single(sources.Advance(new EffectTime(0))).Light;
        Assert.Equal(new Vector3(5.5f, 2000f / 4096, -5.5f), light.Position);
        Assert.Equal(0x2780 / 4096f, light.Radius);
        Assert.Equal(1, light.Intensity);
        Assert.Equal(new Vector3(1, 33f / 255, 26f / 255), light.Colour);
        Assert.Equal(ClassicLightFlags.NormalObject, light.Flags);
        Assert.Equal(ClassicLightFlags.CheapObject, Assert.Single(sources.Advance(new EffectTime(0), true)).Light.Flags);
    }

    [Fact]
    public void StrictClockAdvancesOnceAfterAStall()
    {
        var sources = new PortalCentreLights(new EffectTime(0));
        sources.Create(Centre);
        Assert.Equal(1, Assert.Single(sources.Advance(new EffectTime(50))).Light.Colour.X);
        Assert.Equal(Vector3.Zero, Assert.Single(sources.Advance(new EffectTime(1001))).Light.Colour);
        Assert.Equal(Vector3.Zero, Assert.Single(sources.Advance(new EffectTime(1050))).Light.Colour);
        Assert.Equal(new Vector3(10f / 255, 1f / 255, 1f / 255), Assert.Single(sources.Advance(new EffectTime(1051))).Light.Colour);
    }

    [Fact]
    public void PulseHasFiftyUpdatesAndNativeGreenBlueSwap()
    {
        var sources = new PortalCentreLights(new EffectTime(0));
        sources.Create(Centre);
        for (int update = 1; update <= 51; update++)
        {
            ClassicPointLight light = Assert.Single(sources.Advance(new EffectTime(update * 50 + 1))).Light;
            if (update == 26) Assert.Equal(new Vector3(250f / 255, 25f / 255, 32f / 255), light.Colour);
            if (update == 51) Assert.Equal(Vector3.Zero, light.Colour);
        }
    }

    [Fact]
    public void EachRefreshCreatesAnotherSourceWithItsOwnPulseState()
    {
        var sources = new PortalCentreLights(new EffectTime(0));
        sources.Create(Centre);
        sources.Advance(new EffectTime(51));
        sources.Advance(new EffectTime(101));
        sources.Create(Centre);
        IReadOnlyList<PortalLightView> lights = sources.Advance(new EffectTime(101));
        Assert.Equal(2, lights.Count);
        Assert.NotEqual(lights[0].Id, lights[1].Id);
        Assert.Equal(new Vector3(10f / 255, 1f / 255, 1f / 255), lights[0].Light.Colour);
        Assert.Equal(new Vector3(1, 33f / 255, 26f / 255), lights[1].Light.Colour);
    }

    [Fact]
    public void AdjoiningFogTopReceivesLightOnlyInsideSphereWithInvertedNormal()
    {
        var sources = new PortalCentreLights(new EffectTime(0));
        sources.Create(Centre);
        var cache = new ClassicWorldLightCache(12, 12);
        cache.UpdatePortalLights(sources.Advance(new EffectTime(0)));
        cache.PrepareFrame([new TilePos(7, 5)]);
        ClassicPointLight light = Assert.Single(cache.VisibleTables[0].Lights.ToArray());
        Assert.True(light.ContributionAt(new Vector3(7.2f, 2, -5.5f), -Vector3.UnitY).X > 0);
        Assert.Equal(Vector3.Zero, light.ContributionAt(new Vector3(7.2f, 2, -5.5f), Vector3.UnitY));
        Assert.Equal(Vector3.Zero, light.ContributionAt(new Vector3(8, 2, -5.5f), -Vector3.UnitY));
        sources.Create(Centre);
        cache.UpdatePortalLights(sources.Advance(new EffectTime(0)));
        cache.PrepareFrame([new TilePos(7, 5)]);
        Assert.Equal(2, cache.VisibleTables[0].Lights.Length);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(25, 1)]
    [InlineData(2, 0)]
    public void MapRequestsDoNotExploreOrLeakOtherRooms(byte roomId, int count)
    {
        var (db, world) = Map(roomId);
        var simulation = new GameSimulation(db, world, 1);
        ulong hash = simulation.StateHash();
        var view = new WorldView(db, 3);
        Assert.Equal(count, view.PortalLightTiles(world).Count);
        Assert.Equal(count, view.PortalLightTiles(world).Count);
        if (count != 0) Assert.Equal(new TilePos(3, 3), Assert.Single(view.PortalLightTiles(world)));
        Assert.False(view.TileAt(world, 3, 3).Explored);
        Assert.False(view.TileAt(world, 5, 3).Explored);
        Assert.Equal(hash, simulation.StateHash());
    }

    [Fact]
    public void TenSlabLoadedEntranceKeepsTheSourceCreatedAtNine()
    {
        var (db, world) = Map(25, extraTile: true);
        Assert.Equal(new TilePos(3, 3), Assert.Single(new WorldView(db, 3).PortalLightTiles(world)));
    }

    [Fact]
    public void RefreshNotificationsKeepMultipleSameTickRequestsAndActualFifthSlab()
    {
        var (db, world) = Map(25);
        var (controlDb, control) = Map(25);
        var view = new WorldView(db, 3);
        Assert.Single(view.PortalLightTiles(world));
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Change(world, db, 2, 12);
            Assert.Equal(repeat + 1, view.PortalLightTiles(world).Count);
            Change(world, db, 12, 2);
            Change(control, controlDb, 2, 12);
            Change(control, controlDb, 12, 2);
        }
        Assert.Equal(3, view.PortalLightTiles(world).Count);
        Assert.Equal(new TilePos(4, 3), view.PortalLightTiles(world)[1]);
        Assert.Equal(new GameSimulation(controlDb, control, 1).StateHash(), new GameSimulation(db, world, 1).StateHash());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CaptureCreatesALightOnlyWhenAnActualMergeReachesNine(bool merge, bool growsPastNine)
    {
        var (db, world) = Map(25);
        for (int y = 2; y <= 4; y++)
            for (int x = 2; x <= 4; x++)
                if (!merge || x == 2 || y == 2) world.Tile(x, y).OwnerPlayerId = 3;
        if (growsPastNine) world.Tile(5, 2).TerrainId = 12;
        var (controlDb, control) = Map(25);
        for (int i = 0; i < world.TileCount; i++) control.TileAtIndex(i) = world.TileAtIndex(i);
        var view = new WorldView(db, 2);
        int before = view.PortalLightTiles(world).Count;
        Assert.Equal(merge ? 0 : 1, before);
        TileWorkResult result = RoomHealthRules.Convert(world, db, new TilePos(2, 2), new PlayerId(2));
        Assert.Equal(TileWorkOutcome.Transformed, result.Outcome);
        Assert.Equal(merge ? before + 1 : before, view.PortalLightTiles(world).Count);
        Assert.Equal(TileWorkOutcome.Transformed, RoomHealthRules.Convert(control, controlDb, new TilePos(2, 2), new PlayerId(2)).Outcome);
        Assert.Equal(new GameSimulation(controlDb, control, 1).StateHash(), new GameSimulation(db, world, 1).StateHash());
    }

    private static void Change(WorldState world, GameDatabase db, byte terrain, byte before)
    {
        var tile = new TilePos(2, 2);
        world.Tile(tile).TerrainId = terrain;
        SlabChain.Changed(world, db, world.TileIndex(tile), before, new PlayerId(2));
    }

    private static (GameDatabase, WorldState) Map(byte roomId, bool extraTile = false)
    {
        TerrainDef Terrain(byte id, TerrainFlags flags) => new()
        {
            Name = id == 27 ? "Fog Of War" : id == 38 ? "Claim Scan" : "test", TerrainId = id, Flags = flags,
            StartingHealth = 1, MaxHealth = 1, GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = 0,
            MaxHealthTypeTerrainId = id, DestroyedTypeTerrainId = id, Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var room = new RoomDef
        {
            Name = "test", RoomId = roomId, TerrainId = 12, Flags = RoomFlags.PlaceableOnLand,
            Construction = TileConstruction.ThreeByThree, Cost = 0, ReturnPercentage = 0,
            CreatedCreatureId = 0, ObjectIds = [], HealthGain = 0, CeilingHeight = new Fp32(0),
            RecommendedSizeX = 3, RecommendedSizeY = 3, OrderInEditor = 0, NameStringId = 0, TooltipStringId = 0,
        };
        var variables = new GlobalVariables();
        variables.Misc[VariableTypeId.DungeonHeartObjectHealth] = new(VariableTypeId.DungeonHeartObjectHealth, 100, 0, 0);
        variables.Misc[VariableTypeId.ConvertRoomHealth] = new(VariableTypeId.ConvertRoomHealth, -100, 0, 0);
        var db = new GameDatabase([Terrain(2, TerrainFlags.Solid), Terrain(12, TerrainFlags.Room | TerrainFlags.Ownable | TerrainFlags.Attackable),
            Terrain(27, TerrainFlags.Solid), Terrain(38, TerrainFlags.Solid)], [room], [], variables);
        var cells = Enumerable.Repeat(new MapTile(2, 2, 0, 0), 64).ToArray();
        for (int y = 2; y <= 4; y++)
            for (int x = 2; x <= 4; x++) cells[y * 8 + x] = new MapTile(12, 2, 0, 0);
        if (extraTile) cells[4 * 8 + 5] = new MapTile(12, 2, 0, 0);
        WorldState world = WorldState.FromMap(db, new LevelMap(8, 8, cells), [2, 3], fogOfWarEnabled: true);
        for (int i = 0; i < world.TileCount; i++) world.TileAtIndex(i).ExploredBy = PlayerMask.None;
        return (db, world);
    }
}

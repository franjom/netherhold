// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class TerrainPointLightViewTests
{
    [Fact]
    public void TorchAdmissionTracksExplorationScanningAndPossessionWithoutChangingLayout()
    {
        var (world, view, layout, target, presenter) = TorchRig();
        TorchView? authored = layout.At(4);
        Assert.NotNull(authored);
        Assert.DoesNotContain(4, target.Torches);
        world.Tile(1, 1).SetDiscoveredBy(1);
        presenter.Present(world);
        Assert.DoesNotContain(4, target.Torches);
        world.Tile(1, 1).SetExploredBy(1);
        presenter.Present(world);
        Assert.Contains(4, target.Torches);
        Assert.Equal(1, target.TorchCreates);
        presenter.Present(world);
        Assert.Equal(0, presenter.TorchesPushed);
        world.Tile(1, 1).ClearExploredBy(1);
        presenter.Present(world);
        Assert.DoesNotContain(4, target.Torches);
        view.SetPossessionView(true);
        presenter.Present(world);
        Assert.Contains(4, target.Torches);
        view.SetPossessionView(false);
        presenter.Present(world);
        Assert.DoesNotContain(4, target.Torches);
        Assert.Equal(authored, layout.At(4));
        Assert.Empty(layout.Update(world));
    }

    [Fact]
    public void SightOpeningAndExpiryRefreshTorchesAndTilesWithoutChangingTerrainOrRegionVersion()
    {
        var (world, view, layout, target, presenter) = TorchRig();
        var simulation = new GameSimulation(TorchDatabase(), world, 1, []);
        TorchView? authored = layout.At(4);
        view.SetSightRegion(new SightRegion(GameTick.Zero, new TilePos(1, 1), 64, 5, 3, 3));
        presenter.Present(world);
        Assert.DoesNotContain(4, target.Torches);
        int version = view.PreviewVersion;
        simulation.Run(1);
        target.Updates = 0;
        presenter.Present(world);
        Assert.Contains(4, target.Torches);
        Assert.True(target.Updates > 0);
        Assert.Equal(version, view.PreviewVersion);
        simulation.Run(63);
        target.Updates = 0;
        presenter.Present(world);
        Assert.DoesNotContain(4, target.Torches);
        Assert.True(target.Updates > 0);
        Assert.Equal(version, view.PreviewVersion);
        Assert.Equal(authored, layout.At(4));
        Assert.Empty(layout.Update(world));
    }

    private static GameDatabase TorchDatabase() => Database([
        Terrain(1) with { Flags = TerrainFlags.RevealThroughFogOfWar },
        Terrain(2, "Wall") with { Flags = TerrainFlags.Solid | TerrainFlags.Torch },
        Terrain(27, "Fog Of War") with { Flags = TerrainFlags.Solid },
        Terrain(38, "Claim Scan") with { Flags = TerrainFlags.Solid }]);

    private static (WorldState, WorldView, TorchLayout, Target, ScenePresenter) TorchRig()
    {
        GameDatabase database = TorchDatabase();
        WorldState world = World(database, fog: true);
        world.Tile(1, 0).TerrainId = 2;
        var view = new WorldView(database, 1);
        var layout = new TorchLayout(database);
        var target = new Target();
        var presenter = new ScenePresenter(view, AssetProviderChain.Standard(), target, layout);
        presenter.Present(world);
        return (world, view, layout, target, presenter);
    }

    [Theory]
    [InlineData(TileConstruction.QuadConstruction)]
    [InlineData(TileConstruction.Normal)]
    [InlineData(TileConstruction.DoubleQuad)]
    public void RoomSourceRequiresAllTerrainAndOwnerNeighbours(TileConstruction construction)
    {
        var database = Database([Terrain(1)], [Room(construction)]);
        WorldState world = World(database);
        var view = new WorldView(database, 1);
        Assert.NotNull(view.TileAt(world, 1, 1).PointLight);
        Assert.Null(view.TileAt(world, 0, 0).PointLight);
        world.Tile(0, 0).OwnerPlayerId = 2;
        Assert.Null(view.TileAt(world, 1, 1).PointLight);
        world.Tile(0, 0).OwnerPlayerId = 1;
        world.Tile(0, 0).TerrainId = 2;
        Assert.Null(view.TileAt(world, 1, 1).PointLight);
    }

    [Theory]
    [InlineData(TileConstruction.ThreeByThree)]
    [InlineData(TileConstruction.ThreeByThreeRotated)]
    [InlineData(TileConstruction.FiveByFiveRotated)]
    public void PositionCodedRoomConstructorsDoNotBecomeMaskInteriors(TileConstruction construction)
    {
        var database = Database([Terrain(1)], [Room(construction)]);
        Assert.Null(new WorldView(database, 1).TileAt(World(database), 1, 1).PointLight);
    }

    [Fact]
    public void PlainSourcePreservesBoostedWordsAndRequiresAuthoredHeightAndFlag()
    {
        TerrainDef terrain = Terrain(1) with { Flags = TerrainFlags.TerrainLight | TerrainFlags.TerrainColorRed };
        var database = Database([terrain]);
        ClassicPointLight light = Assert.IsType<ClassicPointLight>(new WorldView(database, 1).TileAt(World(database), 1, 1).PointLight);
        Assert.Equal(new Vector3(1.5f, 2, -1.5f), light.Position);
        Assert.Equal(275 / 255f, light.Colour.X);
        Assert.Equal(ClassicLightFlags.Terrain, light.Flags);
        foreach (TerrainDef absent in new[] { terrain with { TerrainLightHeight = null }, terrain with { Flags = 0 } })
        {
            database = Database([absent]);
            Assert.Null(new WorldView(database, 1).TileAt(World(database), 1, 1).PointLight);
        }
    }

    [Fact]
    public void DoorSuppressesPlainTerrainSource()
    {
        var database = Database([Terrain(1)]);
        WorldState world = World(database);
        ThingProjection.Place(world, database,
            [new DoorThing(new byte[20]) { X = 1, Y = 1, DoorId = 1, PlayerId = 1, Unknown_0011 = new byte[3] }]);
        Assert.Null(new WorldView(database, 1).TileAt(world, 1, 1).PointLight);
    }

    [Theory]
    [InlineData(false, false, 0, -1)]
    [InlineData(true, false, 0, 200)]
    [InlineData(true, true, 0, 200)]
    [InlineData(true, true, 5, 20)]
    public void FogUsesSelectedTerrainAndNativeScanOverride(bool scanned, bool reveal, ushort gold, int red)
    {
        var database = Database([
            Terrain(1) with { GoldValue = gold, Flags = TerrainFlags.TerrainLight | (reveal ? TerrainFlags.RevealThroughFogOfWar : 0) },
            Terrain(27, "Fog Of War", 7), Terrain(38, "Claim Scan", 9)]);
        WorldState world = World(database, fog: true);
        world.Tile(1, 1).ExploredBy = default;
        world.Tile(1, 1).DiscoveredBy = default;
        if (scanned) world.Tile(1, 1).SetDiscoveredBy(1);
        var view = new WorldView(database, 1);
        ClassicPointLight? light = view.TileAt(world, 1, 1).PointLight;
        if (red < 0) Assert.Null(light);
        else Assert.Equal(red / 255f, light!.Value.Colour.X);
        WorldState fogOff = World(database);
        fogOff.Tile(1, 1).ExploredBy = default;
        if (scanned) fogOff.Tile(1, 1).SetDiscoveredBy(1);
        Assert.Equal(20 / 255f, view.TileAt(fogOff, 1, 1).PointLight!.Value.Colour.X);
    }

    [Fact]
    public void PossessionExemptionKeepsNativeBitsForScanColour()
    {
        var database = Database([Terrain(1), Terrain(38, "Claim Scan", red: 9)]);
        WorldState world = World(database, fog: true);
        world.Tile(1, 1).SetDiscoveredBy(1);
        var view = new WorldView(database, 1);
        view.SetPossessionView(true);
        Assert.False(view.TileAt(world, 1, 1).Explored);
        Assert.Equal(200 / 255f, view.TileAt(world, 1, 1).PointLight!.Value.Colour.X);
    }

    [Fact]
    public void ScannedRoomUsesItsActualInteriorBeforePlainTerrainFogSelection()
    {
        var database = Database([Terrain(1) with { GoldValue = 5 },
            Terrain(27, "Fog Of War", red: 7),
            Terrain(38, "Claim Scan", red: 9) with { Flags = TerrainFlags.Solid | TerrainFlags.TerrainLight }],
            [Room(TileConstruction.Normal)]);
        WorldState world = World(database, fog: true);
        world.Tile(1, 1).SetDiscoveredBy(1);
        var view = new WorldView(database, 1);
        Assert.Equal("terrain.claim_scan.mesh", view.TileAt(world, 1, 1).Ground.Key);
        Assert.Equal(20 / 255f, view.TileAt(world, 1, 1).PointLight!.Value.Colour.X);
        world.Tile(0, 0).OwnerPlayerId = 2;
        Assert.Null(view.TileAt(world, 1, 1).PointLight);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanSelectionRetainsProxyHeightAndSkipsDoorGuardEvenWhenPossessed(bool possession)
    {
        var database = Database([Terrain(1) with { TerrainLightHeight = new Fp32(8192) },
            Terrain(27, "Fog Of War"),
            Terrain(38, "Claim Scan") with { TerrainLightHeight = new Fp32(12288) }]);
        WorldState world = World(database, fog: true);
        world.Tile(1, 1).SetDiscoveredBy(1);
        ThingProjection.Place(world, database,
            [new DoorThing(new byte[20]) { X = 1, Y = 1, DoorId = 1, PlayerId = 1, Unknown_0011 = new byte[3] }]);
        var view = new WorldView(database, 1);
        view.SetPossessionView(possession);
        ClassicPointLight light = view.TileAt(world, 1, 1).PointLight!.Value;
        Assert.Equal(3, light.Position.Y);
        Assert.Equal(200 / 255f, light.Colour.X);
    }

    [Fact]
    public void PresenterPushesChangedPointLightWithoutOtherVisualChanges()
    {
        var database = Database([Terrain(1), Terrain(2, red: 50)]);
        WorldState world = World(database);
        var target = new Target();
        var view = new WorldView(database, 1);
        var presenter = new ScenePresenter(view, AssetProviderChain.Standard(), target);
        presenter.Present(world);
        world.Tile(1, 1).TerrainId = 2;
        presenter.Present(world);
        Assert.Equal(1, target.Updates);
        Assert.Equal(50 / 255f, target.Last.PointLight!.Value.Colour.X);
        presenter.Present(world);
        Assert.Equal(1, target.Updates);
    }

    [Fact]
    public void PresenterPushesLightAndScanChangesWithoutOtherVisualChanges()
    {
        var database = Database([Terrain(1)]);
        WorldState world = World(database);
        var target = new Target();
        var presenter = new ScenePresenter(new WorldView(database, 1), AssetProviderChain.Standard(), target);
        presenter.Present(world);
        target.Updates = 0;
        world.Tile(1, 1).SetDiscoveredBy(1);
        presenter.Present(world);
        Assert.Equal(1, target.Updates);
        Assert.True(target.Last.Scanned);
        Assert.NotNull(target.Last.PointLight);
        presenter.Present(world);
        Assert.Equal(1, target.Updates);
    }

    private static TerrainDef Terrain(byte id, string name = "Path", byte red = 20) => new()
    {
        Name = name, TerrainId = id, Flags = TerrainFlags.TerrainLight, StartingHealth = 1,
        MaxHealth = 1, GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = 0,
        MaxHealthTypeTerrainId = id, DestroyedTypeTerrainId = id, Depth = default,
        NameStringId = 0, TooltipStringId = 0, TerrainLightHeight = new Fp32(8192),
        Lighting = new TerrainLighting(red, 30, 40, 0, 0, 0),
    };

    private static RoomDef Room(TileConstruction construction) => new()
    {
        Name = "Room", RoomId = 3, TerrainId = 1, Flags = 0, Cost = 0, ReturnPercentage = 0,
        Construction = construction, CreatedCreatureId = 0, ObjectIds = [], HealthGain = 0,
        CeilingHeight = default, RecommendedSizeX = 0, RecommendedSizeY = 0, OrderInEditor = 0,
        NameStringId = 0, TooltipStringId = 0,
    };

    private static GameDatabase Database(TerrainDef[] terrain, RoomDef[]? rooms = null) =>
        new(terrain, rooms ?? [], [], new GlobalVariables(), doors:
            [new DoorDef { Name = "Door", DoorId = 1, Health = 100, GoldCost = 0, ManufactureRequired = 0, Flags = 0 }]);

    private static WorldState World(GameDatabase database, bool fog = false)
    {
        WorldState world = WorldState.FromMap(database, new LevelMap(3, 3,
            Enumerable.Repeat(new MapTile(1, 1, 0, 0), 9).ToArray()), [1, 2], fogOfWarEnabled: fog);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++) world.Tile(x, y).OwnerPlayerId = 1;
        return world;
    }

    private sealed class Target : IRenderTarget
    {
        public int Updates;
        public int TorchCreates;
        public readonly HashSet<int> Torches = [];
        public TileView Last;
        public void CreateTile(int i, in TileView t, in ResolvedAsset g, in ResolvedAsset f) { }
        public void UpdateTile(int i, in TileView t, in ResolvedAsset g, in ResolvedAsset f) { Updates++; Last = t; }
        public void CreateCreature(int id, in CreatureView c, in ResolvedAsset m, in ResolvedAsset a) { }
        public void UpdateCreature(int id, in CreatureView c, in ResolvedAsset m, in ResolvedAsset a) { }
        public void RemoveCreature(int id) { }
        public void CreateObject(int id, in ObjectView o, in ResolvedAsset m) { }
        public void UpdateObject(int id, in ObjectView o, in ResolvedAsset m) { }
        public void RemoveObject(int id) { }
        public void CreateStructure(StructureId id, in StructureView s, in ResolvedAsset m) { }
        public void UpdateStructure(StructureId id, in StructureView s, in ResolvedAsset m) { }
        public void RemoveStructure(StructureId id) { }
        public void CreateTorch(int id, in TorchView t, in ResolvedAsset m) { TorchCreates++; Torches.Add(id); }
        public void UpdateTorch(int id, in TorchView t, in ResolvedAsset m) { }
        public void RemoveTorch(int id) { Torches.Remove(id); }
        public void Flush() { }
        public void Clear() { }
    }
}

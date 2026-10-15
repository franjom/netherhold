// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Data;
using DK2.Core;
using DK2.Art;
using DK2.Simulation;
using DK2.Data.Model;
using DK2.TestSupport;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Presentation.Tests;

public sealed class StructureViewTests
{
    private static (DK2.Data.Model.GameDatabase Database, WorldState World) Level8()
    {
        var database = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        var map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level8")!;
        var world = WorldState.FromMap(database, map, [0, 1, 2, 3, 4], fogOfWarEnabled: false);
        ThingProjection.Place(world, database, GameDatabaseLoader.LoadCampaignThings(Dk2InstallGate.Path, "Level8")!);
        return (database, world);
    }

    [InstallFact]
    public void DoorLocksResolveTheirOwnShippedChainModels()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        using var provider = InstallArtProvider.Open(Dk2InstallGate.Path, database);
        foreach (var (id, modelName) in new[] { (1, "Door_ChainWooden"), (3, "Door_ChainRound"), (8, "Door_Chain_HeroPortcullis") })
        {
            AssetRef key = AssetRef.Create(AssetRef.DoorCategory, database.Doors[(byte)id].Name, AssetKind.Mesh, "lock");
            Assert.True(provider.TryResolve(key, out ResolvedAsset asset), key.Key);
            Assert.Contains(modelName, asset.InstallEntry!, StringComparison.OrdinalIgnoreCase);
            Assert.True(provider.TryLoadModel(asset, out var model));
            Assert.NotEmpty(RenderMesh.FromModel(model).Positions);
        }
    }

    [InstallFact]
    public void SnapdragonAuthoredLocksRemainClosedAndRequestChainsAfterTheFirstUpdate()
    {
        var (database, world) = Level8();
        var locked = world.Doors.Where(door => (door.Flags & 1) != 0).ToArray();
        Assert.Equal(new[] { new TilePos(20, 28), new TilePos(24, 39) },
            locked.Select(door => new TilePos(door.X, door.Y)).OrderBy(tile => tile.X).ToArray());
        new GameSimulation(database, world, 7, [new DoorSystem()]).Tick(InputFrame.Empty);
        var view = new WorldView(database);
        foreach (DoorState door in locked)
        {
            Assert.Equal(3, door.LockMode);
            Assert.False(door.Open);
            StructureView drawn = view.Structures(world).Single(value => value.Id == new StructureId(StructureKind.Door, door.Id));
            Assert.Equal(AssetRef.ForDoorLock(database.Doors[door.DoorId].Name), drawn.LockMesh);
        }
    }

    [InstallFact]
    public void EveryShippedDoorAndTrapHasCatalogueMeshes()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var keys = AssetCatalogue.ForDatabase(database).Select(value => value.Key).ToArray();
        Assert.Equal(database.Doors.Count * 3, keys.Count(value => value.StartsWith("door.")));
        Assert.Equal(database.Traps.Count, keys.Count(value => value.StartsWith("trap.")));
    }

    [InstallFact]
    public void SnapdragonStructuresUseShippedModelsWithoutChangingSimulation()
    {
        var (database, world) = Level8();
        var before = Hash64.Start();
        world.HashInto(ref before);
        StructureView[] structures = new WorldView(database).Structures(world).ToArray();
        Assert.NotEmpty(world.Doors);
        Assert.Equal(world.Doors.Count, structures.Count(value => value.Id.Kind == StructureKind.Door));
        Assert.Equal(world.Traps.Count(value => !value.Hidden || value.PlayerId == 1),
            structures.Count(value => value.Id.Kind == StructureKind.Trap));
        using var provider = InstallArtProvider.Open(Dk2InstallGate.Path, database);
        foreach (StructureView structure in structures)
        {
            Assert.True(provider.TryResolve(structure.Mesh, out ResolvedAsset asset), structure.Mesh.Key);
            Assert.True(provider.TryLoadModel(asset, out var model), asset.InstallEntry);
            int frame = structure.LastFrame && model.Animation is { IsComplete: true }
                ? model.Animation.Frames.Count - 1 : 0;
            RenderMesh mesh = RenderMesh.FromModel(model, frame);
            Assert.NotEmpty(mesh.Positions);
            Assert.True(mesh.Positions.Max(vertex => vertex.Y) + structure.Height / (float)Fp32.One > 1f, asset.InstallEntry);
            Assert.Equal(Fp32.One, structure.Height);
            Assert.Equal(structure.Tile.X * Fp32.One + Fp32.One / 2, structure.PosX);
        }
        var after = Hash64.Start();
        world.HashInto(ref after);
        Assert.Equal(before.Value, after.Value);
    }

    [InstallFact]
    public void DoorOpenChangesMeshAndHeadingMatchesSupportOrientation()
    {
        var (database, world) = Level8();
        var door = world.Doors.First();
        var view = new WorldView(database);
        var id = new StructureId(StructureKind.Door, door.Id);
        StructureView closed = view.Structures(world).Single(value => value.Id == id);
        Assert.Equal(door.Orientation == 0 ? 0x600 : 0x400, closed.MeshYaw);
        door.Open = true;
        StructureView open = view.Structures(world).Single(value => value.Id == id);
        Assert.NotEqual(closed.Mesh, open.Mesh);
        Assert.Equal(closed.Id, open.Id);
        Assert.Equal(closed.Tile, open.Tile);
    }

    [Fact]
    public void ForeignConstructionAndHiddenTrapsAreOmitted()
    {
        var (database, world) = Fixture();
        var door = world.Doors.First(value => value.PlayerId != 1);
        door.UnderConstruction = true;
        Assert.DoesNotContain(new WorldView(database).Structures(world),
            value => value.Id == new StructureId(StructureKind.Door, door.Id));
        Assert.Contains(new WorldView(database, door.PlayerId).Structures(world),
            value => value.Id == new StructureId(StructureKind.Door, door.Id));
        var trap = world.Traps.First(value => value.PlayerId != 1);
        trap.Hidden = true;
        Assert.DoesNotContain(new WorldView(database).Structures(world),
            value => value.Id == new StructureId(StructureKind.Trap, trap.Id));
        Assert.Contains(new WorldView(database, trap.PlayerId).Structures(world),
            value => value.Id == new StructureId(StructureKind.Trap, trap.Id));
    }

    [Fact]
    public void LockAttachmentFollowsSettledStateAndBothSupportOrientations()
    {
        var (database, world) = Fixture();
        var view = new WorldView(database);
        foreach (DoorState door in world.Doors) door.LockMode = 3;
        StructureView[] locked = view.Structures(world).ToArray();
        Assert.Equal(2, locked.Length);
        Assert.All(locked, door => Assert.Equal(AssetRef.ForDoorLock("Door"), door.LockMesh));
        var changed = world.Doors.First();
        var changedId = new StructureId(StructureKind.Door, changed.Id);
        changed.LockPickTicks = 10;
        Assert.True(view.Structures(world).Single(door => door.Id == changedId).LockMesh.IsEmpty);
        changed.LockPickTicks = 0;
        changed.Open = true;
        Assert.True(view.Structures(world).Single(door => door.Id == changedId).LockMesh.IsEmpty);
        changed.Open = false;
        changed.LockMode = 0;
        Assert.True(view.Structures(world).Single(door => door.Id == changedId).LockMesh.IsEmpty);
        changed.LockMode = 3;
        changed.Health = 0;
        Assert.True(view.Structures(world).Single(door => door.Id == changedId).LockMesh.IsEmpty);
        changed.Health = 100;
        changed.LockMode = 3;
        changed.UnderConstruction = true;
        Assert.True(new WorldView(database, changed.PlayerId).Structures(world)
            .Single(door => door.Id == changedId).LockMesh.IsEmpty);
    }

    [Theory]
    [InlineData(DoorFlags.Barricade)]
    [InlineData(DoorFlags.Secret)]
    public void BarricadesAndUnmodelledSecretDisguisesDoNotShowLocks(DoorFlags flags)
    {
        var (database, world) = Fixture(doorFlags: flags);
        foreach (DoorState door in world.Doors) door.LockMode = 3;
        Assert.All(new WorldView(database).Structures(world), structure => Assert.True(structure.LockMesh.IsEmpty));
    }

    [Fact]
    public void PresenterTracksBothRegistriesAndOnlyPushesVisibleChanges()
    {
        var (database, world) = Fixture();
        var target = new Target();
        var presenter = new ScenePresenter(new WorldView(database), AssetProviderChain.Standard(), target);
        var door = world.Doors.First();
        var trap = world.Traps.First();
        Assert.Equal(door.Id, trap.Id);
        var doorId = new StructureId(StructureKind.Door, door.Id);
        var trapId = new StructureId(StructureKind.Trap, trap.Id);
        presenter.Present(world);
        Assert.Contains(doorId, target.Visible.Keys);
        Assert.DoesNotContain(trapId, target.Visible.Keys);
        Assert.Equal(2, target.Creates);
        presenter.Present(world);
        Assert.Equal(2, target.Creates);
        Assert.Equal(0, target.Updates);
        door.Open = true;
        trap.Hidden = false;
        presenter.Present(world);
        Assert.Equal(3, target.Creates);
        Assert.Equal(1, target.Updates);
        Assert.Contains(trapId, target.Visible.Keys);
        Assert.Equal(AssetRef.ForDoor("Door", open: true), target.Visible[doorId].Mesh);
        door.Open = false;
        door.LockMode = 3;
        presenter.Present(world);
        Assert.Equal(2, target.Updates);
        Assert.Equal(AssetRef.ForDoorLock("Door"), target.Visible[doorId].LockMesh);
        trap.Heading = 0x200;
        presenter.Present(world);
        Assert.Equal(3, target.Updates);
        Assert.Equal(0x600, target.Visible[trapId].MeshYaw);
        trap.Hidden = true;
        door.UnderConstruction = true;
        presenter.Present(world);
        Assert.Equal(2, target.Removes);
        Assert.DoesNotContain(doorId, target.Visible.Keys);
        Assert.DoesNotContain(trapId, target.Visible.Keys);
        presenter.Resync();
        presenter.Present(world);
        Assert.Equal(4, target.Creates);
    }

    [Fact]
    public void BothWallOrientationsAndFogAreRespected()
    {
        var (database, world) = Fixture(fog: true);
        var view = new WorldView(database);
        Assert.Empty(view.Structures(world));
        foreach (DoorState door in world.Doors) world.Tile(door.X, door.Y).SetExploredBy(1);
        StructureView[] doors = view.Structures(world).ToArray();
        Assert.Equal(2, doors.Length);
        Assert.Contains(doors, door => door.MeshYaw == 0x600);
        Assert.Contains(doors, door => door.MeshYaw == 0x400);
    }

    private static (GameDatabase, WorldState) Fixture(bool fog = false, DoorFlags doorFlags = DoorFlags.None)
    {
        TerrainDef Terrain(byte id, TerrainFlags flags) => new()
        {
            Name = id == 1 ? "Floor" : "Wall", TerrainId = id, Flags = flags,
            StartingHealth = 100, MaxHealth = 100, GoldValue = 0, ManaGain = 0, MaxManaGain = 0,
            Damage = 0, MaxHealthTypeTerrainId = id, DestroyedTypeTerrainId = id,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var database = new GameDatabase([Terrain(1, 0), Terrain(2, TerrainFlags.Solid)], [], [],
            new GlobalVariables(), doors: [new DoorDef { Name = "Door", DoorId = 1,
                Health = 100, GoldCost = 0, ManufactureRequired = 0, Flags = doorFlags }],
            traps: [new TrapDef { Name = "Trap", TrapId = 1, Health = 100,
                GoldCost = 0, ManufactureRequired = 0, Flags = 0x20, ShotTypeId = 0, ManaUpkeepPerSecond = 0 }]);
        var tiles = Enumerable.Repeat(new MapTile(1, 2, 0, 0), 35).ToArray();
        foreach (TilePos wall in new[] { new TilePos(2, 1), new TilePos(2, 3), new TilePos(4, 2), new TilePos(6, 2) })
            tiles[wall.Y * 7 + wall.X] = new MapTile(2, 2, 0, 0);
        var world = WorldState.FromMap(database, new LevelMap(7, 5, tiles), [1, 2], fogOfWarEnabled: fog);
        ThingProjection.Place(world, database,
        [
            new DoorThing(new byte[20]) { X = 2, Y = 2, DoorId = 1, PlayerId = 2, Unknown_0011 = new byte[3] },
            new DoorThing(new byte[20]) { X = 5, Y = 2, DoorId = 1, PlayerId = 2, Unknown_0011 = new byte[3] },
            new TrapThing(new byte[16]) { X = 1, Y = 2, TrapId = 1, PlayerId = 2, NumberOfShots = 1 },
        ]);
        return (database, world);
    }

    private sealed class Target : IRenderTarget
    {
        public readonly Dictionary<StructureId, StructureView> Visible = [];
        public int Creates, Updates, Removes;
        public void CreateStructure(StructureId id, in StructureView s, in ResolvedAsset m) { Visible[id] = s; Creates++; }
        public void UpdateStructure(StructureId id, in StructureView s, in ResolvedAsset m) { Visible[id] = s; Updates++; }
        public void RemoveStructure(StructureId id) { Visible.Remove(id); Removes++; }
        public void CreateTile(int i, in TileView t, in ResolvedAsset g, in ResolvedAsset f) { }
        public void UpdateTile(int i, in TileView t, in ResolvedAsset g, in ResolvedAsset f) { }
        public void CreateCreature(int i, in CreatureView c, in ResolvedAsset m, in ResolvedAsset a) { }
        public void UpdateCreature(int i, in CreatureView c, in ResolvedAsset m, in ResolvedAsset a) { }
        public void RemoveCreature(int i) { }
        public void CreateObject(int i, in ObjectView o, in ResolvedAsset m) { }
        public void UpdateObject(int i, in ObjectView o, in ResolvedAsset m) { }
        public void RemoveObject(int i) { }
        public void CreateTorch(int i, in TorchView t, in ResolvedAsset m) { }
        public void UpdateTorch(int i, in TorchView t, in ResolvedAsset m) { }
        public void RemoveTorch(int i) { }
        public void Flush() { }
    }
}

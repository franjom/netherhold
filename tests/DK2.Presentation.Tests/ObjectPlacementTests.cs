// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Art;
using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Where objects stand, which way they face and which art they show (furniture-placement.md,
/// object-drawing.md), on rooms carved into the shipped Level 1.</summary>
public class ObjectPlacementTests
{
    private const int X0 = 10, Y0 = 10;
    private static readonly PlayerId Keeper = new(3);

    private sealed record Room(GameDatabase Database, WorldState World, WorldView View);

    /// <summary>A room inside a ring of Reinforced Wall, owned by player 3, its top-left tile at
    /// (X0 + 1, Y0 + 1).</summary>
    private static Room Carve(RoomTypeId type, int width, int height)
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!;
        WorldState world = WorldState.FromMap(db, map, [0, 1]);
        byte wall = db.Terrain.Values.Single(t => t.Name == "Reinforced Wall").TerrainId;
        byte floor = db.Room(type).TerrainId;
        for (int y = Y0; y <= Y0 + height + 1; y++)
            for (int x = X0; x <= X0 + width + 1; x++)
            {
                bool ring = x == X0 || x == X0 + width + 1 || y == Y0 || y == Y0 + height + 1;
                world.Tile(x, y).TerrainId = ring ? wall : floor;
                world.Tile(x, y).OwnerPlayerId = 3;
            }
        return new Room(db, world, new WorldView(db));
    }

    private static List<ObjectView> Decorations(Room room, byte objectTypeId) =>
        room.View.Decorations(room.World).Where(o => o.ObjectTypeId == objectTypeId).ToList();

    private static int Centre(int tile) => (tile * Fp32.One) + (Fp32.One / 2);

    [InstallTheory]
    [InlineData(0)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(1536)]
    public void AChickenFacesItsSavedMovementHeading(int heading)
    {
        Room room = Carve(RoomTypeId.Hatchery, 3, 3);
        ObjectState chicken = room.World.SpawnObject(room.Database, HatcheryRules.ChickenObjectId, Keeper,
            (Y0 + 2) * room.World.Width + X0 + 2);
        chicken.Heading = heading;
        ObjectView rendered = room.View.Objects(room.World).Single(o => o.Id == chicken.Id);
        Assert.Equal(heading, rendered.Heading);
        Assert.Equal((heading + 1024) & 2047, rendered.MeshYaw);
    }

    [InstallFact]
    public void HatcheryBirdsRetainTheirIndividualPositionsAcrossHatching()
    {
        Room room = Carve(RoomTypeId.Hatchery, 3, 3);
        RoomInstance hatchery = RoomIndex.Build(room.World, room.Database)
            .Single(r => r.Definition.TypeId == RoomTypeId.Hatchery && r.Contains((Y0 + 2) * room.World.Width + X0 + 2));
        HatcheryRules.PlaceCoops(room.World, room.Database, hatchery);
        var rng = new SimRandom(7);
        for (int i = 0; i < 9; i++)
        {
            ObjectState egg = HatcheryRules.TryLayEgg(room.World, room.Database, hatchery, rng)!;
            ObjectView eggView = room.View.Objects(room.World).Single(o => o.Id == egg.Id);
            Assert.Equal((egg.PosX, egg.PosY), (eggView.PosX, eggView.PosY));
            Assert.DoesNotContain(room.View.Decorations(room.World), o => o.ObjectTypeId == HatcheryRules.PillarObjectId && o.Tile == eggView.Tile);
            ObjectState bird = HatcheryRules.Hatch(room.World, room.Database, egg);
            ObjectView birdView = room.View.Objects(room.World).Single(o => o.Id == bird.Id);
            Assert.Equal((eggView.PosX, eggView.PosY), (birdView.PosX, birdView.PosY));
        }
        ObjectView[] birds = room.View.Objects(room.World).Where(o => o.ObjectTypeId == HatcheryRules.ChickenObjectId).ToArray();
        Assert.Equal(9, birds.Select(o => (o.PosX, o.PosY)).Distinct().Count());
        Assert.True(birds.Select(o => o.Tile).Distinct().Count() > 1);
    }

    [InstallFact]
    public void AStrappedMistressUsesHerTorturePoseAtTheWallWheel()
    {
        Room room = Carve(RoomTypeId.TortureChamber, 3, 5);
        RoomInstance chamber = RoomIndex.Build(room.World, room.Database)
            .Single(r => r.Definition.TypeId == RoomTypeId.TortureChamber);
        int tile = chamber.TileIndices.First(t => AutoFurnitureRules.WallSide(room.World, room.Database, chamber, t) == 0);
        ObjectState device = room.World.SpawnObject(room.Database, 8, Keeper, tile);
        device.AutoPlaced = true;
        CreatureState mistress = room.World.SpawnCreature(room.Database, 4, Keeper,
            new TilePos(tile % room.World.Width, tile / room.World.Width));
        Assert.True(TortureRules.TryStrapOwn(new StateHookContext(room.World, room.Database), mistress, tile));

        CreatureView victim = room.View.Creatures(room.World).Single(c => c.Id == mistress.Id);
        ObjectView wheel = room.View.Objects(room.World).Single(o => o.Id == device.Id);
        Assert.Equal(AssetRef.ForCreature("Mistress", AssetKind.Animation, "being_tortured"), victim.Mesh);
        Assert.Equal((wheel.PosX, wheel.PosY + 0x1C2), (victim.PosX, victim.PosY));
        Assert.Equal(Fp32.One + 0x800, victim.Height);
        Assert.Equal((wheel.MeshYaw, 0, wheel.Roll), (victim.MeshYaw, victim.Pitch, victim.Roll));
        using var art = InstallArtProvider.Open(Dk2InstallGate.Path, room.Database);
        Assert.True(art.TryResolve(victim.Mesh, out ResolvedAsset resolved));
        Assert.Equal("Mis_Torture.kmf", resolved.InstallEntry);
    }

    [InstallFact]
    public void AFloorWheelVictimLiesOnItsDiscAndFollowsItsTurn()
    {
        Room room = Carve(RoomTypeId.TortureChamber, 3, 5);
        var at = new TilePos(X0 + 2, Y0 + 3);
        int tile = at.Y * room.World.Width + at.X;
        ObjectState device = room.World.SpawnObject(room.Database, 8, Keeper, tile);
        device.AutoPlaced = true;
        CreatureState mistress = room.World.SpawnCreature(room.Database, 4, Keeper, at);
        Assert.True(TortureRules.TryStrapOwn(new StateHookContext(room.World, room.Database), mistress, tile));
        CreatureView victim = room.View.Creatures(room.World).Single(c => c.Id == mistress.Id);
        ObjectView wheel = room.View.Objects(room.World).Single(o => o.Id == device.Id);
        Assert.Equal((wheel.PosX, wheel.PosY), (victim.PosX, victim.PosY));
        Assert.Equal(Fp32.One + 0x200, victim.Height);
        Assert.Equal((wheel.MeshYaw, 0x600, 0), (victim.MeshYaw, victim.Pitch, victim.Roll));
        TortureRules.Unstrap(new StateHookContext(room.World, room.Database), mistress);
        CreatureView released = room.View.Creatures(room.World).Single(c => c.Id == mistress.Id);
        Assert.Equal(AssetRef.ForCreature("Mistress", AssetKind.Mesh), released.Mesh);
        Assert.Equal((Fp32.One, 0, 0), (released.Height, released.Pitch, released.Roll));
    }

    /// <summary>A 3×3 Treasury takes four pillars, one in each corner tile, headings 0x600, 0, 0x200,
    /// 0x400 for NE, SE, SW, NW (<c>0x004EF1F0</c>). DK2 moves each a quarter tile inward and its draw
    /// offset moves it back, so it is drawn at its tile's centre (object-drawing.md §4).</summary>
    [InstallFact]
    public void AThreeByThreeTreasuryHasAPillarInEachCornerTile()
    {
        List<ObjectView> pillars = Decorations(Carve(RoomTypeId.Treasury, 3, 3), 76);

        Assert.Equal(4, pillars.Count);
        ObjectView northWest = pillars.Single(p => p.Tile == new TilePos(X0 + 1, Y0 + 1));
        Assert.Equal((Centre(X0 + 1), Centre(Y0 + 1)), (northWest.PosX, northWest.PosY));
        Assert.Equal(0, northWest.Heading);
        Assert.Equal(0x400, pillars.Single(p => p.Tile == new TilePos(X0 + 3, Y0 + 3)).Heading);
    }

    /// <summary>A room shorter than three tiles on either axis has no pillars.</summary>
    [InstallFact]
    public void ATwoWideTreasuryHasNoPillars() =>
        Assert.Empty(Decorations(Carve(RoomTypeId.Treasury, 2, 5), 76));

    /// <summary>A treasury tile's gold shows Urn1 below 1000, Urn2 below 2000 and the Chest from 2000
    /// (<c>0x004F4440</c>, cap 3000); an empty tile shows nothing.</summary>
    [InstallFact]
    public void TreasuryGoldShowsUrnsThenTheChestByFill()
    {
        Room room = Carve(RoomTypeId.Treasury, 3, 3);
        room.World.Tile(X0 + 1, Y0 + 1).GoldHeld = 999;
        room.World.Tile(X0 + 2, Y0 + 1).GoldHeld = 1000;
        room.World.Tile(X0 + 3, Y0 + 1).GoldHeld = 2000;

        List<ObjectView> piles = Decorations(room, 3);
        string name = room.Database.ObjectById(3)!.Name;
        Assert.Equal(3, piles.Count);
        Assert.Equal(AssetCatalogue.ObjectArt(name, 5), piles.Single(p => p.GoldHeld == 999).Mesh);
        Assert.Equal(AssetCatalogue.ObjectArt(name, 6), piles.Single(p => p.GoldHeld == 1000).Mesh);
        Assert.Equal(AssetRef.ForObject(name, AssetKind.Mesh), piles.Single(p => p.GoldHeld == 2000).Mesh);
    }

    /// <summary>A Guard Room's posts stand on its convex corners, 0x82A out along the diagonal
    /// (<c>0x004DDE80</c>): four for a 3×3, the NW one at (−0x58F, −0x5FB) from its tile's centre.</summary>
    [InstallFact]
    public void AGuardRoomHasAPostOnEachConvexCorner()
    {
        List<ObjectView> posts = Decorations(Carve(RoomTypeId.GuardRoom, 3, 3), 70);

        Assert.Equal(4, posts.Count);
        ObjectView northWest = posts.Single(p => p.Tile == new TilePos(X0 + 1, Y0 + 1));
        Assert.Equal((Centre(X0 + 1) - 0x58F, Centre(Y0 + 1) - 0x5FB), (northWest.PosX, northWest.PosY));
        Assert.Equal(Fp32.One, northWest.Height);
    }

    /// <summary>A 5×5 Heart stands its Heart object, three Big Steps a third of a turn apart and the
    /// Arches on chain index 12, taken to be its centre tile (STRONGLY INFERRED from the chain
    /// order), in the pit (height 0; <c>0x004DB700</c>, <c>0x004545C0</c>).</summary>
    [InstallFact]
    public void AHeartStandsItsSetOnChainIndex12TakenToBeTheCentre()
    {
        Room room = Carve(RoomTypeId.DungeonHeart, 5, 5);
        var centre = new TilePos(X0 + 3, Y0 + 3);

        // Level 1's own Heart is elsewhere on the map; only the carved one's centre is asked.
        ObjectView heart = Decorations(room, 13).Single(o => o.Tile == centre);
        Assert.Equal(0, heart.Height);
        Assert.True(heart.PlaysAnimation);
        Assert.False(Decorations(room, 86).Single(o => o.Tile == centre).PlaysAnimation);
        Assert.Equal([0, 0x2AA, 0x554], Decorations(room, 88).Where(o => o.Tile == centre).Select(s => s.Heading).OrderBy(h => h));
        Assert.Single(Decorations(room, 86), o => o.Tile == centre);
    }

    /// <summary>A Book Case faces by its row and shows book_case1 to 3 by the coordinate selector
    /// (the arm at <c>0x004EE6A5</c>). The selector itself is the simulation's, pinned by its own
    /// tests; this pins that the view asks it for three and adds five.</summary>
    [InstallFact]
    public void ABookCaseShowsArtFivePlusTheSelectorsDrawOfThree()
    {
        Room room = Carve(RoomTypeId.Library, 3, 3);
        int x = X0 + 2, y = Y0 + 2;
        ObjectState shelf = room.World.SpawnObject(room.Database, 64, Keeper, (y * room.World.Width) + x);
        shelf.AutoPlaced = true;

        ObjectView view = room.View.Objects(room.World).Single(o => o.Id == shelf.Id);
        Assert.Equal(AssetCatalogue.ObjectArt(room.Database.ObjectById(64)!.Name, 5 + AutoFurnitureRules.Choice(x, y, 3)),
            view.Mesh);
        Assert.Equal((y & 1) << 10, view.Heading);
    }

    /// <summary>A floor pile is drawn at half size empty and grows with its gold (<c>0x0049C760</c>).</summary>
    [InstallFact]
    public void AFloorPileGrowsWithItsGold()
    {
        Room room = Carve(RoomTypeId.Treasury, 3, 3);
        ObjectState pile = room.World.SpawnObject(room.Database, 1, PlayerIds.Unowned, (5 * room.World.Width) + 5);
        pile.GoldHeld = 1000;

        Assert.Equal(0x800 + 500, room.View.Objects(room.World).Single(o => o.Id == pile.Id).Scale);
    }

    /// <summary>A Workshop's wall object stands 0x700 toward its wall and faces into the room, the
    /// default arm <c>0x004EEE1F</c> included.</summary>
    [InstallFact]
    public void WallFurnitureStandsTowardItsWallAndFacesIn()
    {
        Room room = Carve(RoomTypeId.Workshop, 3, 5);
        RoomDef workshop = room.Database.Room(RoomTypeId.Workshop);
        byte wallObject = workshop.ObjectIds[3];
        RoomInstance instance = RoomIndex.Build(room.World, room.Database).Single(r => r.Definition.TypeId == RoomTypeId.Workshop);
        int tile = instance.TileIndices.First(t => AutoFurnitureRules.WallSide(room.World, room.Database, instance, t) >= 0);
        int side = AutoFurnitureRules.WallSide(room.World, room.Database, instance, tile);
        ObjectState thing = room.World.SpawnObject(room.Database, wallObject, Keeper, tile);
        thing.AutoPlaced = true;

        ObjectView view = room.View.Objects(room.World).Single(o => o.Id == thing.Id);
        int[] dx = [0, 1, 0, -1], dy = [-1, 0, 1, 0];
        Assert.Equal((Centre(tile % room.World.Width) + (dx[side] * 0x700), Centre(tile / room.World.Width) + (dy[side] * 0x700)),
            (view.PosX, view.PosY));
        Assert.Equal(((side + 2) & 3) << 9, view.Heading);
    }

    /// <summary>A Temple's candlesticks stand on its convex corners, a diagonal quarter tile out
    /// (<c>0x004F1240</c>): four for a 5×5, the NE one at (+0x400, −0x400).</summary>
    [InstallFact]
    public void ATempleHasACandlestickOnEachConvexCorner()
    {
        List<ObjectView> candles = Decorations(Carve(RoomTypeId.Temple, 5, 5), 111);

        Assert.Equal(4, candles.Count);
        ObjectView northEast = candles.Single(c => c.Tile == new TilePos(X0 + 5, Y0 + 1));
        Assert.Equal((Centre(X0 + 5) + 0x400, Centre(Y0 + 1) - 0x400), (northEast.PosX, northEast.PosY));
    }

    /// <summary>A sleeping creature's bed is hidden, the creature showing its own sleep clip (state
    /// 83, <c>0x0046FA60</c>); awake, the bed is drawn.</summary>
    [InstallFact]
    public void ABedIsHiddenWhileItsCreatureSleeps()
    {
        Room room = Carve(RoomTypeId.Lair, 3, 3);
        CreatureDef goblin = room.Database.Creatures.Values.First(c => c.LairObjectId == 22);
        CreatureState sleeper = room.World.SpawnCreature(room.Database, goblin.CreatureId, Keeper, new TilePos(X0 + 2, Y0 + 2));
        ObjectState bed = room.World.SpawnObject(room.Database, 22, Keeper, ((Y0 + 2) * room.World.Width) + X0 + 2);
        bed.CreatureId = sleeper.Id;

        Assert.Contains(room.View.Objects(room.World), o => o.Id == bed.Id);
        sleeper.Dk2StateId = LairRules.SleepingState;
        Assert.DoesNotContain(room.View.Objects(room.World), o => o.Id == bed.Id);
    }

    /// <summary>A Prison's door and bar stand on its door tile, facing 0x400 (<c>0x004E32C0</c>).</summary>
    [InstallFact]
    public void APrisonStandsItsDoorAndBarOnTheDoorTile()
    {
        Room room = Carve(RoomTypeId.Prison, 4, 4);
        ObjectView door = Decorations(room, 109).Single();
        ObjectView bar = Decorations(room, 116).Single();

        Assert.Equal(door.Tile, bar.Tile);
        Assert.Equal((0x400, 0x400), (door.Heading, bar.Heading));
    }

    [InstallFact]
    public void PrisonDoorMeshesStandAtTheCellEntranceRatherThanTheNorthWall()
    {
        Room room = Carve(RoomTypeId.Prison, 4, 4);
        using InstallArtProvider art = InstallArtProvider.Open(Dk2InstallGate.Path, room.Database);
        foreach (byte type in new byte[] { 109, 116 })
        {
            ObjectView instance = Decorations(room, type).Single();
            Assert.True(art.TryResolve(instance.Mesh, out ResolvedAsset resolved));
            Assert.True(art.TryLoadModel(resolved, out var model));
            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, instance.MeshYaw * MathF.PI / 1024f);
            Vector3[] vertices = RenderMesh.FromModel(model).Positions.Select(p => Vector3.Transform(p, turn)).ToArray();
            // The cell is south of its north-rim door tile: both meshes must face that opening.
            Assert.InRange(vertices.Min(p => -p.Z), 0.19f, 0.6f);
            Assert.InRange(vertices.Max(p => -p.Z), 0.19f, 0.6f);
            Assert.InRange(vertices.Min(p => p.Y), 0f, 0.42f);
        }
    }

    /// <summary>A floor Torture Wheel lies flat (<c>+0x64 = 0x600</c>) and spins about the vertical;
    /// a wheel on a wall stands, drawn half a tile up (<c>+0x99 = 0x800</c>), with no pitch
    /// (furniture-placement.md §2–§3, object-drawing.md §4).</summary>
    [InstallFact]
    public void AFloorTortureWheelLiesFlatAndAWallWheelStandsHalfATileUp()
    {
        Room room = Carve(RoomTypeId.TortureChamber, 3, 5);
        RoomInstance instance = RoomIndex.Build(room.World, room.Database).Single(r => r.Definition.TypeId == RoomTypeId.TortureChamber);
        int floorTile = ((Y0 + 3) * room.World.Width) + X0 + 2;
        int wallTile = instance.TileIndices.First(t => AutoFurnitureRules.WallSide(room.World, room.Database, instance, t) >= 0);
        ObjectState floorWheel = room.World.SpawnObject(room.Database, 8, Keeper, floorTile);
        ObjectState wallWheel = room.World.SpawnObject(room.Database, 8, Keeper, wallTile);
        floorWheel.AutoPlaced = wallWheel.AutoPlaced = true;

        ObjectView flat = room.View.Objects(room.World).Single(o => o.Id == floorWheel.Id);
        ObjectView upright = room.View.Objects(room.World).Single(o => o.Id == wallWheel.Id);
        Assert.Equal(0x600, flat.Pitch);
        Assert.Equal(0, upright.Pitch);
        Assert.Equal(Fp32.One + 0x800, upright.Height);
    }
}

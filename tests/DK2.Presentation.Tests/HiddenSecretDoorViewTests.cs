// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>A Secret Door nobody has uncovered is drawn as a door only for its owner. For any other
/// viewer DK2 leaves the door and its lock undrawn and draws the tile as the owner's Reinforced Wall
/// (the handler of bridge command <c>0x11</c>, <c>0x004488A0</c>, and the terrain builder's prelude
/// at <c>0x0043E8FC</c>).</summary>
public sealed class HiddenSecretDoorViewTests
{
    private static readonly PlayerId DoorOwner = new(4);
    private static readonly PlayerId Stranger = new(3);

    /// <summary>Level 8 with the first of its doors made a Secret Door of <see cref="DoorOwner"/>'s.</summary>
    private static (GameDatabase Database, WorldState World, DoorState Door) Level8WithASecretDoor()
    {
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level8")!;
        var world = WorldState.FromMap(database, map, [0, 1, 2, 3, 4], fogOfWarEnabled: false);

        byte secretDoorId = database.Doors.Values.First(door => (door.Flags & DoorFlags.Secret) == DoorFlags.Secret).DoorId;
        DoorThing shipped = GameDatabaseLoader.LoadCampaignThings(Dk2InstallGate.Path, "Level8")!.OfType<DoorThing>().First();
        ThingProjection.Place(world, database, [new DoorThing(new byte[20])
        {
            X = shipped.X, Y = shipped.Y, PlayerId = DoorOwner.Value, DoorId = secretDoorId, Unknown_0011 = new byte[3],
        }]);
        return (database, world, world.Doors.Single());
    }

    private static bool DrawsTheDoor(WorldView view, WorldState world, DoorState door) =>
        view.Structures(world).Any(structure => structure.Id == new StructureId(StructureKind.Door, door.Id));

    [InstallFact]
    public void TheOwnerSeesItsHiddenDoorAsADoor()
    {
        (GameDatabase? database, WorldState? world, DoorState? door) = Level8WithASecretDoor();
        Assert.True(world.Tile(door.X, door.Y).SecretDoor);
        var view = new WorldView(database, DoorOwner);

        Assert.True(DrawsTheDoor(view, world, door));
        TileView tile = view.TileAt(world, door.X, door.Y);
        Assert.True(tile.HasDoor);
        Assert.False(tile.Solid);
    }

    [InstallFact]
    public void AnotherPlayerSeesAWallWhereTheHiddenDoorStands()
    {
        (GameDatabase? database, WorldState? world, DoorState? door) = Level8WithASecretDoor();
        var view = new WorldView(database, Stranger);

        Assert.False(DrawsTheDoor(view, world, door));
        TileView tile = view.TileAt(world, door.X, door.Y);
        Assert.False(tile.HasDoor);
        Assert.True(tile.Solid);

        // It is the view a real Reinforced Wall on that tile gives.
        TileView wall = view.TileAt(WallInsteadOfTheDoor(database, door), door.X, door.Y);
        Assert.Equal(wall.PieceKeys, tile.PieceKeys);
        Assert.Equal(wall.Pieces, tile.Pieces);
        Assert.Equal(wall.Walls, tile.Walls);
        Assert.Equal(wall.OwnerPlayerId, tile.OwnerPlayerId);
    }

    [InstallFact]
    public void OnceUncoveredTheDoorIsDrawnForEveryone()
    {
        (GameDatabase? database, WorldState? world, DoorState? door) = Level8WithASecretDoor();
        var view = new WorldView(database, Stranger);
        world.Tile(door.X, door.Y).SecretDoor = false;

        Assert.True(DrawsTheDoor(view, world, door));
        TileView tile = view.TileAt(world, door.X, door.Y);
        Assert.True(tile.HasDoor);
        Assert.False(tile.Solid);
    }

    /// <summary>The neighbours treat the hidden door as wall too: the tile beside it along the passage
    /// is drawn exactly as it would be beside a real Reinforced Wall.</summary>
    [InstallFact]
    public void TheTilesBesideAHiddenDoorAreDrawnAsBesideAWall()
    {
        (GameDatabase? database, WorldState? world, DoorState? door) = Level8WithASecretDoor();
        WorldState walled = WallInsteadOfTheDoor(database, door);
        var view = new WorldView(database, Stranger);
        var walledView = new WorldView(database, Stranger);

        foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int x = door.X + dx, y = door.Y + dy;
            TileView beside = view.TileAt(world, x, y), besideAWall = walledView.TileAt(walled, x, y);
            Assert.Equal(besideAWall.Walls, beside.Walls);
            Assert.Equal(besideAWall.Pieces, beside.Pieces);
        }
    }

    /// <summary>The same level with no door and the door owner's Reinforced Wall on its tile.</summary>
    private static WorldState WallInsteadOfTheDoor(GameDatabase database, DoorState door)
    {
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level8")!;
        var world = WorldState.FromMap(database, map, [0, 1, 2, 3, 4], fogOfWarEnabled: false);
        ref TileState tile = ref world.Tile(door.X, door.Y);
        tile.TerrainId = 9;
        return world;
    }
}

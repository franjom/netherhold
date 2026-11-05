// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A closed, completed door pushes the creatures on its tile off it (door update
/// <c>0x004A55F0</c> step 4, docs/research/door-push.md): a wall down column 10 with the door in it at
/// (10,10), its walls north and south, so the push runs along x. The push adds to a creature's
/// persistent velocity, which the physical pass (<c>0x004B2030</c>) spends.</summary>
public class DoorPushTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte WoodenDoor = 1;
    private const byte Barricade = 6;
    private static readonly TilePos DoorTile = new(10, 10);

    private static int Centre(int tile) => (tile * Fp32.One) + 0x800;

    private static (GameDatabase db, WorldState world, DoorState door) Walled(byte doorId, byte flags = 0, bool built = true)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                tile.TerrainId = x == DoorTile.X ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.DirtPath;
            }
        }
        ref TileState at = ref world.Tile(DoorTile);
        at.TerrainId = Dk2TerrainFixture.ClaimedPath;
        at.OwnerPlayerId = Keeper;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = DoorTile.X, Y = DoorTile.Y, PlayerId = Keeper, Flags = flags, DoorId = doorId, Unknown_0011 = new byte[3] }]);
        DoorState door = world.Doors[0];
        door.UnderConstruction = !built;
        return (db, world, door);
    }

    /// <summary>The creature thing pass with the doors after it, as DK2's: the pass is the movement
    /// step's, and the push is the door update's.</summary>
    private static GameSimulation Sim(GameDatabase db, WorldState world, bool withDoorSystem = true)
    {
        ISimulationSystem[] systems = withDoorSystem
            ? [new DoorSystem(), new CreatureMovementSystem(), new DoorHealthSystem()]
            : [new CreatureMovementSystem(), new DoorHealthSystem()];
        return new GameSimulation(db, world, 1, systems);
    }

    private static int TurnsUntilOffTheDoorTile(GameSimulation sim, CreatureState creature, int limit)
    {
        for (int turn = 1; turn <= limit; turn++)
        {
            sim.Run(1);
            if (creature.Tile != DoorTile) return turn;
        }
        return -1;
    }

    /// <summary>The owner's report: Imps standing on a barricade when it is completed stayed in it. The
    /// barricade is closed for good and reaches the push before its early return.</summary>
    [Theory]
    [InlineData(0x300)]
    [InlineData(-0x300)]
    [InlineData(0x10)]
    public void AnImpOnACompletedBarricadeIsPushedOutAlongThePassage(int offsetFromCentre)
    {
        (GameDatabase db, WorldState world, DoorState door) = Walled(Barricade);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, DoorTile);
        imp.PosX = Centre(DoorTile.X) + offsetFromCentre;
        GameSimulation sim = Sim(db, world);

        int turns = TurnsUntilOffTheDoorTile(sim, imp, 60);

        Assert.InRange(turns, 1, 40);
        Assert.False(door.Open, "a barricade never opens");
        Assert.Equal(offsetFromCentre > 0 ? DoorTile.X + 1 : DoorTile.X - 1, imp.Tile.X);
        Assert.Equal(Centre(DoorTile.Y), imp.PosY);

        // Off the tile the push stops, and the air friction brings what it had to a standstill.
        sim.Run(60);
        Assert.Equal(0, imp.PhysicalVelocityX);
        Assert.Equal(0, imp.PhysicalVelocityY);
        Assert.NotEqual(DoorTile, imp.Tile);
    }

    /// <summary>On the push point itself the vector is the creature's heading, as the flee step back's:
    /// heading 512 is east.</summary>
    [Fact]
    public void ACreatureOnThePushPointIsPushedAlongItsHeading()
    {
        (GameDatabase db, WorldState world, _) = Walled(Barricade);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, DoorTile);
        imp.PosX = Centre(DoorTile.X);
        imp.PosY = Centre(DoorTile.Y);
        imp.Heading = 512;
        GameSimulation sim = Sim(db, world);

        sim.Run(1);

        Assert.Equal(PushVector(512).X, imp.PhysicalVelocityX);
        Assert.Equal(0, imp.PhysicalVelocityY);
        Assert.True(TurnsUntilOffTheDoorTile(sim, imp, 60) > 0);
        Assert.Equal(DoorTile.X + 1, imp.Tile.X);
    }

    private static (int X, int Y) PushVector(int heading) =>
        ((int)(((long)0x80 * TrapRules.Sine(heading)) >> 16), -(int)(((long)0x80 * TrapRules.Cosine(heading)) >> 16));

    /// <summary>The push's first turn is the door update's: it adds <c>0x80</c> along the passage and
    /// the creature has not moved; the pass spends it the next turn and the air friction takes 327 of 4096.</summary>
    [Fact]
    public void ThePushIsAVelocityTheNextTurnSpends_NotAMove()
    {
        (GameDatabase db, WorldState world, _) = Walled(Barricade);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, DoorTile);
        imp.PosX = Centre(DoorTile.X) + 0x300;
        int startX = imp.PosX;
        GameSimulation sim = Sim(db, world);

        sim.Run(1);
        Assert.Equal(startX, imp.PosX);
        Assert.Equal(0x80, imp.PhysicalVelocityX);

        sim.Run(1);
        Assert.Equal(startX + (0x80 / 16), imp.PosX);
        // The pass dropped 327/4096 of 0x80, then the door added 0x80 again.
        Assert.Equal((0x80 * (4096 - 327) / 4096) + 0x80, imp.PhysicalVelocityX);
    }

    /// <summary>The push is for every creature: a rival's, in any state, on the keeper's door.</summary>
    [Fact]
    public void ARivalsCreatureOnAClosedWoodenDoorIsPushedToo_AndALockDoesNotStopIt()
    {
        (GameDatabase db, WorldState world, DoorState door) = Walled(WoodenDoor, flags: 0x01);
        Assert.Equal(3, door.LockMode);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, DoorTile);
        goblin.PosX = Centre(DoorTile.X) - 0x200;
        GameSimulation sim = Sim(db, world);

        Assert.False(door.Open);
        Assert.InRange(TurnsUntilOffTheDoorTile(sim, goblin, 60), 1, 40);
        Assert.Equal(DoorTile.X - 1, goblin.Tile.X);
    }

    [Fact]
    public void AnOpenDoorPushesNobody()
    {
        (GameDatabase db, WorldState world, DoorState door) = Walled(WoodenDoor);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, DoorTile);
        goblin.PosX = Centre(DoorTile.X) + 0x300;
        door.Open = true;
        GameSimulation sim = Sim(db, world, withDoorSystem: false);

        sim.Run(30);

        Assert.Equal(DoorTile, goblin.Tile);
        Assert.Equal(0, goblin.PhysicalVelocityX);
    }

    /// <summary>A door still a blueprint (<c>+0x109</c> not zero) pushes nobody: the push is the built door's.</summary>
    [Fact]
    public void ABlueprintPushesNobody_ButItsCompletionDoes()
    {
        (GameDatabase db, WorldState world, DoorState door) = Walled(Barricade, built: false);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, DoorTile);
        imp.PosX = Centre(DoorTile.X) + 0x300;
        GameSimulation sim = Sim(db, world);

        sim.Run(30);
        Assert.Equal(DoorTile, imp.Tile);
        Assert.Equal(0, imp.PhysicalVelocityX);

        door.UnderConstruction = false;
        Assert.InRange(TurnsUntilOffTheDoorTile(sim, imp, 60), 1, 40);
    }

    /// <summary>A creature on the neighbouring tile is not on the door's chain and is left alone.</summary>
    [Fact]
    public void ACreatureBesideTheDoorIsNotPushed()
    {
        (GameDatabase db, WorldState world, _) = Walled(Barricade);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(11, 10));
        int x = imp.PosX;
        GameSimulation sim = Sim(db, world);

        sim.Run(30);

        Assert.Equal(x, imp.PosX);
        Assert.Equal(0, imp.PhysicalVelocityX);
    }

    /// <summary>Opens the neighbours of the door tile except the shut sides (bit 0 north, 1 east, 2 south, 3 west
    /// of <paramref name="shutSides"/>) and puts an Imp on the door tile, at the given offset from its centre.</summary>
    private static (GameSimulation Sim, CreatureState Imp, GameDatabase Db, WorldState World) Enclosed(
        int shutSides, int heading, int offsetX = 0, int offsetY = 0)
    {
        (GameDatabase db, WorldState world, _) = Walled(Barricade);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                if (x != DoorTile.X || y != DoorTile.Y) world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
            }
        }

        (int X, int Y)[] sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        for (int side = 0; side < 4; side++)
        {
            if ((shutSides & (1 << side)) == 0) continue;
            world.Tile(new TilePos(DoorTile.X + sides[side].X, DoorTile.Y + sides[side].Y)).TerrainId = Dk2TerrainFixture.Rock;
        }

        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, DoorTile);
        imp.PosX = Centre(DoorTile.X) + offsetX;
        imp.PosY = Centre(DoorTile.Y) + offsetY;
        imp.Heading = heading;
        return (Sim(db, world), imp, db, world);
    }

    private static void AssertPushedClearOfTheWalls(GameSimulation sim, CreatureState imp, GameDatabase db, WorldState world)
    {
        Assert.InRange(TurnsUntilOffTheDoorTile(sim, imp, 80), 1, 60);
        sim.Run(80);

        Assert.False(db.TerrainById(world.Tile(imp.Tile).TerrainId).IsSolid, "the imp ended inside a wall");
        Assert.NotEqual(DoorTile, imp.Tile);
        Assert.Equal(0, imp.PhysicalVelocityX);
        Assert.Equal(0, imp.PhysicalVelocityY);
        // Standing clear of every wall edge by more than a step, so the next route can start.
        int dx = Math.Abs(imp.PosX - Centre(imp.Tile.X)), dy = Math.Abs(imp.PosY - Centre(imp.Tile.Y));
        Assert.True(dx < 0x800 && dy < 0x800);
    }

    /// <summary>OURS, not DK2 (ADR-0682): the owner's report was an Imp that arrives on the exact centre and turns
    /// toward north (the bearing of a zero vector); DK2's push then runs along that heading into the wall of an
    /// east-west passage. In a passage the push length is 0 along the whole centre line across it (the along-wall
    /// component is zeroed first, <c>0x004A58A2</c>), so these cases stand at several points of that line.
    /// <c>DoorPushRules.TowardAnOpenSide</c> sends the creature to an open side when its heading faces a shut one.</summary>
    [Theory]
    [InlineData(0b0101, 0, 0, 0)]       // walls north and south, heading north (shut), on the centre
    [InlineData(0b0101, 0, 0, 0x300)]   // the same, below the centre on the line
    [InlineData(0b0101, 1024, 0, -0x400)] // heading south (shut), above the centre
    [InlineData(0b0101, 1024, 0, 0x6FF)]
    [InlineData(0b1010, 512, 0, 0)]     // walls east and west, heading east (shut)
    [InlineData(0b1010, 512, 0x300, 0)]
    [InlineData(0b1010, 1536, -0x400, 0)] // heading west (shut)
    [InlineData(0b1010, 1536, 0x6FF, 0)]
    public void OursPushesACreatureOnTheCentreLineTowardAnOpenSide(int shutSides, int heading, int offsetX, int offsetY)
    {
        (GameSimulation sim, CreatureState imp, GameDatabase db, WorldState world) = Enclosed(shutSides, heading, offsetX, offsetY);

        AssertPushedClearOfTheWalls(sim, imp, db, world);
    }

    /// <summary>These three never reach <c>TowardAnOpenSide</c>: their push point is off the centre, so a creature
    /// on the centre has a non-zero length and DK2's own vector already points to the open side. They guard that
    /// DK2's vector, unchanged, leaves no creature in a wall for a dead end and a single shut side.</summary>
    [Theory]
    [InlineData(0b0111, 0)]    // a dead end open to the west, heading north
    [InlineData(0b1011, 1024)] // open to the south, heading south
    [InlineData(0b0001, 0)]    // only the north shut: it is pushed away from it
    public void ADeadEndOrOneShutSideIsLeftToDK2sOwnVector(int shutSides, int heading)
    {
        (GameSimulation sim, CreatureState imp, GameDatabase db, WorldState world) = Enclosed(shutSides, heading);

        AssertPushedClearOfTheWalls(sim, imp, db, world);
    }

    /// <summary>DK2's heading vector stands when the heading faces an open side: a passage east-west, heading east
    /// (open), on the centre, takes <c>0x80</c> along the heading and none across it.</summary>
    [Theory]
    [InlineData(0b0101, 512)]   // heading east, open
    [InlineData(0b0101, 1536)]  // heading west, open
    [InlineData(0b1010, 0)]     // heading north, open
    [InlineData(0b1010, 1024)]  // heading south, open
    public void AHeadingThatFacesAnOpenSideStands_AsDK2s(int shutSides, int heading)
    {
        (GameSimulation sim, CreatureState imp, _, _) = Enclosed(shutSides, heading);

        sim.Run(1);

        Assert.Equal(PushVector(heading).X, imp.PhysicalVelocityX);
        Assert.Equal(PushVector(heading).Y, imp.PhysicalVelocityY);
    }

    /// <summary>With all four sides shut there is no open side: DK2's heading vector stands, whatever the heading.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(1536)]
    public void WithAllFourSidesShutDK2sHeadingVectorStands(int heading)
    {
        (GameSimulation sim, CreatureState imp, _, _) = Enclosed(0b1111, heading);

        sim.Run(1);

        Assert.Equal(PushVector(heading).X, imp.PhysicalVelocityX);
        Assert.Equal(PushVector(heading).Y, imp.PhysicalVelocityY);
    }

    /// <summary>With all four sides shut there is nowhere to go: DK2's heading push stands, and the creature stays.</summary>
    [Fact]
    public void ACreatureInAClosedBoxStaysOnTheTile()
    {
        (GameDatabase db, WorldState world, _) = Walled(Barricade);
        world.Tile(new TilePos(DoorTile.X, DoorTile.Y - 1)).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(new TilePos(DoorTile.X, DoorTile.Y + 1)).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(new TilePos(DoorTile.X - 1, DoorTile.Y)).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(new TilePos(DoorTile.X + 1, DoorTile.Y)).TerrainId = Dk2TerrainFixture.Rock;
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, DoorTile);
        imp.PosX = Centre(DoorTile.X);
        imp.PosY = Centre(DoorTile.Y);
        imp.Heading = 0;
        GameSimulation sim = Sim(db, world);

        sim.Run(60);

        Assert.Equal(DoorTile, imp.Tile);
        Assert.False(db.TerrainById(world.Tile(imp.Tile).TerrainId).IsSolid);
    }

    /// <summary>The vector is part of the state: it changes the hash and survives a save.</summary>
    [Fact]
    public void ThePersistentVelocityIsHashedAndSaved()
    {
        (GameDatabase db, WorldState world, _) = Walled(Barricade);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(5, 5));
        GameSimulation sim = new(db, world, 1, []);
        ulong before = sim.StateHash();

        imp.PhysicalVelocityX = 77;
        imp.PhysicalVelocityY = -33;
        Assert.NotEqual(before, sim.StateHash());

        GameSimulation loaded = WorldSnapshot.LoadGame(WorldSnapshot.ToBytes(sim), db);
        CreatureState copy = loaded.World.Creatures.Single();
        Assert.Equal(77, copy.PhysicalVelocityX);
        Assert.Equal(-33, copy.PhysicalVelocityY);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }
}

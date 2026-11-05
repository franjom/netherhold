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

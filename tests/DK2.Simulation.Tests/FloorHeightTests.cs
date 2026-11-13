// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>DK2's floor height (<c>0x004545C0</c>) and the physical pass's fall to it (<c>0x004B2030</c>, Q795).</summary>
public class FloorHeightTests
{
    private const byte Keeper = 3;

    // A water pool over tiles (3..7, 3..7); the row below it is dry.
    private static (GameDatabase, WorldState) Pool()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(12, 12), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId =
                    x is >= 3 and <= 7 && y is >= 3 and <= 7 ? Dk2TerrainFixture.Water : Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    // A point on tile (5,7), south of its centre and a little west: dry tiles lie south of it.
    private const int BankX = (5 * 4096) + 0x800 - 1443, BankY = (7 * 4096) + 0x800 + 1803;

    [Fact]
    public void ABankSlopesFromTheDrySideByTheLargerOffset()
    {
        (GameDatabase db, WorldState world) = Pool();

        // The south octant with the south tile dry takes max(|dx|, |dy|): 0xA66 + (1803 x 0x59A >> 11).
        Assert.Equal(3924, FloorHeightRules.At(world, db, BankX, BankY));
    }

    [Fact]
    public void OpenWaterIsTheLiquidFloorAndDryGroundTheOrdinaryOne()
    {
        (GameDatabase db, WorldState world) = Pool();

        Assert.Equal(FloorHeightRules.Liquid, FloorHeightRules.At(world, db, CreatureState.CentreOf(5), CreatureState.CentreOf(5)));
        Assert.Equal(FloorHeightRules.Ordinary, FloorHeightRules.At(world, db, CreatureState.CentreOf(9), CreatureState.CentreOf(9)));
    }

    [Fact]
    public void ABridgeIsItsDeck()
    {
        (GameDatabase db, WorldState world) = Pool();
        ref TileState tile = ref world.Tile(5, 5);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.WoodenBridge), Keeper);

        Assert.Equal(FloorHeightRules.Bridge, FloorHeightRules.At(world, db, CreatureState.CentreOf(5), CreatureState.CentreOf(5)));
    }

    [Fact]
    public void AHatcherySinksInsideAndAtItsRimTilesCentreButNotAtTheOuterEdge()
    {
        (GameDatabase db, WorldState world) = Pool();
        for (int y = 8; y <= 10; y++)
            for (int x = 2; x <= 4; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Hatchery), Keeper);
            }

        Assert.Equal(0xE00, FloorHeightRules.At(world, db, CreatureState.CentreOf(3), CreatureState.CentreOf(9)));
        Assert.Equal(0xE00, FloorHeightRules.At(world, db, CreatureState.CentreOf(2), CreatureState.CentreOf(9)));
        // The rim tile's west strip has no Hatchery to its west.
        Assert.Equal(FloorHeightRules.Ordinary, FloorHeightRules.At(world, db, (2 * 4096) + 0x100, CreatureState.CentreOf(9)));
    }

    // Read from the body only (no capture sample for an odd octant): the second mask is not cut to a byte, so for
    // octant 7 the always-set bit 8 of the NOT takes the larger offset even with the north tile wet.
    [Fact]
    public void OctantSevenTakesTheLargerOffsetWhateverTheNorthTile_ReadNotCaptured()
    {
        (GameDatabase db, WorldState world) = Pool();
        // Tile (3,4): water to the north, dry to the west, north-west and south-west.
        int x = CreatureState.CentreOf(3) - 500, y = CreatureState.CentreOf(4) - 1500;

        Assert.Equal(FloorHeightRules.Liquid + ((1500 * 0x59A) >> 11), FloorHeightRules.At(world, db, x, y));
    }

    [Fact]
    public void LavaHasTheLiquidFloor()
    {
        (GameDatabase db, WorldState world) = Pool();
        world.Tile(new TilePos(9, 9)).TerrainId = Dk2TerrainFixture.Lava;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                world.Tile(new TilePos(9 + dx, 9 + dy)).TerrainId = Dk2TerrainFixture.Lava;

        Assert.Equal(FloorHeightRules.Liquid, FloorHeightRules.At(world, db, CreatureState.CentreOf(9), CreatureState.CentreOf(9)));
    }

    [Theory]
    [InlineData(RoomTypeId.Graveyard, 0xF00)]
    [InlineData(RoomTypeId.Temple, 0)]
    [InlineData(RoomTypeId.CombatPit, 0x400)]
    public void ARoomsInteriorIsItsFloorSlot(RoomTypeId type, int floor)
    {
        (GameDatabase db, WorldState world) = Square(type);

        Assert.Equal(floor, FloorHeightRules.At(world, db, CreatureState.CentreOf(3), CreatureState.CentreOf(9)));
    }

    [Fact]
    public void APitsRimIsRaisedOnlyInTheZonesThatTouchItsInterior()
    {
        (GameDatabase db, WorldState world) = Square(RoomTypeId.CombatPit);

        // Rim tile (2,9): its east strip touches the interior (3,9); its centre and west strip do not.
        Assert.Equal(0x1200, FloorHeightRules.At(world, db, (2 * 4096) + 0xE00, CreatureState.CentreOf(9)));
        Assert.Equal(FloorHeightRules.Ordinary, FloorHeightRules.At(world, db, CreatureState.CentreOf(2), CreatureState.CentreOf(9)));
        Assert.Equal(FloorHeightRules.Ordinary, FloorHeightRules.At(world, db, (2 * 4096) + 0x100, CreatureState.CentreOf(9)));
    }

    private static (GameDatabase, WorldState) Square(RoomTypeId type)
    {
        (GameDatabase db, WorldState world) = Pool();
        for (int y = 8; y <= 10; y++)
            for (int x = 2; x <= 4; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                RoomRules.Place(db, ref tile, db.Room(type), Keeper);
            }
        return (db, world);
    }

    [Fact]
    public void ACreatureBelowAHigherFloorIsRaisedOntoIt()
    {
        (GameDatabase db, WorldState world) = Pool();
        ref TileState tile = ref world.Tile(5, 5);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.WoodenBridge), Keeper);
        CreatureState goblin = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        goblin.PosZ = 0;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(new Probe())]);

        sim.Tick();

        Assert.Equal(FloorHeightRules.Bridge - FloorHeightRules.Ordinary, goblin.PosZ);
    }

    [Fact]
    public void AHandDropsLandingKeepsItsOwnHeight()
    {
        (GameDatabase db, WorldState world) = Pool();
        CreatureState goblin = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        goblin.PosZ = 256;
        goblin.LandingPending = true;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(new Probe())]);

        sim.Tick();

        Assert.Equal(256, goblin.PosZ);
    }

    // 0x004B5020: a height still above 0x3000 (absolute) after the fall's step is put at 0x2000.
    [Fact]
    public void AFallThatEndsHighIsPutATileAboveTheFloor()
    {
        (GameDatabase db, WorldState world) = Pool();
        CreatureState goblin = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(9, 9));
        goblin.PosZ = 0x3000;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(new Probe())]);

        sim.Tick();

        Assert.Equal(0x2000 - FloorHeightRules.Ordinary, goblin.PosZ);
    }

    [Fact]
    public void ACreatureAboveItsFloorFallsToItAndItsBodyGoesBackOnItKeepingItsVelocity()
    {
        (GameDatabase db, WorldState world) = Pool();
        CreatureState goblin = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 7));
        goblin.PosX = BankX;
        goblin.PosY = BankY;
        goblin.PosZ = 0;
        goblin.Body.Pos = new DK2.Simulation.Steering.Fixed16Vector((BankX << 4) + 5, (BankY << 4) + 9);
        var velocity = new DK2.Simulation.Steering.Fixed16Vector(-1596, -13075);
        goblin.Body.Vel = velocity;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(new Probe())]);

        sim.Tick();

        Assert.Equal(3924 - FloorHeightRules.Ordinary, goblin.PosZ);
        Assert.Equal(new DK2.Simulation.Steering.Fixed16Vector(BankX << 4, BankY << 4), goblin.Body.Pos);
        Assert.Equal(velocity, goblin.Body.Vel);
        Assert.Equal(0, goblin.CreationVelocityZ);
    }

    [Fact]
    public void ACreatureOnItsFloorKeepsItsBody()
    {
        (GameDatabase db, WorldState world) = Pool();
        CreatureState goblin = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(9, 9));
        var body = new DK2.Simulation.Steering.Fixed16Vector((goblin.PosX << 4) + 5, (goblin.PosY << 4) + 9);
        goblin.Body.Pos = body;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(new Probe())]);

        sim.Tick();

        Assert.Equal(0, goblin.PosZ);
        Assert.Equal(body, goblin.Body.Pos);
    }

    /// <summary>The physical pass alone, as the creature update runs it.</summary>
    private sealed class Probe : ICreatureUpdateStep
    {
        public string Name => "physical pass";

        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

        public void Update(SimulationContext context, CreatureState creature) => CreaturePhysicsRules.Step(context, creature);
    }
}

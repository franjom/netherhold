// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class StructureDeathEffectTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private sealed class LethalDoorShot : ISimulationSystem
    {
        public string Name => "lethal_door_shot";
        public void Execute(SimulationContext context)
        {
            foreach (DoorState door in context.World.Doors)
                DoorRules.Damage(context, door, -door.Health, Keeper);
        }
    }

    private static (GameDatabase Db, WorldState World) Setup(ushort doorEffect = 217, ushort trapEffect = 263)
    {
        GameDatabase source = Dk2TerrainFixture.Database();
        var db = new GameDatabase(source.Terrain.Values, source.Rooms.Values, source.Creatures.Values,
            source.Variables, source.KeeperSpells.Values, source.Objects.Values,
            source.Doors.Values.Select(d => d with { DeathEffectId = doorEffect }),
            source.Traps.Values.Select(t => t with { DeathEffectId = trapEffect }));
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, Keeper]);
        return (db, world);
    }

    private static ListEventSink Run(GameDatabase db, WorldState world, params ISimulationSystem[] systems)
    {
        var sim = new GameSimulation(db, world, 7, systems);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Run(2);
        return events;
    }

    [Theory]
    [InlineData(0, false, 217, 0, 1)]
    [InlineData(-1, false, 217, 1, 1)]
    [InlineData(0, true, 217, 0, 0)]
    [InlineData(0, false, 0, 0, 0)]
    [InlineData(100, false, 217, 0, 0)]
    public void DoorUpdateRequestsOnlyCompletedDeathEffect(int health, bool construction, ushort effect, byte orientation, int expected)
    {
        (GameDatabase? db, WorldState? world) = Setup(doorEffect: effect);
        if (orientation == 0)
        {
            world.Tile(2, 1).TerrainId = Dk2TerrainFixture.Rock;
            world.Tile(2, 3).TerrainId = Dk2TerrainFixture.Rock;
        }
        else
        {
            world.Tile(1, 2).TerrainId = Dk2TerrainFixture.Rock;
            world.Tile(3, 2).TerrainId = Dk2TerrainFixture.Rock;
        }
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 2, Y = 2,
            PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] }]);
        DoorState door = Assert.Single(world.Doors);
        Assert.Equal(orientation, door.Orientation);
        door.Health = health;
        door.UnderConstruction = construction;
        ListEventSink events = Run(db, world, new DoorHealthSystem(), new DoorRemovalSystem());
        SimEvent[] effects = events.Events.Where(e => e.EffectRequest.HasValue).ToArray();
        Assert.Equal(expected, effects.Length);
        if (expected == 0) return;
        ViewEffectRequest request = Assert.Single(effects).EffectRequest!.Value;
        Assert.Equal(new EffectId(effect), request.EffectId);
        Assert.Equal(new PlayerId(Keeper), request.Owner);
        Assert.Equal(new TilePos(2, 2), request.Tile);
        Assert.Equal(10240, request.Position.X.Raw);
        Assert.Equal(10240, request.Position.Y.Raw);
        Assert.Equal(FloorHeightRules.Ordinary, request.Height.Raw);
        Assert.Equal(new Dk2Angle(orientation == 0 ? (ushort)0x200 : (ushort)0), request.Heading);
        Assert.Empty(world.Doors);
    }

    [Theory]
    [InlineData(0, false, 263, 1)]
    [InlineData(-1, false, 63, 1)]
    [InlineData(0, true, 263, 0)]
    [InlineData(0, false, 0, 0)]
    [InlineData(100, false, 263, 0)]
    public void TrapUpdateRequestsOnlyCompletedDeathEffect(int health, bool construction, ushort effect, int expected)
    {
        (GameDatabase? db, WorldState? world) = Setup(trapEffect: effect);
        byte id = db.Traps.Keys.First();
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = 2, Y = 2,
            PlayerId = Keeper, TrapId = id, NumberOfShots = 1 }]);
        TrapState trap = Assert.Single(world.Traps);
        trap.Health = health;
        trap.UnderConstruction = construction;
        trap.Heading = 0x357;
        ListEventSink events = Run(db, world, new TrapSystem(), new TrapRemovalSystem());
        SimEvent[] effects = events.Events.Where(e => e.EffectRequest.HasValue).ToArray();
        Assert.Equal(expected, effects.Length);
        if (expected == 0) return;
        ViewEffectRequest request = Assert.Single(effects).EffectRequest!.Value;
        Assert.Equal(new EffectId(effect), request.EffectId);
        Assert.Equal(new PlayerId(Keeper), request.Owner);
        Assert.Equal(new TilePos(2, 2), request.Tile);
        Assert.Equal(10240, request.Position.X.Raw);
        Assert.Equal(10240, request.Position.Y.Raw);
        Assert.Equal(FloorHeightRules.Ordinary, request.Height.Raw);
        Assert.Equal(new Dk2Angle(0x357), request.Heading);
        Assert.Empty(world.Traps);
    }

    [Fact]
    public void OwnerLossRequestsCompletedDoorAndTrapEffects()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        ThingProjection.Place(world, db,
        [
            new DoorThing(new byte[20]) { X = 2, Y = 2, PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] },
            new TrapThing(new byte[16]) { X = 1, Y = 1, PlayerId = Keeper, TrapId = db.Traps.Keys.First(), NumberOfShots = 1 },
        ]);
        world.Tile(2, 2).OwnerPlayerId = PlayerIds.Neutral;
        world.Tile(1, 1).OwnerPlayerId = PlayerIds.Neutral;
        ListEventSink events = Run(db, world, new DoorHealthSystem(), new TrapSystem(), new DoorRemovalSystem(), new TrapRemovalSystem());
        ushort[] ids = events.Events.Where(e => e.EffectRequest.HasValue)
            .Select(e => e.EffectRequest!.Value.EffectId.Value).OrderBy(id => id).ToArray();
        Assert.Equal(new ushort[] { 217, 263 }, ids);
        Assert.All(events.Events.Where(e => e.EffectRequest.HasValue),
            e => Assert.Equal(new PlayerId(Keeper), e.EffectRequest!.Value.Owner));
        Assert.Empty(world.Doors);
        Assert.Empty(world.Traps);
    }

    [Fact]
    public void SupportLossRequestsDoorDeathEffect()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 2, Y = 2,
            PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] }]);
        ListEventSink events = Run(db, world, new DoorHealthSystem(), new DoorRemovalSystem());
        Assert.Single(events.Events, e => e.EffectRequest.HasValue);
        Assert.Empty(world.Doors);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void DeadBarricadeHasItsOwnEffectArmBeforeSupport(bool construction, int expected)
    {
        (GameDatabase? db, WorldState? world) = Setup();
        byte id = db.Doors.Values.First(d => (d.Flags & DoorFlags.Barricade) != 0).DoorId;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 2, Y = 2,
            PlayerId = Keeper, DoorId = id, Unknown_0011 = new byte[3] }]);
        world.Doors[0].Health = 0;
        world.Doors[0].UnderConstruction = construction;
        ListEventSink events = Run(db, world, new DoorHealthSystem(), new DoorRemovalSystem());
        Assert.Equal(expected, events.Events.Count(e => e.EffectRequest.HasValue));
        Assert.Empty(world.Doors);
    }

    [Fact]
    public void LethalShotRequestsDoorEffectBeforeTheRemovalPass()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        world.Tile(2, 1).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(2, 3).TerrainId = Dk2TerrainFixture.Rock;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 2, Y = 2,
            PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] }]);
        ISimulationSystem[] systems = SimulationSystems.Default()
            .Select(s => s is ShotSystem ? new LethalDoorShot() : s).ToArray();
        ListEventSink events = Run(db, world, systems);
        Assert.Single(events.Events, e => e.EffectRequest?.EffectId == new EffectId(217));
        Assert.Empty(world.Doors);
    }

    [Fact]
    public void DoorEffectsFollowTheNewestFirstThingUpdateOrder()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        ThingProjection.Place(world, db,
        [
            new DoorThing(new byte[20]) { X = 1, Y = 1, PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] },
            new DoorThing(new byte[20]) { X = 3, Y = 3, PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] },
        ]);
        foreach (DoorState door in world.Doors) door.Health = 0;
        ListEventSink events = Run(db, world, new DoorHealthSystem(), new DoorRemovalSystem());
        TilePos[] order = events.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value.Tile).ToArray();
        Assert.Equal(new[] { new TilePos(3, 3), new TilePos(1, 1) }, order);
    }

    [Fact]
    public void RemovalPassesDoNotRequestEffects()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        ThingProjection.Place(world, db,
        [
            new DoorThing(new byte[20]) { X = 2, Y = 2, PlayerId = Keeper, DoorId = 1, Unknown_0011 = new byte[3] },
            new TrapThing(new byte[16]) { X = 2, Y = 2, PlayerId = Keeper, TrapId = db.Traps.Keys.First(), NumberOfShots = 1 },
        ]);
        world.Doors[0].Health = 0;
        world.Traps[0].Health = 0;
        ListEventSink events = Run(db, world, new DoorRemovalSystem(), new TrapRemovalSystem());
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
        Assert.Empty(world.Doors);
        Assert.Empty(world.Traps);
    }
}

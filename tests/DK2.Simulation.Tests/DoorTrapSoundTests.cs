// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The sounds of docs/research/sound-effects-objects.md section 2, each in the thing's definition's category
/// (door <c>+0xA9</c>, trap <c>+0xB0</c>) at its position with its handle: a door's open state changing
/// (<c>Door_Update</c>, <c>0x004A55F0</c>, <c>0xE7</c> open and <c>0xE8</c> shut), its lock mode set
/// (<c>Door_SetLockMode</c>, <c>0x004A6800</c>, <c>0xE9</c> locked and <c>0xEA</c> unlocked), a trap's shot made
/// (<c>Trap_CreateShot</c>, <c>0x004A80E0</c>, <c>0xEB</c>) and a trap's cleanup (<c>0x004A7B20</c>, <c>0xF4</c>).</summary>
public sealed class DoorTrapSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    // ---------------------------------------------------------------------------------------- doors

    private static (GameDatabase Db, WorldState World, DoorState Door, GameSimulation Sim, ListEventSink Events) DoorSetup(
        byte doorId = 1, bool underConstruction = false)
    {
        GameDatabase db = SoundFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        foreach (int x in new[] { 4, 5, 6, 7 })
            world.Tile(new TilePos(x, 2)).TerrainId = Dk2TerrainFixture.DirtPath;
        world.Tile(new TilePos(5, 2)).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(new TilePos(5, 2)).OwnerPlayerId = Keeper;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 5, Y = 2, PlayerId = Keeper, Flags = 0, DoorId = doorId, Unknown_0011 = new byte[3] }]);
        DoorState door = world.Doors[0];
        door.UnderConstruction = underConstruction;
        var sim = new GameSimulation(db, world, 1, [new DoorSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (db, world, door, sim, events);
    }

    private static FpPosition3 DoorAt(WorldState world, DoorState door) => Centre(world, (door.Y * world.Width) + door.X);

    [Fact]
    public void ADoorOpeningForAnAlliedCreatureAsksE7AndShuttingAsksE8InItsCategoryAtItsTileWithItsVoice()
    {
        var (db, world, door, sim, events) = DoorSetup();
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 2));

        sim.Tick(InputFrame.Empty);

        Assert.True(door.Open);
        ThingSoundRequest opened = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.DoorOpened, opened.Event);
        Assert.Equal((ushort)0xE7, (ushort)opened.Event);
        Assert.Equal("DOOR_WOOD", opened.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Door, door.Id), opened.Source);
        Assert.Equal(DoorAt(world, door), opened.Position);
        Assert.True(opened.Voiced);

        sim.Tick(InputFrame.Empty);
        Assert.Single(Sounds(events));   // it stays open: nothing changed

        door.LockMode = 3;
        sim.Tick(InputFrame.Empty);
        Assert.False(door.Open);
        ThingSoundRequest shut = Sounds(events)[1];
        Assert.Equal((ThingSoundEvent.DoorClosed, "DOOR_WOOD", opened.Source), (shut.Event, shut.Category, shut.Source));
        Assert.Equal((ushort)0xE8, (ushort)shut.Event);
        Assert.Equal(2, Sounds(events).Length);
    }

    [Fact]
    public void ABarricadeAndADoorUnderConstructionAskNothingAndADeadDoorIsPassedOver()
    {
        var (db, world, _, sim, events) = DoorSetup(doorId: 6);   // the Barricade
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 2));
        sim.Tick(InputFrame.Empty);
        Assert.Empty(Sounds(events));

        var (db2, world2, _, sim2, events2) = DoorSetup(underConstruction: true);
        world2.SpawnCreature(db2, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 2));
        sim2.Tick(InputFrame.Empty);
        Assert.Empty(Sounds(events2));

        var (db3, world3, dead, sim3, events3) = DoorSetup();
        world3.SpawnCreature(db3, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 2));
        dead.Health = 0;   // 0x004A55F0 returns before the open state for a door that is dead
        sim3.Tick(InputFrame.Empty);
        Assert.Empty(Sounds(events3));
    }

    [Fact]
    public void TheLockCommandAsksE9WhenItLocksAndEaWhenItUnlocksAndNothingForADoorUnderConstruction()
    {
        var (_, world, door, sim, events) = DoorSetup();
        Assert.Equal(0, door.LockMode);
        sim.Tick(InputFrame.Of(new LockDoorCommand(Keeper, new TilePos(5, 2))));
        Assert.Equal(3, door.LockMode);
        ThingSoundRequest locked = Assert.Single(Sounds(events));
        Assert.Equal((ThingSoundEvent.DoorLocked, "DOOR_WOOD"), (locked.Event, locked.Category));
        Assert.Equal((ushort)0xE9, (ushort)locked.Event);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Door, door.Id), locked.Source);
        Assert.Equal(DoorAt(world, door), locked.Position);
        Assert.True(locked.Voiced);

        sim.Tick(InputFrame.Of(new LockDoorCommand(Keeper, new TilePos(5, 2))));
        ThingSoundRequest unlocked = Sounds(events)[1];
        Assert.Equal((ThingSoundEvent.DoorUnlocked, "DOOR_WOOD", locked.Source), (unlocked.Event, unlocked.Category, unlocked.Source));
        Assert.Equal((ushort)0xEA, (ushort)unlocked.Event);

        var (_, _, _, sim2, events2) = DoorSetup(underConstruction: true);
        sim2.Tick(InputFrame.Of(new LockDoorCommand(Keeper, new TilePos(5, 2))));
        Assert.Empty(Sounds(events2));
    }

    [Fact]
    public void ADoorOfADefinitionWithNoCategoryAsksNothing()
    {
        GameDatabase db = SoundFixture.Database(objects: null, doors: false, traps: false);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        world.Tile(new TilePos(5, 2)).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(new TilePos(5, 2)).OwnerPlayerId = Keeper;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 5, Y = 2, PlayerId = Keeper, Flags = 0, DoorId = 1, Unknown_0011 = new byte[3] }]);
        var sim = new GameSimulation(db, world, 1, [new DoorSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Tick(InputFrame.Of(new LockDoorCommand(Keeper, new TilePos(5, 2))));
        Assert.Empty(Sounds(events));
    }

    // ---------------------------------------------------------------------------------------- traps

    private static (GameDatabase Db, WorldState World) Open()
    {
        GameDatabase db = SoundFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        world.Player(Keeper).Mana = 1000;
        return (db, world);
    }

    private static TrapState Place(GameDatabase db, WorldState world, byte trapId, int x, int y, byte owner = Keeper)
    {
        ref TileState tile = ref world.Tile(x, y);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = owner;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = x, Y = y, TrapId = trapId, PlayerId = owner, NumberOfShots = 1 }]);
        return world.Traps[^1];
    }

    private static GameSimulation TrapSim(GameDatabase db, WorldState world, out ListEventSink events)
    {
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem(), new ShotSystem(), new TrapSystem(), new TrapRemovalSystem()]);
        events = new ListEventSink();
        sim.ObserveWith(events);
        return sim;
    }

    private static FpPosition3 TrapAt(WorldState world, TrapState trap) => Centre(world, (trap.Y * world.Width) + trap.X);

    [Fact]
    public void ASentryFiringAsksEbInItsCategoryAtItsTileWithItsVoiceForEveryone()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 5);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(8, 5));
        goblin.Health = 100000;
        GameSimulation sim = TrapSim(db, world, out ListEventSink events);

        sim.Run(2);

        Assert.Contains(events.Events, e => e.Kind == "trap_fired");
        ThingSoundRequest sound = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.TrapFired, sound.Event);
        Assert.Equal((ushort)0xEB, (ushort)sound.Event);
        Assert.Equal("TRAP_SENTRY", sound.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Trap, sentry.Id), sound.Source);
        Assert.Equal(TrapAt(world, sentry), sound.Position);
        Assert.True(sound.Voiced);
        Assert.Null(sound.ViewerOnlyFor);
    }

    /// <summary>The Alarm's shot (id 13) is the one <c>0x004A82C2</c> and <c>0x004A830E</c> gate on the local player: the first
    /// when the target creature is its, the second when the trap is.</summary>
    [Fact]
    public void AnAlarmFiringAsksEbForTheTargetsOwnerAndForTheTrapsOwnerOnly()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState alarm = Place(db, world, Dk2TerrainFixture.AlarmTrapId, 5, 5);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 5));
        goblin.Health = 100000;
        GameSimulation sim = TrapSim(db, world, out ListEventSink events);

        sim.Run(2);

        Assert.Contains(events.Events, e => e.Kind == "trap_fired");
        ThingSoundRequest[] heard = Sounds(events);
        Assert.Equal(2, heard.Length);
        Assert.All(heard, s => Assert.Equal((ThingSoundEvent.TrapFired, "TRAP_ALARM", new SoundSourceId(SoundSourceKind.Trap, alarm.Id)),
            (s.Event, s.Category, s.Source)));
        Assert.Equal(new PlayerId(Rival), heard[0].ViewerOnlyFor);   // 0x004A82C2: the target creature's owner
        Assert.Equal(new PlayerId(Keeper), heard[1].ViewerOnlyFor);  // 0x004A830E: the trap's owner
    }

    [Fact]
    public void ADestroyedTrapAsksF4InItsCategoryAtItsTileWithItsVoiceWhetherOrNotItWasBuilt()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState boulder = Place(db, world, Dk2TerrainFixture.BoulderTrapId, 6, 6);
        TrapState building = Place(db, world, Dk2TerrainFixture.AlarmTrapId, 9, 9);
        building.UnderConstruction = true;
        GameSimulation sim = TrapSim(db, world, out ListEventSink events);
        boulder.Health = 0;
        building.Health = 0;

        sim.Tick(InputFrame.Empty);

        ThingSoundRequest[] heard = Sounds(events);   // the removal pass walks the list from its end
        Assert.Equal(2, heard.Length);
        ThingSoundRequest spent = heard.Single(s => s.Source == new SoundSourceId(SoundSourceKind.Trap, boulder.Id));
        Assert.Equal((ThingSoundEvent.TrapDestroyed, "TRAP_BOULDER"), (spent.Event, spent.Category));
        Assert.Equal((ushort)0xF4, (ushort)spent.Event);
        Assert.Equal(TrapAt(world, boulder), spent.Position);
        Assert.True(spent.Voiced);
        Assert.Equal("TRAP_ALARM", heard.Single(s => s.Source == new SoundSourceId(SoundSourceKind.Trap, building.Id)).Category);
        Assert.Empty(world.Traps);
    }

    [Fact]
    public void AttachingASoundSinkDoesNotChangeTheStateHashOfADoorAndTrapRun()
    {
        static GameSimulation Run(ListEventSink? sink)
        {
            (GameDatabase db, WorldState world) = Open();
            Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 5);
            ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 12, Y = 12, PlayerId = Keeper, Flags = 0, DoorId = 1, Unknown_0011 = new byte[3] }]);
            world.Tile(12, 12).OwnerPlayerId = Keeper;
            world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(8, 5)).Health = 100000;
            world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(12, 12));
            var sim = new GameSimulation(db, world, 1, [new DoorSystem(), new CreatureStatusSystem(), new ShotSystem(), new TrapSystem(), new TrapRemovalSystem()]);
            if (sink is not null) sim.ObserveWith(sink);
            sim.Tick(InputFrame.Of(new LockDoorCommand(Keeper, new TilePos(12, 12))));
            sim.Run(6);
            return sim;
        }
        var events = new ListEventSink();
        GameSimulation heard = Run(events);
        Assert.NotEmpty(Sounds(events));
        Assert.Equal(Run(null).StateHash(), heard.StateHash());
    }
}

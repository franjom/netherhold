// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The imp job sounds of docs/research/sound-effects.md section 10, all requested with the imp's own
/// position: the gold deposit <c>0xC4</c> (<c>ImpState29_DepositGold</c>, handle 0, the gold object's
/// category), the carrying sound <c>0x10D</c> (every update of a creature that holds a thing, and the burial
/// state), and <c>0x326</c>/<c>0x327</c>/<c>0x328</c> when an imp shelves a book, stores a crate or installs a
/// door or a trap (the imp's handle, the imp's category).</summary>
public sealed class ImpJobSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;
    private const int Second = SimulationTiming.TicksPerSecond;

    /// <summary>A world stepped one tick at a time, noting every thing sound with the tick it came on and
    /// where the imp stood and what it held when the tick ended.</summary>
    private sealed class Run
    {
        public readonly GameSimulation Sim;
        public readonly ListEventSink Events = new();
        public readonly List<Heard> Sounds = [];

        public Run(GameDatabase db, WorldState world, params ISimulationSystem[] systems)
        {
            Sim = new GameSimulation(db, world, seed: 42, systems);
            Sim.ObserveWith(Events);
        }

        public WorldState World => Sim.World;

        public void Until(Func<bool> done, int limit)
        {
            for (int i = 0; i < limit && !done(); i++)
            {
                int seen = Events.Events.Count;
                Sim.Tick();
                CreatureState? imp = World.Creatures.FirstOrDefault(c => c.CreatureTypeId == ImpId);
                for (int e = seen; e < Events.Events.Count; e++)
                    if (Events.Events[e].ThingSound is { } sound)
                        Sounds.Add(new Heard(Events.Events[e].Tick.Value, sound, imp!.Id,
                            new(new Fp32(imp.PosX), new Fp32(imp.PosY), new Fp32(imp.PosZ)),
                            imp.CarriedObjectId >= 0 || imp.CarriedCreatureId >= 0));
            }
        }

        public bool Saw(string kind) => Events.Events.Any(e => e.Kind == kind);

        public Heard[] Of(ThingSoundEvent sound) => Sounds.Where(s => s.Sound.Event == sound).ToArray();
    }

    /// <summary>A sound with the imp as the tick ended: its id, where it stood and whether it held a thing.</summary>
    private readonly record struct Heard(long Tick, ThingSoundRequest Sound, int ImpId, FpPosition3 ImpPosition, bool Carrying);

    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, params TilePos[] at)
    {
        foreach (TilePos tile in at) RoomRules.Place(db, ref world.Tile(tile.X, tile.Y), db.Room(type), Keeper);
    }

    // ---- the gold deposit ------------------------------------------------------------------------

    [Fact]
    public void AnImpDepositingGoldRequestsC4InObjectGoldAtItselfWithoutAVoice()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        Paint(db, world, RoomTypeId.Treasury, new TilePos(1, 1));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 2));
        imp.GoldHeld = db.CreatureById(ImpId).MaxGoldHeld;
        imp.LastTaskKind = ImpTaskKind.FetchGold;
        imp.DroppedTask = ImpTaskKind.FetchGold;
        var run = new Run(db, world, Dk2TerrainFixture.DefaultSystems());

        run.Until(() => run.Saw("gold_delivered"), 10 * Second);

        Assert.True(run.Saw("gold_delivered"));
        Heard heard = Assert.Single(run.Of(ThingSoundEvent.GoldDeposited));
        Assert.Equal((ushort)0xC4, (ushort)heard.Sound.Event);
        Assert.Equal("OBJECT_GOLD", heard.Sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(heard.ImpId)), heard.Sound.Source);
        Assert.Equal(heard.ImpPosition, heard.Sound.Position);
        Assert.False(heard.Sound.Voiced);   // handle 0
        Assert.Equal(run.Events.Events.First(e => e.Kind == "gold_delivered").Tick.Value, heard.Tick);
    }

    // ---- carrying and shelving -------------------------------------------------------------------

    private static (GameDatabase Db, WorldState World, Run Run, CreatureState Imp) Shelving(byte objectType)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, 1, PlayerIds.Neutral, Keeper]);
        Paint(db, world, RoomTypeId.Library, new TilePos(3, 3), new TilePos(3, 2));
        world.SpawnObject(db, objectType, PlayerIds.Unowned, (1 * world.Width) + 1);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 2));
        var run = new Run(db, world, new HandSystem(), new TileTagSystem(), new CreatureMovementSystem(),
            new ImpTaskSystem(), new TileWorkSystem(), new CarriedThingSoundSystem());
        return (db, world, run, imp);
    }

    [Fact]
    public void AnImpCarryingABookRequests10DInItsCategoryWithItsVoiceAtItselfOnEveryUpdateItHoldsIt()
    {
        var (_, _, run, _) = Shelving(Dk2TerrainFixture.SpellBookObjectId);

        run.Until(() => run.Saw("object_stored"), 20 * Second);

        Assert.True(run.Saw("object_stored"));
        Heard[] carrying = run.Of(ThingSoundEvent.CarriedThing);
        Assert.NotEmpty(carrying);
        Assert.Equal((ushort)0x10D, (ushort)ThingSoundEvent.CarriedThing);
        foreach (Heard heard in carrying)
        {
            Assert.Equal("CREATURE_IMP", heard.Sound.Category);
            Assert.Equal(SoundSourceId.Of(new CreatureId(heard.ImpId)), heard.Sound.Source);
            Assert.True(heard.Sound.Voiced);
            Assert.Equal(heard.ImpPosition, heard.Sound.Position);
            Assert.True(heard.Carrying, "the sound is only for a creature that holds a thing");
        }

        // One request per update it holds the book, from the tick it picks it up to the one before it shelves it.
        long picked = run.Events.Events.First(e => e.Kind == "object_picked_up").Tick.Value;
        long stored = run.Events.Events.First(e => e.Kind == "object_stored").Tick.Value;
        Assert.Equal(Enumerable.Range((int)picked, (int)(stored - picked)).Select(t => (long)t), carrying.Select(h => h.Tick));
    }

    [Fact]
    public void AnImpThatHoldsNothingRequestsNoCarryingSound()
    {
        var (_, _, run, _) = Shelving(Dk2TerrainFixture.SpellBookObjectId);

        run.Until(() => run.Saw("object_claimed"), 20 * Second);

        Assert.DoesNotContain(run.Sounds, s => s.Sound.Event == ThingSoundEvent.CarriedThing);
    }

    [Fact]
    public void ShelvingABookRequests326AtTheImpWithItsVoiceAndASpecialDoesNot()
    {
        var (_, _, book, _) = Shelving(Dk2TerrainFixture.SpellBookObjectId);
        book.Until(() => book.Saw("object_stored"), 20 * Second);

        Heard stored = Assert.Single(book.Of(ThingSoundEvent.ImpStoredItem));
        Assert.Equal((ushort)0x326, (ushort)stored.Sound.Event);
        Assert.Equal("CREATURE_IMP", stored.Sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(stored.ImpId)), stored.Sound.Source);
        Assert.Equal(stored.ImpPosition, stored.Sound.Position);
        Assert.True(stored.Sound.Voiced);
        Assert.Equal(book.Events.Events.First(e => e.Kind == "object_stored").Tick.Value, stored.Tick);

        // State 33 (a Special delivered to a Library, 0x00466F30) has no sound request.
        var (_, _, special, _) = Shelving(Dk2TerrainFixture.RevealMapSpecialObjectId);
        special.Until(() => special.Saw("object_stored"), 20 * Second);
        Assert.True(special.Saw("object_stored"));
        Assert.NotEmpty(special.Of(ThingSoundEvent.CarriedThing));
        Assert.Empty(special.Of(ThingSoundEvent.ImpStoredItem));
    }

    // ---- crates ------------------------------------------------------------------------------------

    private const byte MakerId = 77;
    private const byte DoorCrate = Dk2TerrainFixture.WoodenDoorCrateObjectId;
    private const byte TrapCrate = Dk2TerrainFixture.AlarmCrateObjectId;

    private static (GameDatabase Db, WorldState World) Workshop()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, (byte)1, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 3; y++)
        for (int x = 1; x <= 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), Keeper);
        for (int x = 4; x <= 12; x++)
        {
            world.Tile(x, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, 2).OwnerPlayerId = Keeper;
        }
        Dk2TerrainFixture.FurnishRooms(db, world);
        return (db, world);
    }

    private static Run CrateRun(GameDatabase db, WorldState world) => new(db, world, new HandSystem(),
        new CreatureMovementSystem(), new ImpTaskSystem(), new CreatureJobSystem(), new CarriedThingSoundSystem());

    [Fact]
    public void StoringALooseCrateInTheWorkshopRequests326AtTheImpWithItsVoice()
    {
        (GameDatabase db, WorldState world) = Workshop();
        world.SpawnObject(db, DoorCrate, PlayerIds.Unowned, (2 * world.Width) + 8);
        world.SpawnCreature(db, ImpId, Keeper, new TilePos(6, 2));
        Run run = CrateRun(db, world);

        run.Until(() => run.Saw("object_stored"), 20 * Second);

        Assert.True(run.Saw("object_stored"));
        Heard stored = Assert.Single(run.Of(ThingSoundEvent.ImpStoredItem));
        Assert.Equal("CREATURE_IMP", stored.Sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(stored.ImpId)), stored.Sound.Source);
        Assert.Equal(stored.ImpPosition, stored.Sound.Position);
        Assert.True(stored.Sound.Voiced);
        Assert.Equal(run.Events.Events.First(e => e.Kind == "object_stored").Tick.Value, stored.Tick);
        Assert.NotEmpty(run.Of(ThingSoundEvent.CarriedThing));
    }

    [Fact]
    public void InstallingADoorRequests327AndATrap328AtTheImpWithItsVoice()
    {
        (GameDatabase db, WorldState world) = Workshop();
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = 9, Y = 2, DoorId = 1, PlayerId = Keeper, Flags = 0x02, Unknown_0011 = new byte[3] }]);
        world.Player(Keeper).ManufactureQueue.Clear();
        ObjectState crate = world.SpawnObject(db, DoorCrate, Keeper, (2 * world.Width) + 2);
        Assert.True(CrateRules.List(new StateHookContext(world, db), RoomIndex.RoomAt(world, db, world.Width + 1)!, crate));
        world.SpawnCreature(db, ImpId, Keeper, new TilePos(6, 2));
        Run door = CrateRun(db, world);
        door.Until(() => door.Saw("door_built"), 20 * Second);

        Assert.True(door.Saw("door_built"));
        Heard installed = Assert.Single(door.Of(ThingSoundEvent.ImpInstalledDoor));
        Assert.Equal((ushort)0x327, (ushort)installed.Sound.Event);
        Assert.Equal("CREATURE_IMP", installed.Sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(installed.ImpId)), installed.Sound.Source);
        Assert.Equal(installed.ImpPosition, installed.Sound.Position);
        Assert.True(installed.Sound.Voiced);
        Assert.Equal(door.Events.Events.First(e => e.Kind == "door_built").Tick.Value, installed.Tick);
        Assert.Empty(door.Of(ThingSoundEvent.ImpInstalledTrap));

        (GameDatabase db2, WorldState world2) = Workshop();
        ThingProjection.Place(world2, db2, [new TrapThing(new byte[16]) { X = 11, Y = 2, TrapId = 7, PlayerId = Keeper }]);
        world2.SpawnObject(db2, TrapCrate, PlayerIds.Unowned, (2 * world2.Width) + 5);
        world2.SpawnCreature(db2, ImpId, Keeper, new TilePos(6, 2));
        Run trap = CrateRun(db2, world2);
        trap.Until(() => trap.Saw("trap_built"), 20 * Second);

        Assert.True(trap.Saw("trap_built"));
        Heard fitted = Assert.Single(trap.Of(ThingSoundEvent.ImpInstalledTrap));
        Assert.Equal((ushort)0x328, (ushort)fitted.Sound.Event);
        Assert.Equal("CREATURE_IMP", fitted.Sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(fitted.ImpId)), fitted.Sound.Source);
        Assert.Equal(fitted.ImpPosition, fitted.Sound.Position);
        Assert.Equal(trap.Events.Events.First(e => e.Kind == "trap_built").Tick.Value, fitted.Tick);
        Assert.Empty(trap.Of(ThingSoundEvent.ImpInstalledDoor));
    }

    // ---- bodies -----------------------------------------------------------------------------------

    private static Run Burial()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, 1, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 3; y++)
        for (int x = 4; x <= 6; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Graveyard), Keeper);
        Dk2TerrainFixture.FurnishRooms(db, world);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, goblin);
        world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        return new Run(db, world, new CreatureMovementSystem(), new ImpTaskSystem(), new CreatureSleepSystem(),
            new CreatureHealthSystem(), new CarriedThingSoundSystem());
    }

    [Fact]
    public void CarryingACorpseSoundsEveryUpdateAndTheBurialStateRequestsItOnceMoreAsTheCorpseIsLaid()
    {
        Run run = Burial();

        run.Until(() => run.Saw("corpse_laid"), 30 * Second);

        Assert.True(run.Saw("corpse_laid"));
        long laid = run.Events.Events.First(e => e.Kind == "corpse_laid").Tick.Value;
        Heard[] carrying = run.Of(ThingSoundEvent.CarriedThing);
        Assert.NotEmpty(carrying);
        Assert.All(carrying, h => Assert.Equal("CREATURE_IMP", h.Sound.Category));
        Assert.All(carrying, h => Assert.Equal(SoundSourceId.Of(new CreatureId(h.ImpId)), h.Sound.Source));

        // On the tick the corpse is laid the imp holds nothing when the update ends (the state clears it),
        // so only state 41's own request (0x00467B37) can have sounded there.
        Heard last = carrying[^1];
        Assert.Equal(laid, last.Tick);
        Assert.False(last.Carrying);
        Assert.Equal(last.ImpPosition, last.Sound.Position);
        Assert.True(last.Sound.Voiced);
    }
}

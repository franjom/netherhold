// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The creature-state sounds of docs/research/sound-effects.md section 11: arriving from a portal
/// (<c>0xE0</c>, only for the local keeper's creatures), the tremor knockdown (<c>0xC7</c>), a guard's lost
/// patrol leg (<c>0xDD</c>) and a meal (<c>0x111</c> in the chicken's category, then <c>0xCD</c> in the
/// eater's). Each is requested with the creature's own handle at its position; the withdraw and alarm
/// sounds are in <c>CombatFearTests</c> and <c>CombatNonFighterTests</c>.</summary>
public sealed class CreatureStateSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static ThingSoundRequest[] Sounds(ListEventSink sink) =>
        sink.Events.Where(e => e.ThingSound.HasValue).Select(e => e.ThingSound!.Value).ToArray();

    private static FpPosition3 At(CreatureState creature) =>
        new(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ));

    // ---- state 193 ------------------------------------------------------------------------------

    /// <summary>State 193 (<c>0x00459C20</c>) requests <c>0xE0</c> once its action has ended, before it steps off the
    /// portal's centre, and only when the creature's owner is the local player (<c>+0x264</c>): ours marks the
    /// request for the owner, and the view plays it only when that keeper is the viewer.</summary>
    [Fact]
    public void ACreatureLeavingItsPortalRequestsE0ForItsOwnerBeforeTheStepOffTheHole()
    {
        CreatureDef joiner = Dk2TerrainFixture.Database().CreatureById(GoblinId) with
        {
            CreatureId = 91, EntranceOffsetX = 0, EntranceOffsetY = 2929,
            EntranceFrames = 147, EntranceFramesPerSecond = 30,
        };
        GameDatabase db = Dk2TerrainFixture.Database(joiner);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, 2, 3, 4]);
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
                RoomRules.Place(db, ref world.Tile(5 + dx, 5 + dy), db.Room(RoomTypeId.Portal), 3);
        var sim = new GameSimulation(db, world, seed: 1, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        CreatureState creature = PortalRules.CreateScriptedArrival(new StateHookContext(world, db), 3, 91, 1)!;
        Assert.Equal(PortalRules.JoiningState, creature.Dk2StateId);

        FpPosition3 before = default;
        int left = -1;
        for (int i = 0; i < 100 && left < 0; i++)
        {
            before = At(creature);
            Assert.DoesNotContain(Sounds(sink), s => s.Event == ThingSoundEvent.JoinedDungeon);
            sim.Tick();
            if (creature.Dk2StateId != PortalRules.JoiningState) left = i;
        }

        Assert.True(left >= 0);
        ThingSoundRequest sound = Assert.Single(Sounds(sink), s => s.Event == ThingSoundEvent.JoinedDungeon);
        Assert.Equal((ushort)0xE0, (ushort)sound.Event);
        Assert.Equal("CREATURE_GOBLIN", sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(creature.Id)), sound.Source);
        Assert.Equal(new PlayerId(3), sound.ViewerOnlyFor);
        Assert.True(sound.Voiced);
        Assert.Equal(new Fp32(CreatureState.CentreOf(6)), sound.Position.X);
        Assert.Equal(new Fp32(CreatureState.CentreOf(6)), sound.Position.Y);   // not the entrance offset's
        Assert.Equal(before, sound.Position);
        Assert.Equal(CreatureState.CentreOf(6) + 2929, creature.PosY);
    }

    // ---- state 210 ------------------------------------------------------------------------------

    [Fact]
    public void TheTremorKnockdownStarterRequestsC7OnceAtTheCreatureWithItsVoice()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, 2, Keeper]);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Dk2StateId = TremorRules.KnockdownState;
        var sim = new GameSimulation(db, world, 1, [new TremorCreatureSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Tick();

        ThingSoundRequest sound = Assert.Single(Sounds(sink));
        Assert.Equal(ThingSoundEvent.KnockedDown, sound.Event);
        Assert.Equal((ushort)0xC7, (ushort)sound.Event);
        Assert.Equal("CREATURE_GOBLIN", sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(goblin.Id)), sound.Source);
        Assert.Equal(At(goblin), sound.Position);
        Assert.True(sound.Voiced);
        Assert.Equal(TremorRules.WaitingState, goblin.Dk2StateId);

        sim.Run(5);
        Assert.Single(Sounds(sink));   // 263 and 264 request nothing
    }

    // ---- state 98 -------------------------------------------------------------------------------

    /// <summary>State 98's patrol leg that cannot be routed (<c>0x004630FF</c>): the keeper's own locked door in the
    /// leg's way stops a walk that respects doors (option 0x80), as the Guard Room's walks do.</summary>
    [Fact]
    public void AGuardWhoseNextLegCannotBeRoutedRequestsDdAndWaitsInNinetyNine()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        // A corridor one tile wide in solid rock, so the door is the only way along the room.
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = y == 3 && x is >= 4 and <= 8 ? Dk2TerrainFixture.ClaimedPath : Dk2TerrainFixture.Rock;
            tile.OwnerPlayerId = Keeper;
        }
        for (int x = 5; x <= 7; x++) RoomRules.Place(db, ref world.Tile(x, 3), db.Room(RoomTypeId.GuardRoom), Keeper);
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = 6, Y = 3, DoorId = 1, PlayerId = Keeper, Flags = 0x02, Unknown_0011 = new byte[3] }]);
        world.Doors[0].UnderConstruction = false;
        world.Doors[0].LockMode = 1;
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(5, 3));
        goblin.CurrentJob = JobType.Guard;
        StatePlacement.As(goblin, CreatureActivity.AtWork);
        StatePlacement.Place(goblin, GuardRules.PatrolState);
        goblin.GuardFacing = 0;
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Tick();

        Assert.Equal(GuardRules.LostState, goblin.Dk2StateId);
        ThingSoundRequest sound = Assert.Single(Sounds(sink));
        Assert.Equal(ThingSoundEvent.GuardLostItsWay, sound.Event);
        Assert.Equal((ushort)0xDD, (ushort)sound.Event);
        Assert.Equal("CREATURE_GOBLIN", sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(goblin.Id)), sound.Source);
        Assert.Equal(At(goblin), sound.Position);
        Assert.True(sound.Voiced);
    }

    // ---- state 93 -------------------------------------------------------------------------------

    [Fact]
    public void EatingAChickenRequests111InTheChickensCategoryThenCdInTheEatersWithTheirOwnVoices()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, 4]);
        for (int y = 1; y < 11; y++)
        for (int x = 1; x < 11; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }
        for (int y = 6; y < 9; y++)
        for (int x = 6; x < 9; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Hatchery), Keeper);
        ObjectState bird = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, (6 * world.Width) + 7);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Health = 500;
        goblin.HungerNeed = 1;
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem(), new CreatureEatSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        // A chicken keeps no height here, so the request takes the floor's (a deviation, ThingSoundRules): the
        // hatchery's, 0xE00.
        var birdAt = new FpPosition3(new Fp32(bird.PosX), new Fp32(bird.PosY), new Fp32(0xE00));

        for (int i = 0; i < 40 * Second && !sink.Events.Any(e => e.Kind == "chicken_eaten"); i++) sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "chicken_eaten");
        ThingSoundRequest[] heard = Sounds(sink);
        Assert.Equal(3, heard.Length);   // and the bird's deletion asks its own F4 (ObjectSoundTests)
        Assert.Equal(ThingSoundEvent.ChickenEaten, heard[0].Event);
        Assert.Equal((ushort)0x111, (ushort)heard[0].Event);
        Assert.Equal("OBJECT_CHICKEN", heard[0].Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, bird.Id), heard[0].Source);
        Assert.Equal(birdAt, heard[0].Position);
        Assert.True(heard[0].Voiced);

        Assert.Equal(ThingSoundEvent.Ate, heard[1].Event);
        Assert.Equal((ushort)0xCD, (ushort)heard[1].Event);
        Assert.Equal("CREATURE_GOBLIN", heard[1].Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(goblin.Id)), heard[1].Source);
        Assert.Equal(At(goblin), heard[1].Position);
        Assert.True(heard[1].Voiced);

        Assert.Equal((ThingSoundEvent.ObjectRemoved, "OBJECT_CHICKEN", heard[0].Source), (heard[2].Event, heard[2].Category, heard[2].Source));
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Anvil's sound <c>0x112</c> (<c>CreatureAction_Manufacture</c>, <c>0x00498D20</c> at <c>0x00498E08</c>): on each call of action
/// 32's handler once the game turn passes the action's start stamp less one plus the definition's hit delay (<c>+0x5CA</c>), a
/// manufacturer in a Workshop state asks it in the category of the first Anvil (object 15) on its state target's tile, with the
/// Anvil's own handle, at the Anvil's position. A Work Bench (16) or the Torture-machine-like 46 has none. Docs:
/// sound-effects-continuous.md section 2.</summary>
public sealed class ForgeSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Vampire = Dk2TerrainFixture.VampireCreatureId;
    private const int DelaySeconds = 2;

    private static GameDatabase Db()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var clip = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 20, FramesPerSecond = 20 };
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        foreach (int slot in new[] { 1, 20, WorkshopStep.WorkArtSlot }) slots[slot] = clip;
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Vampire
                ? c with { ArtSlots = slots, ManufacturePerSecond = 40, MeleeHitDelay = new Fp32(DelaySeconds << 12) } : c),
            basis.Variables, basis.KeeperSpells.Values,
            objects: basis.Objects.Values.Select(o => o.ObjectId == WorkshopStep.Anvil ? o with { SoundCategory = "OBJECT_FORGE" } : o),
            shots: basis.Shots.Values);
    }

    private static (WorldState World, CreatureState Maker, ObjectState Machine) Setup(GameDatabase db, byte machineType)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 5; y++)
            for (int x = 1; x <= 5; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), Keeper);
        Dk2TerrainFixture.FurnishRooms(db, world);
        ManufactureRules.Enqueue(ref world.Player(Keeper), new ManufactureOrder(ManufactureRules.DoorKind, 1, 60_000));
        ObjectState machine = world.Objects.First(o => o.ObjectTypeId == machineType);
        var at = new TilePos(3, 3);
        CreatureState it = world.SpawnCreature(db, Vampire, Keeper, at);
        it.CurrentJob = JobType.Manufacture;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, WorkshopStep.AtTheMachineState);
        it.HeldPlace = new RoomPlace((at.Y * world.Width) + at.X, RoomCapacityRules.Units);
        world.Player(Keeper).ManufacturingCreatures++;
        (it.StateTargetX, it.StateTargetY) = (CreatureState.CentreOf(machine.TileIndex % world.Width), CreatureState.CentreOf(machine.TileIndex / world.Width));
        it.MachineMode = WorkshopStep.AtIt;
        return (world, it, machine);
    }

    private static (ListEventSink Events, long Began) Run(GameDatabase db, WorldState world, CreatureState it)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        long began = -1;
        // Only the first work action: the maker goes on to choose another machine afterwards.
        for (int i = 0; i < 400 && it.CurrentJob == JobType.Manufacture; i++)
        {
            sim.Tick();
            if (began < 0 && it.Dk2StateId == WorkshopStep.WorkingState) began = it.StateEnteredTick;
            else if (began >= 0 && it.Dk2StateId != WorkshopStep.WorkingState) break;
        }
        return (events, began);
    }

    private static SimEvent[] Forge(ListEventSink events) =>
        events.Events.Where(e => e.ThingSound is { Source.Kind: SoundSourceKind.Object } s && (ushort)s.Event == 0x112).ToArray();

    [Fact]
    public void AnAnvilIsHeardOnEveryCallOfTheWorkActionOnceTheHitDelayHasPassedWithItsOwnHandleAndPosition()
    {
        GameDatabase db = Db();
        (WorldState world, CreatureState it, ObjectState anvil) = Setup(db, WorkshopStep.Anvil);
        var (events, began) = Run(db, world, it);

        Assert.True(began >= 0);
        SimEvent[] heard = Forge(events);
        Assert.NotEmpty(heard);
        int delay = CombatRules.TicksOf(new Fp32(DelaySeconds << 12));
        Assert.Equal(began + delay, heard[0].Tick.Value);
        Assert.Equal(heard.Select(e => e.Tick.Value), Enumerable.Range(0, heard.Length).Select(i => began + delay + i));
        Assert.All(heard, e =>
        {
            ThingSoundRequest sound = e.ThingSound!.Value;
            Assert.Equal("OBJECT_FORGE", sound.Category);
            Assert.Equal(new SoundSourceId(SoundSourceKind.Object, anvil.Id), sound.Source);
            Assert.True(sound.Voiced);
        });
    }

    [Fact]
    public void AWorkBenchHasNoSound()
    {
        GameDatabase db = Db();
        (WorldState world, CreatureState it, _) = Setup(db, WorkshopStep.Machine16);
        var (events, began) = Run(db, world, it);

        Assert.True(began >= 0);
        Assert.Empty(Forge(events));
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class PortalDropTests
{
    [Fact]
    public void OwnPortalCentreEnters197ThenRunsSackingWithoutSpillingGold()
    {
        var (sim, creature, events) = Setup();
        creature.DroppedToDoJob = JobType.Train;
        sim.World.Player(PlayerIds.FirstKeeper).EntranceInfo.Add(new(7, 0, 0));
        creature.GoldHeld = 350;
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(PlayerIds.FirstKeeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(PlayerIds.FirstKeeper, new TilePos(6, 6))));
        Assert.Equal(197, creature.Dk2StateId);
        Assert.Equal(JobType.None, creature.DroppedToDoJob);
        Assert.Contains(creature, sim.World.Creatures);
        sim.Tick();
        Assert.DoesNotContain(creature, sim.World.Creatures);
        Assert.Equal(0, creature.GoldHeld);
        Assert.Equal(40960, Assert.Single(sim.World.Player(PlayerIds.FirstKeeper).EntranceInfo).Block);
        Assert.Equal((byte)7, sim.World.Player(PlayerIds.FirstKeeper).LastSackedCreatureTypeId);
        Assert.Contains(sim.World.Messages.Entries, m => (uint)m.Kind == 20 && m.Data1 == 7);
        Assert.Equal(CreatureSoundEvent.Sacked, Assert.Single(events.Events, e => e.CreatureSound.HasValue).CreatureSound!.Value.Event);
        Assert.Equal((ushort)96, Assert.Single(events.Events, e => e.EffectRequest.HasValue).EffectRequest!.Value.EffectId.Value);
        Assert.Empty(sim.World.Objects);
    }

    [Fact]
    public void ImpIsRefusedAndOffCentreDropDoesNotEnter197()
    {
        var (sim, goblin, _) = Setup();
        var imp = sim.World.SpawnCreature(sim.Database, 1, PlayerIds.FirstKeeper, new TilePos(4, 4));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(PlayerIds.FirstKeeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(PlayerIds.FirstKeeper, new TilePos(6, 6))));
        Assert.True(imp.InHand);
        Assert.NotEqual(197, imp.Dk2StateId);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(PlayerIds.FirstKeeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(PlayerIds.FirstKeeper, new TilePos(5, 5))));
        Assert.False(goblin.InHand);
        Assert.NotEqual(197, goblin.Dk2StateId);
        Assert.Contains(goblin, sim.World.Creatures);
    }

    [Theory]
    [InlineData(5, 5, 3)]
    [InlineData(6, 6, 4)]
    public void State197RequiresAnOwnedPortalCentre(int x, int y, byte owner)
    {
        var (sim, creature, _) = Setup(owner);
        creature.PosX = CreatureState.CentreOf(x);
        creature.PosY = CreatureState.CentreOf(y);
        creature.Dk2StateId = 197;
        sim.Tick();
        Assert.Contains(creature, sim.World.Creatures);
        Assert.NotEqual(196, creature.Dk2StateId);
    }

    [Fact]
    public void GetUpRoomDropRespectsCasinoWinnerGate()
    {
        var (sim, creature, _) = Setup();
        creature.PosX = CreatureState.CentreOf(6);
        creature.PosY = CreatureState.CentreOf(6);
        creature.CasinoBigWinnerTicks = 10;
        creature.Dk2StateId = DropLandingRules.GettingUpState;
        creature.ActionUntilTick = 0;
        sim.Tick();
        Assert.NotEqual(197, creature.Dk2StateId);
    }

    [Fact]
    public void PlacedNonImpWorkerUsesThePortalCentreState()
    {
        var (sim, creature, _) = Setup(worker: true);
        creature.PosX = CreatureState.CentreOf(6);
        creature.PosY = CreatureState.CentreOf(6);
        creature.Flags |= LevelPlacedCreatureRules.PlacedAsDropped;
        creature.Dk2StateId = DropLandingRules.LandingState;
        var placed = new GameSimulation(sim.Database, sim.World, 1, [new AutoFurnitureSystem(), new CreatureStatusSystem(), new CreatureJobSystem()]);
        placed.Tick();
        Assert.Equal(197, creature.Dk2StateId);
        placed.Tick();
        Assert.DoesNotContain(creature, sim.World.Creatures);
    }

    [Fact]
    public void NonWorkerHandDropRespectsCasinoWinnerGate()
    {
        var (sim, creature, _) = Setup();
        creature.CasinoBigWinnerTicks = 20;
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(PlayerIds.FirstKeeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(PlayerIds.FirstKeeper, new TilePos(6, 6))));
        Assert.NotEqual(197, creature.Dk2StateId);
        Assert.Contains(creature, sim.World.Creatures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimedChickenExpiryAndPossessionPreventThePortalHandler(bool possessing)
    {
        var (sim, creature, _) = Setup();
        creature.PosX = CreatureState.CentreOf(6);
        creature.PosY = CreatureState.CentreOf(6);
        creature.Dk2StateId = 197;
        creature.Possessed = possessing;
        if (!possessing) creature.ChickenTicks = 1;
        sim.Tick();
        Assert.Contains(creature, sim.World.Creatures);
        Assert.Equal(0, creature.ChickenTicks);
        if (possessing) Assert.Equal(197, creature.Dk2StateId);
        else Assert.NotEqual(197, creature.Dk2StateId);
    }

    private static (GameSimulation Sim, CreatureState Creature, ListEventSink Events) Setup(byte portalOwner = 3, bool worker = false)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        GameDatabase db = new(fixture.Terrain.Values,
            fixture.Rooms.Values.Select(r => r.TypeId == RoomTypeId.Portal ? r with { CreatedObjectEffectId = 96 } : r),
            fixture.Creatures.Values.Select(c => worker && c.CreatureId == 7 ? c with { Flags = c.Flags | CreatureFlags.IsWorker } : c), fixture.Variables);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, (byte)3, (byte)4]);
        for (int y = 4; y <= 8; y++)
        for (int x = 4; x <= 8; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = PlayerIds.FirstKeeper;
            if (x is >= 5 and <= 7 && y is >= 5 and <= 7)
                RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Portal), portalOwner);
        }
        var creature = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, new TilePos(4, 4));
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new HandSystem(), new CreatureStatusSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(events);
        return (sim, creature, events);
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class HandPickupEffectTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Floor = new(2, 2);

    private static (GameDatabase Db, WorldState World, GameSimulation Sim, ListEventSink Events) Setup(bool stored = false)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, Keeper]);
        var sim = new GameSimulation(db, world, 42, [new HandSystem { StoredCommandPickups = stored }]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (db, world, sim, events);
    }

    private static ViewEffectRequest[] Effects(ListEventSink events) =>
        events.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HumanPreparationRequestsOneUnattachedPuffAtCurrentXYAndAbsoluteHeight(bool stored)
    {
        var (db, world, sim, events) = Setup(stored);
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, Floor);
        creature.PosX += 321;
        creature.PosY -= 654;
        creature.PosZ = 0x2800;
        creature.Heading = 0x321;
        var expected = new ViewEffectRequest(new EffectId(117), Keeper,
            new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(0x1000)), new Dk2Angle((ushort)creature.Heading));

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));

        Assert.Equal(expected, Assert.Single(Effects(events)));
        Assert.False(creature.InHand);
        sim.Tick();
        sim.Tick();
        Assert.True(creature.InHand);
        Assert.Single(Effects(events));
    }

    [Fact]
    public void AForeignPrisonersPuffBelongsToTheLiftingKeeper()
    {
        var (db, world, sim, events) = Setup();
        CreatureState prisoner = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Neutral, Floor);
        PrisonRules.Imprison(new StateHookContext(world, db), prisoner, Keeper, world.TileIndex(Floor));

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, prisoner.Id)));

        Assert.Equal((PlayerId)Keeper, Assert.Single(Effects(events)).Owner);
        Assert.Equal((PlayerId)PlayerIds.Neutral, prisoner.OwnerPlayerId);
    }

    [Fact]
    public void AFullHandStillPuffsOncePerSuccessfulCallbackIncludingAlreadyPreparedCreatures()
    {
        var (db, world, sim, events) = Setup();
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, Floor);
        while (world.Player(Keeper).Hand.Count < HandRules.Capacity(db))
            world.Player(Keeper).Hand.Add(HandItem.Of(creature));

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        Assert.Single(Effects(events));
        Assert.Equal(HandRules.PreparationState, creature.Dk2StateId);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        Assert.Equal(2, Effects(events).Length);
        sim.Tick();
        sim.Tick();
        Assert.False(creature.InHand);
        Assert.Equal(2, Effects(events).Length);
    }

    [Fact]
    public void RefusedPermissionDoesNotPrepareOrPuff()
    {
        var (db, world, sim, events) = Setup();
        CreatureState foreign = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Neutral, Floor);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, foreign.Id)));

        Assert.NotEqual(HandRules.PreparationState, foreign.Dk2StateId);
        Assert.Empty(Effects(events));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectComputerTakeDoesNotRunTheHumanCallbacksPuff(bool stored)
    {
        var (db, world, sim, events) = Setup(stored);
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, Floor);

        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, creature.Id)));

        Assert.True(creature.InHand);
        Assert.Empty(Effects(events));
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A creature put down by the hand: 76's stun, 257's room drop and 250 (hand-drop-landing.md, ADR-0226).</summary>
public class DropLandingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;
    private static readonly TilePos Floor = new(2, 2);

    /// <summary>Off a room: stunned for the definition's <c>StunDuration</c> (Q201: drops flat,
    /// cannot fight, 2–3 s in the shipped data), up in 257 for the Goblin's eleven ticks (every
    /// Level 1–3 capture), then 250 for fifty turns from getting up, then idle. The state word
    /// <c>+0x7E</c> is stamped by the drop (<c>0x004E8DA0</c>), so a stale deadline from long ago
    /// does not shorten the stand (ADR-0345; before it, three of Level 1 seed 13's six Goblins
    /// idled at once).</summary>
    [Fact]
    public void AnOwnCreatureDroppedOnPlainFloor_IsStunned_GetsUpForElevenTicks_ThenStandsFiftyFromGettingUp_WhateverTheOldDeadline()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        int stun = SimulationTiming.TicksFromSeconds((int)db.CreatureById(GoblinId).StunDuration.ToDouble());
        Assert.Equal(11, DropLandingRules.GetUpTicks(db.CreatureById(GoblinId)));

        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, goblin.Id)));
        goblin.StateDeadlineTick = sim.World.CurrentTick.Value - 1000;
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        sim.Tick();
        long dropped = sim.World.CurrentTick.Value;
        Assert.Equal(255, goblin.Dk2StateId);

        while (goblin.Dk2StateId == 255 && sim.World.CurrentTick.Value < dropped + 200) sim.Tick();
        long up = sim.World.CurrentTick.Value;
        // 255 counts from the turn after it is set (Age 1, 0x004810E0): the Goblin's 1 s is four
        // turns in 255, as Level 1's capture measured.
        Assert.Equal(stun, up - dropped);
        Assert.Equal(257, goblin.Dk2StateId);
        Assert.Contains(sink.Events, e => e.Kind == "getting_up");

        while (goblin.Dk2StateId == 257 && sim.World.CurrentTick.Value < up + 40) sim.Tick();
        long stood = sim.World.CurrentTick.Value;
        Assert.Equal(11, stood - up);
        Assert.Equal(DropLandingRules.StandingState, goblin.Dk2StateId);
        // The counter has already moved past the tick whose systems stamped the word.
        long stamped = goblin.StateDeadlineTick;
        Assert.Equal(stood - 1, stamped);
        Assert.Contains(sink.Events, e => e.Kind == "standing" && e.Detail.EndsWith($"until={stamped + DropLandingRules.StandTicks}"));

        while (goblin.Dk2StateId == DropLandingRules.StandingState && sim.World.CurrentTick.Value < stood + 80) sim.Tick();
        Assert.Equal(stamped + DropLandingRules.StandTicks + 1, sim.World.CurrentTick.Value);
        Assert.NotEqual(DropLandingRules.StandingState, goblin.Dk2StateId);
    }

    /// <summary>The get-up length is the Getup animation's frames at its rate in four-tick seconds,
    /// rounded up, plus one; with no art it is one tick (ADR-0348).</summary>
    [Theory]
    [InlineData(69, 30, 11)]
    [InlineData(51, 30, 8)]
    [InlineData(16, 30, 4)]
    [InlineData(0, 0, 1)]
    public void GetUpTicks_IsTheGetupAnimationRoundedUpPlusOne(int frames, int fps, int ticks)
    {
        CreatureDef goblin = Dk2TerrainFixture.Database().CreatureById(GoblinId);
        Assert.Equal(ticks, DropLandingRules.GetUpTicks(goblin with { GetUpFrames = frames, GetUpFramesPerSecond = fps }));
    }

    // Twelve drops in the Level 5 seed-1 capture: state 76 for the drop's turn at height 4352 (256 over
    // the floor) on the packet's point, then the stun the turn after.
    [Fact]
    public void ADropFallsForItsOwnTurnAtTheCommandsPointAndLandsOnTheNext_ConfirmedInCapture()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        var point = new FpPosition2(new Fp32((Floor.X * Fp32.One) + 700), new Fp32((Floor.Y * Fp32.One) + 3100));

        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor, 0, point)));
        Assert.True(goblin.LandingPending);
        Assert.False(goblin.InHand);
        Assert.Equal((point.X.Raw, point.Y.Raw, DropLandingRules.FallingHeight), (goblin.PosX, goblin.PosY, goblin.PosZ));
        Assert.Equal(256, DropLandingRules.FallingHeight);
        Assert.NotEqual(255, goblin.Dk2StateId);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "dropped_stunned");
        ulong falling = sim.StateHash();
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        CreatureState loaded = restored.World.CreatureById(goblin.Id)!;
        Assert.Equal((true, (byte)Keeper), (loaded.LandingPending, loaded.LandingDropperPlayerId));
        Assert.Equal(falling, restored.StateHash());
        goblin.LandingPending = false;
        Assert.NotEqual(falling, sim.StateHash());
        goblin.LandingPending = true;

        sim.Tick();
        Assert.False(goblin.LandingPending);
        Assert.Equal((point.X.Raw, point.Y.Raw, 0, 255), (goblin.PosX, goblin.PosY, goblin.PosZ, goblin.Dk2StateId));
        Assert.Contains(sink.Events, e => e.Kind == "dropped_stunned" && e.Tick.Value == sim.CurrentTick.Value - 1);
    }

    [Fact]
    public void APointOutsideTheCommandsTileIsRefused()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor, 0,
            new FpPosition2(new Fp32((Floor.X + 3) * Fp32.One), new Fp32(Floor.Y * Fp32.One)))));
        Assert.True(goblin.InHand);
        Assert.False(goblin.LandingPending);
        Assert.Contains(sink.Events, e => e.Kind == "drop_refused");
    }

    // While it falls the creature is out of the hand but cannot be attacked, looked up for a fight (even
    // forced: 0x0045A6D0 refuses state 76 outright), lifted with the cursor's permission or hit by a boulder's test.
    [Fact]
    public void AFallingCreatureIsUnattackableAndCannotBeLiftedByPermission()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        Assert.True(goblin.LandingPending);

        Assert.True(HandRules.IsHeld(goblin));
        Assert.False(Combat.CombatRules.IsAttackable(goblin));
        Assert.False(Combat.CombatRules.MayLookForAFight(world, db, goblin, force: true));
        Assert.False(HandRules.HasPickupPermission(world, db, goblin, new PlayerId(Keeper)));
        goblin.LandingPending = false;
        Assert.True(HandRules.HasPickupPermission(world, db, goblin, new PlayerId(Keeper)));
    }

    // A stored pick-up has no permission test, so it can lift a falling creature; the landing then
    // leaves it alone. A creature that died on the way down does not get the stun either.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALandingLeavesALiftedOrDeadCreatureAlone(bool lifted)
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        var sim = new GameSimulation(db, world, 7, [new HandSystem { StoredCommandPickups = true }, new CreatureStatusSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(lifted
            ? InputFrame.Of(new DropCommand(Keeper, Floor), new PickUpCreatureCommand(Keeper, goblin.Id))
            : InputFrame.Of(new DropCommand(Keeper, Floor)));
        if (!lifted) goblin.Health = 0;

        sim.Run(lifted ? 2 : 1);

        Assert.False(goblin.LandingPending);
        Assert.Equal(lifted, goblin.InHand);
        Assert.NotEqual(255, goblin.Dk2StateId);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "dropped_stunned");
    }

    [Fact]
    public void AWorkerIsNotStunned()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, _) = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        sim.Tick();

        Assert.NotEqual(255, imp.Dk2StateId);
    }

    /// <summary>255's first call (Age 1, <c>0x004810E0</c>) asks once for the Creature Dazed Effect
    /// (101) at the creature, its owner's, tied to its head point (5000), which the view follows.</summary>
    [Fact]
    public void AStunnedCreature_AsksForTheDazedEffectOnce_TheTurnAfterItIsStunned()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        sim.Tick();
        long stunnedOn = sim.World.CurrentTick.Value - 1;
        for (int i = 0; i < 20; i++) sim.Tick();

        var dazed = sink.Events.Where(e => e.EffectRequest?.EffectId.Value == Combat.StatusEffectRules.DazedEffectId).ToList();
        Assert.Single(dazed);
        Assert.Equal(stunnedOn + 1, dazed[0].Tick.Value);
        ViewEffectRequest request = dazed[0].EffectRequest!.Value;
        Assert.Equal(Keeper, request.Owner.Value);
        Assert.Equal(Floor, request.Tile);
        Assert.Equal(goblin.Id, request.AttachedCreatureId);
        Assert.Equal(Combat.StatusEffectRules.DazedAttachPoint, request.AttachPoint);
    }

    /// <summary>A prisoner put down outside a cell goes free and is stunned like any drop: 76 tests no
    /// prison field (0x004BC710, 0x0046EFA7). The owner, 2026-11-01: "stunned like any drop, their
    /// stunned timers differ by creature type". Q201's "prison releases exempt" is the walk-out.</summary>
    [Fact]
    public void AFreedPrisonerIsStunnedLikeAnyDrop_AndItsJailersToLiftUntilItGetsUp()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState captive = world.SpawnCreature(db, GoblinId, Keeper + 1, new TilePos(3, 3));
        captive.PrisonOwnerPlayerId = Keeper;
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, captive.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "prisoner_freed");
        Assert.Equal(255, captive.Dk2StateId);

        // Stunned, it is still its jailer's to lift (+0x3CB kept); 255's end clears it (0x004811E5),
        // so once up it is not (owner, 2026-11-01).
        Assert.True(HandRules.HasPickupPermission(sim.World, db, captive, Keeper));
        while (captive.Dk2StateId == 255) sim.Tick();
        Assert.Equal(257, captive.Dk2StateId);
        Assert.False(PrisonRules.IsImprisoned(captive));
        Assert.False(HandRules.HasPickupPermission(sim.World, db, captive, Keeper));
    }

    /// <summary>76 never compares the creature's owner with the hand's: a non-worker (0x0046EFA7), or a
    /// worker of a player numbered below 3 (0x0046EFB7), is stunned at 0x0046F173. Another keeper's or a
    /// neutral's creature reaches the hand only through a stored pick-up (ADR-0616).</summary>
    [Theory]
    [InlineData(GoblinId, Keeper + 1, true)]
    [InlineData(Dk2TerrainFixture.ImpCreatureId, Keeper + 1, false)]
    [InlineData(Dk2TerrainFixture.ImpCreatureId, PlayerIds.Neutral, true)]
    public void AStoredPickUpsForeignCreatureLandsAs76SaysWhoeverDropsIt(byte type, int owner, bool stunned)
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState foreign = world.SpawnCreature(db, type, (byte)owner, new TilePos(3, 3));
        var sim = new GameSimulation(db, world, 7,
            [new HandSystem { StoredCommandPickups = true }, new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureJobSystem()]);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, foreign.Id)));
        sim.Run(2); // hand state 1 takes the prepared creature two turns after the command
        Assert.True(foreign.InHand);
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        sim.Tick();

        Assert.Equal(stunned, foreign.Dk2StateId == 255);
        Assert.Equal((byte)owner, foreign.OwnerPlayerId);
    }

    private static (GameDatabase, WorldState) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Keeper + 1]);
        return (db, world);
    }

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, 7,
            [new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureJobSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }
}

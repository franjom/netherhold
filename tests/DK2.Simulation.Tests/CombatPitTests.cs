// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Combat Pit, states 137 to 141 (ADR-0213, <c>combat-pit.md</c>).</summary>
public sealed class CombatPitTests
{
    private const byte Keeper = 4;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;

    // A 5x5 pit at (4..8, 2..6): its floor is the 3x3 at (5..7, 3..5).
    private static readonly TilePos Floor = new(6, 4);
    private static readonly TilePos OtherFloor = new(5, 3);
    private static readonly TilePos Rim = new(4, 4);
    private static readonly TilePos Outside = new(10, 4);

    private const byte Rival = 5;

    private static (GameDatabase, WorldState) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        for (int y = 2; y <= 6; y++)
        for (int x = 4; x <= 8; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.CombatPit), Keeper);
        return (db, world);
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42,
            [new HandSystem(), new RoomSellSystem(), new CombatSystem(), new CombatPitSystem(), new CreatureMovementSystem(), new CreatureHealthSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    private static void Drop(GameSimulation sim, CreatureState creature, TilePos at)
    {
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, at)));
    }

    private static void RunUntil(GameSimulation sim, Func<bool> done, int limit = 400)
    {
        for (int i = 0; i < limit && !done(); i++) sim.Tick();
        Assert.True(done(), "the condition was not reached");
    }

    [Fact]
    public void AnOwnCreatureDroppedOnThePitsFloorArrivesIn137_ThenIsPlacedIn138_ThenWaitsIn139AsAFighter()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Outside);

        Drop(sim, goblin, Floor);
        Assert.Equal(CombatPitRules.ArrivingState, goblin.Dk2StateId);
        Assert.True(goblin.CombatPitFighter);
        Assert.Equal(CreatureActivity.PitFighting, goblin.Activity);

        sim.Tick();
        Assert.Equal(CombatPitRules.PlacingState, goblin.Dk2StateId);
        sim.Tick();
        Assert.Equal(CombatPitRules.WaitingState, goblin.Dk2StateId);
        Assert.Contains(sink.Events, e => e.Kind == "pit_fighter");
    }

    [Fact]
    public void ACreatureDroppedOnThePitsRimIsAnOrdinaryDrop()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Outside);

        Drop(sim, goblin, Rim);

        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
        Assert.False(goblin.CombatPitFighter);
    }

    [Fact]
    public void TwoFightersArePairedAsEachOthersForcedTarget_AndFightThoughNeitherIsAttackable()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState a = world.SpawnCreature(db, GoblinId, Keeper, Outside);
        CreatureState b = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(11, 4));

        Drop(sim, a, Floor);
        Drop(sim, b, OtherFloor);
        RunUntil(sim, () => a.AttackingId == b.Id && b.AttackingId == a.Id);
        Assert.True(a.AttackAbnormalTargets && b.AttackAbnormalTargets);
        Assert.False(CombatRules.IsAttackable(a));

        RunUntil(sim, () => a.Activity == CreatureActivity.Fighting && b.Activity == CreatureActivity.Fighting);
        Assert.Equal(b.Id, a.FightTargetId);
        Assert.Equal(a.Id, b.FightTargetId);
    }

    [Fact]
    public void APitFighterHitsTwiceAsHard_AndTakesTwentyOf128OfWhatLands()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState a = world.SpawnCreature(db, GoblinId, Keeper, Outside);
        CreatureState b = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(11, 4));
        Drop(sim, a, Floor);
        Drop(sim, b, OtherFloor);
        Assert.Equal(20, StatusEffectRules.DamageMultiplier(world, db, a));

        RunUntil(sim, () => sink.Events.Any(e => e.Kind == "hit"), 800);
        string hit = sink.Events.First(e => e.Kind == "hit").Detail;
        int blow = Field(hit, "blow=");
        int landed = Field(hit, "landed=");
        int doubled = MeleeDamageRules.Swing(db, db.CreatureById(GoblinId), 1, MeleeDamageRules.Stance.CombatPitFighter);
        Assert.True(-blow >= doubled, $"blow {blow} is under the pit's doubled swing {doubled}");
        Assert.Equal(blow * 20 / 128, landed);
    }

    [Fact]
    public void TheWinnerLetsGoOfAKnockedOutOpponentWithNoVictory_AndGoesBackToThePit()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState a = world.SpawnCreature(db, GoblinId, Keeper, Outside);
        CreatureState b = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(11, 4));
        Drop(sim, a, Floor);
        Drop(sim, b, OtherFloor);
        RunUntil(sim, () => a.Activity == CreatureActivity.Fighting && a.FightTargetId == b.Id);

        // The opponent is not attackable, and still a valid target for a fighter only.
        Assert.True(FightRules.TargetIsValid(world, a));
        a.CombatPitFighter = false;
        Assert.False(FightRules.TargetIsValid(world, a));
        a.CombatPitFighter = true;

        // Owner (2026-09-25): the winner does not stand over the loser; it takes another fighter
        // or walks about. The leave hook 0x0045ADC0 releases it without the victory nibble.
        CreatureDeathRules.Collapse(b);
        sim.Tick();
        Assert.Equal(CombatRules.NoTarget, a.FightTargetId);
        Assert.False(a.AttackAbnormalTargets);
        Assert.NotEqual(CreatureActivity.Celebrating, a.Activity);
        RunUntil(sim, () => a.Activity == CreatureActivity.PitFighting && a.Dk2StateId != CombatPitRules.PlacingState, 10);
        Assert.True(a.CombatPitFighter);
    }

    [Fact]
    public void AnIdleFighterGoesBackTo138_AndThePickUpClearsTheFighterAndItsForcedTarget()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState a = world.SpawnCreature(db, GoblinId, Keeper, Outside);
        CreatureState b = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(11, 4));
        Drop(sim, a, Floor);
        Drop(sim, b, OtherFloor);
        RunUntil(sim, () => a.AttackingId == b.Id);

        CreatureStateMachine.TrySetState(b, CreatureStateMachine.Idle);
        b.StopMoving();
        sim.Tick();
        Assert.Equal(CombatPitRules.PlacingState, b.Dk2StateId);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, a.Id)));
        Assert.False(a.CombatPitFighter);
        Assert.False(a.AttackAbnormalTargets);
        Assert.Equal(CombatRules.NoTarget, a.AttackingId);
    }

    [Fact]
    public void SellingATileOfThePitThrowsItsFightersOutTo141_WhoComeBackThrough138()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState a = world.SpawnCreature(db, GoblinId, Keeper, Outside);
        Drop(sim, a, Floor);
        RunUntil(sim, () => a.Dk2StateId == CombatPitRules.WaitingState && !a.HasRoute);

        sim.Tick(InputFrame.Of(new SellRoomCommand(Keeper, [new TilePos(8, 6)])));
        Assert.Equal(CombatPitRules.ThrownOutState, a.Dk2StateId);
        Assert.False(a.CombatPitFighter);

        sim.Tick();
        Assert.Equal(CombatPitRules.ThrownOutState, a.Dk2StateId);
        sim.Tick();
        Assert.Equal(CombatPitRules.PlacingState, a.Dk2StateId);
    }

    [Fact]
    public void AWaitingFightersStepStaysOnTheFloor_AndItsAngerRisesEveryTurn()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState a = world.SpawnCreature(db, GoblinId, Keeper, Outside);
        Drop(sim, a, Floor);
        RunUntil(sim, () => a.Dk2StateId == CombatPitRules.WaitingState);

        RoomInstance pit = CombatPitRules.PitAt(world, db, (Floor.Y * world.Width) + Floor.X)!;
        int before = a.Anger[AngerBucket.Work], company = a.Anger[AngerBucket.Company];
        for (int i = 0; i < 200; i++)
        {
            sim.Tick();
            TilePos at = a.Tile;
            Assert.True(CombatPitRules.IsFloor(pit, (at.Y * world.Width) + at.X), $"left the floor at {at.X},{at.Y}");
        }

        // Every waiting turn adds angerNoWork to two buckets; a turn spent walking adds nothing.
        int noWork = db.CreatureById(GoblinId).AngerNoWork;
        Assert.True(noWork > 0);
        Assert.True(a.Anger[AngerBucket.Work] >= before + (10 * noWork), $"work anger {a.Anger[AngerBucket.Work]}");
        Assert.True(a.Anger[AngerBucket.Company] >= company + (10 * noWork), $"company anger {a.Anger[AngerBucket.Company]}");
    }

    [Fact]
    public void AFighterAtThePitsLevelCapGainsNoExperience_AndOneWhoReachesItIsSentToIdle()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        CreatureDef def = db.CreatureById(GoblinId);
        goblin.CombatPitFighter = true;
        goblin.Level = 8;
        Assert.False(CreatureLevelRules.Grant(db, def, goblin, 1_000_000));
        Assert.Equal(0, goblin.ExperiencePoints);

        goblin.Level = 7;
        CreatureStateMachine.TrySetState(goblin, CombatPitRules.ArrivingState);
        CreatureStateMachine.TrySetState(goblin, CombatPitRules.PlacingState);
        CreatureStateMachine.TrySetState(goblin, CombatPitRules.WaitingState);
        Assert.True(CreatureLevelRules.Grant(db, def, goblin, 1_000_000));
        Assert.Equal(8, goblin.Level);
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
    }

    [Fact]
    public void TheOpponentIsTheNearestEnemyFighterOnTheFloor_AFriendOnlyWhenThereIsNoEnemy()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState seeker = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(5, 4));
        CreatureState friend = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 4));
        CreatureState enemy = world.SpawnCreature(db, GoblinId, Rival, new TilePos(7, 5));
        CreatureState rimEnemy = world.SpawnCreature(db, GoblinId, Rival, new TilePos(4, 4));
        foreach (CreatureState c in new[] { friend, enemy, rimEnemy }) c.CombatPitFighter = true;
        RoomInstance pit = CombatPitRules.PitAt(world, db, (4 * world.Width) + 5)!;

        Assert.Same(enemy, CombatPitRules.Opponent(world, db, pit, seeker));
        enemy.CombatPitFighter = false;
        Assert.Same(friend, CombatPitRules.Opponent(world, db, pit, seeker));
    }

    [Fact]
    public void Placing138OnThePitsRimMakesNoFighter_AndGoesIdleFor142IsNotBuilt()
    {
        (GameDatabase db, WorldState world) = World();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Rim);
        Assert.True(CreatureStateMachine.TrySetState(goblin, CombatPitRules.ArrivingState));
        Assert.True(CreatureStateMachine.TrySetState(goblin, CombatPitRules.PlacingState));
        goblin.PitStateEnteredTick = -1;

        sim.Tick();

        Assert.False(goblin.CombatPitFighter);
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
    }

    private static int Field(string detail, string name)
    {
        int start = detail.IndexOf(name, StringComparison.Ordinal) + name.Length;
        int end = detail.IndexOf(' ', start);
        return int.Parse(end < 0 ? detail[start..] : detail[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The spell arms and the shots they fire — docs/research/combat-spells.md and shots.md,
/// ADR-0167: a caster at range engages with its surveyed attack spell, casts when the cooldown
/// allows, and the shot leaves after the slot's wind-up, flies at the definition's speed, and
/// deals its data word flat on contact. The fixture Warlock carries the shipped Fireball and
/// Firebomb slots against Goblin-stat targets.</summary>
public class CombatSpellTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Theory]
    [InlineData(0, false, false, true)]
    [InlineData(-1, false, false, false)]
    [InlineData(0, true, false, false)]
    [InlineData(0, false, true, false)]
    public void BattleUpdateRequiresCurrentEntryStampAndHostileMode(
        long stampOffset, bool allySpell, bool leaveBeforeUpdate, bool expected)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState creature = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        long tick = world.CurrentTick.Value;
        Assert.True(CreatureStateMachine.TrySetState(creature, 199, tick + stampOffset));
        if (allySpell) creature.AttackType = CombatRules.AttackTypes.SpellAtAlly;
        if (leaveBeforeUpdate) Assert.True(CreatureStateMachine.TrySetState(creature, 1, tick));
        Assert.Empty(world.Messages.Entries);
        new GameSimulation(db, world, 1, [new CombatSystem()]).Tick();
        Assert.Equal(expected, world.Messages.Entries.Any(m => m.Kind == PlayerMessageKind.CombatStarted));
    }

    [Fact]
    public void TravelArrivalAloneDoesNotPostAndItsEntryStampSurvivesSnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState creature = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        CreatureStateMachine.BeginTravel(creature, 6, 199);
        Assert.True(CreatureStateMachine.Arrive(creature, world.CurrentTick.Value));
        Assert.Empty(world.Messages.Entries);
        var sim = new GameSimulation(db, world, 1, [new CombatSystem()]);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new CombatSystem()]);
        Assert.Equal(creature.StateEnteredTick, restored.World.CreatureById(creature.Id)!.StateEnteredTick);
        sim.Tick(); restored.Tick();
        Assert.Single(world.Messages.Entries);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    [Fact]
    public void EnteringBattlePostsTheAttackersPositionWithoutRepeatingEveryTick()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        GameSimulation sim = Combat(db, world);
        sim.Tick();
        PlayerMessage message = Assert.Single(world.Messages.Entries, m => m.PlayerId == Keeper);
        Assert.Equal((PlayerMessageKind.CombatStarted, 5, 10), (message.Kind, message.Data1, message.Data2));
        sim.Tick();
        Assert.DoesNotContain(world.Messages.Entries, m => m.PlayerId == Keeper);
        Assert.Equal(CreatureActivity.Fighting, warlock.Activity);
    }

    [Fact]
    public void AWarlockThreeTilesFromAnEnemyEngagesWithItsFireball()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        GameSimulation sim = Combat(db, world);

        sim.Run(3);
        Assert.Equal(CreatureActivity.Fighting, warlock.Activity);
        Assert.Equal(goblin.Id, warlock.FightTargetId);
        Assert.Equal(CombatRules.AttackTypes.SpellAtCreature, warlock.AttackType);
        Assert.Equal(0, warlock.AttackSpellSlot);
        Assert.Equal((16384 >> 12) * 16384, warlock.AttackRangeSquared);
        Assert.Contains(warlock.Id, goblin.EngagedRanged);
        Assert.DoesNotContain(warlock.Id, goblin.EngagedMelee);
    }

    [Fact]
    public void TheShotLeavesTwelveTicksAfterTheCastAndLandsThreeHundred()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(40);
        SimEvent cast = sink.Events.First(e => e.Kind == "cast" && e.Detail.Contains("creature=" + warlock.Id) && e.Detail.Contains("slot=0"));
        var fired = sink.Events.First(e => e.Kind == "shot_fired" && e.Detail.Contains("creature=" + warlock.Id));
        // The wind-up is 11345/4096 s = 11 ticks; the shot leaves on the first tick past it.
        Assert.Equal(cast.Tick.Value + 12, fired.Tick.Value);
        // A Support re-acquires every tick and casts again when the recharge allows: the stamp is the latest cast's.
        Assert.True(warlock.SpellStamps[0] >= cast.Tick.Value);

        var hit = sink.Events.First(e => e.Kind == "shot_hit" && e.Detail.Contains("target=" + goblin.Id));
        Assert.Contains("damage=300", hit.Detail);
        Assert.Contains("landed=-300", hit.Detail);
        // A Fireball covers 3686 units a tick: the three tiles take four ticks.
        Assert.InRange(hit.Tick.Value - fired.Tick.Value, 1, 5);
        // The first shot is spent on the hit; a later recast may still be in flight.
        string shotId = fired.Detail.Split("shot=")[1].Split(' ')[0];
        Assert.Contains(sink.Events, e => e.Kind == "shot_died" && e.Detail.Contains("shot=" + shotId + " "));
    }

    [Fact]
    public void ARecastWaitsForTheRecharge_TwentyTicksStrictlyPast()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        // A frozen target stands and cannot close to melee, which would turn the struck Warlock to its fists.
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        goblin.FrozenTicks = 1000;
        StatePlacement.As(goblin, CreatureActivity.Frozen);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(80);
        long[] casts = sink.Events.Where(e => e.Kind == "cast" && e.Detail.Contains("creature=" + warlock.Id)).Select(e => e.Tick.Value).ToArray();
        Assert.True(casts.Length >= 2, "expected at least two casts: " + string.Join(",", casts));
        // stamp + 20 < now: the second cast is at least 21 ticks after the first.
        Assert.True(casts[1] - casts[0] >= 21, $"casts at {casts[0]} and {casts[1]}");
    }

    [Fact]
    public void ALevelEightWarlockThrowsTheFirebomb_WhoseExplosionInheritsAZeroRadius()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        warlock.Level = 8;
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        CreatureState bystander = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 9, 10);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(3);
        Assert.Equal(CombatRules.AttackTypes.SpellAtCreature, warlock.AttackType);
        Assert.Equal(1, warlock.AttackSpellSlot);

        sim.Run(40);
        var hit = sink.Events.First(e => e.Kind == "shot_hit" && e.Detail.Contains("caster=" + warlock.Id));
        Assert.Contains("damage=600", hit.Detail);
        // The explosion chained on the hit carries the Firebomb spell's data words: 600 and 0 —
        // a zero-tile area that hurts nobody (shots.md §7, CONFIRMED from the binary and the owner).
        var chained = sink.Events.First(e => e.Kind == "shot_chained" && e.Detail.Contains("type=" + Dk2TerrainFixture.FirebombExplosionShotId));
        Assert.Contains("data1=600 data2=0", chained.Detail);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "shot_area_hit");
        Assert.True(goblin.Health < 1500 || bystander.Health < 1500, "a Goblin took the Firebomb on the nose");
    }

    [Fact]
    public void AFireballStopsAtAWallAndDies()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        ShotDef fireball = db.Shots[Dk2TerrainFixture.FireballShotId];
        // From the third tile westward: the border tile is impenetrable rock.
        ShotState shot = ShotRules.Spawn(world, fireball, Keeper, CreatureState.CentreOf(3), CreatureState.CentreOf(10), 300, 0,
            0, FightTargetKind.None, 0, 0, -1, CreatureState.CentreOf(0), CreatureState.CentreOf(10));
        ShotRules.Launch(shot, fireball, CreatureState.CentreOf(0), CreatureState.CentreOf(10));
        Assert.Equal(-3686, shot.VelX);
        Assert.Equal(0, shot.VelY);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(6);
        Assert.Contains(sink.Events, e => e.Kind == "shot_stopped" && e.Detail.Contains("shot=" + shot.Id));
        Assert.Contains(sink.Events, e => e.Kind == "shot_died" && e.Detail.Contains("shot=" + shot.Id));
        Assert.Empty(world.Shots);
    }

    [Fact]
    public void AShotNeverHitsItsCaster_AndOnlyHitsEnemies()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        CreatureState friend = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 6, 10);
        ShotDef fireball = db.Shots[Dk2TerrainFixture.FireballShotId];
        ShotState shot = ShotRules.Spawn(world, fireball, Keeper, warlock.PosX, warlock.PosY, 300, 0,
            warlock.Id, FightTargetKind.None, 0, 0, -1, CreatureState.CentreOf(9), CreatureState.CentreOf(10));

        Assert.Null(ShotRules.FindHit(world, db, shot, fireball));
        shot.PosX = friend.PosX;
        Assert.Null(ShotRules.FindHit(world, db, shot, fireball));
        CreatureState enemy = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 10);
        Assert.Same(enemy, ShotRules.FindHit(world, db, shot, fireball));
    }

    [Fact]
    public void AShotInFlightSurvivesASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        ShotDef fireball = db.Shots[Dk2TerrainFixture.FireballShotId];
        ShotState shot = ShotRules.Spawn(world, fireball, Keeper, CreatureState.CentreOf(5), CreatureState.CentreOf(10), 300, 7,
            3, FightTargetKind.Creature, 9, 0, -1, CreatureState.CentreOf(8), CreatureState.CentreOf(10));
        ShotRules.Launch(shot, fireball, CreatureState.CentreOf(8), CreatureState.CentreOf(10));
        GameSimulation original = Combat(db, world);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new CombatSystem(), new ShotSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        ShotState copy = Assert.Single(restored.World.Shots);
        Assert.Equal((shot.Id, shot.VelX, shot.VelY, shot.Data1, shot.Data2, shot.CasterId, shot.TargetId, shot.TargetX),
            (copy.Id, copy.VelX, copy.VelY, copy.Data1, copy.Data2, copy.CasterId, copy.TargetId, copy.TargetX));
    }

    // ------------------------------------------------------------------ helpers

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId == Dk2TerrainFixture.Rock || tile.TerrainId == Dk2TerrainFixture.Gold
                    || tile.TerrainId == Dk2TerrainFixture.Gems)
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
            }
        }
        return (db, world);
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    private static GameSimulation Combat(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem(), new ShotSystem()]);
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>State 8 and the victory states — docs/research/combat-approach-and-victory.md,
/// ADR-0169: a fighter out of reach walks in state 8, refreshing its route every ten calls and
/// giving up a retreating target after the chase variable; a kill leaves the victory nibble and the
/// killer stands celebrating, or walks to a nearby corpse first. The pause's length is our
/// stand-in (Q376). Targets are frozen where they must stand still and not fight back.</summary>
public class CombatApproachTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte BodyLoverCreatureId = 51;

    [Theory]
    [InlineData(199)]
    [InlineData(200)]
    public void FightingWithoutARouteStillIntegratesNeighbourContact(int state)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState fighter = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 8, 10);
        CreatureState neighbour = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        StatePlacement.As(fighter, CreatureActivity.Fighting);
        fighter.Dk2StateId = state;
        neighbour.PosX += 100;
        fighter.StopMoving();
        int initialX = fighter.PosX;
        fighter.Heading = 321;
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);

        sim.Tick();

        Assert.NotEqual(initialX, fighter.PosX);
        Assert.Equal(321, fighter.Heading);
        Assert.False(fighter.IsRouted);
    }

    [Fact]
    public void AFighterOutOfReachWalksInStateEight_AndRefreshesItsRouteEveryTenCalls()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // Three tiles apart: within the fixture's four-tile sight, and some fifteen ticks' walk.
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 3, 10);
        CreatureState statue = Frozen(Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 10));
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(3);
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);
        Assert.True(goblin.Approaching, "state 8 entered");
        Assert.Contains(sink.Events, e => e.Kind == "approaches" && e.Detail.Contains("creature=" + goblin.Id));

        // The route is refreshed at the tenth handler call though the target has not moved a step.
        sim.Run(10);
        Assert.True(goblin.Approaching, "still walking");
        Assert.Single(sink.Events, e => e.Kind == "reroutes" && e.Detail.Contains("creature=" + goblin.Id));

        sim.Run(40);
        Assert.False(goblin.Approaching, "in reach, spFighting swings");
        Assert.Contains(sink.Events, e => e.Kind == "approach_ends" && e.Detail.Contains("reason=in_reach"));
        Assert.True(statue.Health < 1500, "a blow landed");
    }

    /// <summary>A target on the fighter's own tile, out of reach, is walked to: DK2's route within
    /// one triangle is the target itself (<c>0x004CF490</c>), not a finished route (ADR-0388). On
    /// Level 11A two imps in the hole in Morgana's wall stood 0.58 tile apart for 250 ticks.</summary>
    [Fact]
    public void AFighterWalksToATargetOnItsOwnTileOutOfReach()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 3, 10);
        CreatureState statue = Frozen(Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 3, 10));
        goblin.PosX = (3 * Fp32.One) + 200;
        statue.PosX = (3 * Fp32.One) + 3900;
        Assert.True(CombatRules.DistanceSquared(goblin.PosX, goblin.PosY, statue.PosX, statue.PosY) >= goblin.AttackRangeSquared
            || goblin.AttackRangeSquared == 0, "starts out of reach");
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(40);

        Assert.Contains(sink.Events, e => e.Kind == "approach_ends" && e.Detail.Contains("creature=" + goblin.Id) && e.Detail.Contains("reason=in_reach"));
        Assert.True(statue.Health < 1500, "a blow landed");
    }

    /// <summary>The approach is state 8, a walk resuming 199, and the reselection that picks the same
    /// target every tick leaves it walking: the engagement enters 199 only outside the Combat category
    /// read through a walk (<c>0x0045A550</c>, ADR-0483). Under a flag on 199 it never left 199.</summary>
    [Fact]
    public void TheApproachStaysInStateEight_WhileItsReselectionKeepsTheTarget_AndResumes199()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 3, 10);
        Frozen(Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 10));
        GameSimulation sim = Sim(db, world);

        int walked = 0;
        for (int i = 0; i < 40 && walked == 0 || i < 40 && goblin.Dk2StateId == 8; i++)
        {
            sim.Tick();
            if (goblin.Dk2StateId == 8)
            {
                walked++;
                Assert.Equal(199, goblin.UtilityNewState);
                Assert.Equal(CreatureActivity.Fighting, goblin.Activity);
                Assert.Equal(CreatureActivity.Fighting, ActivityRules.Of(goblin));
            }
        }

        Assert.True(walked > 5, $"walked {walked} ticks in state 8");
        Assert.Equal(199, goblin.Dk2StateId);
        Assert.Equal(CreatureStateMachine.Idle, goblin.UtilityNewState);
    }

    /// <summary>Q598: a dead creature that still holds a forced target is not processed. DK2's dead
    /// creature is a CDeadBody off the creature list; ours, a creature with no state, ran the forced
    /// arm, which skips the may-I-look gate, and was moved back into 199.</summary>
    [Fact]
    public void ACorpseHoldingAForcedTarget_IsNotProcessed()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState lord = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 3, 10);
        CreatureState rival = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 4, 10);
        lord.AttackAbnormalTargets = true;
        lord.AttackingId = rival.Id;
        StatePlacement.As(lord, CreatureActivity.Dead);
        GameSimulation sim = Sim(db, world);

        sim.Run(5);

        Assert.Equal(CreatureStateMachine.NoState, lord.Dk2StateId);
        Assert.Equal(FightTargetKind.None, lord.FightTargetKind);
    }

    [Fact]
    public void AGoodCreatureGivesUpARetreatingTargetAfterThreeSeconds_AnEvilOneKeepsChasing()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState hero = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 3, 6);
        CreatureState evil = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Keeper, 3, 14);
        CreatureState runner = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 6);
        CreatureState runner2 = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 14);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(3);
        Assert.True(hero.Approaching && evil.Approaching, "both approach");

        // The targets are held on the withdraw walk every tick, in place: the chase counters run. The
        // route stays loaded, because a withdraw that arrives ends, and its Combat leave hook lets
        // its attackers go (ADR-0319).
        var held = new[] { runner, runner2 }.Select(r => (r, r.PosX, r.PosY)).ToArray();
        for (int i = 0; i < 20; i++)
        {
            foreach ((CreatureState r, int x, int y) in held)
            {
                StatePlacement.As(r, CreatureActivity.Withdrawing);
                (r.PosX, r.PosY) = (x, y);
                var here = new Fixed16Vector(x << 4, y << 4);
                var away = new Fixed16Vector((x + (6 * Fp32.One)) << 4, y << 4);
                RouteWindow.Load(r.Body, [here, away], away, 0);
            }
            sim.Run(1);
        }

        // 3 s × 4 ticks: the Good creature gives up on its thirteenth approach tick; 7 s holds the evil one.
        SimEvent gaveUp = Assert.Single(sink.Events, e => e.Kind == "gives_up_chase");
        Assert.Contains("creature=" + hero.Id, gaveUp.Detail);
        Assert.Contains("ticks=13", gaveUp.Detail);
        Assert.Equal(runner.Id, hero.RetreatingCreature);
        Assert.False(hero.Approaching);
        Assert.Equal(CreatureActivity.Idle, hero.Activity);
        // The evil Knight's seven seconds outlast the walk: it never gives up.
        Assert.Equal(CombatRules.NoTarget, evil.RetreatingCreature);
        Assert.Equal(CreatureActivity.Fighting, evil.Activity);
    }

    [Fact]
    public void TheKillerCelebratesForOnePlayOfItsClip_StaysAttackable_AndDoesNotTurnOnABlow_Q376()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        goblin.DieInstantly = true; // only an outright death gives the victory (Q511)
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureHealthSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        for (int i = 0; i < 60 && !CreatureDeathRules.IsDown(goblin); i++) sim.Run(1);
        Assert.True(CreatureDeathRules.IsDown(goblin));

        // The blow lands before spFighting runs on the same tick, so the unlink is already made.
        Assert.Contains(sink.Events, e => e.Kind == "target_died" && e.Detail.Contains("victim=" + goblin.Id));
        Assert.Equal(CombatRules.AttackMode.Victorious, demon.AttackType);
        Assert.Equal(CreatureActivity.Fighting, demon.Activity);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Celebrating, demon.Activity);
        Assert.Equal(VictoryRules.PauseTicks(db, demon), demon.CelebrationTicks);
        Assert.True(CombatRules.IsAttackable(demon));
        Assert.False(CombatRules.IsFighting(demon));

        // The fixture's creatures have no clip, so their pause is one turn; a Bile Demon's slot-8
        // clip is 19 (combat-approach-and-victory.md §7). Give it that, so a blow lands within it.
        const int pause = 19;
        demon.CelebrationTicks = pause;

        // A second Goblin arrives and strikes: the celebration is not interrupted.
        CreatureState second = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon, dy: 1);
        sim.Run(pause - 1);
        Assert.Equal(CreatureActivity.Celebrating, demon.Activity);
        Assert.Equal(CombatRules.NoTarget, demon.FightTargetId);
        Assert.True(second.SwingInProgress || demon.Health < 6000, "the second Goblin fights the celebrant");

        sim.Run(1);
        Assert.NotEqual(CreatureActivity.Celebrating, demon.Activity);
        Assert.Contains(sink.Events, e => e.Kind == "celebration_over" && e.Detail.Contains("creature=" + demon.Id));
    }

    [Fact]
    public void ANeedBodyForFightIdleCreatureWalksToACorpseInReach_AndCelebratesThere()
    {
        CreatureDef lover = Dk2TerrainFixture.Goblin() with
        {
            Name = "Body lover", CreatureId = BodyLoverCreatureId,
            Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.NeedBodyForFightIdle | CreatureFlags.IsFearless,
        };
        GameDatabase db = Dk2TerrainFixture.Database(lover);
        WorldState world = OpenWorld(db);
        CreatureState killer = Spawn(db, world, BodyLoverCreatureId, Keeper, 5, 5);
        CreatureState victim = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, killer);
        victim.Health = 100;
        victim.DieInstantly = true; // an outright death gives the victory (Q511)
        CreatureState corpse = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 5);
        StatePlacement.As(corpse, CreatureActivity.Dead);
        corpse.Health = 0;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem(), new CreatureHealthSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        for (int i = 0; i < 40 && !CreatureDeathRules.IsDown(victim); i++) sim.Run(1);
        Assert.True(CreatureDeathRules.IsDown(victim));
        Assert.Same(corpse, VictoryRules.NearestBody(world, killer));
        sim.Run(1);
        Assert.Contains(sink.Events, e => e.Kind == "seeks_body");
        Assert.Equal(CreatureActivity.GoingToBody, killer.Activity);
        Assert.Equal(corpse.Id, killer.BodyTargetId);
        Assert.Contains(sink.Events, e => e.Kind == "seeks_body" && e.Detail.Contains("body=" + corpse.Id));

        for (int i = 0; i < 40 && killer.Activity == CreatureActivity.GoingToBody; i++) sim.Run(1);
        Assert.Equal(CreatureActivity.Celebrating, killer.Activity);
        Assert.Equal(CreatureActivity.Dead, corpse.Activity);
        Assert.True(CombatRules.DistanceSquared(killer.PosX, killer.PosY, corpse.PosX, corpse.PosY) < Fp32.One * Fp32.One >> 12,
            "standing within a tile of the body");
        // State 259 turns to face the body before the pause (0x004B2AE0, Q400).
        Assert.Equal(0, DK2.Simulation.Steering.Bearing.Distance(killer.Heading,
            DK2.Simulation.Steering.Bearing.Of(corpse.PosX - killer.PosX, corpse.PosY - killer.PosY)));
    }

    [Fact]
    public void TheWalkToABodyIsSixTowardsTwoFiftyNine_ThenTheCelebrationInTwoSixty()
    {
        CreatureDef lover = Dk2TerrainFixture.Goblin() with
        {
            Name = "Body lover", CreatureId = BodyLoverCreatureId,
            Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.NeedBodyForFightIdle | CreatureFlags.IsFearless,
        };
        GameDatabase db = Dk2TerrainFixture.Database(lover);
        WorldState world = OpenWorld(db);
        CreatureState killer = Spawn(db, world, BodyLoverCreatureId, Keeper, 5, 5);
        CreatureState victim = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, killer);
        victim.Health = 100;
        victim.DieInstantly = true; // an outright death gives the victory (Q511)
        CreatureState corpse = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 5);
        StatePlacement.As(corpse, CreatureActivity.Dead);
        corpse.Health = 0;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem(), new CreatureHealthSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        var seen = StateTrace.Of(sim, killer, 120, () => killer.Activity == CreatureActivity.Celebrating);

        Assert.Equal(199, seen[0].State);
        Assert.Contains((6, 199, 259), seen);
        Assert.Equal((260, 199, 1), seen[^1]);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void ANeedBodyForFightIdleCreatureWithNoCorpseNearCelebratesWhereItStands()
    {
        CreatureDef lover = Dk2TerrainFixture.Goblin() with
        {
            Name = "Body lover", CreatureId = BodyLoverCreatureId,
            Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.NeedBodyForFightIdle | CreatureFlags.IsFearless,
        };
        GameDatabase db = Dk2TerrainFixture.Database(lover);
        WorldState world = OpenWorld(db);
        CreatureState killer = Spawn(db, world, BodyLoverCreatureId, Keeper, 5, 5);
        CreatureState victim = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, killer);
        victim.Health = 100;
        victim.DieInstantly = true; // an outright death gives the victory (Q511)
        // A corpse two tiles off: beyond 0x2000 squared.
        CreatureState far = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 7, 5);
        StatePlacement.As(far, CreatureActivity.Dead);
        far.Health = 0;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureHealthSystem()]);

        for (int i = 0; i < 40 && !CreatureDeathRules.IsDown(victim); i++) sim.Run(1);
        sim.Run(1);
        Assert.Equal(CreatureActivity.Celebrating, killer.Activity);
        Assert.Null(VictoryRules.NearestBody(world, killer));
    }

    /// <summary>Q400: a Flanker aims behind its target's heading through the sine tables
    /// (<c>0x00470E30</c>), standing target or not: a quarter tile back for melee. Ours used the
    /// target's velocity, and a standing target gave its own position.</summary>
    [Fact]
    public void AFlankerAimsBehindAStandingTargetsHeading()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState flanker = Spawn(db, world, Dk2TerrainFixture.SkeletonCreatureId, Keeper, 3, 8);
        CreatureState target = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 8);
        target.Heading = 0x200; // facing +x
        flanker.AttackType = CombatRules.AttackMode.MeleeAtCreature;
        (int X, int Y) aim = default;
        new GameSimulation(db, world, 1, [new Probe(context => { context.Navigation.Refresh(); ApproachRules.Destination(context, flanker,
            db.CreatureById(flanker.CreatureTypeId), target, target.PosX, target.PosY, out aim.X, out aim.Y); })]).Tick(InputFrame.Empty);

        Assert.Equal((target.PosX - ApproachRules.FlankerMeleeOffset, target.PosY), aim);
    }

    /// <summary>Q400: at the body, state 259 turns by the creature's turn rate each tick
    /// (<c>0x004B2AE0</c>) and celebrates only once it faces the body.</summary>
    [Fact]
    public void AtTheBodyTheCreatureTurnsToFaceItBeforeTheCelebration()
    {
        CreatureDef slow = Dk2TerrainFixture.Goblin() with { Name = "Slow turner", CreatureId = BodyLoverCreatureId, TurnRate = 256 };
        GameDatabase db = Dk2TerrainFixture.Database(slow);
        WorldState world = OpenWorld(db);
        CreatureState killer = Spawn(db, world, BodyLoverCreatureId, Keeper, 5, 5);
        CreatureState corpse = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 5, 5);
        StatePlacement.As(corpse, CreatureActivity.Dead);
        corpse.PosX = killer.PosX + 0x10;
        killer.BodyTargetId = corpse.Id;
        killer.Heading = 0x600; // facing -x, half a turn from the body
        int calls = 0;
        var sim = new GameSimulation(db, world, 1, [new Probe(context =>
        {
            if (killer.Activity == CreatureActivity.Celebrating) return;
            calls++;
            VictoryRules.GoingToBody(context, killer);
        })]);

        sim.Run(8);

        Assert.Equal(CreatureActivity.Celebrating, killer.Activity);
        Assert.Equal(4, calls); // 1024 to turn at 256 a tick
    }

    /// <summary>Q398: shots are list 1 of the frame's entity pass (<c>0x004B7830</c>), each list
    /// walked newest first since the linker adds at the head (<c>0x004B4A10</c>). Two shots that
    /// end on the same tick end newest first. Ours walked them oldest first.</summary>
    [Fact]
    public void ShotsAreUpdatedNewestFirst()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        byte type = db.Shots.Keys.First();
        ShotState older = world.SpawnShot(type, Keeper, 0x5800, 0x5800);
        ShotState newer = world.SpawnShot(type, Keeper, 0x6800, 0x5800);
        older.Health = 0;
        newer.Health = 0;
        var sim = new GameSimulation(db, world, 1, [new ShotSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Empty);

        Assert.Equal([newer.Id, older.Id], sink.Events.Where(e => e.Kind == "shot_died")
            .Select(e => int.Parse(e.Detail.Split(' ')[0]["shot=".Length..], System.Globalization.CultureInfo.InvariantCulture)));
    }

    private sealed class Probe(Action<SimulationContext> run) : ISimulationSystem
    {
        public string Name => "probe";
        public void Execute(SimulationContext context) => run(context);
    }

    [Fact]
    public void TheApproachAndVictoryWordsSurviveASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        goblin.Dk2StateId = 8; // the approach, resuming 199
        goblin.UtilityNewState = 199;
        goblin.RouteRefreshCountdown = 4;
        goblin.ChaseTicks = 5;
        goblin.RetreatingCreature = 6;
        goblin.CelebrationTicks = 7;
        goblin.BodyTargetId = 9;
        GameSimulation original = Sim(db, world);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new CombatSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        CreatureState copy = restored.World.CreatureById(goblin.Id)!;
        Assert.Equal((true, 4, 5, 6, 7, 9),
            (copy.Approaching, copy.RouteRefreshCountdown, copy.ChaseTicks, copy.RetreatingCreature, copy.CelebrationTicks, copy.BodyTargetId));
    }

    // ------------------------------------------------------------------ helpers

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        return (db, OpenWorld(db));
    }

    private static WorldState OpenWorld(GameDatabase db)
    {
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
        return world;
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    /// <summary>A creature placed within melee reach of another, a third of a tile to its east.</summary>
    private static CreatureState Touching(GameDatabase db, WorldState world, byte type, byte owner, CreatureState other, int dy = 0)
    {
        CreatureState c = Spawn(db, world, type, owner, other.Tile.X, other.Tile.Y + dy);
        c.PosX = other.PosX + (Fp32.One / 3);
        c.PosY = other.PosY + (dy * Fp32.One / 3);
        return c;
    }

    /// <summary>A target that stands and does not fight back, still attackable: frozen for a long time.</summary>
    private static CreatureState Frozen(CreatureState c)
    {
        c.FrozenTicks = 10000;
        StatePlacement.As(c, CreatureActivity.Frozen);
        return c;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CreatureStatusSystem(), new CombatSystem(), new CreatureMovementSystem(), new ShotSystem()]);
}

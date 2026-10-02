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
    public void TheKillerCelebratesForEightTicks_StaysAttackable_AndDoesNotTurnOnABlow_Q376()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureHealthSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        for (int i = 0; i < 60 && !CreatureDeathRules.IsDown(goblin); i++) sim.Run(1);
        Assert.True(CreatureDeathRules.IsDown(goblin));

        // The blow lands before spFighting runs on the same tick, so the unlink is already made.
        Assert.Contains(sink.Events, e => e.Kind == "target_died" && e.Detail.Contains("victim=" + goblin.Id));
        Assert.Equal(CombatRules.AttackTypes.Victorious, demon.AttackType);
        Assert.Equal(CreatureActivity.Fighting, demon.Activity);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Celebrating, demon.Activity);
        Assert.Equal(VictoryRules.PauseTicks, demon.CelebrationTicks);
        Assert.True(CombatRules.IsAttackable(demon));
        Assert.False(CombatRules.IsFighting(demon));

        // A second Goblin arrives and strikes: the celebration is not interrupted.
        CreatureState second = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon, dy: 1);
        sim.Run(VictoryRules.PauseTicks - 1);
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
        // A corpse two tiles off: beyond 0x2000 squared.
        CreatureState far = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 7, 5);
        StatePlacement.As(far, CreatureActivity.Dead);
        far.Health = 0;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureHealthSystem()]);

        for (int i = 0; i < 40 && !CreatureDeathRules.IsDown(victim); i++) sim.Run(1);
        sim.Run(2);
        Assert.Equal(CreatureActivity.Celebrating, killer.Activity);
        Assert.Null(VictoryRules.NearestBody(world, killer));
    }

    [Fact]
    public void TheApproachAndVictoryWordsSurviveASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        goblin.Approaching = true;
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

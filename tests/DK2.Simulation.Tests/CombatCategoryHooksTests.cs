// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using System.Reflection;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

/// <summary>The Combat category's enter hook <c>0x0047A810</c> and leave hook <c>0x0047A980</c>
/// (combat-category-hooks.md, ADR-0319), on the routes out of category 22 that ours builds.</summary>
public class CombatCategoryHooksTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const int Engaging = 199;
    private const int Celebrating = 260;
    private const int Fleeing = 203;
    private const int Collapsed = 216;
    private const int TravelToPoint = 6;

    [Theory]
    [InlineData("Disengage")]
    [InlineData("SetStateOne")]
    [InlineData("HandPreparation")]
    [InlineData("Collapse")]
    public void LeavingCombatByEachBuiltRoute_RunsTheLeaveHookOnce_AndReleasesBothSides(string route)
    {
        (WorldState world, StateHookContext hooks) = OpenWorld(out GameDatabase db);
        CreatureState me = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState them = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 5);
        Engage(hooks, me, them);
        Engage(hooks, them, me);

        int runs = LeaveRuns(me, () =>
        {
            switch (route)
            {
                case "Disengage": CombatRules.Disengage(hooks, me); break;
                case "SetStateOne": Assert.True(CreatureStateMachine.TrySetState(hooks, me, CreatureStateMachine.Idle)); break;
                case "HandPreparation": Assert.True(CreatureStateMachine.TrySetState(hooks, me, HandRules.PreparationState)); break;
                case "Collapse":
                    me.Health = 0;
                    Assert.True(CreatureDeathRules.Collapse(hooks, me));
                    break;
            }
        });

        Assert.Equal(1, runs);
        Assert.Equal(FightTargetKind.None, me.FightTargetKind);
        Assert.DoesNotContain(me.Id, them.EngagedMelee);
        Assert.False(me.Running);
        // The other side: my attacker lets go of me, and my slots are empty.
        Assert.Equal(FightTargetKind.None, them.FightTargetKind);
        Assert.DoesNotContain(them.Id, me.EngagedMelee);
    }

    [Fact]
    public void ACollapsingFighter_GivesItsAttackersNoVictory_InferredFromDk2HoldingTheCountdownInHealth()
    {
        (WorldState world, StateHookContext hooks) = OpenWorld(out GameDatabase db);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 5);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        Engage(hooks, goblin, demon);
        Engage(hooks, demon, goblin);

        goblin.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(hooks, goblin));

        Assert.Equal(Collapsed, goblin.Dk2StateId);
        Assert.Equal(FightTargetKind.None, demon.FightTargetKind);
        Assert.NotEqual(CombatRules.AttackTypes.Victorious, demon.AttackType);
    }

    [Fact]
    public void AFighterThatDiesOutright_GivesItsAttackersTheVictoryNibble()
    {
        (WorldState world, StateHookContext hooks) = OpenWorld(out GameDatabase db);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, 6, 5);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        Engage(hooks, imp, demon);
        Engage(hooks, demon, imp);
        Assert.False(CreatureDeathRules.HasDyingState(world, db, db.CreatureById(imp.CreatureTypeId), imp));

        imp.Health = 0;
        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, CreatureStateMachine.Idle));

        Assert.Equal(CombatRules.AttackTypes.Victorious, demon.AttackType);
        Assert.Equal(FightTargetKind.None, demon.FightTargetKind);
    }

    [Theory]
    [InlineData(Celebrating)]
    [InlineData(Fleeing)]
    public void AMoveBetweenTwoCombatStates_RunsNoHook(int next)
    {
        (WorldState world, StateHookContext hooks) = OpenWorld(out GameDatabase db);
        CreatureState me = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState them = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 5);
        Engage(hooks, me, them);
        Engage(hooks, them, me);
        me.ActionUntilTick = 99;

        int runs = LeaveRuns(me, () => Assert.True(CreatureStateMachine.TrySetState(hooks, me, next)));

        Assert.Equal(0, runs);
        Assert.Equal(them.Id, me.FightTargetId);
        Assert.Contains(me.Id, them.EngagedMelee);
        Assert.Equal(me.Id, them.FightTargetId);
        Assert.True(me.Running);
        Assert.Equal(99, me.ActionUntilTick);
    }

    [Fact]
    public void EnteringCombat_StopsTheCreature_AndEndsARunningAction()
    {
        (WorldState world, StateHookContext hooks) = OpenWorld(out GameDatabase db);
        CreatureState me = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        var here = new Fixed16Vector(me.PosX << 4, me.PosY << 4);
        RouteWindow.Load(me.Body, [here, new Fixed16Vector((me.PosX + 4096) << 4, me.PosY << 4)], here, 0);
        Assert.True(me.HasRoute);
        me.ActionUntilTick = 99;
        me.SwingInProgress = true;
        me.Tunnel.ActionUntilTick = 99;

        Assert.True(CreatureStateMachine.TrySetState(hooks, me, Engaging));

        Assert.False(me.HasRoute);
        Assert.Equal(0, me.ActionUntilTick);
        Assert.False(me.SwingInProgress);
        Assert.Equal(0, me.Tunnel.ActionUntilTick);
    }

    [Fact]
    public void APitFightersKnockedOutOpponent_ReleasesItWithNoVictory_AndItsForcedTargetGoes()
    {
        (WorldState world, StateHookContext hooks) = OpenWorld(out GameDatabase db);
        CreatureState loser = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 6, 5);
        CreatureState winner = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        Engage(hooks, loser, winner);
        Engage(hooks, winner, loser);
        winner.AttackingId = loser.Id;
        winner.AttackAbnormalTargets = true;
        winner.CombatPitFighter = true;

        loser.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(hooks, loser));

        // ADR-0213: no victory, and the fight is over for the winner.
        Assert.NotEqual(CombatRules.AttackTypes.Victorious, winner.AttackType);
        Assert.Equal(FightTargetKind.None, winner.FightTargetKind);
        Assert.Equal(CombatRules.NoTarget, winner.AttackingId);
        Assert.False(winner.AttackAbnormalTargets);
    }

    [Fact]
    public void AWalkFromCombatToAnErrandOutsideIt_ReleasesTheFight_AsTheRouteHelpersDo()
    {
        (WorldState world, StateHookContext hooks) = OpenWorld(out GameDatabase db);
        CreatureState me = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState them = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 6, 5);
        Engage(hooks, me, them);
        Engage(hooks, them, me);

        CreatureStateMachine.BeginTravel(hooks, me, TravelToPoint, CreatureStateMachine.Idle);

        Assert.Equal(FightTargetKind.None, me.FightTargetKind);
        Assert.Equal(FightTargetKind.None, them.FightTargetKind);
        Assert.DoesNotContain(me.Id, them.EngagedMelee);
        // 0x0045ADC0 alone: the walk speed is the leave hook's, which a walk does not run.
        Assert.True(me.Running);
    }

    // ------------------------------------------------------------------ helpers

    private static (WorldState, StateHookContext) OpenWorld(out GameDatabase db)
    {
        db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        return (world, new StateHookContext(world, db));
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    /// <summary>Into 199 with a melee engagement on <paramref name="target"/>, registered there.</summary>
    private static void Engage(in StateHookContext hooks, CreatureState me, CreatureState target)
    {
        if (me.Dk2StateId != Engaging) Assert.True(CreatureStateMachine.TrySetState(hooks, me, Engaging));
        me.FightTargetId = target.Id;
        me.FightTargetKind = FightTargetKind.Creature;
        me.AttackType = CombatRules.AttackTypes.MeleeAtCreature;
        me.Running = true;
        Assert.True(CombatRules.Register(target, me.Id, ranged: false));
    }

    // The Combat leave delegate, wrapped once for the process with a counter for watched creatures;
    // the wrapper calls the original, so other tests running at the same time see no difference.
    private static readonly ConcurrentDictionary<CreatureState, int> Watched = new();
    private static Delegate? original;
    private static readonly object InstallGate = new();

    private static int LeaveRuns(CreatureState creature, Action act)
    {
        Install();
        Watched[creature] = 0;
        act();
        Watched.TryRemove(creature, out int runs);
        return runs;
    }

    private static void Install()
    {
        lock (InstallGate)
        {
            if (original is not null) return;
            var leaves = (Array)typeof(CategoryHooks).GetField("Leaves", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            original = (Delegate)leaves.GetValue((int)CreatureStateCategory.Combat)!;
            MethodInfo counting = typeof(CombatCategoryHooksTests).GetMethod(nameof(Counting), BindingFlags.NonPublic | BindingFlags.Static)!;
            leaves.SetValue(Delegate.CreateDelegate(original.GetType(), counting), (int)CreatureStateCategory.Combat);
        }
    }

    private static void Counting(in StateHookContext hooks, CreatureState creature)
    {
        if (Watched.TryGetValue(creature, out int n)) Watched[creature] = n + 1;
        original!.DynamicInvoke(hooks, creature);
    }
}

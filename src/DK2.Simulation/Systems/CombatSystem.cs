// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Systems;

/// <summary>DK2's <c>ProcessFight</c> (<c>0x0045E9F0</c>) for every creature but the possessed one,
/// <c>spFighting</c> for those already fighting, the flee and withdraw states for those leaving,
/// and the fear accumulator after each (combat-acquisition.md §3, combat-fight-state.md §2,
/// combat-fear.md): a neutral creature never looks; one that may look and is due runs
/// <c>SelectAVictim</c>; a fighter holds its target while it stands, backs off or runs when its
/// fear says so, winds up and lands its swing, or looks again and walks closer. The scan and the fear
/// accumulator are <see cref="Update"/>; the handlers are <see cref="States"/>.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-fight-state.md")]
public sealed class CombatSystem : ICreatureUpdateStep
{
    private const string SystemName = "combat";

    private readonly List<Fixed16Vector> _scratch = [];
    private readonly HeartSites _hearts = new();
    private readonly RoomCache _rooms = new();
    private WorldSight? _sight;
    private WorldState? _sightWorld;

    public CombatSystem() => States = new StateHandlers(this);

    /// <summary>Turns a creature keeps tracking one it gave up chasing (<c>0x00489AA3</c>: 0x3C).</summary>
    public const int RetreatTrackingTicks = 60;

    public string Name => SystemName;

    /// <summary>The combat states' handlers (<c>spFighting</c>, the flee, withdraw, alarm and
    /// victory states), which DK2's dispatcher <c>0x00476FC0</c> runs after the needs routine, in the
    /// same update as the fight scan: a creature the scan engages runs its fight state at once.
    /// A <see cref="CreatureUpdateSystem"/> that takes this system must take this step too, or it
    /// loses every combat handler and the trap reveal.</summary>
    public ICreatureUpdateStep States { get; }

    /// <summary>Alone, the scan and the handlers for each creature in turn.</summary>
    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this, States);

    public void BeginTurn(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        WorldSteering navigation = context.Navigation;
        navigation.Refresh();
        if (!ReferenceEquals(_sightWorld, world))
        {
            _sight = new WorldSight(navigation, world, database);
            _sightWorld = world;
        }
        _hearts.Refresh(world, database);
    }

    /// <summary>The action runner (<c>0x00489A68</c>), the fight scan (<c>0x00489AC7</c>) and the fear
    /// radiation (<c>0x00489ACE</c>), before the state dispatcher; newer creatures react before older ones.</summary>
    public void Update(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        // DK2's dead creature is a CDeadBody, not on this list; ours keeps the creature with no
        // state (ADR-0206), whose forced target would otherwise still be pursued (Q598).
        if (CreatureDeathRules.IsCorpse(creature)) return;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        // Action handlers continue while possession pauses state dispatch.
        if (creature.SwingInProgress)
        {
            if (creature.SwingKind == SpellRules.SpellCast)
            {
                if (FightRules.CastFires(creature, definition, context.Tick.Value)) FightRules.FireSpell(context, creature, definition);
            }
            else if (creature.SwingKind == SpellRules.FreeAimCast)
            {
                if (FightRules.CastFires(creature, definition, context.Tick.Value)) FightRules.FireFreeAim(context, creature, definition);
            }
            else if (CombatRules.BlowLands(creature, definition, context.Tick.Value))
            {
                FightRules.LandBlow(context, creature, definition);
            }
        }

        // The tracking of a creature given up for retreating lapses after 60 turns, the chase
        // counter with it (0x00489A8A-0x00489AB5, ADR-0584).
        if (creature.RetreatingCreature != 0)
        {
            if (++creature.AvoidRetreatersCounter >= RetreatTrackingTicks)
            {
                creature.RetreatingCreature = 0;
                creature.ChaseTicks = 0;
                creature.AvoidRetreatersCounter = 0;
            }
        }
        else creature.AvoidRetreatersCounter = 0;

        // A creature in a combat state is its handler's; 6 -> 254 and 254 are outside the combat
        // group and look for a fight on the way.
        if (!CreatureDeathRules.IsPossessed(world, creature) && !InCombatState(creature))
            ProcessFight(context, creature, definition);

        // 0x0045E4F0 runs in the creature's own update, after its fight processing.
        FearRules.Radiate(context, _sight!, _hearts, creature, definition);
    }

    private static bool InCombatState(CreatureState creature) => creature.Activity is CreatureActivity.Fighting
        or CreatureActivity.Fleeing or CreatureActivity.Withdrawing or CreatureActivity.RaisingAlarm
        or CreatureActivity.Celebrating or CreatureActivity.GoingToBody;

    /// <summary>The dispatcher's turn for a combat state (<c>0x0048A030</c>), then the trap reveal that
    /// follows it (<c>0x0048A0B4</c>).</summary>
    private void Dispatch(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        if (CreatureDeathRules.IsCorpse(creature)) return;
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        if (!CreatureDeathRules.IsPossessed(world, creature))
        {
            bool handled = true;
            switch (creature.Activity)
            {
                case CreatureActivity.Fighting: Fight(context, creature, definition); break;
                case CreatureActivity.Fleeing: FearRules.Flee(context, _sight!, _hearts, _rooms, creature, definition, _scratch); break;
                case CreatureActivity.Withdrawing: FearRules.Withdrawing(context, creature); break;
                case CreatureActivity.RaisingAlarm: FearRules.RaisingAlarm(context, _rooms, creature, definition); break;
                case CreatureActivity.Celebrating: VictoryRules.Celebrating(context, creature); break;
                case CreatureActivity.GoingToBody: VictoryRules.GoingToBody(context, creature); break;
                case CreatureActivity.Reporting: FearRules.Reporting(context, creature); break;
                default: handled = false; break;
            }
            // A handler's zero, or its own SetState(1), ends the turn: state 1's handler runs on the
            // next one (0x00476FC0 runs one handler a turn, ADR-0574).
            if (handled && creature.Dk2StateId == CreatureStateMachine.Idle) context.IdledByCombatHandlerThisTick.Add(creature);
        }

        // A creature on a new tile reveals adjacent traps.
        TilePos tile = creature.Tile;
        if (creature.LastFightScanTileIndex != (tile.Y * world.Width) + tile.X)
            TrapRules.RevealAround(context, creature, definition);
        creature.LastFightScanTileIndex = (tile.Y * world.Width) + tile.X;
    }

    private sealed class StateHandlers(CombatSystem combat) : ICreatureUpdateStep
    {
        public string Name => "combat states";
        public void Execute(SimulationContext context)
        {
            combat.BeginTurn(context);
            CreatureUpdateSystem.RunAlone(context, this);
        }
        // Inside a sweep the scan's BeginTurn ran first; this only covers a sweep without it.
        public void BeginTurn(SimulationContext context)
        {
            if (!ReferenceEquals(combat._sightWorld, context.World)) combat.BeginTurn(context);
        }
        public void Update(SimulationContext context, CreatureState creature) => combat.Dispatch(context, creature);
    }

    private void ProcessFight(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.OwnerPlayerId == PlayerIds.Neutral) return;
        // A forced target skips the may-I-look gate (combat-pit.md §4).
        if (!creature.AttackAbnormalTargets && !CombatRules.MayLookForAFight(context.World, context.Database, creature, force: false)) return;

        TilePos tile = creature.Tile;
        bool moved = creature.LastFightScanTileIndex != (tile.Y * context.World.Width) + tile.X;
        if (!CombatRules.ScanIsDue(creature, moved)) return;

        // A creature this engages raises the battle notice from 199's handler, later in this update.
        CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch);
    }

    /// <summary><c>spFighting</c> in the order it runs (combat-fight-state.md §2), with state 8 as
    /// <see cref="ApproachRules"/> while <see cref="CreatureState.Approaching"/> is set: a dead target
    /// leaves the victory nibble; no target seeks one or takes the victory arm; an invalid target is
    /// let go and another sought; the fear checks, when an escape tile exists; a swing in progress
    /// stops everything else; in reach, a swing or a cast begins when the recharge allows; out of
    /// reach, a creature target is sought again and the approach entered.</summary>
    private void Fight(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        long tick = context.Tick.Value;
        CombatMessageRules.UpdateBattle(world, creature, tick);

        if (!CombatRules.MayLookForAFight(world, context.Database, creature, force: false))
        {
            LetGo(context, creature, "cannot_continue");
            return;
        }

        if (creature.Approaching)
        {
            ApproachRules.Tick(context, _sight!, _hearts, _rooms, creature, definition, _scratch);
            return;
        }

        // Step 1: a target that has died is dropped with the 0xF nibble (§6); the victory arm runs next tick.
        // A healer holds no attacker slot on its ally, so an ally's death marks nothing.
        if (creature.FightTargetKind == FightTargetKind.Creature && creature.AttackType != CombatRules.AttackMode.SpellAtAlly)
        {
            // A knocked-out opponent that was fighting released this fighter through its leave hook
            // already, with no victory (ADR-0213, ADR-0319); one knocked out outside Combat did not,
            // and the fight ends here, the forced target going with this fighter's own leave hook.
            CreatureState? dying = world.CreatureById(creature.FightTargetId);
            if (dying != null && creature.CombatPitFighter && CreatureStateMachine.HasState(dying) && CreatureDeathRules.IsDown(dying))
            {
                LetGo(context, creature, "opponent_knocked_out");
                return;
            }
            if (dying != null && CreatureDeathRules.IsDown(dying))
            {
                // Only an outright death gives the nibble (0x0045E4A0 from the death bookkeeping);
                // a collapsing victim fails IsAttackable at step 5 and is let go with none (Q511).
                bool outright = dying.Dk2StateId == CreatureStateMachine.NoState
                    || !CreatureDeathRules.HasDyingState(world, context.Database, context.Database.CreatureById(dying.CreatureTypeId), dying);
                if (outright) VictoryRules.TargetDied(context, creature);
                else VictoryRules.TargetDown(context, creature);
                return;
            }
        }

        // Step 3: no target — a victim is sought, or the victory arm taken.
        // The victim search comes first: only a fighter that finds none celebrates (0x0045F1C1-0x0045F1F1).
        if (creature.FightTargetKind == FightTargetKind.None)
        {
            if (CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
                return;
            if (creature.AttackType == CombatRules.AttackMode.Victorious)
                VictoryRules.Victory(context, creature, definition);
            else
                LetGo(context, creature, "nobody");
            return;
        }

        if (!FightRules.TargetIsValid(world, context.Database, creature))
        {
            // A forced target that fails is let go with the fight, the leave hook dropping it: no
            // other is sought (§4).
            if (creature.AttackAbnormalTargets)
            {
                LetGo(context, creature, "forced_target_gone");
                return;
            }
            // Unlinked in place and another sought, in 199; only none ends the state and runs the
            // Combat leave hook (§2 steps 5 and 10, ADR-0319).
            CombatRules.Unlink(world, creature);
            if (!CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
                LetGo(context, creature, "target_gone");
            return;
        }

        if (FearRules.FearChecks(context, _sight!, _hearts, _rooms, creature, definition, _scratch)) return;

        // Busy (0x00496D00): the state handler does not select another attack or approach.
        if (FightRules.MeleeActionIsRunning(creature, definition, context.Tick)) return;

        // Step 8: a Support re-acquires every tick, keeping a new target and restoring the old one
        // when nothing is found (combat-fight-state.md §2).
        if (definition.FightStyle == FightStyle.Support && !FightRules.Reacquire(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
            return;

        if (!FightRules.TargetPosition(world, context.Database, creature, out int x, out int y, out TilePos targetTile)) return;
        if (FightRules.InRange(_sight!, creature, x, y))
        {
            creature.StopMoving();
            int heading = creature.Heading;
            int left = Bearing.TurnToward(ref heading, Bearing.Of(x - creature.PosX, y - creature.PosY), SimulationTiming.TurnRatePerTick(definition));
            creature.Heading = heading;
            if (left != 0) return;
            if (creature.AttackType == CombatRules.AttackMode.SpellAtAlly)
            {
                // The ally arm (combat-spells.md §8): cast once when the cooldown allows, else stand.
                if (SpellRules.CanCast(context.Database, creature, definition, creature.AttackSpellSlot, tick))
                    FightRules.CastOnAlly(context, creature, definition);
            }
            else if (creature.AttackType is CombatRules.AttackMode.SpellAtCreature or CombatRules.AttackMode.SpellAtThing)
            {
                // The spell arm (combat-spells.md §6): cast when the cooldown allows, else §9.
                if (SpellRules.CanCast(context.Database, creature, definition, creature.AttackSpellSlot, tick))
                    FightRules.StartCast(context, creature, definition, creature.AttackSpellSlot);
                else
                    CannotCast(context, creature, definition, x, y);
            }
            else if (CombatRules.SwingMayBegin(context.Database, creature, definition, tick))
            {
                FightRules.StartSwing(context, _sight!, creature, definition, _scratch);
            }
            return;
        }

        // Out of reach: a creature target is chosen afresh, and no choice ends the fight. A forced
        // target is kept and walked to (0x0045F885).
        if (creature.FightTargetKind == FightTargetKind.Creature && !creature.AttackAbnormalTargets)
        {
            int previous = creature.FightTargetId;
            CombatRules.Unlink(world, creature);
            if (!CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
            {
                LetGo(context, creature, "nobody_in_reach");
                return;
            }
            if (!CombatRules.IsFighting(creature)) return;
            if (creature.FightTargetId != previous) creature.StopMoving();
            if (!FightRules.TargetPosition(world, context.Database, creature, out x, out y, out targetTile)) return;
        }

        // Only this out-of-reach path clears the count, after re-selection (0x0045F960-0x0045F97D).
        if (creature.FightTargetKind == FightTargetKind.Creature && world.CreatureById(creature.FightTargetId) is { } fought
            && !ApproachRules.IsRetreating(fought))
            creature.ChaseTicks = 0;

        // State 8 (combat-approach-and-victory.md §1): the walk, with its own handler from next tick.
        if (ApproachRules.Enter(context, creature, x, y))
            CreatureSoundRules.Request(context, SystemName, creature, CreatureSoundEvent.BattleCall);
    }

    /// <summary>A spell that cannot be cast (combat-spells.md §9, §10): the target is let go and
    /// another sought; the same target chosen again with a spell nibble makes the caster step back
    /// when within a quarter of its range and sight, else run. DK2 restores the old target when
    /// nothing is found; here the creature stands idle until its next scan (Q398).</summary>
    private void CannotCast(SimulationContext context, CreatureState creature, CreatureDef definition, int targetX, int targetY)
    {
        WorldState world = context.World;
        FightTargetKind kind = creature.FightTargetKind;
        int previous = creature.FightTargetId;
        if (creature.AttackAbnormalTargets)
        {
            // 0x0045FDB7: a Support steps back or runs at once; anyone else drops the engagement and
            // its forced target, is forced again on the old target (0x0048F390) and re-selects.
            if (definition.FightStyle == FightStyle.Support)
            {
                StepBackOrRun(context, creature, definition, targetX, targetY, previous);
                return;
            }
            // 0x0045FE7B pushes the engaged id, so the forced target is the engaged one, of its kind.
            CombatRules.Unlink(world, creature);
            creature.AttackAbnormalTargets = true;
            creature.AttackingId = previous;
            creature.AttackingKind = kind;
            if (!CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
                LetGo(context, creature, "cannot_cast");
            return;
        }
        CombatRules.Unlink(world, creature);
        if (!CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
        {
            LetGo(context, creature, "cannot_cast");
            return;
        }
        if (!CombatRules.IsFighting(creature)) return;
        if (creature.FightTargetKind != kind || creature.FightTargetId != previous) return;
        if (creature.AttackType is not (CombatRules.AttackMode.SpellAtCreature or CombatRules.AttackMode.SpellAtThing)) return;
        StepBackOrRun(context, creature, definition, targetX, targetY, previous);
    }

    /// <summary>combat-spells.md §10 (inside <c>spFighting</c>, <c>0x0045FF3D</c>): within a quarter of both range and sight the
    /// caster steps back, else it runs.</summary>
    private void StepBackOrRun(SimulationContext context, CreatureState creature, CreatureDef definition, int targetX, int targetY, int previous)
    {
        int distance = CombatRules.DistanceSquared(creature.PosX, creature.PosY, targetX, targetY);
        int sightSq = CombatRules.RangeSquared(definition.DistanceCanSee);
        if (distance < creature.AttackRangeSquared / 4 && distance < sightSq / 4)
        {
            if (FearRules.StepBack(context, creature, definition, FearRules.CastStepBackLength(definition), _scratch))
                context.Emit(SystemName, "steps_back", $"creature={creature.Id} target={previous}");
        }
        else
        {
            creature.Running = true;
        }
    }

    private static void LetGo(SimulationContext context, CreatureState creature, string reason)
    {
        CombatRules.Disengage(context.Hooks, creature);
        context.Emit(SystemName, "disengaged", $"creature={creature.Id} player={creature.OwnerPlayerId} reason={reason}");
    }
}

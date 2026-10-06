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
/// fear says so, winds up and lands its swing, or looks again and walks closer.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-fight-state.md")]
public sealed class CombatSystem : ISimulationSystem
{
    private const string SystemName = "combat";

    private readonly List<Fixed16Vector> _scratch = [];
    private readonly HeartSites _hearts = new();
    private readonly RoomCache _rooms = new();
    private WorldSight? _sight;
    private WorldState? _sightWorld;

    public string Name => SystemName;

    public void Execute(SimulationContext context)
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

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
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

            if (!CreatureDeathRules.IsPossessed(world, creature))
            {
                switch (creature.Activity)
                {
                    case CreatureActivity.Fighting: Fight(context, creature, definition); break;
                    case CreatureActivity.Fleeing: FearRules.Flee(context, _sight!, _hearts, _rooms, creature, definition, _scratch); break;
                    case CreatureActivity.Withdrawing: FearRules.Withdrawing(context, creature); break;
                    case CreatureActivity.RaisingAlarm: FearRules.RaisingAlarm(context, _rooms, creature, definition); break;
                    case CreatureActivity.Celebrating: VictoryRules.Celebrating(context, creature); break;
                    case CreatureActivity.GoingToBody: VictoryRules.GoingToBody(context, creature); break;
                    case CreatureActivity.Reporting:
                        // States 6 → 254 and 254 are outside the combat group: they look for a fight on the way.
                        FearRules.Reporting(context, creature);
                        ProcessFight(context, creature, definition);
                        break;
                    default: ProcessFight(context, creature, definition); break;
                }
            }

            // 0x0045E4F0 runs in the creature's own update, after its fight processing.
            FearRules.Radiate(context, _sight!, _hearts, creature, definition);

            TilePos tile = creature.Tile;
            // 0x0048A0B4, after the fight processing: a creature on a new tile reveals adjacent traps.
            if (creature.LastFightScanTileIndex != (tile.Y * world.Width) + tile.X && !CreatureDeathRules.IsCorpse(creature))
                TrapRules.RevealAround(context, creature, definition);
            creature.LastFightScanTileIndex = (tile.Y * world.Width) + tile.X;
        }
    }

    private void ProcessFight(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.OwnerPlayerId == PlayerIds.Neutral) return;
        // A forced target skips the may-I-look gate (combat-pit.md §4).
        if (!creature.AttackAbnormalTargets && !CombatRules.MayLookForAFight(context.World, context.Database, creature, force: false)) return;

        TilePos tile = creature.Tile;
        bool moved = creature.LastFightScanTileIndex != (tile.Y * context.World.Width) + tile.X;
        if (!CombatRules.ScanIsDue(creature, moved)) return;

        CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch);
        CombatMessageRules.UpdateBattle(context.World, creature, context.Tick.Value);
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
        if (creature.FightTargetKind == FightTargetKind.Creature && creature.AttackType != CombatRules.AttackTypes.SpellAtAlly)
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
        if (creature.FightTargetKind == FightTargetKind.None)
        {
            if (creature.AttackType == CombatRules.AttackTypes.Victorious)
            {
                VictoryRules.Victory(context, creature, definition);
                return;
            }
            if (!CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
                LetGo(context, creature, "nobody");
            return;
        }

        if (!FightRules.TargetIsValid(world, creature))
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

        // Busy (0x00496D00): a swinging creature neither moves nor looks.
        if (creature.SwingInProgress) return;

        // Step 8: a Support re-acquires every tick, keeping a new target and restoring the old one
        // when nothing is found (combat-fight-state.md §2).
        if (definition.FightStyle == FightStyle.Support && !FightRules.Reacquire(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
            return;

        if (!FightRules.TargetPosition(world, creature, out int x, out int y, out TilePos targetTile)) return;
        if (FightRules.InRange(_sight!, creature, x, y))
        {
            creature.StopMoving();
            if (creature.AttackType == CombatRules.AttackTypes.SpellAtAlly)
            {
                // The ally arm (combat-spells.md §8): cast once when the cooldown allows, else stand.
                if (SpellRules.CanCast(context.Database, creature, definition, creature.AttackSpellSlot, tick))
                    FightRules.CastOnAlly(context, creature, definition);
            }
            else if (creature.AttackType is CombatRules.AttackTypes.SpellAtCreature or CombatRules.AttackTypes.SpellAtThing)
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

        // Out of reach: a creature target is chosen afresh, and no choice ends the fight.
        if (creature.FightTargetKind == FightTargetKind.Creature)
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
            if (!FightRules.TargetPosition(world, creature, out x, out y, out targetTile)) return;
        }

        // State 8 (combat-approach-and-victory.md §1): the walk, with its own handler from next tick.
        ApproachRules.Enter(context, creature, x, y);
    }

    /// <summary>A spell that cannot be cast (combat-spells.md §9, §10): the target is let go and
    /// another sought; the same target chosen again with a spell nibble makes the caster step back
    /// when within a quarter of its range and sight, else run. DK2 restores the old target when
    /// nothing is found; here the creature stands idle until its next scan (Q398).</summary>
    private void CannotCast(SimulationContext context, CreatureState creature, CreatureDef definition, int targetX, int targetY)
    {
        WorldState world = context.World;
        byte kind = creature.FightTargetKind;
        int previous = creature.FightTargetId;
        CombatRules.Unlink(world, creature);
        if (!CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch))
        {
            LetGo(context, creature, "cannot_cast");
            return;
        }
        if (!CombatRules.IsFighting(creature)) return;
        if (creature.FightTargetKind != kind || creature.FightTargetId != previous) return;
        if (creature.AttackType is not (CombatRules.AttackTypes.SpellAtCreature or CombatRules.AttackTypes.SpellAtThing)) return;

        int distance = CombatRules.DistanceSquared(creature.PosX, creature.PosY, targetX, targetY);
        int sightSq = CombatRules.RangeSquared(definition.DistanceCanSee);
        if (distance < creature.AttackRangeSquared / 4 && distance < sightSq / 4)
        {
            if (FearRules.StepBack(context, creature, definition, targetX, targetY, _scratch))
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

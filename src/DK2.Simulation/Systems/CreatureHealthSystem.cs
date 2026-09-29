// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>Regeneration, hunger's clock, and death — once a game second, on its last
/// tick, the phase <see cref="ManaSystem"/> uses.</summary>
/// <remarks>Per second rather than per tick because every number it applies is written
/// per second in the data; Q1 is settled at DK2's own four ticks a second and these are
/// still per-second numbers. Zero health is not death: a creature collapses for
/// <c>CreatureDyingStateDurationSeconds</c> (60), rescuable by an imp that carries it
/// back to its bed, and only then dies — CONFIRMED by the owner (Q99); the whole
/// process, and which half of it is missing, is in <see cref="CreatureDeathRules"/>.
/// Registered after <see cref="PortalSystem"/>, so a creature that arrives this tick is
/// not regenerated on it and its hunger clock starts from a full second.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-needs.md")]
public sealed class CreatureHealthSystem : ISimulationSystem
{
    private const string SystemName = "CreatureHealth";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        // Descending, so removing a dead creature cannot skip its neighbour.
        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState state = world.Creatures[i];

            // A prisoner's HEALTH belongs to PrisonSystem alone: it withers at
            // PrisonModifyCreatureHealthPerSecond and dies into a Skeleton, not a
            // collapse, so nothing here touches its health or dying clock
            // (ADR-0064). Its HUNGER is this system's: DK2 suppresses the hunger
            // clock on a state-flag bit only the fifteen drinking, gambling and
            // dancing states set, so a prisoner does get hungry (ADR-0074).
            if (PrisonRules.IsImprisoned(state))
            {
                Hunger(context, database.CreatureById(state.CreatureTypeId), state);
                continue;
            }

            CreatureDef creature = database.CreatureById(state.CreatureTypeId);

            // Terrain first, and it applies to the dying as well as the
            // standing: a creature that collapses in lava is still in lava, and
            // lying in it is no safer than walking through it. A creature that
            // can cross the hazard pays nothing — which for water is all but
            // the two vampires (CONFIRMED, Q118).
            int terrain = CreatureHealthRules.TerrainHealthPerSecond(
                world, database, creature, state);
            if (terrain != 0)
            {
                int landed = CreatureHealthRules.Apply(database, creature, state, terrain);
                if (landed != 0)
                {
                    context.Emit(SystemName, "creature_terrain_health",
                        $"creature={state.Id} type={creature.Name} " +
                        $"player={state.OwnerPlayerId} delta={landed} " +
                        $"health={state.Health}/{CreatureHealthRules.MaxHealth(database, creature, state)} " +
                        $"at=({state.Tile.X},{state.Tile.Y})");
                }
            }

            // A dying creature does nothing but die: no hunger clock and, above
            // all, no regeneration. A corpse counts its own clock down and does
            // nothing else — no health to regenerate, no hunger to feel, nothing
            // left to collapse into.
            if (CreatureDeathRules.IsCorpse(state))
            {
                Rotting(context, creature, state);
                continue;
            }

            if (CreatureDeathRules.IsDying(state))
            {
                Dying(context, creature, state);
                continue;
            }

            Hunger(context, creature, state);
            CannotSleep(context, database, creature, state);

            // Healing sources add: nothing in the data or in OpenKeeper makes them
            // exclusive, and the own-land term is zero for 44 of the 46 creatures
            // anyway (Q100). Own-land regeneration is suppressed at zero health
            // and lair healing is not, because a bed is the one route back —
            // otherwise King Reginald, at 400 a second on his own land, would be
            // unkillable at home.
            int regen = CreatureHealthRules.OwnLandRegenPerSecond(world, creature, state)
                        + CreatureHealthRules.LairHealPerSecond(world, database, state);

            if (regen != 0)
            {
                int landed = CreatureHealthRules.Apply(database, creature, state, regen);
                if (landed != 0)
                {
                    context.Emit(SystemName, "creature_health",
                        $"creature={state.Id} type={creature.Name} " +
                        $"player={state.OwnerPlayerId} delta={landed} " +
                        $"health={state.Health}/{CreatureHealthRules.MaxHealth(database, creature, state)}");
                }
            }

            // Last, and deliberately after the healing above: a creature
            // rescued into its bed at zero health is healed to 100 on this same
            // second and so is never seen collapsed. CreatureDeathRules.Rescue
            // explains why reversing these two would undo every rescue.
            if (!CreatureDeathRules.HasCollapsed(state)) continue;

            // Four gates, not one: no body, possessed, standing in a Prison or a
            // Torture Chamber, or DieInstantly out of doors (Q303). Everything else
            // gets its sixty seconds.
            if (CreatureDeathRules.HasDyingState(world, database, creature, state))
                Collapse(context, creature, state);
            else
                Die(context, creature, state);
        }
    }

    /// <summary>Tired and not asleep: the needs routine's health loss (NeedRules, ADR-0199).</summary>
    private static void CannotSleep(
        SimulationContext context, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        int delta = NeedRules.CannotSleepHealthPerSecond(database, creature, state);
        if (delta == 0) return;
        int landed = CreatureHealthRules.Apply(database, creature, state, delta);
        if (landed == 0) return;
        context.Emit(SystemName, "creature_cannot_sleep_health",
            $"creature={state.Id} type={creature.Name} player={state.OwnerPlayerId} delta={landed} " +
            $"health={state.Health}/{CreatureHealthRules.MaxHealth(database, creature, state)}");
    }

    private static void Hunger(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (!CreatureHealthRules.CanGetHungry(creature)) return;

        state.HungerTicks += SimulationTiming.TicksPerSecond;

        // The Casino's states stop the clock but not its time (NeedRules, ADR-0198).
        if (!NeedRules.HungerClockRuns(state)) return;

        // The clock ADDS a helping and restarts; it does not raise a flag. A
        // creature with nowhere to eat therefore accumulates, and owes every
        // helping it missed (EatingRules.Tick, docs/research/eating.md).
        int rate = EatingRules.HungerRateSeconds(creature);
        if (!EatingRules.Tick(context.Database, creature, state)) return;

        context.Emit(SystemName, "creature_hungry",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} need={state.HungerNeed} " +
            $"after={rate}s");
    }

    /// <summary>Counts the rescue window down, and kills what it runs out on.</summary>
    private static void Dying(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        // 216's handler moves to 217 once the fall's action ends; ours has no animation and
        // lies down on the first second (ADR-0206).
        if (state.Dk2StateId == CreatureDeathRules.FallingState)
            CreatureStateMachine.TrySetState(state, CreatureDeathRules.DyingState);

        // 217's handler clears CombatPitFighter: a knocked-out loser is no longer a fighter.
        if (state.Dk2StateId == CreatureDeathRules.DyingState) state.CombatPitFighter = false;

        state.DyingTicks += SimulationTiming.TicksPerSecond;

        if (!CreatureDeathRules.DyingWindowElapsed(context.Database, state)) return;

        Die(context, creature, state);
    }

    /// <summary>
    /// Counts the corpse window down, and removes what it runs out on — the
    /// owner's <i>"if that timer runs out and no one carries the corpse it just
    /// disappears"</i> (Q99).
    ///
    /// <para><b>Even in an imp's arms.</b> A corpse being carried to a graveyard
    /// that is too far away rots on the way, and the imp arrives holding
    /// nothing. UNKNOWN whether the original pauses the clock for a carried body
    /// (Q164); this does not, because the alternative is a corpse that keeps for
    /// ever as long as somebody is walking with it.</para>
    /// </summary>
    private static void Rotting(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        state.DyingTicks += SimulationTiming.TicksPerSecond;

        if (!CreatureDeathRules.BodyWindowElapsed(context.Database, state)) return;

        context.Emit(SystemName, "corpse_rotted",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"decompose_value={creature.DecomposeValue} " +
            $"carried_by={state.CarriedByCreatureId}");

        Remove(context, state);
    }

    /// <summary>
    /// Takes a creature out of the world, and out of whatever arms were holding
    /// it. One place, so that no removal path can leave a carrier pointing at a
    /// creature that no longer exists.
    /// </summary>
    private static void Remove(SimulationContext context, CreatureState state)
    {
        CarryRules.DropWhatever(context.World, state);
        if (state.CarriedByCreatureId >= 0)
        {
            CreatureState? carrier = context.World.CreatureById(state.CarriedByCreatureId);
            if (carrier is not null) CarryRules.PutDown(carrier, state);
        }

        // And whatever it was carrying goes on the floor where it fell, rather
        // than out of the world with it (ADR-0065).
        if (ObjectCarryRules.DropWhatever(context.World, state))
        {
            context.Emit(SystemName, "object_dropped",
                $"imp={state.Id} object={state.CarriedObjectId} " +
                $"at=({state.Tile.X},{state.Tile.Y}) reason=carrier_gone");
        }

        context.World.RemoveCreature(state.Id);
    }

    /// <summary>
    /// Drops a creature to the floor. It is still alive, still its keeper's, and
    /// still holds its bed and its gold.
    /// </summary>
    private static void Collapse(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (!CreatureDeathRules.Collapse(state)) return;
        HeroPartyRules.KnockedOut(context.World, context.Triggers, state);

        context.Emit(SystemName, "creature_collapsed",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"rescue_window={CreatureDeathRules.DyingWindowTicks(context.Database)
                / SimulationTiming.TicksPerSecond}s " +
            $"bed={LairRules.BedTileIndex(context.World, context.Database, state)}");
    }

    private static void Die(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        context.FireCreatureEvent(state, CreatureTriggerEvent.Killed);

        // A dead creature gives up its bed, or a lair leaks a tile forever.
        // Not on collapse — the rescue path carries it back to that bed. A
        // creature that goes on to be a corpse releases it here too, so a body
        // lying on the floor is not still holding a lair tile.
        LairRules.ReleaseBed(context.World, context.Database, state);

        // The body is delivered by an imp task (ADR-0063). A corpse is not an
        // object (objects.md section 4); DK2 makes it a CDeadBody thing, ours
        // keeps it in the creature list with no state id (ADR-0206).
        bool leavesBody = CreatureDeathRules.LeavesBody(creature);

        // What it was carrying falls where it fell -- in one, two or three
        // piles, and the original's integer division destroys the remainder
        // (FloorGold.DropFrom). Reported as dropped-and-lost rather than as one
        // number, because the two are different amounts and the difference is
        // DK2's own quirk.
        int carried = state.GoldHeld;
        int dropped = FloorGold.DropFrom(context.World, context.Database, state);

        context.Emit(SystemName, "creature_died",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"gold_dropped={dropped} gold_lost_to_rounding={carried - dropped} " +
            $"piles={FloorGold.PileCount(carried)} dying_for={state.DyingTicks} " +
            $"leaves_body={leavesBody}");

        // A creature that leaves a body does not leave the world: it becomes a
        // corpse and starts its second clock. Everything else is gone on the
        // spot -- the Imp is "dead right away" and a Vampire reincarnates.
        if (leavesBody)
        {
            CarryRules.DropWhatever(context.World, state);
            ObjectCarryRules.DropWhatever(context.World, state);
            CreatureDeathRules.BecomeCorpse(context.World, context.Database, state);
            return;
        }

        Remove(context, state);
    }
}

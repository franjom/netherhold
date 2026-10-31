// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>Hunger clock updates each tick; regeneration, damage and death once per game second.</summary>
/// <remarks>Zero health enters the rescue window rather than immediately creating a corpse.
/// Lair healing follows the sleep step so a rescued sleeper can recover before collapse is booked.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-needs.md")]
public sealed class CreatureHealthSystem : ISimulationSystem
{
    private const string SystemName = "CreatureHealth";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        bool healthDue = (context.Tick.Value + 1) % SimulationTiming.TicksPerSecond == 0;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        // Descending, so removing a dead creature cannot skip its neighbour.
        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState state = world.Creatures[i];
            if (!healthDue)
            {
                if (!state.Possessed && !CreatureDeathRules.IsDying(state) && !CreatureDeathRules.IsCorpse(state))
                    Hunger(context, database.CreatureById(state.CreatureTypeId), state);
                continue;
            }

            // A prisoner's HEALTH belongs to PrisonSystem alone: it withers at
            // PrisonModifyCreatureHealthPerSecond and dies into a Skeleton, not a
            // collapse, so nothing here touches its health or dying clock
            // (ADR-0064). Its HUNGER is this system's: DK2 suppresses the hunger
            // clock on a state-flag bit only the fifteen drinking, gambling and
            // dancing states set, so a prisoner does get hungry (ADR-0074).
            if (PrisonRules.IsImprisoned(state))
            {
                if (!state.Possessed) Hunger(context, database.CreatureById(state.CreatureTypeId), state);
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
                // 0x0048A789: the source is the owner of the tile underfoot (+8 & 0xFFF).
                int landed = CreatureHealthRules.Apply(database, creature, state, terrain,
                    world.Tile(state.Tile.X, state.Tile.Y).OwnerPlayerId);
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

            if (!state.Possessed)
            {
                Hunger(context, creature, state);
                CannotSleep(context, database, creature, state);
            }

            // Healing sources add: nothing in the data or in OpenKeeper makes them
            // exclusive, and the own-land term is zero for 44 of the 46 creatures
            // anyway (Q100). Own-land regeneration is suppressed at zero health
            // and lair healing is not, because a bed is the one route back —
            // otherwise King Reginald, at 400 a second on his own land, would be
            // unkillable at home.
            int regen = CreatureHealthRules.OwnLandRegenPerSecond(world, creature, state)
                        + (state.Possessed ? 0 : CreatureHealthRules.LairHealPerSecond(world, database, state));

            if (regen != 0)
            {
                // Regeneration passes the creature's own +0x36 back (0x00488C32): the source is kept. The
                // lair heal's own call site is unread and kept the same way.
                int landed = CreatureHealthRules.Apply(database, creature, state, regen, state.HealthSourcePlayerId);
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
            // A Vampire's run-out health first tries the reincarnation (0x0048FFC0, Q329).
            if (CreatureDeathRules.CanBeResurrected(creature) && CreatureDeathRules.TryReincarnate(context, creature, state))
            {
                context.Emit(SystemName, "creature_reincarnated",
                    $"creature={state.Id} type={creature.Name} player={state.OwnerPlayerId} " +
                    $"level={state.Level} at=({state.Tile.X},{state.Tile.Y})");
                continue;
            }

            if (CreatureDeathRules.HasDyingState(world, database, creature, state))
                Collapse(context, creature, state);
            else
                Die(context, SystemName, creature, state);
        }
    }

    /// <summary>Tired and not asleep: the needs routine's health loss (NeedRules, ADR-0199).</summary>
    private static void CannotSleep(
        SimulationContext context, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        int delta = NeedRules.CannotSleepHealthPerSecond(database, creature, state);
        if (delta == 0) return;
        int landed = CreatureHealthRules.Apply(database, creature, state, delta, state.OwnerPlayerId); // 0x00477E7B: own owner
        if (landed == 0) return;
        context.Emit(SystemName, "creature_cannot_sleep_health",
            $"creature={state.Id} type={creature.Name} player={state.OwnerPlayerId} delta={landed} " +
            $"health={state.Health}/{CreatureHealthRules.MaxHealth(database, creature, state)}");
    }

    private static void Hunger(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (!CreatureHealthRules.CanGetHungry(creature)) return;

        state.HungerTicks++;

        // Group 7 and clock flags defer the update, not elapsed time (ADR-0579).
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
            CreatureStateMachine.TrySetState(context.Hooks, state, CreatureDeathRules.DyingState);

        // 217's handler clears CombatPitFighter: a knocked-out loser is no longer a fighter.
        if (state.Dk2StateId == CreatureDeathRules.DyingState) state.CombatPitFighter = false;

        // The window stands still while the body is carried: 0x004599D0 skips the countdown
        // while BeingDragged (+0x3C7) is set (0x00459BE8), Q164.
        if (state.CarriedByCreatureId >= 0) return;

        state.DyingTicks += SimulationTiming.TicksPerSecond;

        if (!CreatureDeathRules.DyingWindowElapsed(context.Database, state)) return;

        Die(context, SystemName, creature, state);
    }

    /// <summary>
    /// Counts the corpse window down, and removes what it runs out on — the
    /// owner's <i>"if that timer runs out and no one carries the corpse it just
    /// disappears"</i> (Q99).
    ///
    /// <para><b>Not in an imp's arms.</b> The body's lifetime (<c>+0x40</c>) counts down in
    /// <c>0x004B12D0</c> only while its carried flag <c>+0x138</c> is clear; take-hold
    /// <c>0x0048B4A0</c> sets it and the drop <c>0x0048B500</c> clears it (Q164).</para>
    /// </summary>
    private static void Rotting(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (state.CarriedByCreatureId >= 0) return;

        state.DyingTicks += SimulationTiming.TicksPerSecond;

        if (!CreatureDeathRules.BodyWindowElapsed(context.Database, state)) return;

        // One lying in a Graveyard is buried as it goes (0x004B1070, Q578).
        if (GraveyardRules.BuryLying(context, state) is { } result)
        {
            string raised = result.SpawnedCreatureId < 0
                ? "raised=none"
                : $"raised={context.Database.CreatureById(result.SpawnedCreatureTypeId).Name} as={result.SpawnedCreatureId}";
            context.Emit(SystemName, "corpse_buried",
                $"player={state.OwnerPlayerId} body={state.Id} type={creature.Name} " +
                $"at=({state.Tile.X},{state.Tile.Y}) decompose_value={creature.DecomposeValue} " +
                $"tally={result.DecomposeValue} {raised}");
            Remove(context, SystemName, state);
            return;
        }

        context.Emit(SystemName, "corpse_rotted",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"decompose_value={creature.DecomposeValue} " +
            $"carried_by={state.CarriedByCreatureId}");

        Remove(context, SystemName, state);
    }

    /// <summary>
    /// Takes a creature out of the world, and out of whatever arms were holding
    /// it. One place, so that no removal path can leave a carrier pointing at a
    /// creature that no longer exists. <paramref name="system"/> names the caller
    /// in the events: <see cref="ManaRemovalSystem"/> ends an imp through here too.
    /// </summary>
    internal static void Remove(SimulationContext context, string system, CreatureState state)
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
            context.Emit(system, "object_dropped",
                $"imp={state.Id} object={state.CarriedObjectId} " +
                $"at=({state.Tile.X},{state.Tile.Y}) reason=carrier_gone");
        }

        context.World.RemoveCreature(state.Id);
    }

    /// <summary>
    /// Drops a creature to the floor. It is still alive, still its keeper's, and
    /// still holds its bed, but its gold falls out: the state-216 entry 0x004596D0
    /// calls 0x0048F930, so a rescued creature comes back with empty hands.
    /// </summary>
    private static void Collapse(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (!CreatureDeathRules.Collapse(context.Hooks, state)) return;
        // Only when 216 took: 0x004596D0 returns before the drop if SetState refuses it.
        int carried = state.GoldHeld;
        int dropped = state.Dk2StateId == CreatureDeathRules.FallingState
            ? FloorGold.DropFrom(context.World, context.Database, state, context.Rng)
            : 0;
        HeroPartyRules.KnockedOut(context.Hooks, state);

        // Creature event 16, "dying", from the countdown start 0x0048B770 (trigger-walkers.md,
        // "Who fires what"). Level 2 counts four of its heroes going down this way.
        context.FireCreatureEvent(state, CreatureTriggerEvent.Dying);

        context.Emit(SystemName, "creature_collapsed",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"rescue_window={CreatureDeathRules.DyingWindowTicks(context.Database)
                / SimulationTiming.TicksPerSecond}s " +
            $"bed={LairRules.BedTileIndex(context.World, context.Database, state)} " +
            $"gold_dropped={dropped} gold_lost_to_rounding={carried - dropped}");
    }

    /// <summary>DK2's death bookkeeping (<c>0x00487C70</c>): the Killed event, the bed, the
    /// gold, then a corpse or removal. Reached from the dying window here and directly from
    /// <see cref="ManaRemovalSystem"/>, whose zero-health write skips the window.</summary>
    internal static void Die(
        SimulationContext context, string system, CreatureDef creature, CreatureState state)
    {
        // A hypnotised or following creature's bookkeeping (0x00487DD2, 0x00487E6A) before the possession exit.
        PossessionRules.CreatureFreed(context.World, state);
        if (state.Possessed && context.World.HasPlayer(state.OwnerPlayerId))
            PossessionRules.Leave(context, ref context.World.Player(state.OwnerPlayerId));
        context.FireCreatureEvent(state, CreatureTriggerEvent.Killed);

        // A dead creature gives up its bed, or a lair leaks a tile forever.
        // Not on collapse — the rescue path carries it back to that bed. A
        // creature that goes on to be a corpse releases it here too, so a body
        // lying on the floor is not still holding a lair tile.
        LairRules.ReleaseBed(context.World, context.Database, state);

        // The body is delivered by an imp task (ADR-0063). A corpse is not an
        // object (objects.md section 4); DK2 makes it a CDeadBody thing, ours
        // keeps it in the creature list with no state id (ADR-0206). The runtime
        // copy of the flag counts here, as in 0x00487C70 (ADR-0277).
        bool leavesBody = CreatureDeathRules.LeavesBody(creature, state);

        // What it was carrying falls where it fell -- in one, two or three
        // piles, and the original's integer division destroys the remainder
        // (FloorGold.DropFrom). Reported as dropped-and-lost rather than as one
        // number, because the two are different amounts and the difference is
        // DK2's own quirk.
        int carried = state.GoldHeld;
        int dropped = FloorGold.DropFrom(context.World, context.Database, state, context.Rng);
        PortalRules.ArrivalDied(context.World, state);
        CreditHeroKill(context.World, creature, state);

        context.Emit(system, "creature_died",
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
            context.World.RelinkAsBody(state);
            return;
        }

        Remove(context, system, state);
    }

    /// <summary>The hero count of the death bookkeeping (<c>0x0048822F</c>–<c>0x004882DC</c>,
    /// CONFIRMED): at health 0 or below, the credited player is the victim's health source
    /// (<c>+0x36</c>, <see cref="CreatureState.HealthSourcePlayerId"/>), or when that is 0 the
    /// owner of the first engaged attacker, slot by slot from 0 to 7. The mixed-slot adapter
    /// boundary is recorded in <c>docs/research/functions/00487C70.md</c>. With a credited player, a
    /// <see cref="CreatureState.DeathUncounted"/> victim counts nothing; otherwise an
    /// <c>IsGood</c> victim (definition <c>+0x686</c> bit 8) adds one to that player's
    /// <see cref="PlayerState.HeroesDestroyed"/>, with no owner test. Returns the credited player,
    /// 0 for none. The rest of the tally is <see cref="LevelStatisticsRules.CreditDeath"/>; not
    /// built: the MPD score (game mode 4) and the per-owner kill tally (<c>+0x19E</c>).</summary>
    public static byte CreditHeroKill(WorldState world, CreatureDef creature, CreatureState state)
    {
        if (state.Health > 0) return 0;
        byte credited = state.HealthSourcePlayerId;
        if (credited == 0)
        {
            for (int i = 0; i < Combat.CombatRules.Slots; i++)
            {
                int attacker = state.EngagedMelee[i] != 0 ? state.EngagedMelee[i] : state.EngagedRanged[i];
                if (attacker == 0) continue;
                // DK2 reads the slot's thing whatever it is; a creature gone from our list reads as no owner.
                credited = world.CreatureById(attacker)?.OwnerPlayerId ?? 0;
                break;
            }
        }
        LevelStatisticsRules.CreditDeath(world, creature, state, credited);
        if (credited == 0 || state.DeathUncounted) return credited;
        if (creature.Flags.HasFlag(CreatureFlags.IsGood) && world.HasPlayer(credited))
            world.Player(credited).HeroesDestroyed++;
        return credited;
    }
}

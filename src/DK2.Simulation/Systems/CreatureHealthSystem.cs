// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Regeneration, hunger's clock, and death — once a game second.
///
/// <para>Runs on the same cadence and the same phase as <see cref="ManaSystem"/>:
/// the last tick of each game second, so a level does not begin by paying out a
/// second that has not elapsed. Per-second rather than per-tick because every
/// number it applies is written <i>per second</i> in the data, and splitting
/// them across the four ticks of a second would invent precision the files do
/// not have. That reason survived Q1 being settled: the rate is now known to be
/// DK2's own four a second, and these numbers are still per second.</para>
///
/// <para><b>Healing sources add.</b> A creature on its own land in a lair gets
/// both, because nothing in the data or in OpenKeeper suggests they are
/// exclusive — and for 44 of the 46 creatures the own-land term is zero anyway,
/// so the question only arises for an Imp or King Reginald asleep in a lair.
/// Recorded as the reading it is (Q100).</para>
///
/// <para><b>Zero health is not death</b> — CONFIRMED by the owner (Q99), and
/// this system used to have it wrong. A creature that runs out of health
/// <i>collapses</i> and lies there for <c>CreatureDyingStateDurationSeconds</c>
/// (60), rescuable by an imp that carries it back to its bed, and only then
/// dies. See <see cref="CreatureDeathRules"/> for the whole process and for
/// which half of it is still missing.</para>
///
/// <para><b>Not every creature gets that window.</b> One that leaves no body
/// has no lying state and dies on the spot — CONFIRMED by the owner for the Imp
/// (<i>"imps are dead right away, they don't have a lying state"</i>) and by
/// OpenKeeper for all eight bodyless creatures, which tests the very same
/// <c>GenerateDeadBody</c> flag before any unconscious handling.</para>
///
/// <para><b>A creature that is down is skipped entirely</b>, and that guard is
/// not cosmetic. Own-land regeneration would otherwise heal a collapsed
/// creature off the floor: <b>King Reginald</b> regenerates 400 health a second
/// on his own land and does leave a body, so he is the one who would be
/// unkillable at home. (The Imp has 67 and would have been, before the owner's
/// answer took its lying state away.) Recovery is the rescue, not
/// self-healing — so own-land regeneration is suppressed at zero health while
/// lair healing is not, because a bed is the one route back.</para>
///
/// <para>Registered <b>after</b> <see cref="PortalSystem"/> so a creature that
/// arrives this tick is not regenerated on the tick it arrives, and so its
/// hunger clock starts from a full second.</para>
/// </summary>
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
            // PrisonModifyCreatureHealthPerSecond and dies into a Skeleton
            // rather than into a collapse, so nothing here may touch its health
            // or its dying clock (ADR-0064).
            //
            // Its HUNGER is another matter, and this line used to stop that too.
            // DK2's needs routine suppresses the hunger clock on a bit of the
            // state record's flag byte - and the fifteen states that set it are
            // exactly drinking, gambling and dancing, so it is the Casino and
            // not the Prison. Every prison state has the bit clear, which is
            // what makes state 115's own hunger test reachable at all: a
            // prisoner gets hungry, and if nobody drops it a chicken it grows
            // angry about it (ADR-0074).
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

            // A dying creature does nothing but die. No hunger clock, and above
            // all no regeneration — see the type summary for the immortal imp.
            // A corpse counts its own clock down and does nothing else. It
            // has no health to regenerate, no hunger to feel, and nothing left
            // to collapse into.
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

            // A creature that leaves no body has no lying state at all — it is
            // gone on the spot (CONFIRMED: the owner for the Imp, OpenKeeper
            // for all eight). Everything else gets its sixty seconds.
            if (CreatureDeathRules.HasDyingState(creature))
                Collapse(context, creature, state);
            else
                Die(context, creature, state);
        }
    }

    private static void Hunger(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (!CreatureHealthRules.CanGetHungry(creature)) return;

        state.HungerTicks += SimulationTiming.TicksPerSecond;

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
        // A dead creature gives up its bed, or a lair leaks a tile forever.
        // Not on collapse — the rescue path carries it back to that bed. A
        // creature that goes on to be a corpse releases it here too, so a body
        // lying on the floor is not still holding a lair tile.
        LairRules.ReleaseBed(context.World, context.Database, state);

        // The body is delivered at last (ADR-0063). This used to report a debt
        // it could not pay, on the standing explanation that a corpse is an
        // object -- which was wrong: none of the 145 objects is a corpse, and a
        // corpse is a creature in a state (objects.md section 4). What was
        // missing was an imp task that carries one, and now there is one.
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
            CreatureDeathRules.BecomeCorpse(context.World, context.Database, state);
            return;
        }

        Remove(context, state);
    }
}

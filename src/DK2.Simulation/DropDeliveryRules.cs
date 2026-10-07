// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a chicken or a pile of gold did for the creature it was dropped onto.</summary>
/// <param name="Gold">A pile, rather than a chicken.</param>
/// <param name="Healed">Health a chicken gave.</param>
/// <param name="Pocketed">Gold that landed in the purse: half the pile, capped.</param>
/// <param name="WageCleared">Wage debt a pile wiped, all of it whatever the pile held.</param>
public readonly record struct DropDelivery(bool Gold, int Healed, int Pocketed, int WageCleared);

/// <summary>A chicken or gold dropped onto a creature goes to that creature: it is fed or paid on
/// the spot and the object is gone (hand-drop-earmark.md, ADR-0217). CONFIRMED by the owner that
/// the named creature gets it; the mechanism is the binary's. The creature never fetches it, and
/// no search anywhere honours the earmark.
/// <para>DK2 writes the creature's id into the object (<c>WhoGetsThisFromADrop</c>, <c>+0x142</c>)
/// and delivers from the object's landing hook once it settles. Ours has no fall, so the drop
/// delivers in the same tick and the field is not carried: it would always read zero.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hand-drop-earmark.md")]
public static class DropDeliveryRules
{
    /// <summary>The creature a dropped object is for, or null. <c>0x004BC710</c> takes the keeper's
    /// target if it is a live creature and, for a chicken, its level-scaled <c>HungerFill</c> or, for
    /// gold, its level-scaled <c>Pay</c> is non-zero. Nothing else: not ownership, hunger, debt,
    /// distance or state. Ours: a corpse or a creature in a hand is no target.</summary>
    public static CreatureState? RecipientOf(
        WorldState world, GameDatabase database, ObjectState thing, int targetCreatureId)
    {
        if (targetCreatureId <= 0) return null;
        CreatureState? creature = world.CreatureById(targetCreatureId);
        if (creature is null || creature.InHand || CreatureDeathRules.IsCorpse(creature)) return null;

        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (database.ObjectById(thing.ObjectTypeId) is { IsGold: true })
            return PaydayRules.Wage(database, definition, creature.Level) != 0 ? creature : null;
        if (thing.ObjectTypeId == HatcheryRules.ChickenObjectId)
            return EatingRules.HungerFill(database, definition, creature.Level) != 0 ? creature : null;
        return null;
    }

    /// <summary>The landing hook's delivery (<c>0x0049EF90</c>), then the object is removed.
    /// A chicken is the ordinary one-bird meal and the hunger grievance wiped. A pile puts half its
    /// gold in the purse, clears the whole wage owed, sets <see cref="CreatureState.SkipNextPayment"/>
    /// and wipes the pay grievance. Both then take <see cref="CreatureDef.DropDeliveryCalm"/> off
    /// the treatment bucket, read unsigned as DK2 does (zero on every shipped creature).</summary>
    public static DropDelivery Deliver(SimulationContext context, string system, ObjectState thing, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        bool gold = database.ObjectById(thing.ObjectTypeId) is { IsGold: true };
        DropDelivery result;

        if (gold)
        {
            int pocketed = PurseRules.Receive(database, definition, creature, thing.GoldHeld >> 1);
            // 0x0049F14E: a creature holding no place (+0x3B9) celebrates before its debt is cleared.
            if (!creature.HeldPlace.IsHeld) Celebrate(context, system, creature, definition);
            int cleared = creature.WageOwed;
            creature.WageOwed = 0;
            creature.SkipNextPayment = true;
            CreatureAngerRules.Forget(world, definition, creature, AngerBucket.Pay);
            result = new DropDelivery(Gold: true, Healed: 0, Pocketed: pocketed, WageCleared: cleared);
        }
        else
        {
            int healed = EatingRules.EatDropped(database, definition, creature);
            CreatureAngerRules.Forget(world, definition, creature, AngerBucket.Hunger);
            result = new DropDelivery(Gold: false, Healed: healed, Pocketed: 0, WageCleared: 0);
        }

        CreatureAngerRules.Apply(
            world, definition, creature, AngerBucket.Treatment, -(int)(ushort)definition.DropDeliveryCalm,
            AngerComplaint.Money);
        world.RemoveObject(thing.Id);
        return result;
    }

    /// <summary>State 211, the gold celebration (refused-states.md §8).</summary>
    public const int CelebratingState = 211;

    /// <summary>The action <c>0x00481610</c> starts, and the art slot its test reads (<c>0x00494860(8)</c>).</summary>
    private const int CelebrationAction = 35, CelebrationArtSlot = 8;

    /// <summary><c>0x00481610</c>: not while joining (193), <c>SetState(211)</c>; then with art slot 8
    /// present, a running action ended and action 35 for <c>random(3) + 2</c> plays (simulation
    /// stream) and the walk stopped. Without the art, back to the state it was in. DK2 also goes back
    /// when <c>0x00496A30</c> refuses the action; ours starts every action, so that arm is not modelled. 211's handler
    /// <c>0x00458680</c> holds while the action runs (<see cref="Celebrating"/>).</summary>
    public static bool Celebrate(SimulationContext context, string system, CreatureState creature, CreatureDef definition)
    {
        int previous = creature.Dk2StateId;
        if (previous == PortalRules.JoiningState) return false;
        long now = context.Tick.Value;
        if (!CreatureStateMachine.TrySetState(context.Hooks, creature, CelebratingState, now)) return false;
        if (AnimationTimingRules.ResolvesPresent(definition, CelebrationArtSlot))
        {
            int plays = context.Rng.NextInt(3) + 2;
            creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(definition, CelebrationArtSlot, plays);
            creature.StopMoving();
            context.Emit(system, "gold_celebrated", $"creature={creature.Id} action={CelebrationAction} plays={plays}");
            return true;
        }
        CreatureStateMachine.TrySetState(context.Hooks, creature, previous, now);
        return false;
    }

    /// <summary>211's handler <c>0x00458680</c>: whether the action still runs; ended, idle.</summary>
    public static bool Celebrating(SimulationContext context, CreatureState creature) =>
        context.Tick.Value < creature.ActionUntilTick;
}

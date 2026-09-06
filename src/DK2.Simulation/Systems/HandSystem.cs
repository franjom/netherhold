// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// The Hand of Evil: this tick's pick-ups, drops and slaps, and the two clocks
/// that holding and slapping start.
///
/// <para><b>Runs first, before anything a creature does of its own accord.</b>
/// A creature the keeper picked up this tick must not also take a step this
/// tick, and one dropped onto marked work should be able to start on it at
/// once rather than after a tick of standing still. DK2's own order is UNKNOWN
/// (the standing caveat on <see cref="ISimulationSystem"/>); this is the order
/// that makes the keeper's hand feel like a hand, and it is declared rather
/// than inferred.</para>
///
/// <para>Evidence, addresses and the open questions: docs/research/hand-of-evil.md.
/// Decisions: ADR-0062.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hand-of-evil.md")]
public sealed class HandSystem : ISimulationSystem
{
    private const string SystemName = "Hand";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        // The clocks first, then the keeper. A slap sets WorkFasterTimer to its
        // full length and must not be shortened by the same tick's decrement -
        // the original decrements in its creature update and slaps from the
        // input path, so its two never meet on one tick either.
        Tend(context);

        foreach (PlayerCommand command in context.Input.All)
        {
            switch (command)
            {
                case PickUpCreatureCommand pick: PickUp(context, pick); break;
                case PickUpObjectCommand lift: PickUpObject(context, lift); break;
                case DropCommand drop: Drop(context, drop); break;
                case SlapCreatureCommand slap: Slap(context, slap); break;
            }
        }
    }

    // ---------------------------------------------------------------- pick up

    private static void PickUp(SimulationContext context, PickUpCreatureCommand command)
    {
        WorldState world = context.World;
        CreatureState? creature = world.CreatureById(command.CreatureId);
        if (creature is null) return;

        if (!HandRules.CanPickUp(world, context.Database, creature, command.PlayerId))
        {
            context.Emit(SystemName, "pickup_refused",
                $"player={command.PlayerId} creature={command.CreatureId} " +
                $"held={world.Player(command.PlayerId).Hand.Count}");
            return;
        }

        // The job goes before the creature does. An imp lifted off a seam has
        // stopped mining it, and leaving the target behind would have it walk
        // back to a tile it is no longer standing near the moment it lands.
        //
        // And so does its load. A worker lifted while carrying a book or a body
        // leaves both on the floor where it was standing: an imp in the keeper's
        // hand is out of the dungeon, and a body that followed it up there would
        // be somewhere no rule could reach (ADR-0065).
        DropLoad(context, creature);
        creature.ClearJob();

        creature.InHand = true;
        creature.PickupTick = context.Tick.Value;
        world.Player(command.PlayerId).Hand.Add(HandItem.Of(creature));

        context.Emit(SystemName, "picked_up",
            $"player={command.PlayerId} creature={creature.Id} " +
            $"held={world.Player(command.PlayerId).Hand.Count}");
    }

    /// <summary>
    /// Lifts a loose object into the hand — a chicken out of a hatchery, a pile
    /// of gold off the floor, a special.
    ///
    /// <para>CONFIRMED by the owner (2026-09-06). Until ADR-0075 the hand held
    /// creature ids only, which left the prison eating of ADR-0074 complete and
    /// unreachable in play.</para>
    /// </summary>
    private static void PickUpObject(SimulationContext context, PickUpObjectCommand command)
    {
        WorldState world = context.World;
        ObjectState? thing = world.ObjectById(command.ObjectId);
        if (thing is null) return;

        if (!HandRules.CanPickUpObject(world, context.Database, thing, command.PlayerId))
        {
            context.Emit(SystemName, "object_pickup_refused",
                $"player={command.PlayerId} object={command.ObjectId} " +
                $"held={world.Player(command.PlayerId).Hand.Count}");
            return;
        }

        thing.InHand = true;
        world.Player(command.PlayerId).Hand.Add(HandItem.Of(thing));

        context.Emit(SystemName, "object_picked_up",
            $"player={command.PlayerId} object={thing.Id} " +
            $"type={thing.ObjectTypeId} " +
            $"from=({thing.TileIndex % world.Width},{thing.TileIndex / world.Width}) " +
            $"held={world.Player(command.PlayerId).Hand.Count}");
    }

    /// <summary>
    /// Makes a worker let go of the object and the body it was carrying, where
    /// it stands.
    /// </summary>
    private static void DropLoad(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;

        if (ObjectCarryRules.DropWhatever(world, creature))
        {
            context.Emit(SystemName, "object_dropped",
                $"creature={creature.Id} at=({creature.Tile.X},{creature.Tile.Y}) " +
                "reason=carrier_picked_up");
        }

        if (creature.CarriedCreatureId < 0) return;

        CreatureState? body = world.CreatureById(creature.CarriedCreatureId);
        if (body is null)
        {
            creature.CarriedCreatureId = -1;
            return;
        }

        CarryRules.PutDown(creature, body);
        context.Emit(SystemName, "body_dropped",
            $"creature={creature.Id} body={body.Id} " +
            $"at=({body.Tile.X},{body.Tile.Y}) reason=carrier_picked_up");
    }

    // ------------------------------------------------------------------- drop

    private static void Drop(SimulationContext context, DropCommand command)
    {
        WorldState world = context.World;
        List<HandItem> hand = world.Player(command.PlayerId).Hand;
        if (hand.Count == 0) return;

        if (!world.Contains(command.Tile))
        {
            context.Emit(SystemName, "drop_refused",
                $"player={command.PlayerId} tile={command.Tile.X},{command.Tile.Y} off_map");
            return;
        }

        // Last in, first out - CONFIRMED by the owner, and the original reads
        // back the slot it is about to free. A keeper holding three things and
        // dropping twice puts down the third and then the second.
        HandItem held = hand[^1];
        if (held.IsObject)
        {
            DropObject(context, command, held);
            return;
        }

        int creatureId = held.Id;
        CreatureState? creature = world.CreatureById(creatureId);
        if (creature is null)
        {
            // The thing in the hand stopped existing. Nothing in this project
            // can do that yet; the hand is emptied rather than wedged.
            hand.RemoveAt(hand.Count - 1);
            return;
        }

        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        var grid = new WorldPathGrid(context.Database, world, definition);
        if (!grid.IsWalkable(command.Tile.X, command.Tile.Y))
        {
            context.Emit(SystemName, "drop_refused",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} not_walkable");
            return;
        }

        hand.RemoveAt(hand.Count - 1);
        creature.InHand = false;
        creature.PickupTick = context.Tick.Value;
        creature.PosX = CreatureState.CentreOf(command.Tile.X);
        creature.PosY = CreatureState.CentreOf(command.Tile.Y);
        creature.MoveRemainder = 0;
        creature.ClearJob();

        // A creature lifted out of a cell is still that keeper's prisoner while
        // it is in the hand, and where it lands decides whether it stays one:
        // back onto a tile of one of the keeper's prisons and it is imprisoned
        // again, anywhere else and it is loose in the dungeon (ADR-0064). The
        // third destination the owner named - a torture rack - needs the
        // Torture Chamber, which is not built.
        if (PrisonRules.IsImprisoned(creature))
        {
            if (DropIntoACell(context, command, creature)) return;

            context.Emit(SystemName, "prisoner_freed",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"owner={creature.OwnerPlayerId} " +
                $"tile={command.Tile.X},{command.Tile.Y} reason=dropped_outside " +
                $"held={hand.Count}");

            PrisonRules.Release(creature);
        }

        // The keeper's 12.5% starts here but is not decided here. Dropping a
        // worker sets a one-shot ticket and nothing else: the next evaluation
        // spends it by looking around where the worker landed, and the bonus
        // turns on only if that look finds something (ADR-0069). Deciding it
        // here - "is there marked work beside this tile?" - is what this system
        // used to do, and it was wrong twice over: the box is bigger than the
        // four neighbours, and marked tile work is not the only thing a dropped
        // worker will take.
        //
        // The timer goes to zero with it, because DK2's chooser skips its own
        // idle throttle while the ticket is unspent: a dropped worker evaluates
        // on the spot rather than on the next scheduled think.
        bool worker = definition.Flags.HasFlag(CreatureFlags.IsWorker);
        creature.JustDropped = worker;
        creature.DroppedTask = ImpTaskKind.None;
        if (worker) creature.TimerTicks = 0;

        context.Emit(SystemName, "dropped",
            $"player={command.PlayerId} creature={creature.Id} " +
            $"tile={command.Tile.X},{command.Tile.Y} " +
            $"worker={(worker ? "yes" : "no")} held={hand.Count}");
    }

    /// <summary>
    /// Puts a prisoner back in a cell if it was dropped on a tile of one of the
    /// dropper's own prisons, and says whether it took.
    ///
    /// <para><b>The tile need not be free.</b> A keeper who drops a prisoner
    /// into a prison that is already full has overcrowded it on purpose, which
    /// is exactly the manual's <i>"manual overcrowding"</i> and is what
    /// <see cref="PrisonSystem"/> rolls a jail break on. An imp will not do
    /// this; only a keeper can.</para>
    /// </summary>
    private static bool DropIntoACell(
        SimulationContext context, DropCommand command, CreatureState creature)
    {
        WorldState world = context.World;
        int here = (command.Tile.Y * world.Width) + command.Tile.X;

        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (!PrisonRules.IsPrisonOf(room, command.PlayerId)) continue;
            if (!room.TileIndices.Contains(here)) continue;

            PrisonRules.Imprison(world, context.Database, creature, command.PlayerId, here);

            int held = PrisonRules.PrisonerCount(world, room);
            context.Emit(SystemName, "prisoner_returned",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"owner={creature.OwnerPlayerId} " +
                $"tile={command.Tile.X},{command.Tile.Y} " +
                $"held={held} capacity={PrisonRules.Capacity(room)}");
            return true;
        }

        return false;
    }


    /// <summary>
    /// Puts an object down on a tile the keeper owns.
    ///
    /// <para><b>Owned ground, CONFIRMED by the owner</b> (2026-09-06) — and
    /// <b>not</b> <c>CanBeDroppedOnAnyLand</c>, which is set on one record and
    /// read by nothing (<see cref="HandRules.CanDropObjectOn"/>).</para>
    ///
    /// <para><b>Two branches of the original's are read and not built</b>, both
    /// because they need a target this project's drop has no way to name:</para>
    ///
    /// <list type="bullet">
    /// <item><b>A chicken dropped into somebody else's Hatchery becomes
    /// theirs.</b> The handler tests object id 9 against a tile owned by another
    /// keeper, in a room of type 4, and reassigns the bird. Unreachable here
    /// while the drop is owned-tiles-only, and unreachable in the original for
    /// the same reason as far as anyone has watched.</item>
    /// <item><b>A chicken or a pile of gold dropped ONTO a creature is
    /// earmarked for it</b> — the handler writes that creature's id into a field
    /// on the object when the creature's level-scaled <c>HungerFill</c> (or
    /// <c>MaxGoldHeld</c>) is non-zero. That is a reservation, and it is the one
    /// case where a bird is <i>not</i> up for grabs. The prison search of
    /// ADR-0074 does not consult the field, so a race for an earmarked bird
    /// would still be a race; whether something else does is unread.</item>
    /// </list>
    /// </summary>
    private static void DropObject(
        SimulationContext context, DropCommand command, HandItem held)
    {
        WorldState world = context.World;
        List<HandItem> hand = world.Player(command.PlayerId).Hand;

        ObjectState? thing = world.ObjectById(held.Id);
        if (thing is null)
        {
            // The thing in the hand stopped existing. The slot is freed rather
            // than wedged, exactly as for a creature.
            hand.RemoveAt(hand.Count - 1);
            return;
        }

        if (!HandRules.CanDropObjectOn(world, command.Tile, command.PlayerId))
        {
            context.Emit(SystemName, "drop_refused",
                $"player={command.PlayerId} object={thing.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} not_owned");
            return;
        }

        hand.RemoveAt(hand.Count - 1);
        thing.InHand = false;
        thing.TileIndex = (command.Tile.Y * world.Width) + command.Tile.X;

        context.Emit(SystemName, "object_dropped",
            $"player={command.PlayerId} object={thing.Id} " +
            $"type={thing.ObjectTypeId} " +
            $"tile={command.Tile.X},{command.Tile.Y} held={hand.Count}");
    }

    // ------------------------------------------------------------------- slap

    private static void Slap(SimulationContext context, SlapCreatureCommand command)
    {
        WorldState world = context.World;
        CreatureState? creature = world.CreatureById(command.CreatureId);
        if (creature is null) return;

        if (!HandRules.CanSlap(context.Database, creature, command.PlayerId))
        {
            context.Emit(SystemName, "slap_refused",
                $"player={command.PlayerId} creature={command.CreatureId}");
            return;
        }

        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);

        creature.LastSlapTick = context.Tick.Value;

        // Rewritten, not added to: ten slaps are worth one, which the owner
        // established by trying (docs/research/tile-work.md).
        creature.WorkFasterTicks = EfficiencyRules.SlapBonusTicks;

        int hurt = CreatureHealthRules.Apply(
            context.Database, definition, creature, -definition.SlapDamage);
        int anger = CreatureAngerRules.Slap(definition, creature);

        context.Emit(SystemName, "slapped",
            $"player={command.PlayerId} creature={creature.Id} " +
            $"health={creature.Health} damage={-hurt} anger={creature.Anger} (+{anger}) " +
            $"efficiency={EfficiencyRules.Percent(creature, definition)}%");

        // A slap can be the last one. Collapsing is CreatureHealthSystem's to
        // notice, on this same tick, and nothing here short-circuits it.
    }

    // ------------------------------------------------------------------ clocks

    /// <summary>
    /// The two clocks the hand starts: a slap's raised Efficiency running out,
    /// and a creature held too long minding it.
    /// </summary>
    private static void Tend(SimulationContext context)
    {
        WorldState world = context.World;

        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.WorkFasterTicks > 0) creature.WorkFasterTicks--;

            if (!HandRules.AngerDueThisTick(creature, context.Tick)) continue;

            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            if (!CreatureAngerRules.CanBeAngered(definition)) continue;

            CreatureAngerRules.Held(definition, creature);
        }
    }
}

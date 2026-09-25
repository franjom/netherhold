// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>The Hand of Evil: this tick's pick-ups, drops and slaps, plus the
/// two clocks that holding and slapping start.</summary>
/// <remarks>
/// <para>Runs before anything a creature does of its own accord, so a creature
/// picked up this tick takes no step and one dropped onto marked work can start
/// on it at once. DK2's own order is UNKNOWN (the standing caveat on
/// <see cref="ISimulationSystem"/>); this order is declared, not inferred.</para>
/// <para>Evidence, addresses and open questions: docs/research/hand-of-evil.md.
/// Decisions: ADR-0062.</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hand-of-evil.md")]
public sealed class HandSystem : ISimulationSystem
{
    private const string SystemName = "Hand";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        // The clocks first, then the keeper. A slap sets WorkFasterTicks to its
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

        // The job and the load both go before the creature does. An imp lifted
        // off a seam has stopped mining it; a worker lifted while carrying a
        // book or a body leaves both on the floor where it stood, because a
        // body carried up into the hand would be somewhere no rule could reach
        // (ADR-0065).
        DropLoad(context, creature);
        TortureRules.Unstrap(creature);
        CombatPitRules.Unmark(world, creature);   // 0x004BC500 clears the fighter and its forced target
        creature.ClearJob();
        // 76, the hand's own state, is not built: ours holds 1 in the hand (ADR-0209).
        CreatureStateMachine.TrySetState(creature, CreatureStateMachine.Idle);

        creature.InHand = true;
        creature.PickupTick = context.Tick.Value;
        world.Player(command.PlayerId).Hand.Add(HandItem.Of(creature));

        context.Emit(SystemName, "picked_up",
            $"player={command.PlayerId} creature={creature.Id} " +
            $"held={world.Player(command.PlayerId).Hand.Count}");
    }

    /// <summary>Lifts a loose object into the hand — a chicken out of a
    /// hatchery, a pile of gold off the floor, a special. CONFIRMED by the owner
    /// (2026-09-06); the hand holds objects as well as creatures per
    /// ADR-0075, which is what makes ADR-0074's prison eating reachable.</summary>
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

        // Disposal's tiles are the ones a keeper may drop onto though nothing may
        // stand there, so the walkability refusal is asked second: a portal's
        // centre for anything but a worker, a heart's platform for a worker
        // (DisposalRules, ADR-0148). A prisoner is refused both - DK2 does not
        // take a captive through a portal (CONFIRMED, owner 2026-09-13).
        bool disposed = !PrisonRules.IsImprisoned(creature)
            && DisposalRules.Disposes(
                world, context.Database, definition, command.Tile, command.PlayerId);

        var grid = new WorldPathGrid(context.Database, world, definition);
        if (!disposed && !grid.IsWalkable(command.Tile.X, command.Tile.Y))
        {
            context.Emit(SystemName, "drop_refused",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} not_walkable");
            return;
        }

        // Standing somewhere is not the same as being allowed to be put there.
        // Own ground for everybody, plus unclaimed floor for a worker - Q198,
        // CONFIRMED by the owner, and this test is the whole of ADR-0077.
        if (!HandRules.CanDropCreatureOn(
                world, context.Database, command.Tile, command.PlayerId, definition))
        {
            context.Emit(SystemName, "drop_refused",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} not_owned");
            return;
        }

        // A captive IS an offering, and the Temple is ADR-0064's one exception:
        // a wizard prisoner dropped on a Temple is sacrificed, not freed (Q314,
        // CONFIRMED owner 2026-09-16). The intake is tested before the release.
        int landingTileIndex = (command.Tile.Y * world.Width) + command.Tile.X;
        TempleSacrificeRules.Reconcile(world, context.Database);
        RoomInstance? temple = RoomIndex.RoomAt(world, context.Database, landingTileIndex);
        if (TempleSacrificeRules.IsTemple(temple, command.PlayerId))
        {
            hand.RemoveAt(hand.Count - 1);
            creature.InHand = false;
            SacrificeResult result = TempleSacrificeRules.OfferCreature(
                world, context.Database, temple!, creature, landingTileIndex);
            EmitSacrifice(context, command.PlayerId, "creature", creature.CreatureTypeId,
                command.Tile, hand.Count, result);
            return;
        }

        hand.RemoveAt(hand.Count - 1);
        creature.InHand = false;
        creature.PickupTick = context.Tick.Value;

        // 76 records whether it landed on a room; the needs leave it be for five seconds (ADR-0202).
        creature.DroppedOnARoom = RoomIndex.RoomAt(world, context.Database, landingTileIndex) is not null;
        creature.PosX = CreatureState.CentreOf(command.Tile.X);
        creature.PosY = CreatureState.CentreOf(command.Tile.Y);
        creature.Body.Vel = Steering.Fixed16Vector.Zero;
        creature.ClearJob();

        // Thrown away: the same removal by the same event kind that a deserter
        // reaching a portal gets, at whichever of the two tiles took it. The gold
        // goes with it rather than spilling - "a creature leaving (BEING THROWN)
        // through portal does not drop its gold" (Q137, owner), and an imp's gold
        // vanishes with it at the heart too (owner, 2026-09-15). No captive here.
        if (disposed)
        {
            context.Emit(SystemName, "creature_left",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} " +
                $"at=({command.Tile.X},{command.Tile.Y}) " +
                $"gold_lost={creature.GoldHeld} via=thrown_in " +
                $"by={command.PlayerId} held={hand.Count}");

            world.RemoveCreature(creature.Id);
            return;
        }

        // A creature lifted out of a cell stays that keeper's prisoner while in
        // the hand, and where it lands decides whether it stays one: back onto
        // a tile of one of the keeper's prisons and it is imprisoned again,
        // anywhere else and it is loose (ADR-0064). The third destination the owner
        // named is a torture device in the keeper's own Torture Chamber (ADR-0193).
        if (PrisonRules.IsImprisoned(creature))
        {
            if (TortureRules.TryStrap(world, context.Database, creature, command.PlayerId, landingTileIndex))
            {
                context.Emit(SystemName, "captive_strapped",
                    $"player={command.PlayerId} creature={creature.Id} " +
                    $"owner={creature.OwnerPlayerId} tile={command.Tile.X},{command.Tile.Y} " +
                    $"held={hand.Count}");
                return;
            }

            if (DropIntoACell(context, command, creature)) return;

            context.Emit(SystemName, "prisoner_freed",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"owner={creature.OwnerPlayerId} " +
                $"tile={command.Tile.X},{command.Tile.Y} reason=dropped_outside " +
                $"held={hand.Count}");

            PrisonRules.Release(creature);
        }
        else if (creature.OwnerPlayerId == command.PlayerId
                 && TortureRules.TryStrapOwn(world, context.Database, creature, landingTileIndex))
        {
            // 77 in a Torture Chamber is 130 for any creature but a Horned Reaper (ADR-0200).
            context.Emit(SystemName, "own_strapped",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} held={hand.Count}");
            return;
        }

        // An own non-worker on the floor of the keeper's Combat Pit: the room record's state,
        // 137, through 1 (0x004E8DA0(0), combat-pit.md §1).
        RoomInstance? pit = CombatPitRules.PitAt(world, context.Database, landingTileIndex);
        if (creature.OwnerPlayerId == command.PlayerId && pit?.OwnerPlayerId == command.PlayerId
            && !definition.Flags.HasFlag(CreatureFlags.IsWorker) && CombatPitRules.IsFloor(pit, landingTileIndex)
            && CombatPitSystem.Move(context, creature, CombatPitRules.ArrivingState))
        {
            context.Emit(SystemName, "dropped_in_pit",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} held={hand.Count}");
            return;
        }

        // Dropping a worker sets a one-shot ticket and nothing else; the next
        // evaluation spends it by looking around where the worker landed, and
        // the keeper's 12.5% bonus turns on only if that look finds something
        // (ADR-0069). The timer goes to zero with it because DK2's chooser skips
        // its own idle throttle while the ticket is unspent: a dropped worker
        // evaluates on the spot, not on the next scheduled think.
        bool worker = definition.Flags.HasFlag(CreatureFlags.IsWorker);
        creature.JustDropped = worker;
        creature.DroppedTask = ImpTaskKind.None;
        if (worker) creature.TimerTicks = 0;

        context.Emit(SystemName, "dropped",
            $"player={command.PlayerId} creature={creature.Id} " +
            $"tile={command.Tile.X},{command.Tile.Y} " +
            $"worker={(worker ? "yes" : "no")} held={hand.Count}");
    }

    /// <summary>Puts a prisoner back in a cell if it was dropped on a tile of
    /// one of the dropper's own prisons; returns whether it took.</summary>
    /// <remarks>The tile need not be free: dropping into a full prison is the
    /// manual's "manual overcrowding", which <see cref="PrisonSystem"/> rolls a
    /// jail break on. Only a keeper can do it; an imp will not.</remarks>
    private static bool DropIntoACell(
        SimulationContext context, DropCommand command, CreatureState creature)
    {
        WorldState world = context.World;
        int here = (command.Tile.Y * world.Width) + command.Tile.X;

        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (!PrisonRules.IsPrisonOf(room, command.PlayerId)) continue;
            if (!room.TileIndices.Contains(here)) continue;

            PrisonRules.Imprison(world, context.Database, creature, command.PlayerId, here, dropped: true);

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


    /// <summary>Puts an object down on a tile the keeper owns — CONFIRMED by the
    /// owner (2026-09-06), and not <c>CanBeDroppedOnAnyLand</c>, which is set on
    /// one record and read by nothing
    /// (<see cref="HandRules.CanDropObjectOn"/>).</summary>
    /// <remarks>Not implemented: two branches of the original's handler, both
    /// needing a drop target this project cannot name. A chicken dropped into
    /// another keeper's Hatchery becomes theirs (the handler tests object id 9
    /// against another keeper's tile in a room of type 4). A chicken or gold
    /// pile dropped onto a creature is earmarked for it, by that creature's id,
    /// when its level-scaled <c>HungerFill</c> or <c>MaxGoldHeld</c> is
    /// non-zero; ADR-0074's prison search does not consult that field, and
    /// whether anything else does is unread.</remarks>
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

        int landingTileIndex = (command.Tile.Y * world.Width) + command.Tile.X;
        TempleSacrificeRules.Reconcile(world, context.Database);
        RoomInstance? temple = RoomIndex.RoomAt(world, context.Database, landingTileIndex);
        if (TempleSacrificeRules.IsTemple(temple, command.PlayerId))
        {
            hand.RemoveAt(hand.Count - 1);
            thing.InHand = false;
            SacrificeResult result = TempleSacrificeRules.OfferObject(
                world, context.Database, temple!, thing, landingTileIndex);
            EmitSacrifice(context, command.PlayerId, "object", thing.ObjectTypeId,
                command.Tile, hand.Count, result);
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

    private static void EmitSacrifice(
        SimulationContext context, byte playerId, string offeredKind, byte offeredTypeId,
        TilePos tile, int held, SacrificeResult result)
    {
        string kind = result.SpeechId == TempleSacrificeRules.GoldSpeechId
            ? "sacrifice_gold"
            : result.Matched ? "sacrifice_reward" : "sacrifice_failed";
        context.Emit(SystemName, kind,
            $"player={playerId} offered={offeredKind}:{offeredTypeId} " +
            $"tile={tile.X},{tile.Y} matched={(result.Matched ? "yes" : "no")} " +
            $"speech={result.SpeechId} reward={result.RewardKind}:{result.SpawnedThingId} " +
            $"held={held}");
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
        int anger = CreatureAngerRules.Slap(world, definition, creature);

        context.Emit(SystemName, "slapped",
            $"player={command.PlayerId} creature={creature.Id} " +
            $"health={creature.Health} damage={-hurt} anger={creature.Anger.Max} (+{anger}) " +
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

            CreatureAngerRules.Held(world, definition, creature);
        }
    }
}

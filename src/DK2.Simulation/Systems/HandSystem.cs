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
                case PickUpStoreGoldCommand store: PickUpStoreGold(context, store); break;
                case DropCommand drop: Drop(context, drop); break;
                case SlapCreatureCommand slap: Slap(context, slap); break;
                case MintWageCommand mint: MintWage(context, mint); break;
                case WithdrawCounterGoldCommand withdraw: WithdrawCounterGold(context, withdraw); break;
            }
        }
    }

    private static void WithdrawCounterGold(SimulationContext context, WithdrawCounterGoldCommand command)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!world.HasPlayer(command.PlayerId) || command.Amount.Value <= 0) return;
        List<HandItem> hand = world.Player(command.PlayerId).Hand;
        if (hand.Count >= HandRules.Capacity(database)) return;
        if (!database.Objects.ContainsKey(FloorGold.TreasuryPileObjectId)) return;
        int taken = GoldStore.Spend(world, database, command.PlayerId, command.Amount.Value);
        if (taken <= 0) return;
        ObjectState pile = world.SpawnObject(database, FloorGold.TreasuryPileObjectId, PlayerIds.Neutral, 0);
        pile.GoldHeld = taken;
        pile.InHand = true;
        hand.Add(HandItem.Of(pile));
        context.Emit(SystemName, "counter_gold_withdrawn",
            $"player={command.PlayerId} requested={command.Amount.Value} taken={taken} object={pile.Id}");
    }

    /// <summary>The pay remedy's gold (<see cref="MintWageCommand"/>): the wage spent from the
    /// stores, a pile of object 1 for the neutral player holding what was taken (<c>0x004B7A90</c>),
    /// then the hand. Not modelled: the pile's <c>+0x146</c> bit 4, whose reader is unread.</summary>
    private static void MintWage(SimulationContext context, MintWageCommand command)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureState? creature = world.CreatureById(command.CreatureId);
        if (creature is null || creature.OwnerPlayerId != command.PlayerId
            || !RoomIndex.Build(world, database).Any(r => r.OwnerPlayerId == command.PlayerId
                && r.Definition.TypeId == RoomTypeId.Treasury))
        {
            context.Emit(SystemName, "wage_refused", $"player={command.PlayerId} creature={command.CreatureId}");
            return;
        }

        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        int wage = PaydayRules.Wage(database, definition, creature.Level);
        int taken = GoldStore.Spend(world, database, command.PlayerId, wage);
        if (FloorGold.Drop(world, database, 0, taken) is not ObjectState pile)
        {
            context.Emit(SystemName, "wage_refused", $"player={command.PlayerId} creature={creature.Id} reason=no_gold");
            return;
        }

        List<HandItem> hand = world.Player(command.PlayerId).Hand;
        bool held = hand.Count < HandRules.Capacity(database);
        if (held)
        {
            pile.InHand = true;
            hand.Add(HandItem.Of(pile));
        }
        context.Emit(SystemName, "wage_minted",
            $"player={command.PlayerId} creature={creature.Id} wage={wage} gold={taken} object={pile.Id} held={held}");
    }

    // ---------------------------------------------------------------- pick up

    private static void PickUp(SimulationContext context, PickUpCreatureCommand command)
    {
        WorldState world = context.World;
        CreatureState? creature = world.CreatureById(command.CreatureId);
        if (creature is null) return;

        if (!HandRules.HasPickupPermission(world, context.Database, creature, command.PlayerId))
        {
            context.Emit(SystemName, "pickup_refused",
                $"player={command.PlayerId} creature={command.CreatureId} " +
                $"held={world.Player(command.PlayerId).Hand.Count} reason=permission");
            return;
        }

        // 0x004BFBD0 runs before 0x004BC500 tests the hand: state 75, through idle when the
        // current category is outside its mask, so a full hand still takes a fighter out of
        // its fight and leaves it there (HandRules.PreparationState).
        if (!CreatureStateMachine.TrySetState(context.Hooks, creature, HandRules.PreparationState, context.Tick.Value))
        {
            CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
            CreatureStateMachine.TrySetState(context.Hooks, creature, HandRules.PreparationState, context.Tick.Value);
        }
        if (world.Player(command.PlayerId).Hand.Count >= HandRules.Capacity(context.Database))
        {
            context.Emit(SystemName, "pickup_refused",
                $"player={command.PlayerId} creature={command.CreatureId} " +
                $"held={world.Player(command.PlayerId).Hand.Count} reason=hand_full");
            return;
        }
        // Hand_PickUp (0x004BC5EF): another keeper's hand unmasks a disguised creature and lets go.
        if (creature.SneakingAs != 0 && creature.OwnerPlayerId != command.PlayerId.Value)
        {
            creature.SneakingAs = 0;
            context.Emit(SystemName, "pickup_refused",
                $"player={command.PlayerId} creature={command.CreatureId} " +
                $"held={world.Player(command.PlayerId).Hand.Count} reason=disguise");
            return;
        }

        // The job and the load both go before the creature does. An imp lifted
        // off a seam has stopped mining it; a worker lifted while carrying a
        // book or a body leaves both on the floor where it stood, because a
        // body carried up into the hand would be somewhere no rule could reach
        // (ADR-0065).
        DropLoad(context, creature);
        TortureRules.Unstrap(context.Hooks, creature);
        CombatPitRules.Unmark(world, creature);   // 0x004BC500 clears the fighter and its forced target
        creature.ClearJob();
        TrainingStep.CancelAction(creature);
        // 76, the hand's own state, is not built: ours holds 1 in the hand (ADR-0209).
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle);

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

        if (thing.ObjectTypeId == HatcheryRules.ChickenObjectId) ChickenMovementRules.UnlinkHatchery(thing);
        thing.InHand = true;
        world.Player(command.PlayerId).Hand.Add(HandItem.Of(thing));

        context.Emit(SystemName, "object_picked_up",
            $"player={command.PlayerId} object={thing.Id} " +
            $"type={thing.ObjectTypeId} " +
            $"from=({thing.TileIndex % world.Width},{thing.TileIndex / world.Width}) " +
            $"held={world.Player(command.PlayerId).Hand.Count}");
    }

    /// <summary>Lifts a store tile's pile whole (<c>0x004C0CB0</c>): all its gold leaves the room's
    /// tally and the keeper's total, and the tile is left with no pile. Ours makes the Gold Pile 1
    /// object here, from the tile's gold (ADR-0015).</summary>
    /// <remarks>Deviates: the pile is the neutral player's from the lift, as the counter's is
    /// (<see cref="WithdrawCounterGold"/>); DK2's keeps its keeper until its landing hands it to the
    /// neutral player (<c>0x0049EF90</c>). Not modelled: the floating amount (<c>0x0050FB70</c>), the
    /// lift sound (<c>0xC3</c>) and <c>+0x146</c> bit 4.</remarks>
    private static void PickUpStoreGold(SimulationContext context, PickUpStoreGoldCommand command)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!HandRules.CanPickUpStoreGold(world, database, command.Tile, command.PlayerId))
        {
            context.Emit(SystemName, "store_gold_pickup_refused",
                $"player={command.PlayerId} tile={command.Tile.X},{command.Tile.Y}");
            return;
        }

        int tileIndex = world.TileIndex(command.Tile);
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        int gold = tile.GoldHeld;
        tile.GoldHeld = 0;
        ObjectState pile = world.SpawnObject(database, FloorGold.TreasuryPileObjectId, PlayerIds.Neutral, tileIndex);
        pile.GoldHeld = gold;
        pile.InHand = true;
        List<HandItem> hand = world.Player(command.PlayerId).Hand;
        hand.Add(HandItem.Of(pile));
        context.Emit(SystemName, "store_gold_picked_up",
            $"player={command.PlayerId} tile={command.Tile.X},{command.Tile.Y} gold={gold} object={pile.Id} held={hand.Count}");
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

        if (command.Position is FpPosition2 position && position.Tile != command.Tile)
        {
            context.Emit(SystemName, "drop_refused", $"player={command.PlayerId} position_outside_tile");
            return;
        }

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

        // DK2's drop gate (0x0040D8F0, ADR-0526), which DK2 asks before a drop exists; ours here.
        if (!HandRules.CanDrop(world, context.Database, command.PlayerId, held, command.Tile))
        {
            context.Emit(SystemName, "drop_refused",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} not_allowed");
            return;
        }

        // Ours past the gate: a disposal tile (DisposalRules, ADR-0148; never a prisoner), a Heart
        // platform tile the gate let it onto (ADR-0526), or one the creature can stand on.
        if (!HandRules.CreatureLands(world, context.Database, command.PlayerId, creature, command.Tile, out bool disposed))
        {
            context.Emit(SystemName, "drop_refused",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} not_walkable");
            return;
        }

        // A captive IS an offering, and the Temple is ADR-0064's one exception:
        // a wizard prisoner dropped on a Temple is sacrificed, not freed (Q314,
        // CONFIRMED owner 2026-09-16). The intake is tested before the release.
        int landingTileIndex = (command.Tile.Y * world.Width) + command.Tile.X;
        TempleSacrificeRules.Reconcile(world, context.Database);
        RoomInstance? temple = RoomIndex.RoomAt(world, context.Database, landingTileIndex);
        // Only into the pool (temple-states.md §3, owner 2026-10-07); off it the creature lands and
        // the Temple's drop state takes it in to pray.
        if (TempleSacrificeRules.IsTemple(temple, command.PlayerId) && TempleStep.InThePool(world, temple!, landingTileIndex))
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
        creature.DroppedTick = context.Tick.Value;

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
            // A worker through its Heart's centre is 71's give-back (76 sets 71 on cell byte 0x40):
            // Create Imp's mana to the tile owner first, then its gold is lost and it is destroyed.
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker))
            {
                PlayerId owner = world.TileAtIndex(landingTileIndex).OwnerPlayerId;
                int refund = DropLandingRules.CreditWorkerGivenBack(world, context.Database, owner);
                context.Emit(SystemName, "worker_given_back",
                    $"creature={creature.Id} player={owner} mana={refund} gold_lost={creature.GoldHeld} " +
                    $"at=({command.Tile.X},{command.Tile.Y})");
            }

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
            if (TortureRules.TryStrap(context.Hooks, creature, command.PlayerId, landingTileIndex))
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

            PrisonRules.Release(context.Hooks, creature);
        }
        else if (creature.OwnerPlayerId == command.PlayerId
                 && TortureRules.TryStrapOwn(context.Hooks, creature, landingTileIndex))
        {
            // A Torture Chamber's drop state is 130 for any creature but a Horned Reaper (ADR-0200).
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

        // An own non-worker no room takes over lands in 76 and is stunned there (0x00491830(255),
        // Q201): it gets up in 257, which asks the room underfoot for its job, or stands in 250
        // (DropLandingRules, ADR-0226). A freed prisoner is not stunned (owner, Q201).
        if (creature.OwnerPlayerId == command.PlayerId && !definition.Flags.HasFlag(CreatureFlags.IsWorker)
            && Combat.StatusEffectRules.StunOnLanding(context, creature, definition))
        {
            context.Emit(SystemName, "dropped_stunned",
                $"player={command.PlayerId} creature={creature.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} ticks={creature.StunTicks} held={hand.Count}");
            return;
        }

        // A one-shot ticket: the next look spends it around the landing tile, and the
        // 12.5% bonus turns on only if that look finds something (ADR-0069). The timer
        // goes to zero because a dropped worker skips the chooser's cap. The kind just
        // done is zeroed, the aimed kind kept: state 11 writes +0x98 only (0x00463DD2).
        bool worker = definition.Flags.HasFlag(CreatureFlags.IsWorker);
        creature.JustDropped = worker;
        creature.LastTaskKind = ImpTaskKind.None;
        if (worker)
        {
            creature.TimerTicks = 0;
            creature.WorkerIdle = false;
        }

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

            PrisonRules.Imprison(context.Hooks, creature, command.PlayerId, here, dropped: true);

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


    /// <summary>Puts an object down where DK2's drop gate lets it (<see cref="HandRules.CanDrop"/>,
    /// ADR-0526): allied ground, or any open tile for Call To Arms.</summary>
    /// <remarks>A chicken or gold dropped onto a creature feeds or pays it at once
    /// (<see cref="DropDeliveryRules"/>, ADR-0217). Not implemented: the handler's
    /// branch giving a chicken to the keeper of the Hatchery it lands in, which the
    /// hand reaches only on an ally's Hatchery.</remarks>
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

        if (!HandRules.CanDrop(world, context.Database, command.PlayerId, held, command.Tile))
        {
            context.Emit(SystemName, "drop_refused",
                $"player={command.PlayerId} object={thing.Id} " +
                $"tile={command.Tile.X},{command.Tile.Y} not_allowed");
            return;
        }

        int landingTileIndex = (command.Tile.Y * world.Width) + command.Tile.X;
        TempleSacrificeRules.Reconcile(world, context.Database);
        RoomInstance? temple = RoomIndex.RoomAt(world, context.Database, landingTileIndex);
        // Object state 4 offers only on a tile the Temple surrounds (0x0049F632), the pool.
        if (TempleSacrificeRules.IsTemple(temple, command.PlayerId) && TempleStep.InThePool(world, temple!, landingTileIndex))
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
        if (FloorGoldLandingRules.Handles(thing)
            || thing.ObjectTypeId is HatcheryRules.ChickenObjectId or HatcheryRules.EggObjectId)
        {
            FpPosition2? point = FloorGoldLandingRules.Handles(thing) ? command.Position : null;
            thing.PosX = point?.X.Raw ?? CreatureState.CentreOf(command.Tile.X);
            thing.PosY = point?.Y.Raw ?? CreatureState.CentreOf(command.Tile.Y);
        }

        context.Emit(SystemName, "object_dropped",
            $"player={command.PlayerId} object={thing.Id} " +
            $"type={thing.ObjectTypeId} " +
            $"tile={command.Tile.X},{command.Tile.Y} held={hand.Count}");

        if (DropDeliveryRules.RecipientOf(world, context.Database, thing, command.TargetCreatureId)
            is not { } recipient)
        {
            if (FloorGoldLandingRules.Handles(thing)) FloorGoldLandingRules.Land(context, thing);
            if (thing.ObjectTypeId == HatcheryRules.ChickenObjectId) ChickenMovementRules.Land(context, thing);
            return;
        }

        DropDelivery given = DropDeliveryRules.Deliver(context, SystemName, thing, recipient);
        context.Emit(SystemName, "object_delivered",
            $"player={command.PlayerId} object={thing.Id} creature={recipient.Id} " +
            (given.Gold
                ? $"gold pocketed={given.Pocketed} wage_cleared={given.WageCleared}"
                : $"chicken healed={given.Healed} hunger={recipient.HungerNeed}"));
    }

    private static void EmitSacrifice(
        SimulationContext context, PlayerId playerId, string offeredKind, byte offeredTypeId,
        TilePos tile, int held, SacrificeResult result)
    {
        // 0x004F1780 kicks the creature it made to 106 (temple-states.md §6, ADR-0403).
        if (result.RewardKind == SacrificeRewardKind.Creature && context.World.CreatureById(result.SpawnedThingId) is { } summoned)
            TempleStep.Summon(context, SystemName, summoned);
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
        SlapOutcome outcome = OutcomeOf(world, context.Database, definition, creature, command.PlayerId.Value, out RoomInstance? room);
        // The main path's state move (0x0048BA00): refused, the slap does nothing more (ADR-0401).
        if (outcome == SlapOutcome.Full && !SlapRules.Move(context, creature, room))
            outcome = SlapOutcome.Nothing;
        if (outcome == SlapOutcome.Nothing)
        {
            context.Emit(SystemName, "slap_ignored",
                $"player={command.PlayerId} creature={creature.Id}");
            return;
        }

        int hurt, anger = 0;
        if (outcome == SlapOutcome.DamageOnly)
            // Every slap arm passes the slapping player's TagId (+0x04) as the source (0x0048B9A1, 0x0048BBBC, 0x0048BCFB).
            hurt = CreatureHealthRules.Apply(context.Database, definition, creature, -definition.SlapDamage, command.PlayerId.Value);
        else
        {
            creature.LastSlapTick = context.Tick.Value;
            if (outcome is SlapOutcome.Full or SlapOutcome.Uncounted)
            {
                // Rewritten, not added to: ten slaps are worth one, which the owner
                // established by trying (docs/research/tile-work.md).
                creature.WorkFasterTicks = EfficiencyRules.SlapBonusTicks;
                anger = CreatureAngerRules.Slap(world, definition, creature);
            }
            hurt = CreatureHealthRules.Apply(context.Database, definition, creature, -definition.SlapDamage, command.PlayerId.Value);
            if (outcome != SlapOutcome.Uncounted && world.HasPlayer(creature.OwnerPlayerId))
                world.Player(creature.OwnerPlayerId).SlapCount++;
        }

        context.Emit(SystemName, "slapped",
            $"player={command.PlayerId} creature={creature.Id} " +
            $"health={creature.Health} damage={-hurt} anger={creature.Anger.Max} (+{anger}) " +
            $"efficiency={EfficiencyRules.Percent(creature, definition)}%");

        // A slap can be the last one. Collapsing is CreatureHealthSystem's to
        // notice, on this same tick, and nothing here short-circuits it.
    }

    /// <summary>What <c>Hand_SlapCreature</c> (<c>0x0048B8A0</c>) does with a slap.</summary>
    private enum SlapOutcome : byte
    {
        /// <summary>Returns before the slap's tick, damage, anger, bonus and count.</summary>
        Nothing,
        /// <summary>The Torture Chamber arm: damage, then return.</summary>
        DamageOnly,
        /// <summary>States 209 and 255: the slap's tick, damage, events and count, no anger or bonus.</summary>
        Short,
        /// <summary>The main path, after the state move (67, 77 or 120) is taken.</summary>
        Full,
        /// <summary>A Horny: DK2 puts it in state 242 and returns, which ours does not build, so
        /// ours slaps it in full and does not count it.</summary>
        Uncounted,
    }

    /// <summary>The arms of <c>Hand_SlapCreature</c> (<c>0x0048B8A0</c>, Q278, ADR-0373). States 76,
    /// 101 and 193 return at once; 209 and 255 take the short path. Then the room the creature
    /// holds (<c>+0x3B9</c>, <see cref="CreatureState.HeldPlace"/>), not the one under it, decides:
    /// one the slapper does not own returns; a Torture Chamber (type 12) takes damage only. With no
    /// room, states 67, 120, 132 and 197 return. Only the main path and the short path add to the
    /// owner's <c>NumberOfSlaps</c> (<c>+0xEE6</c>), which condition type 71 reads. A Horny is
    /// tested before all of it (<see cref="SlapOutcome.Uncounted"/>). The main path then makes the
    /// state move (<see cref="SlapRules.Move"/>) and stops if it is refused. After a taken move the
    /// state-83 arm (<c>0x0048BA1A</c>) cannot match: the state is the move's.</summary>
    private static SlapOutcome OutcomeOf(
        WorldState world, GameDatabase database, CreatureDef definition, CreatureState creature, byte slapper, out RoomInstance? room)
    {
        room = null;
        if (definition.Flags.HasFlag(CreatureFlags.IsHorny)) return SlapOutcome.Uncounted;
        if (creature.Dk2StateId is 76 or 101 or 193) return SlapOutcome.Nothing;
        if (creature.Dk2StateId is 209 or 255) return SlapOutcome.Short;
        room = creature.HeldPlace.IsHeld
            ? RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex)
            : null;
        if (room is null)
            return creature.Dk2StateId is 67 or 120 or 132 or 197 ? SlapOutcome.Nothing : SlapOutcome.Full;
        if (room.OwnerPlayerId != slapper) return SlapOutcome.Nothing;
        if (room.Definition.TypeId == RoomTypeId.TortureChamber) return SlapOutcome.DamageOnly;
        return SlapOutcome.Full;
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

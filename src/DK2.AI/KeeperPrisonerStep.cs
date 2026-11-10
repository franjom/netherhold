// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The keeper's prisoner step (<c>0x005006D0</c>, step 14): one occupant of one random own
/// Prison is selected and, one allowance spent, moved by the hand: a non-enemy to a Lair, an enemy by
/// <c>ImprisonedCreatureAction</c> to the Torture Chamber (0), nowhere (1), the Combat Pit (2) or the
/// Temple (3).</summary>
/// <remarks>Built whole (ADR-0726, computer-keeper-management.md §1). Deviates as the training moves
/// do: the draws use the keeper's own stream (ADR-0244), and the lift and drop are next-tick
/// commands, the drop issued only when the lift is predicted (ADR-0284).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public sealed class KeeperPrisonerStep
{
    /// <summary>An enemy occupant is selected when <c>random(100)</c> is below this.</summary>
    public const int EnemySelectPercent = 5;

    private readonly KeeperView _view;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly HashSet<int> _prepared;
    private readonly long _turn;

    /// <param name="prepared">The creatures earlier steps of this tick prepared for the hand.</param>
    public KeeperPrisonerStep(KeeperView view, KeeperAIParameters parameters, SimRandom rng,
        List<PlayerCommand> commands, HashSet<int> prepared)
    {
        _view = view;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        _prepared = prepared;
        // DK2's first game turn is 1; the observation's tick is the turn about to run.
        _turn = view.Tick.Value + 1;
    }

    /// <summary>Returns the allowance left: one less once an occupant is selected, whatever follows.</summary>
    public int Run(int allowance)
    {
        // 0x004C6460(11), then the occupant list's head (room +0x0A); empty, nothing is spent.
        KeeperRoomView? prison = _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Prison, _rng);
        if (prison is null || Select(prison) is not KeeperCreatureView occupant) return allowance;
        allowance--;

        if (!IsEnemy(occupant))
        {
            KeeperRoomView? lair = _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Lair, _rng);
            if (lair is not null && Movable(occupant)) Move(occupant, _view.PositionOf(lair));
            return allowance;
        }
        RoomTypeId? destination = (_p.ImprisonedCreatureAction & 3) switch
        {
            0 => RoomTypeId.TortureChamber,
            2 => RoomTypeId.CombatPit,
            3 => RoomTypeId.Temple,
            _ => null,
        };
        if (destination is not RoomTypeId type) return allowance;
        KeeperRoomView? room = _view.RandomRoomOfType(_view.PlayerId, type, _rng);
        if (room is null || !Movable(occupant)) return allowance;
        if (type != RoomTypeId.TortureChamber)
        {
            Move(occupant, _view.PositionOf(room));
            return allowance;
        }
        // 0x004E8210(1, 1): a floor or wall slot, then no creature on a torture device on its tile.
        List<int> slots = room.FurnitureSlots().ToList();
        if (slots.Count == 0) return allowance;
        TilePos slot = _view.FromIndex(slots[_rng.NextInt(slots.Count)]);
        if (_view.Creatures.Any(c => c.OnTortureDevice && c.PosX >> 12 == slot.X && c.PosY >> 12 == slot.Y))
            return allowance;
        Move(occupant, slot);
        return allowance;
    }

    /// <summary>The list from its head, the newest claim first (<c>0x004E5A70</c> head-inserts): the
    /// first non-enemy is taken; an enemy only on the roll, and the walk stops at the first taken.</summary>
    private KeeperCreatureView? Select(KeeperRoomView prison)
    {
        var cells = new HashSet<int>(prison.TileIndices);
        foreach (KeeperCreatureView occupant in _view.Creatures
            .Where(c => c.PlaceTile >= 0 && cells.Contains(c.PlaceTile))
            .OrderByDescending(c => c.PlaceSerial))
        {
            if (!IsEnemy(occupant)) return occupant;
            if (_rng.NextInt(100) < EnemySelectPercent) return occupant;
        }
        return null;
    }

    /// <summary><c>0x004BF390</c> on the occupant's owner: not this keeper and not allied.</summary>
    private bool IsEnemy(KeeperCreatureView creature) => !_view.AreAllied(creature.OwnerPlayerId);

    /// <summary>The guards every move tests after its room draw, in order: the dropped-counter guard,
    /// not in this hand, not landing (76), not stunned, the pick-up permission.</summary>
    private bool Movable(KeeperCreatureView creature)
    {
        if (creature.LastDroppedCounter <= KeeperThreatResponder.DroppedGraceTurns
            && _turn > KeeperThreatResponder.DroppedGraceTurns) return false;
        if (creature.InMyHand) return false;
        int state = StateOf(creature);
        if (state == DropLandingRules.LandingState || KeeperThreatResponder.IsStunned(state)) return false;
        return creature.PickupPermitted;
    }

    /// <summary>A creature an earlier step prepared is, in DK2, landing (76) when the hand took and
    /// dropped it, or still in 75 when the hand refused it.</summary>
    private int StateOf(KeeperCreatureView creature) =>
        !_prepared.Contains(creature.Id) ? creature.StateId
            : creature.CanPickUp ? DropLandingRules.LandingState : KeeperTrainingStep.PreparedState;

    /// <summary><c>0x004BFBD0</c> prepares, <c>0x004BC500</c> lifts unless the hand refuses, and the
    /// drop follows only a lift.</summary>
    private void Move(KeeperCreatureView creature, TilePos at)
    {
        _commands.Add(new PickUpCreatureCommand(_view.PlayerId, creature.Id));
        _prepared.Add(creature.Id);
        if (creature.CanPickUp) _commands.Add(new DropCommand(_view.PlayerId, at));
    }
}

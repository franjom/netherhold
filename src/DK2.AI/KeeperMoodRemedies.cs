// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The mood arms of the keeper's message walk (<c>0x004FA400</c>): make happy
/// (<c>0x004F9710</c>) for 17, and dispose (<c>0x004F9520</c>) or make happy for 18 as the personality chooses
/// (computer-keeper-management.md §13). Each returns whether DK2 marks the message handled.</summary>
/// <remarks>Deviates: the hand's moves are commands applied next tick, so their outcome is
/// predicted from the captured permission and hand count, as the threat responder's are; and our
/// object drop needs the keeper's own tile where DK2's keeper drop tests none (Q541).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public sealed class KeeperMoodRemedies
{
    /// <summary>The job-room guard: a creature put down at most this many turns ago is left
    /// alone once the game turn is past the same number.</summary>
    public const int DroppedGraceTurns = KeeperThreatResponder.DroppedGraceTurns;

    /// <summary>A failed remedy is handled only while fewer event tasks than this are queued
    /// (<c>0x004F99B2</c>).</summary>
    public const int EventTaskCap = 3;

    private readonly KeeperView _view;
    private readonly KeeperAIState _state;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly uint _turn;

    public KeeperMoodRemedies(KeeperView view, KeeperAIState state, KeeperAIParameters parameters,
        SimRandom rng, List<PlayerCommand> commands)
    {
        _view = view;
        _state = state;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        _turn = unchecked((uint)(view.Tick.Value + 1));
    }

    /// <summary>Message 17: make happy when <c>UnhappyCreaturesHappy</c>, else handled.</summary>
    public bool Unhappy(PlayerMessage message) => !_p.UnhappyCreaturesHappy || MakeHappy(message);

    /// <summary>Message 18: dispose when <c>AngryCreaturesDisposeOf</c>, else make happy when
    /// <c>AngryCreaturesHappy</c>, else handled.</summary>
    public bool Angry(PlayerMessage message)
    {
        if (_p.AngryCreaturesDisposeOf) return Dispose(message.Data2);
        return !_p.AngryCreaturesHappy || MakeHappy(message);
    }

    /// <summary><c>0x004F9520</c>: with <c>AngryDisposalMethod</c> 1 a random Temple's floor slot;
    /// otherwise, or when that move fails, a random Temple, else Portal, else Hero Portal at its
    /// position. None, or a refused lift, leaves the message unhandled for a retry. DK2 reads the
    /// creature without a test; ours treats one no longer in the world as a refused lift.</summary>
    public bool Dispose(int creatureId)
    {
        if (Creature(creatureId) is not KeeperCreatureView creature) return false;
        if (_p.AngryDisposalMethod == 1
            && _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Temple, _rng) is KeeperRoomView temple
            && temple.FloorSlots.Count > 0)
        {
            TilePos slot = _view.FromIndex(temple.FloorSlots[_rng.NextInt(temple.FloorSlots.Count)]);
            if (Lift(creature)) { _commands.Add(new DropCommand(_view.PlayerId, slot)); return true; }
        }
        KeeperRoomView? room = _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Temple, _rng)
            ?? _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Portal, _rng)
            ?? _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.HeroPortal, _rng);
        return MoveTo(creature, room);
    }

    /// <summary><c>0x004F9710</c>: by the creature's current largest bucket, never the message's
    /// complaint. A missing or content creature is handled.</summary>
    public bool MakeHappy(PlayerMessage message)
    {
        if (message.Kind is not (PlayerMessageKind.CreatureUnhappy or PlayerMessageKind.CreatureAngry)) return true;
        if (Creature(message.Data2) is not KeeperCreatureView creature) return true;
        CreatureDef definition = _view.Database.CreatureById(creature.CreatureTypeId);
        if (CreatureAngerRules.MoodOf(definition, creature.Anger) == CreatureMood.Content) return true;

        bool done = creature.Anger.Worst switch
        {
            AngerBucket.Hunger => Feed(creature),
            AngerBucket.Lair => MoveTo(creature, _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Lair, _rng)),
            AngerBucket.Work => MoveToJobRoom(creature, definition),
            _ => Pay(creature),
        };
        return done || _state.Responses.Count < EventTaskCap;
    }

    /// <summary>Buckets 0, 4 and 5: a Treasury is drawn, the wage minted into the hand and dropped
    /// on the creature. Nothing to take, or a hand with no room, fails.</summary>
    private bool Pay(KeeperCreatureView creature)
    {
        if (_view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Treasury, _rng) is null) return false;
        if (_view.Gold <= 0) return false;
        _commands.Add(new MintWageCommand(_view.PlayerId, creature.Id));
        return DropOn(creature);
    }

    /// <summary>Bucket 1: the head of a random Hatchery's object list, lifted and dropped on it.</summary>
    private bool Feed(KeeperCreatureView creature)
    {
        if (_view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Hatchery, _rng) is not { ObjectListHead: >= 0 } hatchery)
            return false;
        _commands.Add(new PickUpObjectCommand(_view.PlayerId, hatchery.ObjectListHead));
        return DropOn(creature);
    }

    private bool DropOn(KeeperCreatureView creature)
    {
        if (_view.HandCount >= HandRules.Capacity(_view.Database)) return false;
        _commands.Add(new DropCommand(_view.PlayerId, CreatureTile(creature), creature.Id));
        return true;
    }

    /// <summary>Bucket 3: the happy table in order, each job below 10 through <see cref="JobRoom"/>;
    /// the first whose drawn room, dropped-counter guard and lift pass moves it there.</summary>
    private bool MoveToJobRoom(KeeperCreatureView creature, CreatureDef definition)
    {
        foreach (CreatureJobPreference job in definition.HappyJobs)
        {
            if ((int)job.Job >= 10 || JobRoom(job.Job) is not RoomTypeId type) continue;
            if (_view.RandomRoomOfType(_view.PlayerId, type, _rng) is not KeeperRoomView room) continue;
            if (creature.LastDroppedCounter <= DroppedGraceTurns && _turn > DroppedGraceTurns) continue;
            if (MoveTo(creature, room)) return true;
        }
        return false;
    }

    /// <summary>The lift and drop at the room's position (<c>+0x27</c>), the Lair's arm and each
    /// job room's: refused by <c>0x0048E6A0</c>, or not held after the lift, fails.</summary>
    private bool MoveTo(KeeperCreatureView creature, KeeperRoomView? room)
    {
        if (room is null || !Lift(creature)) return false;
        _commands.Add(new DropCommand(_view.PlayerId, _view.PositionOf(room)));
        return true;
    }

    /// <summary><c>0x0048E6A0</c>, then <c>0x004BFBD0</c> and the hand: whether the creature is
    /// predicted held. A refused permission issues nothing.</summary>
    private bool Lift(KeeperCreatureView creature)
    {
        if (!creature.PickupPermitted) return false;
        _commands.Add(new PickUpCreatureCommand(_view.PlayerId, creature.Id));
        return creature.CanPickUp;
    }

    /// <summary><c>JobRooms</c> at <c>0x006ED490</c>, its room byte only (KEEPER_AI.md §2.3).</summary>
    public static RoomTypeId? JobRoom(JobType job) => job switch
    {
        JobType.Research => RoomTypeId.Library,
        JobType.Train => RoomTypeId.TrainingRoom,
        JobType.Manufacture => RoomTypeId.Workshop,
        JobType.Torture => RoomTypeId.TortureChamber,
        _ => null,
    };

    private KeeperCreatureView? Creature(int id)
    {
        foreach (KeeperCreatureView c in _view.Creatures)
            if (c.Id == id) return c;
        return null;
    }

    private static TilePos CreatureTile(KeeperCreatureView creature) => new(creature.PosX >> 12, creature.PosY >> 12);
}

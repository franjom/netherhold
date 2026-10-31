// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public enum PlayerMessageKind : uint
{
    HeartAttacked = 1,
    CombatStarted = 6,

    /// <summary>Type 13: the first Portal arrival of a type without a level introduction override.</summary>
    CreatureArrived = 13,

    /// <summary>Type 14: first arrival with a level introduction; Data1 the creature, Data2 the speech id.</summary>
    CreatureIntroduction = 14,

    /// <summary>Type 21: retained trigger speech; Data1 the one-based level speech id.</summary>
    ScriptedSpeech = 21,

    /// <summary>Type 12: work brought down a Solid, Ownable tile of the recipient's that another
    /// player's worker was working (<c>0x0045313E</c>); Data1 and Data2 are the tile. Row
    /// <c>0x2032</c>: fifty seconds, tab, no duplicates. The name is ours.</summary>
    WallBreached = 12,

    /// <summary>Type 17: a creature's anger band rose to unhappy (<c>0x00478B80</c>); Data1 is the
    /// wrapper's <see cref="AngerComplaint"/>, Data2 the creature. Row <c>0x2032</c>: fifty
    /// seconds, tab, no duplicates, so one unexpired 17 per player stands for every creature.</summary>
    CreatureUnhappy = 17,

    /// <summary>Type 18: the band rose to angry; otherwise as <see cref="CreatureUnhappy"/>.</summary>
    CreatureAngry = 18,
    CreatureLeft = 20,

    /// <summary>Type 52: Data1 is the warning stage 1–4 (subtable <c>0x006A0610</c>).</summary>
    LowManaWarning = 52,

    /// <summary>Type 53: Data1 3 is a creature chosen for removal; 5 is possession ended for
    /// want of mana, posted by <c>0x004C10E0</c> (subtable <c>0x006A0638</c>).</summary>
    ManaShortfall = 53,

    /// <summary>Type 47: an Alarm trap's shot (process 12, <c>0x004AE120</c>); Data1 and Data2 are the
    /// shot's tile. Row <c>0x2032</c>: fifty seconds, tab, no duplicates.</summary>
    AlarmRaised = 47,

    /// <summary>Type 55: training the keeper cannot pay for. Both Training hooks pass Data1 5,
    /// a 240-second row of subtable <c>0x006A0690</c> (1–4 are 20 seconds and raised by no hook
    /// built here; 1 alone is under the <c>+0xF56</c> deadline, which is not built).</summary>
    TrainingUnpaid = 55,

    /// <summary>Type 32: a room taken from a non-neutral owner, to the new owner; Data1 the room type,
    /// Data2 the room (subtable <c>0x006A0060</c>, <see cref="PlayerMessageQueue.OwnershipLifetimeSeconds"/>).</summary>
    RoomCaptured = 32,

    /// <summary>Type 33: as 32 when the old owner was the neutral player (<c>0x006A00F8</c>).</summary>
    NeutralRoomCaptured = 33,

    /// <summary>Type 34: a room under attack, to its owner; Data1 the room type, Data2 the room
    /// (<c>0x006A0190</c>, 30 seconds).</summary>
    RoomAttacked = 34,

    /// <summary>Type 35: a room lost, to its old owner (<c>0x006A0220</c>); duplicates allowed.</summary>
    RoomLost = 35,

    /// <summary>Type 41: the keeper has no room of the type in Data1 and needs one (subtable
    /// <c>0x006A03D8</c>).</summary>
    RoomNeeded = 41,

    /// <summary>Type 42: every room of the type in Data1 is full (subtable <c>0x006A04B8</c>).</summary>
    RoomFull = 42,

    /// <summary>Type 43: a room of the type in Data1 has space and no route (subtable <c>0x006A0528</c>).</summary>
    RoomUnreachable = 43,

    /// <summary>Type 44: a reached, full room of the type in Data1 whose <c>+0xA3</c> is zero (no
    /// fitting furniture or no interior tile, by type), Data2 the room (subtable <c>0x006A08E0</c>).
    /// The name is ours; the message's text is unread.</summary>
    RoomUnequipped = 44,

    /// <summary>Type 45: a reached room of the type in Data1 with no storage position (a Library's
    /// shelves, <c>0x004E1AC0</c>) whose <c>+0xA3</c> is set, Data2 the room (subtable
    /// <c>0x006A08C8</c>: Library and Workshop, 120 seconds). The name is ours.</summary>
    RoomStorageFull = 45,

    /// <summary>Type 99: 41's twin with one string for every row (subtable <c>0x006A0448</c>, 41's
    /// lifetimes): a Mistress's missing Torture Chamber (<c>0x004F2B80</c>). The name is ours.</summary>
    RoomWanted = 99,

    /// <summary>Type 84: a creature could not route to the Call to Arms flag
    /// (<c>0x00455DA0</c>, <c>0x004778E0</c>); fifty seconds, tab, no duplicates.</summary>
    CallToArmsUnreachable = 84,

    /// <summary>Type 73: a Casino jackpot, Data1 the winner (<c>0x004D6AA0</c>); fifty seconds, tab, no
    /// duplicates. The names of 73 to 91 are ours; their texts are unread (casino.md §11).</summary>
    CasinoJackpot = 73,

    /// <summary>Type 75: more than two of a Casino's occupants are drunk (state 146).</summary>
    CasinoDrinking = 75,

    /// <summary>Type 77: a jackpot winner cannot collect: no route to a Treasury (155) or too little
    /// gold (157).</summary>
    JackpotUnpaid = 77,

    /// <summary>Type 89, a variant list (<c>0x006A09D8</c>), duplicates allowed: 146 posts variant 11,
    /// more than ten drunk occupants, which lasts 20 seconds.</summary>
    CasinoRowdy = 89,

    /// <summary>Type 91: a jackpot winner's deadline ran out (<c>0x004D6A40</c>).</summary>
    JackpotMissed = 91,
}

public readonly record struct PlayerMessage(
    uint Id, PlayerId PlayerId, PlayerMessageKind Kind, int Data1, int Data2,
    uint Age, uint Timeout, bool NeedsTab, bool Handled)
{
    /// <summary>The room type a room message (41 to 43) carries in Data1; null for any other kind.</summary>
    public RoomTypeId? Room => PlayerMessageQueue.IsRoomMessage(Kind) || PlayerMessageQueue.IsOwnershipMessage(Kind)
        ? (RoomTypeId)Data1 : null;

    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(Id); hash.Add(PlayerId.Value); hash.Add((uint)Kind);
        hash.Add(Data1); hash.Add(Data2); hash.Add(Age); hash.Add(Timeout);
        hash.Add(NeedsTab); hash.Add(Handled);
    }
}

/// <summary>The shared retained queue: the two combat position messages, the mood pair, the
/// room-ownership family, mana pressure, room needs, Alarm, scripted speech and first arrivals.
/// Any unsupported kind, or a subtype its table lacks, is refused.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-messages.md")]
public sealed class PlayerMessageQueue
{
    public const int Capacity = 256;
    private readonly List<PlayerMessage> _entries = new();
    private uint _nextId = 1;

    public PlayerMessageQueue() => Entries = _entries.AsReadOnly();
    public IReadOnlyList<PlayerMessage> Entries { get; }

    /// <summary>Kinds whose table flag bit 15 is set: the lifetime comes from a subtype row
    /// selected by Data1, and a duplicate must match Data1 as well as recipient and kind.</summary>
    public static bool HasSubtypes(PlayerMessageKind kind) =>
        kind is PlayerMessageKind.LowManaWarning or PlayerMessageKind.ManaShortfall
            or PlayerMessageKind.TrainingUnpaid or PlayerMessageKind.CasinoRowdy
        || IsRoomMessage(kind) || IsOwnershipMessage(kind);

    /// <summary>The room-ownership family 32 to 35, whose subtables share one shape.</summary>
    public static bool IsOwnershipMessage(PlayerMessageKind kind) =>
        kind is PlayerMessageKind.RoomCaptured or PlayerMessageKind.NeutralRoomCaptured
            or PlayerMessageKind.RoomAttacked or PlayerMessageKind.RoomLost;

    /// <summary>The lifetime of a 32 to 35 subtype row: room types 1 to 4, 6 to 17, 22 and 287 at 20
    /// seconds, 34's without 287 at 30. Null for a type off the table, which takes the terminator's
    /// nonzero string and timeout zero, so the record lasts until the next sweep.</summary>
    public static int? OwnershipLifetimeSeconds(PlayerMessageKind kind, int room)
    {
        bool listed = room is (>= 1 and <= 4) or (>= 6 and <= 17) or 22
            || (room == 287 && kind != PlayerMessageKind.RoomAttacked);
        return !listed ? null : kind == PlayerMessageKind.RoomAttacked ? 30 : 20;
    }

    /// <summary>A 32 to 35 message as the producers post it: the room type and the room.</summary>
    public PlayerMessage? PostOwnership(WorldState world, byte recipient, PlayerMessageKind kind, RoomTypeId room, int roomTag)
    {
        if (!IsOwnershipMessage(kind)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a room-ownership message.");
        return Post(world, recipient, kind, (int)room, roomTag);
    }

    public static bool IsRoomMessage(PlayerMessageKind kind) =>
        kind is PlayerMessageKind.RoomNeeded or PlayerMessageKind.RoomFull or PlayerMessageKind.RoomUnreachable
            or PlayerMessageKind.RoomUnequipped or PlayerMessageKind.RoomStorageFull or PlayerMessageKind.RoomWanted;

    /// <summary>A room message as the producers post it: the room type in Data1; zero in Data2, or
    /// for 44 and 45 the room (ours: its first slab's tile).</summary>
    public PlayerMessage? PostRoom(WorldState world, byte recipient, PlayerMessageKind kind, RoomTypeId room, int roomTag = 0)
    {
        if (!IsRoomMessage(kind)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a room message.");
        bool tagged = kind is PlayerMessageKind.RoomUnequipped or PlayerMessageKind.RoomStorageFull;
        return Post(world, recipient, kind, (int)room, tagged ? roomTag : 0);
    }

    /// <summary>The lifetime in seconds of a room message's subtype row: the rows of <c>0x006A03D8</c>
    /// (41), <c>0x006A04B8</c> (42) and <c>0x006A0528</c> (43), eight bytes each (subtype, seconds,
    /// string). Null for a type the table does not list.</summary>
    public static int? RoomLifetimeSeconds(PlayerMessageKind kind, RoomTypeId room) => (kind, (int)room) switch
    {
        (PlayerMessageKind.RoomNeeded, 1) => 420,
        (PlayerMessageKind.RoomNeeded, 2 or 4) => 180,
        (PlayerMessageKind.RoomNeeded, 11 or 14) => 600,
        (PlayerMessageKind.RoomNeeded, 6 or 7 or 9 or 10 or 12 or 13 or 15 or 16) => 300,
        (PlayerMessageKind.RoomFull, 1 or 4) => 240,
        (PlayerMessageKind.RoomFull, 2) => 90,
        (PlayerMessageKind.RoomFull, 6 or 7 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16) => 300,
        (PlayerMessageKind.RoomUnreachable, 11 or 12) => 360,
        (PlayerMessageKind.RoomUnreachable, 1 or 2 or 3 or 4 or 6 or 7 or 9 or 10 or 13 or 14 or 15 or 16) => 300,
        (PlayerMessageKind.RoomUnequipped, 1 or 2 or 4) => 120,
        (PlayerMessageKind.RoomUnequipped, 6 or 7 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16) => 240,
        (PlayerMessageKind.RoomStorageFull, 6 or 10) => 120,
        (PlayerMessageKind.RoomWanted, 1) => 420,
        (PlayerMessageKind.RoomWanted, 2 or 4) => 180,
        (PlayerMessageKind.RoomWanted, 11 or 14) => 600,
        (PlayerMessageKind.RoomWanted, 6 or 7 or 9 or 10 or 12 or 13 or 15 or 16) => 300,
        _ => null,
    };

    public PlayerMessage? Post(WorldState world, byte recipient, PlayerMessageKind kind, int data1, int data2)
    {
        // 41 or 99 for a Graveyard passes the player's +0xF4E word (0x004C5530) only while it is
        // clear, and a new record sets it (0x004C5650): once a player a level (Q544).
        bool graveyardWanted = kind is PlayerMessageKind.RoomNeeded or PlayerMessageKind.RoomWanted
            && data1 == (int)RoomTypeId.Graveyard;
        uint timeout = kind switch
        {
            PlayerMessageKind.HeartAttacked => (uint)SimulationTiming.TicksFromSeconds(120),
            PlayerMessageKind.CombatStarted => 0,
            PlayerMessageKind.CreatureArrived or PlayerMessageKind.CreatureIntroduction or PlayerMessageKind.ScriptedSpeech =>
                (uint)SimulationTiming.TicksFromSeconds(50),
            PlayerMessageKind.CallToArmsUnreachable => (uint)SimulationTiming.TicksFromSeconds(50),
            PlayerMessageKind.AlarmRaised or PlayerMessageKind.WallBreached => (uint)SimulationTiming.TicksFromSeconds(50),
            PlayerMessageKind.CreatureUnhappy or PlayerMessageKind.CreatureAngry or PlayerMessageKind.CreatureLeft =>
                (uint)SimulationTiming.TicksFromSeconds(50),
            // Both subtables give every listed variant 20 seconds. Warning row 0 names no
            // string, which discards the message; a variant off the table is refused.
            PlayerMessageKind.LowManaWarning when data1 == 0 => uint.MaxValue,
            PlayerMessageKind.LowManaWarning when data1 is >= 1 and <= 4 => (uint)SimulationTiming.TicksFromSeconds(20),
            PlayerMessageKind.ManaShortfall when data1 is >= 1 and <= 5 => (uint)SimulationTiming.TicksFromSeconds(20),
            PlayerMessageKind.TrainingUnpaid when data1 is >= 1 and <= 4 => (uint)SimulationTiming.TicksFromSeconds(20),
            PlayerMessageKind.TrainingUnpaid when data1 == 5 => (uint)SimulationTiming.TicksFromSeconds(240),
            PlayerMessageKind.CasinoJackpot or PlayerMessageKind.CasinoDrinking or PlayerMessageKind.JackpotUnpaid
                or PlayerMessageKind.JackpotMissed => (uint)SimulationTiming.TicksFromSeconds(50),
            PlayerMessageKind.CasinoRowdy when data1 == CasinoStep.RowdyVariant => (uint)SimulationTiming.TicksFromSeconds(20),
            // An unlisted room type would take the table's terminator row: discarded for 41 and 43,
            // kept for one sweep for 42. Nothing here posts one, so it is refused.
            _ when IsRoomMessage(kind) && RoomLifetimeSeconds(kind, (RoomTypeId)data1) is int seconds =>
                (uint)SimulationTiming.TicksFromSeconds(seconds),
            // 42's terminator row names a string, so an unlisted room keeps its record until the next sweep.
            PlayerMessageKind.RoomFull or PlayerMessageKind.RoomUnequipped when RoomLifetimeSeconds(kind, (RoomTypeId)data1) is null => 0,
            _ when IsOwnershipMessage(kind) =>
                OwnershipLifetimeSeconds(kind, data1) is int lasts ? (uint)SimulationTiming.TicksFromSeconds(lasts) : 0,
            PlayerMessageKind.LowManaWarning or PlayerMessageKind.ManaShortfall or PlayerMessageKind.TrainingUnpaid
                or PlayerMessageKind.RoomNeeded or PlayerMessageKind.RoomFull or PlayerMessageKind.RoomUnreachable =>
                throw new ArgumentOutOfRangeException(nameof(data1), data1, $"No subtype row for {kind}."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (timeout == uint.MaxValue) return null;
        if (world.Player(recipient).Status != PlayerStatus.Playing
            || !world.Triggers.NotificationsEnabled && kind is not (PlayerMessageKind.CreatureIntroduction or PlayerMessageKind.ScriptedSpeech))
            return null;
        if (graveyardWanted && world.Player(recipient).GraveyardWantedPosted) return null;
        if (kind is not (PlayerMessageKind.RoomLost or PlayerMessageKind.CasinoRowdy
            or PlayerMessageKind.CreatureArrived or PlayerMessageKind.CreatureIntroduction or PlayerMessageKind.ScriptedSpeech)
            && Unexpired(recipient, kind, HasSubtypes(kind) ? data1 : null) is PlayerMessage retained) return retained;
        if (_entries.Count == Capacity) _entries.RemoveAt(0);
        var added = new PlayerMessage(_nextId, recipient, kind, data1, data2, 0, timeout, true, false);
        _nextId = unchecked(_nextId + 1);
        _entries.Add(added);
        if (graveyardWanted) world.Player(recipient).GraveyardWantedPosted = true;
        return added;
    }

    internal PlayerMessage? Unexpired(byte recipient, PlayerMessageKind kind, int? subtype = null)
    {
        foreach (PlayerMessage message in _entries)
            if (message.PlayerId == recipient && message.Kind == kind && message.Age < message.Timeout
                && (subtype is null || message.Data1 == subtype.Value))
                return message;
        return null;
    }

    internal void Acknowledge(PlayerId player, uint id)
    {
        int index = _entries.FindIndex(m => m.Id == id && m.PlayerId == player);
        if (index >= 0) _entries[index] = _entries[index] with { Handled = true };
    }

    /// <summary><c>0x004C41E0</c>: ages every record, frees a 17 or 18 whose creature still exists
    /// and has left that band, frees one at its timeout and clears NeedsTab past age 199.</summary>
    internal void Sweep(WorldState world, GameDatabase database)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            PlayerMessage message = _entries[i];
            uint age = unchecked(message.Age + 1);
            if (age >= message.Timeout || LeftItsBand(world, database, message)) _entries.RemoveAt(i);
            else _entries[i] = message with { Age = age, NeedsTab = message.NeedsTab && age < 200 };
        }
    }

    /// <summary>DK2 tests the thing its Data2 tag names, when that is a creature: a corpse is
    /// still one, so ours tests any creature record the id names. A creature no longer in the
    /// world leaves the message to its timeout.</summary>
    private static bool LeftItsBand(WorldState world, GameDatabase database, PlayerMessage message)
    {
        CreatureMood band = message.Kind switch
        {
            PlayerMessageKind.CreatureUnhappy => CreatureMood.Unhappy,
            PlayerMessageKind.CreatureAngry => CreatureMood.Angry,
            _ => CreatureMood.Content,
        };
        if (band == CreatureMood.Content || world.CreatureById(message.Data2) is not CreatureState creature) return false;
        return CreatureAngerRules.MoodOf(database.CreatureById(creature.CreatureTypeId), creature) != band;
    }

    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(_nextId); hash.Add(_entries.Count);
        foreach (PlayerMessage message in _entries) message.HashInto(ref hash);
    }

    internal void Write(BinaryWriter writer)
    {
        writer.Write(_nextId); writer.Write(_entries.Count);
        foreach (PlayerMessage m in _entries)
        {
            writer.Write(m.Id); writer.Write(m.PlayerId.Value); writer.Write((uint)m.Kind);
            writer.Write(m.Data1); writer.Write(m.Data2); writer.Write(m.Age); writer.Write(m.Timeout);
            writer.Write(m.NeedsTab); writer.Write(m.Handled);
        }
    }

    internal void Read(BinaryReader reader)
    {
        _nextId = reader.ReadUInt32();
        int count = SaveReader.Count(reader, "retained messages", 27, Capacity);
        _entries.Clear();
        for (int i = 0; i < count; i++)
            _entries.Add(new PlayerMessage(reader.ReadUInt32(), reader.ReadByte(),
                (PlayerMessageKind)reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadBoolean(), reader.ReadBoolean()));
    }
}

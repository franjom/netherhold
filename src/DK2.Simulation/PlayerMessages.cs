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

    /// <summary>Type 52: Data1 is the warning stage 1–4 (subtable <c>0x006A0610</c>).</summary>
    LowManaWarning = 52,

    /// <summary>Type 53: Data1 3 is a creature chosen for removal; 1 is possession ended for
    /// want of mana, which nothing here produces (subtable <c>0x006A0638</c>).</summary>
    ManaShortfall = 53,

    /// <summary>Type 55: training the keeper cannot pay for. Both Training hooks pass Data1 5,
    /// a 240-second row of subtable <c>0x006A0690</c> (1–4 are 20 seconds and raised by no hook
    /// built here; 1 alone is under the <c>+0xF56</c> deadline, which is not built).</summary>
    TrainingUnpaid = 55,

    /// <summary>Type 41: the keeper has no room of the type in Data1 and needs one (subtable
    /// <c>0x006A03D8</c>).</summary>
    RoomNeeded = 41,

    /// <summary>Type 42: every room of the type in Data1 is full (subtable <c>0x006A04B8</c>).</summary>
    RoomFull = 42,

    /// <summary>Type 43: a room of the type in Data1 has space and no route (subtable <c>0x006A0528</c>).</summary>
    RoomUnreachable = 43,

    /// <summary>Type 84: a creature could not route to the Call to Arms flag
    /// (<c>0x00455DA0</c>, <c>0x004778E0</c>); fifty seconds, tab, no duplicates.</summary>
    CallToArmsUnreachable = 84,
}

public readonly record struct PlayerMessage(
    uint Id, byte PlayerId, PlayerMessageKind Kind, int Data1, int Data2,
    uint Age, uint Timeout, bool NeedsTab, bool Handled)
{
    /// <summary>The room type a room message (41 to 43) carries in Data1; null for any other kind.</summary>
    public RoomTypeId? Room => PlayerMessageQueue.IsRoomMessage(Kind) ? (RoomTypeId)Data1 : null;

    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(Id); hash.Add(PlayerId); hash.Add((uint)Kind);
        hash.Add(Data1); hash.Add(Data2); hash.Add(Age); hash.Add(Timeout);
        hash.Add(NeedsTab); hash.Add(Handled);
    }
}

/// <summary>The shared retained queue: the two combat position messages, the two
/// mana-pressure messages, unpaid training and the three room messages. Any other kind, or a
/// subtype its table lacks, is refused.</summary>
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
            or PlayerMessageKind.TrainingUnpaid || IsRoomMessage(kind);

    public static bool IsRoomMessage(PlayerMessageKind kind) =>
        kind is PlayerMessageKind.RoomNeeded or PlayerMessageKind.RoomFull or PlayerMessageKind.RoomUnreachable;

    /// <summary>A room message as every producer posts it: the room type in Data1, zero in Data2.</summary>
    public PlayerMessage? PostRoom(WorldState world, byte recipient, PlayerMessageKind kind, RoomTypeId room)
    {
        if (!IsRoomMessage(kind)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a room message.");
        return Post(world, recipient, kind, (int)room, 0);
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
        _ => null,
    };

    public PlayerMessage? Post(WorldState world, byte recipient, PlayerMessageKind kind, int data1, int data2)
    {
        // 41 for a Graveyard is admitted only through the player's +0xF4E suppression word
        // (0x004C5530), whose initial value is UNKNOWN (computer-keeper-messages.md section 1).
        if (kind == PlayerMessageKind.RoomNeeded && data1 == (int)RoomTypeId.Graveyard)
            throw new NotSupportedException("Message 41 for a Graveyard reads the unmodelled +0xF4E suppression.");
        uint timeout = kind switch
        {
            PlayerMessageKind.HeartAttacked => (uint)SimulationTiming.TicksFromSeconds(120),
            PlayerMessageKind.CombatStarted => 0,
            PlayerMessageKind.CallToArmsUnreachable => (uint)SimulationTiming.TicksFromSeconds(50),
            // Both subtables give every listed variant 20 seconds. Warning row 0 names no
            // string, which discards the message; a variant off the table is refused.
            PlayerMessageKind.LowManaWarning when data1 == 0 => uint.MaxValue,
            PlayerMessageKind.LowManaWarning when data1 is >= 1 and <= 4 => (uint)SimulationTiming.TicksFromSeconds(20),
            PlayerMessageKind.ManaShortfall when data1 is >= 1 and <= 5 => (uint)SimulationTiming.TicksFromSeconds(20),
            PlayerMessageKind.TrainingUnpaid when data1 is >= 1 and <= 4 => (uint)SimulationTiming.TicksFromSeconds(20),
            PlayerMessageKind.TrainingUnpaid when data1 == 5 => (uint)SimulationTiming.TicksFromSeconds(240),
            // An unlisted room type would take the table's terminator row: discarded for 41 and 43,
            // kept for one sweep for 42. Nothing here posts one, so it is refused.
            _ when IsRoomMessage(kind) && RoomLifetimeSeconds(kind, (RoomTypeId)data1) is int seconds =>
                (uint)SimulationTiming.TicksFromSeconds(seconds),
            PlayerMessageKind.LowManaWarning or PlayerMessageKind.ManaShortfall or PlayerMessageKind.TrainingUnpaid
                or PlayerMessageKind.RoomNeeded or PlayerMessageKind.RoomFull or PlayerMessageKind.RoomUnreachable =>
                throw new ArgumentOutOfRangeException(nameof(data1), data1, $"No subtype row for {kind}."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (timeout == uint.MaxValue) return null;
        if (world.Player(recipient).Status != PlayerStatus.Playing || !world.Triggers.NotificationsEnabled)
            return null;
        if (Unexpired(recipient, kind, HasSubtypes(kind) ? data1 : null) is PlayerMessage retained) return retained;
        if (_entries.Count == Capacity) _entries.RemoveAt(0);
        var added = new PlayerMessage(_nextId, recipient, kind, data1, data2, 0, timeout, true, false);
        _nextId = unchecked(_nextId + 1);
        _entries.Add(added);
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

    internal void Acknowledge(byte player, uint id)
    {
        int index = _entries.FindIndex(m => m.Id == id && m.PlayerId == player);
        if (index >= 0) _entries[index] = _entries[index] with { Handled = true };
    }

    internal void Sweep()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            PlayerMessage message = _entries[i];
            uint age = unchecked(message.Age + 1);
            if (age >= message.Timeout) _entries.RemoveAt(i);
            else _entries[i] = message with { Age = age, NeedsTab = message.NeedsTab && age < 200 };
        }
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
            writer.Write(m.Id); writer.Write(m.PlayerId); writer.Write((uint)m.Kind);
            writer.Write(m.Data1); writer.Write(m.Data2); writer.Write(m.Age); writer.Write(m.Timeout);
            writer.Write(m.NeedsTab); writer.Write(m.Handled);
        }
    }

    internal void Read(BinaryReader reader)
    {
        _nextId = reader.ReadUInt32();
        int count = reader.ReadInt32();
        if ((uint)count > Capacity) throw new InvalidDataException("Invalid retained message count.");
        _entries.Clear();
        for (int i = 0; i < count; i++)
            _entries.Add(new PlayerMessage(reader.ReadUInt32(), reader.ReadByte(),
                (PlayerMessageKind)reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadBoolean(), reader.ReadBoolean()));
    }
}

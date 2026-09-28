// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

public enum PlayerMessageKind : uint
{
    HeartAttacked = 1,
    CombatStarted = 6,
}

public readonly record struct PlayerMessage(
    uint Id, byte PlayerId, PlayerMessageKind Kind, int Data1, int Data2,
    uint Age, uint Timeout, bool NeedsTab, bool Handled)
{
    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(Id); hash.Add(PlayerId); hash.Add((uint)Kind);
        hash.Add(Data1); hash.Add(Data2); hash.Add(Age); hash.Add(Timeout);
        hash.Add(NeedsTab); hash.Add(Handled);
    }
}

/// <summary>The shared retained queue; currently supports the two combat position messages.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-messages.md")]
public sealed class PlayerMessageQueue
{
    public const int Capacity = 256;
    private readonly List<PlayerMessage> _entries = new();
    private uint _nextId = 1;

    public PlayerMessageQueue() => Entries = _entries.AsReadOnly();
    public IReadOnlyList<PlayerMessage> Entries { get; }

    public PlayerMessage? Post(WorldState world, byte recipient, PlayerMessageKind kind, int data1, int data2)
    {
        uint timeout = kind switch
        {
            PlayerMessageKind.HeartAttacked => (uint)SimulationTiming.TicksFromSeconds(120),
            PlayerMessageKind.CombatStarted => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (world.Player(recipient).Status != PlayerStatus.Playing || !world.Triggers.NotificationsEnabled)
            return null;
        foreach (PlayerMessage message in _entries)
            if (message.PlayerId == recipient && message.Kind == kind && message.Age < message.Timeout)
                return message;
        if (_entries.Count == Capacity) _entries.RemoveAt(0);
        var added = new PlayerMessage(_nextId, recipient, kind, data1, data2, 0, timeout, true, false);
        _nextId = unchecked(_nextId + 1);
        _entries.Add(added);
        return added;
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

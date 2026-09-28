// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Core;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Tools;

/// <summary>Player actions from a DK2 packet recording. Entity ids belong to
/// the original process and require an observed mapping before replay.</summary>
public sealed class PacketLog
{
    private readonly Dictionary<long, List<PacketAction>> _turns;

    private PacketLog(Dictionary<long, List<PacketAction>> turns) => _turns = turns;

    public int ActionCount => _turns.Values.Sum(actions => actions.Count);
    public int TurnCount => _turns.Count;
    public IReadOnlyList<(long Turn, uint Original, PacketEntityKind Kind)> EntityReferences =>
        [.. _turns.OrderBy(pair => pair.Key)
            .SelectMany(pair => pair.Value
                .Where(action => action.Kind is 55 or 57 or 62 or 88)
                .Select(action => (pair.Key, action.Kind == 55 ? action.Second & 0xffff : action.First, action.Kind == 88 ? PacketEntityKind.Object : PacketEntityKind.Creature)))];

    public static PacketLog Read(string path, int throughTurn, int firstRecord)
    {
        byte[] data = File.ReadAllBytes(path);
        if (firstRecord < 0) throw new ArgumentOutOfRangeException(nameof(firstRecord));
        const int headerSize = 16;
        const int actionSize = 18;
        const int cameraSize = 13;
        if (firstRecord > data.Length - headerSize - cameraSize)
            throw new InvalidDataException("Packet is too short for a turn record.");

        var turns = new Dictionary<long, List<PacketAction>>();
        int offset = firstRecord;
        uint expected = U32(data, offset);
        if (expected > throughTurn) throw new InvalidDataException("Requested range precedes the first packet turn.");
        var headers = new Dictionary<uint, List<int>>();
        for (int at = firstRecord; at <= data.Length - headerSize; at++)
        {
            if (data[at + 12] != 1 || data[at + 13] != 1) continue;
            uint number = U32(data, at);
            if (!headers.TryGetValue(number, out List<int>? positions)) headers[number] = positions = [];
            positions.Add(at);
        }
        while (offset + headerSize + cameraSize <= data.Length)
        {
            uint turn = U32(data, offset);
            if (turn > throughTurn) break;
            if (turn != expected || data[offset + 12] != 1 || data[offset + 13] != 1)
                throw new InvalidDataException($"Unexpected packet turn at byte {offset}: {turn}.");

            int count = -1;
            int next = -1;
            if (headers.TryGetValue(turn + 1, out List<int>? candidates))
                foreach (int candidate in candidates)
                {
                    int actionBytes = candidate - offset - headerSize - cameraSize;
                    if (actionBytes < 0 || actionBytes % actionSize != 0) continue;
                    if (count >= 0) throw new InvalidDataException($"Ambiguous packet framing at turn {turn}.");
                    count = actionBytes / actionSize;
                    next = candidate;
                }
            if (count < 0)
                throw new InvalidDataException($"Missing packet turn after {turn}.");

            var actions = new List<PacketAction>();
            for (int n = 0; n < count; n++)
            {
                int at = offset + headerSize + n * actionSize;
                actions.Add(new PacketAction(
                    U32(data, at), U32(data, at + 4), U32(data, at + 8),
                    U32(data, at + 12), U16(data, at + 16)));
            }
            if (actions.Count > 0) turns.Add(turn, actions);
            offset = next;
            expected++;
        }
        if (expected <= throughTurn)
            throw new InvalidDataException($"Packet ends before turn {throughTurn}.");
        return new PacketLog(turns);
    }

    public InputFrame At(long turn, PacketEntityMap entityIds)
    {
        if (!_turns.TryGetValue(turn, out List<PacketAction>? raw)) return InputFrame.Empty;
        var commands = new List<PlayerCommand>();
        foreach (PacketAction a in raw)
        {
            byte player = checked((byte)a.Player);
            TilePos first = PackedTile(a.First);
            TilePos second = PackedTile(a.Second);
            switch (a.Kind)
            {
                case 40:
                case 41:
                    commands.Add(TagTilesCommand.Rect(
                        player, first.X, first.Y, second.X, second.Y, a.Kind == 40));
                    break;
                case 47:
                    commands.Add(BuildRoomCommand.Rect(
                        player, checked((byte)a.First), second.X, second.Y,
                        PackedTile(a.Third).X, PackedTile(a.Third).Y));
                    break;
                case 55:
                    commands.Add(new CastSpellOnCreatureCommand(player, (byte)(a.First & 0xff),
                        Resolve(a.Second & 0xffff, PacketEntityKind.Creature), (ushort)(a.First >> 16)));
                    break;
                case 57:
                    commands.Add(new PickUpCreatureCommand(player, Resolve(a.First, PacketEntityKind.Creature)));
                    break;
                case 61:
                    commands.Add(new DropCommand(player, new TilePos(
                        checked((int)(a.First >> 12)), checked((int)(a.Second >> 12)))));
                    break;
                case 62:
                    commands.Add(new SlapCreatureCommand(player, Resolve(a.First, PacketEntityKind.Creature)));
                    break;
                case 88:
                    commands.Add(new ActivateSpecialCommand(player, Resolve(a.First, PacketEntityKind.Object)));
                    break;
                case 114:
                    break;
                default:
                    throw new InvalidDataException($"Packet action {a.Kind} at turn {turn} is not decoded.");
            }
        }
        return commands.Count == 0 ? InputFrame.Empty : new InputFrame(commands);

        int Resolve(uint original, PacketEntityKind kind) => entityIds.TryResolve(original, turn, kind, out int ours)
            ? ours : throw new InvalidDataException(
                $"Original entity {original} at turn {turn} has no observed simulation mapping.");
    }

    private static TilePos PackedTile(uint packed) =>
        new((int)(packed & 0xffff), (int)(packed >> 16));

    private static uint U32(byte[] data, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));

    private static ushort U16(byte[] data, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));

    private readonly record struct PacketAction(
        uint First, uint Second, uint Third, uint Kind, ushort Player);
}

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
    public IReadOnlyList<(long Turn, uint Original)> EntityReferences =>
        [.. _turns.OrderBy(pair => pair.Key)
            .SelectMany(pair => pair.Value
                .Where(action => action.Kind is 57 or 62 or 88)
                .Select(action => (pair.Key, action.First)))];

    public static PacketLog Read(string path, int throughTurn)
    {
        byte[] data = File.ReadAllBytes(path);
        const int firstRecord = 69;
        const int headerSize = 16;
        const int actionSize = 18;
        const int cameraSize = 13;
        if (data.Length < firstRecord + headerSize + cameraSize)
            throw new InvalidDataException("Packet is too short for a turn record.");

        var turns = new Dictionary<long, List<PacketAction>>();
        int offset = firstRecord;
        int expected = 2;
        while (offset + headerSize + cameraSize <= data.Length)
        {
            uint turn = U32(data, offset);
            if (turn > throughTurn) break;
            if (turn != expected || data[offset + 12] != 1 || data[offset + 13] != 1)
                throw new InvalidDataException($"Unexpected packet turn at byte {offset}: {turn}.");

            int count = -1;
            int next = -1;
            for (int n = 0; offset + 29 + actionSize * n + headerSize <= data.Length; n++)
            {
                int candidate = offset + 29 + actionSize * n;
                if (U32(data, candidate) != turn + 1 ||
                    data[candidate + 12] != 1 || data[candidate + 13] != 1)
                    continue;
                if (count >= 0)
                    throw new InvalidDataException($"Ambiguous packet framing at turn {turn}.");
                count = n;
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
                case 57:
                    commands.Add(new PickUpCreatureCommand(player, Resolve(a.First)));
                    break;
                case 61:
                    commands.Add(new DropCommand(player, new TilePos(
                        checked((int)(a.First >> 12)), checked((int)(a.Second >> 12)))));
                    break;
                case 62:
                    commands.Add(new SlapCreatureCommand(player, Resolve(a.First)));
                    break;
                case 88:
                    commands.Add(new ActivateSpecialCommand(player, Resolve(a.First)));
                    break;
                case 114:
                    break;
                default:
                    throw new InvalidDataException($"Packet action {a.Kind} at turn {turn} is not decoded.");
            }
        }
        return commands.Count == 0 ? InputFrame.Empty : new InputFrame(commands);

        int Resolve(uint original) => entityIds.TryResolve(original, turn, out int ours)
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

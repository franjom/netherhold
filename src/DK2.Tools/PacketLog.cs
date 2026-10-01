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
                .Where(action => action.Kind is 55 or 57 or 59 or 62 or 88)
                .Select(action => (pair.Key, action.Kind == 55 ? action.Second & 0xffff : action.First,
                    action.Kind switch
                    {
                        88 => PacketEntityKind.Object,
                        59 => PacketEntityKind.GoldTile,
                        _ => PacketEntityKind.Creature,
                    })))];

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

    /// <param name="goldOnTile">Our floor gold pile on a tile index, 0 for none; needed only
    /// when the recording takes gold into the hand (action 59).</param>
    /// <param name="dropAt">Where a drop the recording makes on a tile goes on our side; the
    /// replay's stand-in for the player, who would not drop where it cannot land.</param>
    public InputFrame At(long turn, PacketEntityMap entityIds, Func<int, int>? goldOnTile = null,
        Func<byte, TilePos, TilePos>? dropAt = null)
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
                case 49:
                    // Sell a dragged rectangle (0x005128f0, game-actions.md): its two corners.
                    commands.Add(SellRoomCommand.Rect(player, first.X, first.Y, second.X, second.Y));
                    break;
                case 50:
                case 52:
                    // Place a door (50, 0x00513210 to 0x004C33B0) or a trap (52, 0x00513270 to 0x004C3860)
                    // blueprint: the tile packed as x and y, then the id in the next field's low byte.
                    // Level 3's capture puts DK2's door records on 50's tiles and its traps on 52's.
                    commands.Add(a.Kind == 50
                        ? new PlaceDoorCommand(player, first, (byte)a.Second)
                        : new PlaceTrapCommand(player, first, (byte)a.Second));
                    break;
                case 54:
                    // Cast at a place: spell id (flags above it), then x and y in 1/4096 tile.
                    // Only Create Imp is decoded; its position check is not built (ImpCommands).
                    if ((a.First & 0xff) != 4 || a.First >> 16 != 0)
                        throw new InvalidDataException(
                            $"Packet action 54 casting spell {a.First & 0xff} at turn {turn} is not decoded.");
                    commands.Add(new CreateImpCommand(player,
                        new TilePos(checked((int)(a.Second >> 12)), checked((int)(a.Third >> 12)))));
                    break;
                case 55:
                    commands.Add(new CastSpellOnCreatureCommand(player, (byte)(a.First & 0xff),
                        Resolve(a.Second & 0xffff, PacketEntityKind.Creature), (ushort)(a.First >> 16)));
                    break;
                case 57:
                    commands.Add(new PickUpCreatureCommand(player, Resolve(a.First, PacketEntityKind.Creature)));
                    break;
                case 59:
                    // Our pile on the tile DK2's lay on; none there is a pickup of nothing, which the
                    // hand ignores and the replay's resolver counts as a miss.
                    int pile = goldOnTile?.Invoke(Resolve(a.First, PacketEntityKind.GoldTile))
                        ?? throw new InvalidOperationException("Packet action 59 needs a gold resolver.");
                    commands.Add(new PickUpObjectCommand(player, pile));
                    break;
                case 61:
                    var dropTile = new TilePos(checked((int)(a.First >> 12)), checked((int)(a.Second >> 12)));
                    commands.Add(new DropCommand(player, dropAt?.Invoke(player, dropTile) ?? dropTile));
                    break;
                case 62:
                    commands.Add(new SlapCreatureCommand(player, Resolve(a.First, PacketEntityKind.Creature)));
                    break;
                case 88:
                    commands.Add(new ActivateSpecialCommand(player, Resolve(a.First, PacketEntityKind.Object)));
                    break;
                case 114:
                    break;
                // Not replayed. 75 reveals the map on the issuing machine only, and 92 is the
                // window losing or regaining focus. 134 does change state, but only after the
                // level's result, where a comparison stops; it is not built (game-actions.md §3.1).
                case 75:
                case 92:
                case 134:
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

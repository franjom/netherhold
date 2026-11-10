// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Tools;

/// <summary>Player actions from a DK2 packet recording. Entity ids belong to
/// the original process and require an observed mapping before replay.</summary>
public sealed class PacketLog
{
    private readonly Dictionary<long, List<PacketAction>> _turns;

    private PacketLog(Dictionary<long, List<PacketAction>> turns, PacketRecordStamp[] records)
    {
        _turns = turns;
        Records = records;
    }

    public readonly record struct PacketRecordStamp(GameTick PlaybackTurn, uint RecordedStamp, int HeaderOffset);
    public IReadOnlyList<PacketRecordStamp> Records { get; }

    public int ActionCount => _turns.Values.Sum(actions => actions.Count);
    public int TurnCount => _turns.Count;
    public IReadOnlyList<(long Turn, uint Original, PacketEntityKind Kind)> EntityReferences =>
        [.. _turns.OrderBy(pair => pair.Key)
            .SelectMany(pair => pair.Value
                .Where(action => action.Kind is 55 or 57 or 59 or 62 or 88 or 93)
                .SelectMany(action => References(pair.Key, action)))];

    private static IEnumerable<(long Turn, uint Original, PacketEntityKind Kind)> References(long turn, PacketAction action)
    {
        yield return (turn, action.Kind switch { 55 => action.Second & 0xffff, 93 => action.First & 0xffff, _ => action.First }, action.Kind switch
        {
            88 => PacketEntityKind.Object,
            59 => PacketEntityKind.GoldTile,
            93 => PacketEntityKind.DoorTile,
            _ => PacketEntityKind.Creature,
        });
        // Action 88's target word: a creature for Kill Creatures and Transfer Creature, else zero.
        if (action.Kind == 88 && (action.Second & 0xffff) != 0)
            yield return (turn, action.Second & 0xffff, PacketEntityKind.Creature);
    }

    /// <summary>Reads playback turns from the explicitly selected first stamped header.
    /// Its preceding count byte must exist. That selected record executes on turn 1;
    /// the byte offset does not preserve an excerpt's former absolute time.</summary>
    public static PacketLog Read(string path, int throughTurn, int firstRecord)
    {
        byte[] data = File.ReadAllBytes(path);
        if (firstRecord < 1) throw new ArgumentOutOfRangeException(nameof(firstRecord));
        if (throughTurn < 1) throw new ArgumentOutOfRangeException(nameof(throughTurn));
        const int headerSize = 17;
        const int actionSize = 18;
        const int trailerSize = 12;
        var turns = new Dictionary<long, List<PacketAction>>();
        var records = new List<PacketRecordStamp>();
        int offset = firstRecord - 1;
        // 0055EA10 consumes count/header, payload and trailer sequentially. The
        // stamp produced by00522790 is clock+1, not the playback scheduling clock.
        for (long turn = 1; turn <= throughTurn; turn++)
        {
            if (offset > data.Length - headerSize - trailerSize)
                throw new InvalidDataException($"Packet ends before complete playback turn {turn}.");
            int count = data[offset];
            int size = headerSize + count * actionSize + trailerSize;
            if (size > data.Length - offset)
                throw new InvalidDataException($"Truncated packet payload/trailer at playback turn {turn}.");
            records.Add(new PacketRecordStamp(new GameTick(turn), U32(data, offset + 1), offset + 1));
            var actions = new List<PacketAction>();
            for (int n = 0; n < count; n++)
            {
                int at = offset + headerSize + n * actionSize;
                actions.Add(new PacketAction(
                    U32(data, at), U32(data, at + 4), U32(data, at + 8),
                    U32(data, at + 12), U16(data, at + 16)));
            }
            if (actions.Count > 0) turns.Add(turn, actions);
            offset += size;
        }
        return new PacketLog(turns, [.. records]);
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
                        player, (RoomTypeId)checked((byte)a.First), second.X, second.Y,
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
                        new FpPosition2(new Fp32(unchecked((int)a.Second)), new Fp32(unchecked((int)a.Third)))));
                    break;
                case 55:
                    commands.Add(new CastSpellOnCreatureCommand(player, (byte)(a.First & 0xff),
                        Resolve(a.Second & 0xffff, PacketEntityKind.Creature), (ushort)(a.First >> 16)));
                    break;
                case 21:
                    commands.Add(new LeavePossessionCommand(player));
                    break;
                case 25:
                    // 0x005146B0: X, Y, then Z in the word's low half and the heading in its high half.
                    commands.Add(new MovePossessedCreatureCommand(player, new PossessionPose(
                        new FpPosition3(new((int)a.First), new((int)a.Second), new((int)(a.Third & 0xFFFF))),
                        new Dk2Angle((ushort)(a.Third >> 16)))));
                    break;
                case 26:
                    // 0x00514830 (keeper-spell-possession.md): X and yaw, Y and pitch, then Z, melee
                    // at bit 27 and the slot in the top nibble, taken with an arithmetic shift.
                    commands.Add(new ActPossessedCreatureCommand(player,
                        new PossessionAim(
                            new FpPosition3(new((int)(a.First & 0xFFFFF)), new((int)(a.Second & 0xFFFFF)),
                                new((int)(a.Third & 0x3FFF))),
                            new Dk2Angle((ushort)((a.First >> 20) & 0x7FF)),
                            new Dk2Angle((ushort)((a.Second >> 20) & 0x7FF))),
                        (a.Third & (1u << 27)) != 0, new PossessedSlot((int)a.Third >> 28)));
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
                    // 0x004BC710 takes the packet's point; a stand-in tile has none.
                    TilePos landing = dropAt?.Invoke(player, dropTile) ?? dropTile;
                    commands.Add(landing == dropTile
                        ? new DropCommand(player, landing, 0, new FpPosition2(new Fp32((int)a.First), new Fp32((int)a.Second)),
                            new Dk2Angle((ushort)((a.Third >> 16) & 0x7FF)))
                        : new DropCommand(player, landing, Heading: new Dk2Angle((ushort)((a.Third >> 16) & 0x7FF))));
                    break;
                case 62:
                    commands.Add(new SlapCreatureCommand(player, Resolve(a.First, PacketEntityKind.Creature)));
                    break;
                case 88:
                    // 0x00513760 reads the target as the word at +0x4: the creature's tag from the hand's
                    // targeting (0x00408D07), zero from the hand's object arm, the spells panel and the computer keeper.
                    uint target = a.Second & 0xffff;
                    commands.Add(new ActivateSpecialCommand(player, Resolve(a.First, PacketEntityKind.Object),
                        target == 0 ? 0 : Resolve(target, PacketEntityKind.Creature)));
                    break;
                case 114:
                    // GUI button pressed (0x00513EB0): the category in the dword at +0x0, the id at +0x4.
                    // 0x004C7A50 compares each whole with a record's body byte, so above 255 none matches.
                    if (a.First <= byte.MaxValue && a.Second <= byte.MaxValue)
                        commands.Add(new GuiButtonCommand(player,
                            new GuiButtonTarget((GuiButtonCategory)a.First, (byte)a.Second)));
                    break;
                case 93:
                    // Lock or unlock a door (0x00513800): the door's slot in the word at +0x0, the
                    // player at +0x10; the handler toggles and never reads the second field.
                    int door = Resolve(a.First & 0xffff, PacketEntityKind.DoorTile);
                    commands.Add(new LockDoorCommand(player, PackedTile((uint)door)));
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

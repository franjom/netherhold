// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation;

/// <summary>
/// Lossless binary form of every <see cref="PlayerCommand"/>, for the input journal (ADR-0552).
/// Each command type has a fixed tag; a tag is never reused or renumbered, so a journal written
/// by an older build reads the same. A new command needs a new tag here and a sample in
/// <c>CommandCodecTests</c>, which fails until both exist.
/// </summary>
public static class CommandCodec
{
    public static void Write(BinaryWriter w, PlayerCommand command)
    {
        switch (command)
        {
            case TagTilesCommand c: Head(w, 1, c); Tiles(w, c.Tiles); w.Write(c.Tagged); break;
            case BuildRoomCommand c:
                Head(w, 2, c); w.Write((byte)c.RoomId); Tiles(w, c.Tiles); w.Write((byte)c.Mode);
                Tile(w, c.Press); Tile(w, c.Release); break;
            case SellRoomCommand { Mode: RoomSellMode.PlayerWave } c: Head(w, 34, c); Tile(w, c.Min); Tile(w, c.Max); break;
            case SellRoomCommand c: Head(w, 3, c); Tiles(w, c.Tiles); break;
            case GuiButtonCommand c: Head(w, 35, c); w.Write((byte)c.Target.Category); w.Write(c.Target.Id); break;
            case CreateImpCommand c:
                Head(w, 4, c);
                w.Write(c.Position.HasValue);
                if (c.Position is FpPosition2 p) Position(w, p);
                else { w.Write(c.At.HasValue); if (c.At is TilePos at) Tile(w, at); }
                break;
            case PickUpCreatureCommand c: Head(w, 5, c); w.Write(c.CreatureId); break;
            case TakeCreatureCommand c: Head(w, 36, c); w.Write(c.CreatureId); break;
            case PickUpObjectCommand c: Head(w, 6, c); w.Write(c.ObjectId); break;
            case PickUpStoreGoldCommand c: Head(w, 7, c); Tile(w, c.Tile); break;
            case DropCommand c:
                Head(w, 8, c); Tile(w, c.Tile); w.Write(c.TargetCreatureId);
                w.Write(c.Position.HasValue); if (c.Position is FpPosition2 dp) Position(w, dp);
                w.Write(c.Heading.HasValue); if (c.Heading is Dk2Angle dh) w.Write(dh.Raw); break;
            case SlapCreatureCommand c: Head(w, 9, c); w.Write(c.CreatureId); break;
            case MintWageCommand c: Head(w, 10, c); w.Write(c.CreatureId); break;
            case CastSpellAtPlaceCommand c:
                Head(w, 11, c); w.Write(c.SpellId); w.Write(c.X); w.Write(c.Y); w.Write(c.Flags); break;
            case CastSpellOnCreatureCommand c:
                Head(w, 12, c); w.Write(c.SpellId); w.Write(c.CreatureId); w.Write(c.Flags); break;
            case RemoveCallToArmsCommand c: Head(w, 13, c); break;
            case PlaceDoorCommand c: Head(w, 14, c); Tile(w, c.Tile); w.Write(c.DoorId); break;
            case PlaceTrapCommand c: Head(w, 15, c); Tile(w, c.Tile); w.Write(c.TrapId); break;
            case LockDoorCommand c: Head(w, 16, c); Tile(w, c.Tile); break;
            case PullCasinoLeverCommand c: Head(w, 17, c); Tile(w, c.Tile); break;
            case GrantCreatureLevelCommand c: Head(w, 18, c); w.Write(c.CreatureId); break;
            case SetAllianceCommand c: Head(w, 19, c); w.Write(c.OtherPlayerId.Value); w.Write(c.Allied); break;
            case SkipCameraPathCommand c: Head(w, 20, c); break;
            case AcknowledgePlayerMessageCommand c: Head(w, 21, c); w.Write(c.MessageId); break;
            case LeavePossessionCommand c: Head(w, 22, c); break;
            case ActPossessedCreatureCommand c:
                Head(w, 23, c); Aim(w, c.Aim); w.Write(c.Melee); w.Write(c.Slot.Value); break;
            case DismissFollowersCommand c: Head(w, 24, c); break;
            case ToggleSneakCommand c: Head(w, 25, c); w.Write(c.TileX); w.Write(c.TileY); break;
            case HypnotiseCommand c: Head(w, 26, c); Aim(w, c.Aim); break;
            case SetSniperModeCommand c: Head(w, 27, c); w.Write(c.On); break;
            case MovePossessedCreatureCommand c:
                Head(w, 28, c); Position(w, c.Pose.Position); w.Write(c.Pose.Heading.Raw); break;
            case SetRescuePriorityCommand c: Head(w, 29, c); w.Write(c.CreatureId); w.Write(c.Enabled); break;
            case ContinueAfterResultCommand c: Head(w, 30, c); break;
            case RevealMapCommand c: Head(w, 31, c); break;
            case ActivateSpecialCommand c: Head(w, 32, c); w.Write(c.ObjectId); w.Write(c.TargetId); break;
            case WithdrawCounterGoldCommand c: Head(w, 33, c); w.Write(c.Amount.Value); break;
            default:
                throw new NotSupportedException($"{command.GetType().Name} has no journal tag (CommandCodec).");
        }
    }

    /// <summary>The most tiles one decoded command may carry. A decoding limit (ADR-0724 R1), not
    /// DK2 behaviour: a legitimate list or wave is bounded by its map.</summary>
    public const int MaxTiles = 1 << 20;

    /// <summary>The fewest bytes any encoded command takes: its tag and player.</summary>
    public const int MinBytes = 3;

    private const int TileBytes = 8;

    /// <summary>Decodes one command. Malformed input throws <see cref="InvalidDataException"/>; input
    /// cut short throws <see cref="EndOfStreamException"/>, which journal readers treat as the end.</summary>
    public static PlayerCommand Read(BinaryReader r)
    {
        try
        {
            return ReadUnchecked(r);
        }
        catch (ArgumentException e)
        {
            // A typed value (slot, angle, ...) refusing what the bytes say is malformed input.
            throw new InvalidDataException($"A command carries a value its type refuses: {e.Message}", e);
        }
    }

    private static PlayerCommand ReadUnchecked(BinaryReader r)
    {
        ushort tag = r.ReadUInt16();
        var player = new PlayerId(r.ReadByte());
        return tag switch
        {
            1 => new TagTilesCommand(player, ReadTiles(r), r.ReadBoolean()),
            2 => ReadBuildRoom(r, player),
            3 => new SellRoomCommand(player, ReadTiles(r)),
            4 => r.ReadBoolean()
                ? new CreateImpCommand(player, ReadPosition(r))
                : new CreateImpCommand(player, r.ReadBoolean() ? ReadTile(r) : null),
            5 => new PickUpCreatureCommand(player, r.ReadInt32()),
            36 => new TakeCreatureCommand(player, r.ReadInt32()),
            6 => new PickUpObjectCommand(player, r.ReadInt32()),
            7 => new PickUpStoreGoldCommand(player, ReadTile(r)),
            8 => new DropCommand(player, ReadTile(r), r.ReadInt32(), r.ReadBoolean() ? ReadPosition(r) : null,
                r.ReadBoolean() ? new Dk2Angle(r.ReadUInt16()) : null),
            9 => new SlapCreatureCommand(player, r.ReadInt32()),
            10 => new MintWageCommand(player, r.ReadInt32()),
            11 => new CastSpellAtPlaceCommand(player, r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadUInt16()),
            12 => new CastSpellOnCreatureCommand(player, r.ReadByte(), r.ReadInt32(), r.ReadUInt16()),
            13 => new RemoveCallToArmsCommand(player),
            14 => new PlaceDoorCommand(player, ReadTile(r), r.ReadByte()),
            15 => new PlaceTrapCommand(player, ReadTile(r), r.ReadByte()),
            16 => new LockDoorCommand(player, ReadTile(r)),
            17 => new PullCasinoLeverCommand(player, ReadTile(r)),
            18 => new GrantCreatureLevelCommand(player, r.ReadInt32()),
            19 => new SetAllianceCommand(player, new PlayerId(r.ReadByte()), r.ReadBoolean()),
            20 => new SkipCameraPathCommand(player),
            21 => new AcknowledgePlayerMessageCommand(player, r.ReadUInt32()),
            22 => new LeavePossessionCommand(player),
            23 => new ActPossessedCreatureCommand(player, ReadAim(r), r.ReadBoolean(), new PossessedSlot(r.ReadSByte())),
            24 => new DismissFollowersCommand(player),
            25 => new ToggleSneakCommand(player, r.ReadInt32(), r.ReadInt32()),
            26 => new HypnotiseCommand(player, ReadAim(r)),
            27 => new SetSniperModeCommand(player, r.ReadBoolean()),
            28 => new MovePossessedCreatureCommand(player, new PossessionPose(ReadPosition3(r), new Dk2Angle(r.ReadUInt16()))),
            29 => new SetRescuePriorityCommand(player, r.ReadInt32(), r.ReadBoolean()),
            30 => new ContinueAfterResultCommand(player),
            31 => new RevealMapCommand(player),
            32 => new ActivateSpecialCommand(player, r.ReadInt32(), r.ReadInt32()),
            33 => new WithdrawCounterGoldCommand(player, new GoldAmount(r.ReadInt32())),
            34 => ReadSellWave(r, player),
            35 => new GuiButtonCommand(player, new GuiButtonTarget((GuiButtonCategory)r.ReadByte(), r.ReadByte())),
            _ => throw new InvalidDataException($"Unknown command tag {tag} in the journal."),
        };
    }

    private static BuildRoomCommand ReadBuildRoom(BinaryReader r, PlayerId player)
    {
        var room = (RoomTypeId)r.ReadByte();
        IReadOnlyList<TilePos> tiles = ReadTiles(r);
        var mode = (RoomBuildMode)r.ReadByte();
        TilePos press = ReadTile(r);
        return new BuildRoomCommand(player, room, tiles) { Mode = mode, Press = press, Release = ReadTile(r) };
    }

    private static SellRoomCommand ReadSellWave(BinaryReader r, PlayerId player)
    {
        TilePos a = ReadTile(r), b = ReadTile(r);
        // The wave expands two corners into every tile between them, so bound the area first.
        // Each side is checked before the product, which could otherwise wrap a long.
        long width = Math.Abs((long)a.X - b.X) + 1, height = Math.Abs((long)a.Y - b.Y) + 1;
        if (width > MaxTiles || height > MaxTiles || width * height > MaxTiles)
            throw new InvalidDataException($"A {width}x{height} sell wave exceeds the codec limit {MaxTiles}.");
        return SellRoomCommand.Rect(player, a.X, a.Y, b.X, b.Y);
    }

    private static void Head(BinaryWriter w, ushort tag, PlayerCommand c)
    {
        w.Write(tag);
        w.Write(c.PlayerId.Value);
    }

    private static void Tile(BinaryWriter w, TilePos t) { w.Write(t.X); w.Write(t.Y); }
    private static TilePos ReadTile(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());

    private static void Tiles(BinaryWriter w, IReadOnlyList<TilePos> tiles)
    {
        w.Write(tiles.Count);
        foreach (TilePos t in tiles) Tile(w, t);
    }

    private static TilePos[] ReadTiles(BinaryReader r)
    {
        int count = r.ReadInt32();
        if (count is < 0 or > MaxTiles)
            throw new InvalidDataException($"A tile count of {count} is outside 0..{MaxTiles}.");
        // The count is untrusted: allocate only what the input can still hold.
        Stream s = r.BaseStream;
        if (s.CanSeek && (long)count * TileBytes > s.Length - s.Position)
            throw new EndOfStreamException($"A command's {count} tiles run past the end of the input.");
        var tiles = new TilePos[count];
        for (int i = 0; i < tiles.Length; i++) tiles[i] = ReadTile(r);
        return tiles;
    }

    private static void Position(BinaryWriter w, FpPosition2 p) { w.Write(p.X.Raw); w.Write(p.Y.Raw); }
    private static FpPosition2 ReadPosition(BinaryReader r) => new(new Fp32(r.ReadInt32()), new Fp32(r.ReadInt32()));

    private static void Position(BinaryWriter w, FpPosition3 p) { w.Write(p.X.Raw); w.Write(p.Y.Raw); w.Write(p.Z.Raw); }
    private static FpPosition3 ReadPosition3(BinaryReader r) =>
        new(new Fp32(r.ReadInt32()), new Fp32(r.ReadInt32()), new Fp32(r.ReadInt32()));

    private static void Aim(BinaryWriter w, PossessionAim a)
    {
        Position(w, a.Origin);
        w.Write(a.Yaw.Raw);
        w.Write(a.Pitch.Raw);
    }

    private static PossessionAim ReadAim(BinaryReader r) =>
        new(ReadPosition3(r), new Dk2Angle(r.ReadUInt16()), new Dk2Angle(r.ReadUInt16()));
}

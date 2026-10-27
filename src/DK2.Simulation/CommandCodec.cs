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
            case SellRoomCommand c: Head(w, 3, c); Tiles(w, c.Tiles); break;
            case CreateImpCommand c:
                Head(w, 4, c);
                w.Write(c.Position.HasValue);
                if (c.Position is FpPosition2 p) Position(w, p);
                else { w.Write(c.At.HasValue); if (c.At is TilePos at) Tile(w, at); }
                break;
            case PickUpCreatureCommand c: Head(w, 5, c); w.Write(c.CreatureId); break;
            case PickUpObjectCommand c: Head(w, 6, c); w.Write(c.ObjectId); break;
            case PickUpStoreGoldCommand c: Head(w, 7, c); Tile(w, c.Tile); break;
            case DropCommand c:
                Head(w, 8, c); Tile(w, c.Tile); w.Write(c.TargetCreatureId);
                w.Write(c.Position.HasValue); if (c.Position is FpPosition2 dp) Position(w, dp); break;
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

    public static PlayerCommand Read(BinaryReader r)
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
            6 => new PickUpObjectCommand(player, r.ReadInt32()),
            7 => new PickUpStoreGoldCommand(player, ReadTile(r)),
            8 => new DropCommand(player, ReadTile(r), r.ReadInt32(), r.ReadBoolean() ? ReadPosition(r) : null),
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
        var tiles = new TilePos[r.ReadInt32()];
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

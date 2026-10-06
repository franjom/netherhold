// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Parsers for TERRAIN(110) and ROOMS(120) records.
///
/// Record sizes are self-validating: summing the documented field widths
/// (ArtResource = 84 bytes) reproduces the observed 552 (terrain) and 1055
/// (rooms) byte records exactly. See docs/research/kwd-terrain-rooms.md.
/// </summary>
public static class KwdTerrainRooms
{
    public static byte[] WriteRoomRecord(RoomDef record, KwdRecordImage template)
    {
        byte[] bytes = KwdOverlay.Image(template, KwdRecordType.Rooms, RoomRecordSize);
        KwdOverlay.Ascii(bytes, 0, 32, record.Name, ReadName(bytes, 0, 32), trimmed: false);
        if (record.ObjectIds.Count != 8)
            throw new InvalidDataException("ROOMS requires exactly eight object ids.");
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(RoomLayout.Height), record.CeilingHeight.Raw);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(RoomLayout.Flags), (uint)record.Flags);
        U16(RoomLayout.Tooltip, record.TooltipStringId);
        U16(RoomLayout.NameString, record.NameStringId);
        U16(RoomLayout.Cost, record.Cost);
        bytes[RoomLayout.Id] = record.RoomId;
        bytes[RoomLayout.Return] = record.ReturnPercentage;
        bytes[RoomLayout.Terrain] = record.TerrainId;
        bytes[RoomLayout.Construction] = (byte)record.Construction;
        bytes[RoomLayout.Creature] = record.CreatedCreatureId;
        for (int i = 0; i < 8; i++) bytes[RoomLayout.Objects + i] = record.ObjectIds[i];
        bytes[RoomLayout.Order] = record.OrderInEditor;
        bytes[RoomLayout.SizeX] = record.RecommendedSizeX;
        bytes[RoomLayout.SizeY] = record.RecommendedSizeY;
        U16(RoomLayout.Health, record.HealthGain);
        return bytes;
    }
    public static byte[] WriteTerrainRecord(TerrainDef record, KwdRecordImage template)
    {
        byte[] bytes = KwdOverlay.Image(template, KwdRecordType.Terrain, TerrainRecordSize);
        KwdOverlay.Ascii(bytes, 0, 32, record.Name, ReadName(bytes, 0, 32), trimmed: false);
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(TerrainLayout.Depth), record.Depth.Raw);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(TerrainLayout.Flags), (uint)record.Flags);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(TerrainLayout.Damage), record.Damage);
        U16(TerrainLayout.Gold, record.GoldValue);
        U16(TerrainLayout.Mana, record.ManaGain);
        U16(TerrainLayout.MaxMana, record.MaxManaGain);
        U16(TerrainLayout.Tooltip, record.TooltipStringId);
        U16(TerrainLayout.NameString, record.NameStringId);
        bytes[TerrainLayout.Id] = record.TerrainId;
        U16(TerrainLayout.StartingHealth, record.StartingHealth);
        bytes[TerrainLayout.MaxHealthType] = record.MaxHealthTypeTerrainId;
        bytes[TerrainLayout.DestroyedType] = record.DestroyedTypeTerrainId;
        U16(TerrainLayout.MaxHealth, record.MaxHealth);
        return bytes;
    }
    private const int TerrainRecordSize = 552;
    private const int RoomRecordSize = 1055;

    private static class TerrainLayout
    {
        internal const int Depth = 0x188, Flags = 0x190, Damage = 0x194;
        internal const int Gold = 0x19A, Mana = 0x19C, MaxMana = 0x19E;
        internal const int Tooltip = 0x1A0, NameString = 0x1A2, Id = 0x1D6;
        internal const int StartingHealth = 0x1D7, MaxHealthType = 0x1D9, DestroyedType = 0x1DA;
        internal const int MaxHealth = 0x1FF;
    }

    private static class RoomLayout
    {
        internal const int Height = 0x368, Flags = 0x370, Tooltip = 0x374, NameString = 0x376;
        internal const int Cost = 0x378, Id = 0x392, Return = 0x393, Terrain = 0x394;
        internal const int Construction = 0x395, Creature = 0x396, Objects = 0x39A, Order = 0x3C2;
        internal const int SizeX = 0x41B, SizeY = 0x41C, Health = 0x41D;
        internal const int TorchIntensity = 0x36E, TorchHeight = 0x380, TorchRgb = 0x397;
        internal const int TorchRadius = 0x3C3, TorchArt = 0x3C7;
    }

    public static List<TerrainDef> ParseTerrain(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<TerrainDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Terrain) continue;
            RequireSize(chunk, TerrainRecordSize, "TERRAIN");
            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseTerrainRecord(chunk.Content, i * TerrainRecordSize));
        }
        return result;
    }

    public static List<RoomDef> ParseRooms(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<RoomDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Rooms) continue;
            RequireSize(chunk, RoomRecordSize, "ROOMS");
            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseRoomRecord(chunk.Content, i * RoomRecordSize));
        }
        return result;
    }

    private static void RequireSize(KwdChunk chunk, int expected, string what)
    {
        if (chunk.ItemCount == 0) return;
        int actual = chunk.Content.Length / chunk.ItemCount;
        if (actual != expected || chunk.Content.Length != expected * chunk.ItemCount)
            throw new InvalidDataException(
                $"{what} record size is {actual}, expected {expected}.");
    }

    private static string ReadName(byte[] rec, int at, int len)
    {
        ReadOnlySpan<byte> s = rec.AsSpan(at, len);
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]);
    }

    private static TerrainDef ParseTerrainRecord(byte[] rec, int b)
    {
        ushort U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + off));
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(b + off));
        byte U8(int off) => rec[b + off];

        return new TerrainDef
        {
            Name = ReadName(rec, b + 0x000, 32),
            Depth = new Fp32(BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + TerrainLayout.Depth))),
            Flags = (TerrainFlags)U32(TerrainLayout.Flags),
            Damage = BinaryPrimitives.ReadInt16LittleEndian(rec.AsSpan(b + TerrainLayout.Damage)),
            GoldValue = U16(TerrainLayout.Gold),
            ManaGain = U16(TerrainLayout.Mana),
            MaxManaGain = U16(TerrainLayout.MaxMana),
            TooltipStringId = U16(TerrainLayout.Tooltip),
            NameStringId = U16(TerrainLayout.NameString),
            TerrainId = U8(TerrainLayout.Id),
            StartingHealth = U16(TerrainLayout.StartingHealth),
            MaxHealthTypeTerrainId = U8(TerrainLayout.MaxHealthType),
            DestroyedTypeTerrainId = U8(TerrainLayout.DestroyedType),
            MaxHealth = U16(TerrainLayout.MaxHealth),
        };
    }

    private static RoomDef ParseRoomRecord(byte[] rec, int b)
    {
        ushort U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + off));
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(b + off));
        byte U8(int off) => rec[b + off];

        var objectIds = new byte[8];
        Array.Copy(rec, b + RoomLayout.Objects, objectIds, 0, 8);

        return new RoomDef
        {
            Name = ReadName(rec, b + 0x000, 32),
            CeilingHeight = new Fp32(BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + RoomLayout.Height))),
            Flags = (RoomFlags)U32(RoomLayout.Flags),
            TooltipStringId = U16(RoomLayout.Tooltip),
            NameStringId = U16(RoomLayout.NameString),
            Cost = U16(RoomLayout.Cost),
            RoomId = U8(RoomLayout.Id),
            ReturnPercentage = U8(RoomLayout.Return),
            TerrainId = U8(RoomLayout.Terrain),
            Construction = (TileConstruction)U8(RoomLayout.Construction),
            CreatedCreatureId = U8(RoomLayout.Creature),
            ObjectIds = objectIds,
            OrderInEditor = U8(RoomLayout.Order),
            RecommendedSizeX = U8(RoomLayout.SizeX),
            RecommendedSizeY = U8(RoomLayout.SizeY),
            HealthGain = U16(RoomLayout.Health),
            Torch = Torch(),
        };

        RoomTorch Torch()
        {
            var flags = (RoomFlags)U32(RoomLayout.Flags);
            int High(RoomFlags bit) => (flags & bit) != 0 ? 255 : 0;
            return new RoomTorch(
                U8(RoomLayout.TorchRgb) + High(RoomFlags.TorchRedHigh),
                U8(RoomLayout.TorchRgb + 1) + High(RoomFlags.TorchGreenHigh),
                U8(RoomLayout.TorchRgb + 2) + High(RoomFlags.TorchBlueHigh),
                (byte)U16(RoomLayout.TorchIntensity),
                U16(RoomLayout.TorchHeight),
                (int)U32(RoomLayout.TorchRadius),
                ReadName(rec, b + RoomLayout.TorchArt, 64),
                rec[b + RoomLayout.TorchArt] != 0);
        }
    }
}

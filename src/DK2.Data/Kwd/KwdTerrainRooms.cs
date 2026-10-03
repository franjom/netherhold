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
        Array.Copy(rec, b + 0x39A, objectIds, 0, 8);

        return new RoomDef
        {
            Name = ReadName(rec, b + 0x000, 32),
            CeilingHeight = new Fp32(BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + 0x368))),
            Flags = (RoomFlags)U32(0x370),
            TooltipStringId = U16(0x374),
            NameStringId = U16(0x376),
            Cost = U16(0x378),
            RoomId = U8(0x392),
            ReturnPercentage = U8(0x393),
            TerrainId = U8(0x394),
            Construction = (TileConstruction)U8(0x395),
            CreatedCreatureId = U8(0x396),
            ObjectIds = objectIds,
            OrderInEditor = U8(0x3C2),
            RecommendedSizeX = U8(0x41B),
            RecommendedSizeY = U8(0x41C),
            HealthGain = U16(0x41D),
        };
    }
}

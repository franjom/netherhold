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
    private const int TerrainRecordSize = 552;
    private const int RoomRecordSize = 1055;

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
            Depth = new Fp32(BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + 0x188))),
            Flags = (TerrainFlags)U32(0x190),
            Damage = BinaryPrimitives.ReadInt16LittleEndian(rec.AsSpan(b + 0x194)),
            GoldValue = U16(0x19A),
            ManaGain = U16(0x19C),
            MaxManaGain = U16(0x19E),
            TooltipStringId = U16(0x1A0),
            NameStringId = U16(0x1A2),
            TerrainId = U8(0x1D6),
            StartingHealth = U16(0x1D7),
            MaxHealthTypeTerrainId = U8(0x1D9),
            DestroyedTypeTerrainId = U8(0x1DA),
            MaxHealth = U16(0x1FF),
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

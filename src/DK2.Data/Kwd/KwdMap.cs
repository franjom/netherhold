using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>One tile of a level's MAP grid — 4 bytes in the file.</summary>
public readonly record struct MapTile(byte TerrainId, byte PlayerId, byte Flags, byte Unknown);

/// <summary>
/// A level's tile grid, parsed from the MAP(100) record.
///
/// Layout CONFIRMED: the MAP chunk header carries u32 width, u32 height, and
/// the content is exactly width * height * 4 bytes — one 4-byte tile per cell
/// in row-major order (terrainId, playerId, flag, unknown).
/// See docs/research/kwd-terrain-rooms.md.
/// </summary>
public sealed class MapGrid
{
    private readonly MapTile[] _tiles;

    public int Width { get; }
    public int Height { get; }

    private MapGrid(int width, int height, MapTile[] tiles)
    {
        Width = width;
        Height = height;
        _tiles = tiles;
    }

    public MapTile this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
            return _tiles[y * Width + x];
        }
    }

    public IReadOnlyList<MapTile> Tiles => _tiles;

    public static MapGrid? Parse(IEnumerable<KwdChunk> chunks)
    {
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Map) continue;

            int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk.HeaderBlock);
            int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk.HeaderBlock.AsSpan(4));
            int expected = width * height * 4;

            if (width <= 0 || height <= 0 || chunk.Content.Length < expected)
                throw new InvalidDataException(
                    $"MAP chunk claims {width}x{height} ({expected} bytes) but content is {chunk.Content.Length}.");

            var tiles = new MapTile[width * height];
            for (int i = 0; i < tiles.Length; i++)
            {
                int o = i * 4;
                tiles[i] = new MapTile(
                    chunk.Content[o], chunk.Content[o + 1],
                    chunk.Content[o + 2], chunk.Content[o + 3]);
            }
            return new MapGrid(width, height, tiles);
        }
        return null;
    }
}

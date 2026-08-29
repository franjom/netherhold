using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The mutable per-tile simulation state.
///
/// Distinct from <see cref="MapTile"/>, which is the immutable file view. Only
/// the four fields below are justified by evidence today:
/// <list type="bullet">
/// <item>terrain, owner and flags come straight from the MAP record (CONFIRMED);</item>
/// <item>health is seeded from <see cref="TerrainDef.StartingHealth"/> and is what
/// digging, claiming and reinforcing modify (CONFIRMED — see
/// docs/research/kwd-terrain-rooms.md).</item>
/// </list>
/// Anything else a tile needs (room membership, tagged-for-digging, gold left
/// in a seam) arrives with M3, when there is a mechanic that reads it.
/// </summary>
public struct TileState
{
    public byte TerrainId;
    public byte OwnerPlayerId;

    /// <summary>MAP byte 3. Bridge-related; exact meaning UNKNOWN, preserved verbatim.</summary>
    public byte Flags;

    /// <summary>Current tile health. Seeded from the terrain type's starting health.</summary>
    public int Health;

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(TerrainId);
        hash.Add(OwnerPlayerId);
        hash.Add(Flags);
        hash.Add(Health);
    }
}

/// <summary>
/// Per-player simulation state. Deliberately minimal: gold and mana are the two
/// resources every DK2 player has and that the scenario format already names
/// (DK2_REIMPLEMENTATION_PLAN.md 6.2). Research, spells, creature rosters and
/// the rest arrive with the mechanics that need them.
/// </summary>
public struct PlayerState
{
    public byte PlayerId;
    public int Gold;
    public int Mana;

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(PlayerId);
        hash.Add(Gold);
        hash.Add(Mana);
    }
}

/// <summary>
/// All mutable simulation state for one level. Owns no behaviour — systems act
/// on it. Everything reachable from here is hashed and snapshotted; anything
/// not reachable from here does not exist as far as determinism is concerned,
/// which is the invariant that keeps the gate meaningful.
/// </summary>
public sealed class WorldState
{
    private readonly TileState[] _tiles;
    private readonly PlayerState[] _players; // kept sorted by PlayerId, for stable hashing

    private WorldState(int width, int height, TileState[] tiles, PlayerState[] players)
    {
        Width = width;
        Height = height;
        _tiles = tiles;
        _players = players;
        CurrentTick = GameTick.Zero;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The tick the world is currently *at*; the next Tick() executes this one.</summary>
    public GameTick CurrentTick { get; internal set; }

    public int TileCount => _tiles.Length;

    public ref TileState Tile(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return ref _tiles[(y * Width) + x];
    }

    public ref TileState Tile(TilePos p) => ref Tile(p.X, p.Y);

    public ref TileState TileAtIndex(int index) => ref _tiles[index];

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    public bool Contains(TilePos p) => Contains(p.X, p.Y);

    public IReadOnlyList<byte> PlayerIds => _players.Select(p => p.PlayerId).ToArray();

    public int PlayerCount => _players.Length;

    public ref PlayerState PlayerAtIndex(int index) => ref _players[index];

    public ref PlayerState Player(byte playerId)
    {
        for (int i = 0; i < _players.Length; i++)
            if (_players[i].PlayerId == playerId)
                return ref _players[i];
        throw new KeyNotFoundException($"No player with id {playerId} in this world.");
    }

    public bool HasPlayer(byte playerId)
    {
        foreach (PlayerState p in _players)
            if (p.PlayerId == playerId) return true;
        return false;
    }

    /// <summary>
    /// Builds the initial world for a level: every tile takes its terrain,
    /// owner and flags from the MAP record and its health from that terrain
    /// type's <see cref="TerrainDef.StartingHealth"/>.
    ///
    /// Starting gold and mana are NOT set here — those come from the level's
    /// PLAYERS record, which is not parsed yet (M6). Callers that need them
    /// set them explicitly, and scenarios say so out loud.
    /// </summary>
    public static WorldState FromMap(GameDatabase database, LevelMap map, IEnumerable<byte> playerIds)
    {
        var tiles = new TileState[map.Width * map.Height];
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                MapTile source = map[x, y];
                TerrainDef terrain = database.TerrainById(source.TerrainId);
                tiles[(y * map.Width) + x] = new TileState
                {
                    TerrainId = source.TerrainId,
                    OwnerPlayerId = source.PlayerId,
                    Flags = source.Flags,
                    Health = terrain.StartingHealth,
                };
            }
        }

        PlayerState[] players = playerIds
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new PlayerState { PlayerId = id })
            .ToArray();

        return new WorldState(map.Width, map.Height, tiles, players);
    }

    /// <summary>Reconstructs a world from a snapshot. Used only by <see cref="WorldSnapshot"/>.</summary>
    internal static WorldState FromParts(int width, int height, TileState[] tiles, PlayerState[] players)
        => new(width, height, tiles, players);

    internal TileState[] RawTiles => _tiles;
    internal PlayerState[] RawPlayers => _players;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(CurrentTick);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(_tiles.Length);
        foreach (TileState t in _tiles) t.HashInto(ref hash);
        hash.Add(_players.Length);
        foreach (PlayerState p in _players) p.HashInto(ref hash);
    }
}

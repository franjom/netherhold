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
/// Anything else a tile needs (room membership, gold left in a seam) arrives
/// with the mechanic that reads it, not before.
/// </summary>
public struct TileState
{
    public byte TerrainId;
    public byte OwnerPlayerId;

    /// <summary>MAP byte 3. Bridge-related; exact meaning UNKNOWN, preserved verbatim.</summary>
    public byte Flags;

    /// <summary>Current tile health. Seeded from the terrain type's starting health.</summary>
    public int Health;

    /// <summary>
    /// Which players have this tile tagged for digging: bit <c>n</c> is set for
    /// player id <c>n</c>.
    ///
    /// STRONGLY INFERRED that tagging is per player rather than global: DK2 is a
    /// multiplayer game in which each keeper marks and sees their own dig
    /// orders, and the terrain table carries a per-type <c>Taggable</c> flag
    /// rather than a shared tag list. The bitmask is our representation, not a
    /// claim about the original's storage.
    /// </summary>
    public ushort TaggedBy;

    /// <summary>Highest player id that fits in <see cref="TaggedBy"/>.</summary>
    public const int MaxTaggingPlayerId = 15;

    public readonly bool IsTaggedBy(byte playerId) =>
        playerId <= MaxTaggingPlayerId && (TaggedBy & (ushort)(1 << playerId)) != 0;

    public readonly bool IsTaggedByAnyone => TaggedBy != 0;

    public void SetTaggedBy(byte playerId, bool tagged)
    {
        if (playerId > MaxTaggingPlayerId)
            throw new ArgumentOutOfRangeException(
                nameof(playerId), playerId,
                $"Tile tags only track player ids 0..{MaxTaggingPlayerId}.");

        ushort bit = (ushort)(1 << playerId);
        TaggedBy = tagged ? (ushort)(TaggedBy | bit) : (ushort)(TaggedBy & ~bit);
    }

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(TerrainId);
        hash.Add(OwnerPlayerId);
        hash.Add(Flags);
        hash.Add(Health);
        hash.Add(TaggedBy);
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
    private readonly List<TileWorkOrder> _pendingWork = [];
    private readonly List<CreatureState> _creatures = [];
    private int _nextCreatureId = 1;

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

    /// <summary>
    /// Work applications waiting to be resolved by
    /// <see cref="Systems.TileWorkSystem"/>, in the order they were queued.
    ///
    /// A list, not a set or a dictionary: order is behaviour. Two imps working
    /// the same seam in a different order can end up carrying different amounts
    /// of gold, so the queue is FIFO and never sorted or deduplicated.
    ///
    /// In DK2 the producer is a working creature. That creature does not exist
    /// yet, so for now tests and the scenario harness queue work directly and
    /// say so in their reports.
    /// </summary>
    public IReadOnlyList<TileWorkOrder> PendingWork => _pendingWork;

    public void QueueWork(TileWorkOrder order)
    {
        if ((uint)order.TileIndex >= (uint)_tiles.Length)
            throw new ArgumentOutOfRangeException(
                nameof(order), order.TileIndex, "Work order references a tile outside the map.");
        _pendingWork.Add(order);
    }

    public void QueueWork(int x, int y, TileWorkKind kind, byte playerId)
    {
        if (!Contains(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the map.");
        _pendingWork.Add(new TileWorkOrder((y * Width) + x, kind, playerId));
    }

    internal void ClearPendingWork() => _pendingWork.Clear();

    /// <summary>
    /// Every creature in the level, in spawn order.
    ///
    /// The order is behaviour, not bookkeeping: creatures are updated in it, so
    /// two imps that reach for the same job get it in this order. Never sorted,
    /// never reordered — removing a creature must preserve the order of the
    /// rest.
    /// </summary>
    public IReadOnlyList<CreatureState> Creatures => _creatures;

    /// <summary>
    /// Puts a creature on the map, at the centre of the given tile.
    ///
    /// <b>This is not how DK2 makes creatures.</b> The original's imps come from
    /// a dungeon heart, from the Create Imp spell at a mana cost, and from
    /// specials — governed by <c>TimeBeforeFreeImpGeneratedSeconds</c>,
    /// <c>MinimumImpThreshold</c> and <c>ImpPopCountdownTimeSeconds</c>, none of
    /// which can run without rooms and mana. Until they exist, something outside
    /// the simulation (a test, a scenario file) places creatures directly, and
    /// says so.
    /// </summary>
    public CreatureState SpawnCreature(byte creatureTypeId, byte ownerPlayerId, TilePos at)
    {
        if (!Contains(at))
            throw new ArgumentOutOfRangeException(nameof(at), at, "Spawn tile is outside the map.");

        var creature = new CreatureState
        {
            Id = _nextCreatureId++,
            CreatureTypeId = creatureTypeId,
            OwnerPlayerId = ownerPlayerId,
            PosX = CreatureState.CentreOf(at.X),
            PosY = CreatureState.CentreOf(at.Y),
            Activity = CreatureActivity.Idle,
        };

        _creatures.Add(creature);
        return creature;
    }

    public CreatureState? CreatureById(int id)
    {
        foreach (CreatureState c in _creatures)
            if (c.Id == id) return c;
        return null;
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
    internal List<TileWorkOrder> RawPendingWork => _pendingWork;
    internal List<CreatureState> RawCreatures => _creatures;

    /// <summary>Restores the id counter after a snapshot, so ids stay unique across a save.</summary>
    internal int NextCreatureId
    {
        get => _nextCreatureId;
        set => _nextCreatureId = value;
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(CurrentTick);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(_tiles.Length);
        foreach (TileState t in _tiles) t.HashInto(ref hash);
        hash.Add(_players.Length);
        foreach (PlayerState p in _players) p.HashInto(ref hash);
        hash.Add(_pendingWork.Count);
        foreach (TileWorkOrder o in _pendingWork) o.HashInto(ref hash);

        // The id counter is hashed too: two worlds holding identical creatures
        // but disagreeing about the next id will diverge the moment either one
        // spawns, which is exactly the kind of late divergence the gate exists
        // to catch early.
        hash.Add(_creatures.Count);
        hash.Add(_nextCreatureId);
        foreach (CreatureState c in _creatures) c.HashInto(ref hash);
    }
}

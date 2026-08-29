namespace DK2.Data.Model;

/// <summary>
/// The parsed DK2 static database: every definition the simulation reads and
/// never writes. Loaded once from the original game's KWD files and immutable
/// thereafter.
///
/// This type is the seam between the data layer (DK2.Data, which does file IO)
/// and the simulation layer (DK2.Simulation, which does not). The simulation
/// depends on this assembly only, so it can be handed a synthetic database in
/// tests without any DK2 install present.
///
/// Rule (docs/HANDOFF.md): a balance constant hardcoded in simulation code is a
/// bug. Read it from here.
/// </summary>
public sealed class GameDatabase
{
    public GameDatabase(
        IEnumerable<TerrainDef> terrain,
        IEnumerable<RoomDef> rooms,
        IEnumerable<CreatureDef> creatures,
        GlobalVariables variables)
    {
        Terrain = terrain.ToDictionary(t => t.TerrainId);
        Rooms = rooms.ToDictionary(r => r.RoomId);
        Creatures = creatures.ToDictionary(c => c.CreatureId);
        Variables = variables;

        // Lowest room id wins a shared terrain id. In the shipped data no two
        // rooms share one — RoomsOnRealDataTests asserts that — so the tie-break
        // never fires; it exists so a data set which broke the assumption would
        // still load deterministically rather than throwing at startup.
        RoomsByTerrain = Rooms.Values
            .GroupBy(r => r.TerrainId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.RoomId).First());
    }

    public IReadOnlyDictionary<byte, TerrainDef> Terrain { get; }
    public IReadOnlyDictionary<byte, RoomDef> Rooms { get; }

    /// <summary>
    /// The room each room-terrain type belongs to.
    ///
    /// Rooms own their terrain in the data — the ROOMS record names a terrain id
    /// and the TERRAIN record flags that type <c>Room</c> — so this is the
    /// reverse of a link the files already draw, not an invention. It is what
    /// lets a rule ask "is the tile I am standing on a treasury?" without
    /// naming terrain id 10.
    /// </summary>
    public IReadOnlyDictionary<byte, RoomDef> RoomsByTerrain { get; }
    public IReadOnlyDictionary<byte, CreatureDef> Creatures { get; }
    public GlobalVariables Variables { get; }

    /// <summary>Global rule value by id — the simulation's only source of balance numbers.</summary>
    public int this[VariableTypeId id] => Variables[id];

    public TerrainDef TerrainById(byte id) => Terrain.TryGetValue(id, out TerrainDef? t)
        ? t
        : throw new KeyNotFoundException($"No TERRAIN definition with id {id}.");

    public RoomDef RoomById(byte id) => Rooms.TryGetValue(id, out RoomDef? r)
        ? r
        : throw new KeyNotFoundException($"No ROOMS definition with id {id}.");

    public RoomDef Room(RoomTypeId id) => RoomById((byte)id);

    /// <summary>The room a room-terrain tile belongs to, or null if that terrain is not a room.</summary>
    public RoomDef? RoomByTerrain(byte terrainId) =>
        RoomsByTerrain.TryGetValue(terrainId, out RoomDef? r) ? r : null;

    public CreatureDef CreatureById(byte id) => Creatures.TryGetValue(id, out CreatureDef? c)
        ? c
        : throw new KeyNotFoundException($"No CREATURES definition with id {id}.");

    /// <summary>Look up a creature by its data-file name, case-insensitively.</summary>
    public CreatureDef? CreatureByName(string name) =>
        Creatures.Values.FirstOrDefault(
            c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Look up a room by its data-file name, case-insensitively.</summary>
    public RoomDef? RoomByName(string name) =>
        Rooms.Values.FirstOrDefault(
            r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

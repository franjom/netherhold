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
    }

    public IReadOnlyDictionary<byte, TerrainDef> Terrain { get; }
    public IReadOnlyDictionary<byte, RoomDef> Rooms { get; }
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

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
        GlobalVariables variables,
        IEnumerable<KeeperSpellDef>? keeperSpells = null)
    {
        Terrain = terrain.ToDictionary(t => t.TerrainId);
        Rooms = rooms.ToDictionary(r => r.RoomId);
        Creatures = creatures.ToDictionary(c => c.CreatureId);
        Variables = variables;
        KeeperSpells = (keeperSpells ?? []).ToDictionary(
            s => s.Name, StringComparer.OrdinalIgnoreCase);

        // Lowest room id wins a shared terrain id. In the shipped data no two
        // rooms share one — RoomsOnRealDataTests asserts that — so the tie-break
        // never fires; it exists so a data set which broke the assumption would
        // still load deterministically rather than throwing at startup.
        RoomsByTerrain = Rooms.Values
            .GroupBy(r => r.TerrainId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.RoomId).First());

        WaterTerrain = Terrain.Values.Where(t => t.IsWater).OrderBy(t => t.TerrainId).FirstOrDefault();
        LavaTerrain = Terrain.Values.Where(t => t.IsLava).OrderBy(t => t.TerrainId).FirstOrDefault();

        // The per-level stat table, flattened at construction so a rule that
        // asks it once a tick does not scan 320 rows to do so. Built here
        // rather than lazily because a database is immutable once made and
        // ForLevel() rebuilds one from scratch, so there is nowhere for a stale
        // copy to hide.
        _statPercent = new byte[StatCount * (MaxCreatureLevel + 1)];
        Array.Fill(_statPercent, (byte)100);
        foreach (CreatureStatEntry e in Variables.CreatureStats)
        {
            // First-person rows describe possession, which is a different
            // scaling table for the same stats, and nothing reads it yet.
            if (e.FirstPerson) continue;
            if (e.StatId < 0 || e.StatId >= StatCount) continue;
            if (e.Level < 1 || e.Level > MaxCreatureLevel) continue;
            if (e.Value < 0 || e.Value > 65535) continue;

            _statPercent[(e.StatId * (MaxCreatureLevel + 1)) + e.Level] =
                (byte)Math.Min(e.Value, 255);
        }

        // A percentage above 255 would be silently clamped by the byte above,
        // so the wide values get their own map and the byte array stays the
        // fast path. ExperienceForNextLevel reaches 1000 at level 10, so this
        // is not hypothetical.
        _wideStatPercent = Variables.CreatureStats
            .Where(e => !e.FirstPerson && e.Value > 255
                        && e.Level >= 1 && e.Level <= MaxCreatureLevel)
            .GroupBy(e => (e.StatId, e.Level))
            .ToDictionary(g => g.Key, g => g.Last().Value);
    }

    /// <summary>How many stat ids the shipped `CreatureStats` table covers.</summary>
    private const int StatCount = 32;

    /// <summary>
    /// The highest creature level the shipped table describes.
    ///
    /// <para>Ten, because the rows stop there. Not a rule about what a creature
    /// may reach by play — <c>TrainingRoomMaxExperienceLevel</c> is 4 and
    /// <c>CombatPitMaxExperienceLevel</c> is 8, so the two rooms cap well below
    /// it — but a hard ceiling on what the data can scale.</para>
    /// </summary>
    public const int MaxCreatureLevel = 10;

    private readonly byte[] _statPercent;
    private readonly Dictionary<(int StatId, int Level), int> _wideStatPercent;

    /// <summary>
    /// What percentage of its level-1 value this stat has at this level.
    ///
    /// <para>100 for level 1, for a stat the table does not scale, and for a
    /// level outside the table — so a caller never has to special-case any of
    /// those, and a stat this project has not named still returns something
    /// harmless rather than zero.</para>
    /// </summary>
    public int StatPercent(CreatureStatId stat, int level)
    {
        int id = (int)stat;
        if (id < 0 || id >= StatCount) return 100;
        if (level <= 1) return 100;
        if (level > MaxCreatureLevel) level = MaxCreatureLevel;

        return _wideStatPercent.TryGetValue((id, level), out int wide)
            ? wide
            : _statPercent[(id * (MaxCreatureLevel + 1)) + level];
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

    /// <summary>
    /// The one terrain type flagged <see cref="TerrainFlags.Water"/>, or null if
    /// this data set has none.
    ///
    /// Found by flag rather than by id, for the same reason every gameplay rule
    /// asks the flags: an id would be a fact about the reference install. The
    /// shipped table has exactly one water and one lava type, which
    /// <c>TerrainOnRealDataTests</c> asserts — a set with two would need a rule
    /// nobody has evidence for, so this deliberately refuses to guess and takes
    /// the lowest id.
    /// </summary>
    public TerrainDef? WaterTerrain { get; }

    /// <summary>The one terrain type flagged <see cref="TerrainFlags.Lava"/>. See <see cref="WaterTerrain"/>.</summary>
    public TerrainDef? LavaTerrain { get; }

    /// <summary>
    /// The keeper spells, by the name the data file gives them.
    ///
    /// <para><b>Keyed by name, unusually.</b> Everything else here is keyed by
    /// the id the record carries; the KEEPERSPELLS record's id field has not
    /// been identified, and inventing an ordinal from record order would break
    /// silently if the shipped order ever differed. The names are the file's own
    /// internal ASCII, not the localised display strings (those live behind a
    /// string id), so they are as stable as an id would be. See Q55.</para>
    ///
    /// <para>Empty on a synthetic database that does not supply spells, which is
    /// why <see cref="KeeperSpell"/> returns null rather than throwing: a caller
    /// that genuinely needs one says so with
    /// <see cref="RequireKeeperSpell"/>.</para>
    /// </summary>
    public IReadOnlyDictionary<string, KeeperSpellDef> KeeperSpells { get; }

    /// <summary>Global rule value by id — the simulation's only source of balance numbers.</summary>
    public int this[VariableTypeId id] => Variables[id];

    /// <summary>
    /// The same definitions with one level's own VARIABLES applied over the
    /// global table — see <see cref="GlobalVariables.MergedWith"/> for what
    /// "applied" means and why it is not a union.
    ///
    /// <para>A new instance rather than a mutation, because a
    /// <see cref="GameDatabase"/> is immutable and a running simulation holds
    /// one: a level's numbers must not leak into another level's run. The
    /// definitions themselves are shared, so this costs a few dictionary
    /// rebuilds over about 130 records and nothing else.</para>
    ///
    /// <para>Returns <c>this</c> when the level carries no variables of its own,
    /// so a caller can hand it whatever the loader found without checking.</para>
    /// </summary>
    public GameDatabase ForLevel(GlobalVariables? levelVariables) =>
        levelVariables is null
            ? this
            : new GameDatabase(
                Terrain.Values,
                Rooms.Values,
                Creatures.Values,
                Variables.MergedWith(levelVariables),
                KeeperSpells.Values);

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

    /// <summary>The named keeper spell, or null if this database has none.</summary>
    public KeeperSpellDef? KeeperSpell(string name) =>
        KeeperSpells.TryGetValue(name, out KeeperSpellDef? s) ? s : null;

    /// <summary>
    /// The named keeper spell, or a throw naming what is missing.
    ///
    /// Deliberately loud, and for the reason recorded in docs/HANDOFF.md: a rule
    /// that starts reading a new piece of the database breaks every synthetic
    /// database that does not supply it, and it should break at the seam with a
    /// name in the message rather than quietly behaving differently in tests
    /// than it does on the real data.
    /// </summary>
    public KeeperSpellDef RequireKeeperSpell(string name) =>
        KeeperSpell(name) ?? throw new KeyNotFoundException(
            $"No KEEPERSPELLS definition named '{name}'. This database carries " +
            $"{KeeperSpells.Count} keeper spells; a synthetic one built for a test " +
            "must supply the spells the rules under test read.");

    /// <summary>Look up a creature by its data-file name, case-insensitively.</summary>
    public CreatureDef? CreatureByName(string name) =>
        Creatures.Values.FirstOrDefault(
            c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Look up a room by its data-file name, case-insensitively.</summary>
    public RoomDef? RoomByName(string name) =>
        Rooms.Values.FirstOrDefault(
            r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

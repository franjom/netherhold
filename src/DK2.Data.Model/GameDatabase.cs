// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Data.Model;

/// <summary>
/// The parsed DK2 static database: every definition the simulation reads and never writes. Loaded
/// once from the original game's KWD files and immutable thereafter.
/// <para>This type is the seam between the data layer (DK2.Data, which does file IO) and the
/// simulation layer (DK2.Simulation, which does not). The simulation depends on this assembly
/// only, so a test can hand it a synthetic database with no DK2 install present.</para>
/// <para>Rule (docs/HANDOFF.md): a balance constant hardcoded in simulation code is a bug — read
/// it from here.</para>
/// </summary>
public sealed class GameDatabase
{
    public GameDatabase(
        IEnumerable<TerrainDef> terrain,
        IEnumerable<RoomDef> rooms,
        IEnumerable<CreatureDef> creatures,
        GlobalVariables variables,
        IEnumerable<KeeperSpellDef>? keeperSpells = null,
        IEnumerable<ObjectDef>? objects = null,
        IEnumerable<DoorDef>? doors = null,
        IEnumerable<TrapDef>? traps = null,
        IEnumerable<CreatureSpellDef>? creatureSpells = null,
        IEnumerable<ShotDef>? shots = null,
        IEnumerable<EffectDef>? effects = null,
        IEnumerable<EffectElementDef>? effectElements = null,
        LevelDef? level = null,
        IReadOnlyDictionary<int, int>? cameraPathFrames = null)
    {
        Terrain = terrain.ToDictionary(t => t.TerrainId);
        Rooms = rooms.ToDictionary(r => (RoomTypeId)r.RoomId);
        Creatures = creatures.ToDictionary(c => c.CreatureId);
        Variables = variables;
        Level = level;
        CameraPathFrames = cameraPathFrames ?? new Dictionary<int, int>();
        KeeperSpells = (keeperSpells ?? []).ToDictionary(
            s => s.Name, StringComparer.OrdinalIgnoreCase);

        // Keyed by id because that is how every other record refers to one: a
        // creature names its bed by id, a room names its furniture by id.
        Objects = (objects ?? []).ToDictionary(o => o.ObjectId);
        Doors = (doors ?? []).ToDictionary(d => d.DoorId);
        Traps = (traps ?? []).ToDictionary(t => t.TrapId);
        CreatureSpells = (creatureSpells ?? []).ToDictionary(s => s.CreatureSpellId);
        Shots = (shots ?? []).ToDictionary(s => s.ShotId);
        Effects = (effects ?? []).ToDictionary(e => e.EffectId);
        EffectElements = (effectElements ?? []).ToDictionary(e => e.EffectElementId);

        // Lowest room id wins a shared terrain id. No two shipped rooms share one
        // (RoomsOnRealDataTests.EveryRoomOwnsItsOwnTerrain), so the tie-break never fires; it
        // exists so a data set that broke the assumption would still load deterministically.
        RoomsByTerrain = Rooms.Values
            .GroupBy(r => r.TerrainId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.RoomId).First());

        WaterTerrain = Terrain.Values.Where(t => t.IsWater).OrderBy(t => t.TerrainId).FirstOrDefault();
        LavaTerrain = Terrain.Values.Where(t => t.IsLava).OrderBy(t => t.TerrainId).FirstOrDefault();

        // The per-level stat table, flattened at construction so a rule asking it once a tick
        // does not scan 320 rows. Built eagerly rather than lazily because a database is
        // immutable and ForLevel() rebuilds one, so there is nowhere for a stale copy to hide.
        _statPercent = new byte[StatCount * (MaxCreatureLevel + 1)];
        Array.Fill(_statPercent, (byte)100);
        foreach (CreatureStatEntry e in Variables.CreatureStats)
        {
            // First-person rows use their separate signed-word table.
            if (e.FirstPerson) continue;
            if (e.StatId < 0 || e.StatId >= StatCount) continue;
            if (e.Level < 1 || e.Level > MaxCreatureLevel) continue;
            if (e.Value < 0 || e.Value > 65535) continue;

            _statPercent[(e.StatId * (MaxCreatureLevel + 1)) + e.Level] =
                (byte)Math.Min(e.Value, 255);
        }

        // A percentage above 255 would be silently clamped by the byte array, so wide values get
        // their own map and the byte array stays the fast path. ExperienceForNextLevel reaches
        // 1000 at level 10, so this is not hypothetical.
        _wideStatPercent = Variables.CreatureStats
            .Where(e => !e.FirstPerson && e.Value > 255
                        && e.Level >= 1 && e.Level <= MaxCreatureLevel)
            .GroupBy(e => (e.StatId, e.Level))
            .ToDictionary(g => g.Key, g => g.Last().Value);
    }

    /// <summary>How many stat ids the shipped `CreatureStats` table covers.</summary>
    private const int StatCount = 32;

    /// <summary>The highest creature level the shipped table describes: ten, because the rows stop
    /// there. Not a rule about what play may reach — <c>TrainingRoomMaxExperienceLevel</c> is 4 and
    /// <c>CombatPitMaxExperienceLevel</c> is 8, capping well below it — but a hard ceiling on what
    /// the data can scale.</summary>
    public const int MaxCreatureLevel = 10;

    private readonly byte[] _statPercent;
    private readonly Dictionary<(int StatId, int Level), int> _wideStatPercent;

    /// <summary>What percentage of its level-1 value this stat has at this level. 100 for level 1,
    /// for a stat the table does not scale, and for a level outside the table, so a caller never
    /// special-cases any of those and a stat this project has not named returns something harmless
    /// rather than zero.</summary>
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

    /// <summary>The first-person signed-word table: level is ignored and the last row wins.
    /// Unspecified entries retain the world initializer's 100.</summary>
    public int FirstPersonStat(CreatureStatId stat)
    {
        int id = (int)stat;
        if (id < 0 || id >= StatCount) return 100;
        int value = 100;
        foreach (CreatureStatEntry entry in Variables.CreatureStats)
            if (entry.FirstPerson && entry.StatId == id) value = unchecked((short)entry.Value);
        return value;
    }

    public IReadOnlyDictionary<byte, TerrainDef> Terrain { get; }
    public IReadOnlyDictionary<RoomTypeId, RoomDef> Rooms { get; }

    /// <summary>The room each room-terrain type belongs to. Rooms own their terrain in the data —
    /// the ROOMS record names a terrain id and the TERRAIN record flags that type <c>Room</c> — so
    /// this reverses a link the files already draw. It lets a rule ask "is the tile I am standing
    /// on a treasury?" without naming terrain id 10.</summary>
    public IReadOnlyDictionary<byte, RoomDef> RoomsByTerrain { get; }
    public IReadOnlyDictionary<byte, CreatureDef> Creatures { get; }

    /// <summary>The 145 objects, by id — beds, chickens, gold piles, tombstones, specials and the
    /// room furniture. Empty rather than absent when a data set carries no OBJECTS chunk, so every
    /// rule that asks for an object has to cope with not finding one: the fixture databases in the
    /// test suite do exactly that.</summary>
    public IReadOnlyDictionary<byte, ObjectDef> Objects { get; }

    /// <summary>The eight door definitions, keyed by their data-file id.</summary>
    public IReadOnlyDictionary<byte, DoorDef> Doors { get; }

    /// <summary>The sixteen trap definitions, keyed by their data-file id.</summary>
    public IReadOnlyDictionary<byte, TrapDef> Traps { get; }

    /// <summary>The forty-one creature-spell definitions, keyed by file id.</summary>
    public IReadOnlyDictionary<byte, CreatureSpellDef> CreatureSpells { get; }

    /// <summary>The sixty-five projectile definitions, keyed by file id.</summary>
    public IReadOnlyDictionary<byte, ShotDef> Shots { get; }

    /// <summary>The effect definitions, keyed by file id.</summary>
    public IReadOnlyDictionary<ushort, EffectDef> Effects { get; }

    /// <summary>The effect-element definitions, keyed by file id.</summary>
    public IReadOnlyDictionary<ushort, EffectElementDef> EffectElements { get; }

    /// <summary>The object with this id, or null. Id 0 means "none" throughout the data.</summary>
    public ObjectDef? ObjectById(byte id) =>
        Objects.TryGetValue(id, out ObjectDef? o) ? o : null;

    public GlobalVariables Variables { get; }

    /// <summary>The active level's metadata, or null when no level was supplied.</summary>
    public LevelDef? Level { get; }

    /// <summary>Frame count of each camera path, by the number in its <c>EnginePath%d.kcs</c>
    /// name (Paths.WAD; camera-paths.md). Empty when no install was read: every path then
    /// fails to load and action 31 is refused, as <c>0x0044A370</c> does for a missing file.</summary>
    public IReadOnlyDictionary<int, int> CameraPathFrames { get; }

    /// <summary>The one terrain type flagged <see cref="TerrainFlags.Water"/>, or null if this data
    /// set has none. Found by flag rather than by id, for the reason every gameplay rule asks the
    /// flags: an id would be a fact about the reference install. The shipped table has exactly one
    /// water and one lava type
    /// (<c>BridgesOnRealDataTests.TheInstallHasExactlyOneWaterAndOneLavaTerrain</c>); a set with
    /// two would need a rule nobody has evidence for, so this takes the lowest id.</summary>
    public TerrainDef? WaterTerrain { get; }

    /// <summary>The one terrain type flagged <see cref="TerrainFlags.Lava"/>. See <see cref="WaterTerrain"/>.</summary>
    public TerrainDef? LavaTerrain { get; }

    /// <summary>The keeper spells, by the name the data file gives them.
    /// <para><b>Keyed by name, unusually.</b> Everything else here is keyed by the id the record
    /// carries; the KEEPERSPELLS record's id field has not been identified, and inventing an
    /// ordinal from record order would break silently if the shipped order ever differed. The names
    /// are the file's own internal ASCII, not the localised display strings (those live behind a
    /// string id), so they are as stable as an id would be. Q55.</para>
    /// <para>Empty on a synthetic database that supplies no spells, which is why
    /// <see cref="KeeperSpell"/> returns null rather than throwing: a caller that genuinely needs
    /// one says so with <see cref="RequireKeeperSpell"/>.</para></summary>
    public IReadOnlyDictionary<string, KeeperSpellDef> KeeperSpells { get; }

    /// <summary>Global rule value by id — the simulation's only source of balance numbers.</summary>
    public int this[VariableTypeId id] => Variables[id];

    /// <summary>Applies level VARIABLES and metadata without mutating this database.
    /// Missing variables retain the current table; missing metadata retains the current level.
    /// Returns this instance when neither is supplied. See <see cref="GlobalVariables.MergedWith"/>.</summary>
    /// <remarks><b>Every record collection has to be passed through here.</b> This rebuilds the
    /// database from parts, so a collection added to the constructor and forgotten here is silently
    /// absent from every campaign level while present everywhere else — which happened to
    /// <see cref="Objects"/>. <c>ObjectsOnRealDataTests.ForLevelKeepsEveryRecordCollection</c>
    /// exists so the next record cannot repeat it.</remarks>
    public GameDatabase ForLevel(GlobalVariables? levelVariables, LevelDef? level = null) =>
        levelVariables is null && level is null
            ? this
            : new GameDatabase(
                Terrain.Values,
                Rooms.Values,
                WithFieldOverrides(Creatures.Values, levelVariables is null ? null : Variables.MergedWith(levelVariables)),
                levelVariables is null ? Variables : Variables.MergedWith(levelVariables),
                KeeperSpells.Values,
                Objects.Values,
                Doors.Values,
                Traps.Values,
                CreatureSpells.Values,
                Shots.Values,
                Effects.Values,
                EffectElements.Values,
                level ?? Level,
                CameraPathFrames);

    /// <summary>The level's VARIABLES id 77 records, in file order (<c>0x0051DD10</c> then
    /// <c>0x00494A70</c>): each stores its value into one definition field of one creature type,
    /// a later record for the same field winning. Mapped are the selectors the shipped levels use:
    /// 1 Health, 2 Fear, 3 Threat, 4 MeleeDamage (words, as the loader stores them), 21 and 22 the
    /// speeds in the file's units, which the store divides by variable 19 as the loader does (but
    /// without the loader's minimum of 1, a difference no shipped value reaches), and 30 the hearing
    /// distance (<c>+0x77F</c>) in whole tiles, stored raw; any other selector throws. A record naming a type with no definition is skipped, as
    /// the handler's lookup fails.</summary>
    public static IEnumerable<CreatureDef> WithFieldOverrides(IEnumerable<CreatureDef> creatures, GlobalVariables? level)
    {
        if (level is null || level.CreatureFieldOverrides.Count == 0) return creatures;
        var byId = creatures.ToDictionary(c => c.CreatureId);
        foreach (CreatureFieldOverrideEntry e in level.CreatureFieldOverrides)
        {
            if (e.CreatureId is < 0 or > byte.MaxValue || !byId.TryGetValue((byte)e.CreatureId, out CreatureDef? c)) continue;
            byId[c.CreatureId] = (CreatureStatId)e.Selector switch
            {
                CreatureStatId.Health => c with { Hp = (ushort)e.Value },
                CreatureStatId.Fear => c with { Fear = (ushort)e.Value },
                CreatureStatId.Threat => c with { Threat = (ushort)e.Value },
                CreatureStatId.MeleeDamage => c with { MeleeDamage = (ushort)e.Value },
                (CreatureStatId)21 => c with { Speed = new Fp32(e.Value) },
                (CreatureStatId)22 => c with { RunSpeed = new Fp32(e.Value) },
                // A raw dword in whole tiles: the loader itself rounds the file's 12.12 value to tiles.
                (CreatureStatId)30 => c with { DistanceCanHear = new Fp32(e.Value << 12) },
                _ => throw new InvalidDataException($"VARIABLES id 77 selector {e.Selector} is not mapped (creature {e.CreatureId})."),
            };
        }
        return byId.Values;
    }

    public TerrainDef TerrainById(byte id) => Terrain.TryGetValue(id, out TerrainDef? t)
        ? t
        : throw new KeyNotFoundException($"No TERRAIN definition with id {id}.");

    public RoomDef RoomById(byte id) => Room((RoomTypeId)id);

    public RoomDef Room(RoomTypeId id) => Rooms.TryGetValue(id, out RoomDef? r)
        ? r
        : throw new KeyNotFoundException($"No ROOMS definition with id {(byte)id}.");

    /// <summary>The room a room-terrain tile belongs to, or null if that terrain is not a room.</summary>
    public RoomDef? RoomByTerrain(byte terrainId) =>
        RoomsByTerrain.TryGetValue(terrainId, out RoomDef? r) ? r : null;

    public CreatureDef CreatureById(byte id) => Creatures.TryGetValue(id, out CreatureDef? c)
        ? c
        : throw new KeyNotFoundException($"No CREATURES definition with id {id}.");

    /// <summary>The named keeper spell, or null if this database has none.</summary>
    public KeeperSpellDef? KeeperSpell(string name) =>
        KeeperSpells.TryGetValue(name, out KeeperSpellDef? s) ? s : null;

    /// <summary>The keeper spell with this id (<see cref="KeeperSpellDef.KeeperSpellId"/>), or
    /// null — the type id availability rows and research entries use.</summary>
    public KeeperSpellDef? KeeperSpellById(int id)
    {
        foreach (KeeperSpellDef spell in KeeperSpells.Values)
            if (spell.KeeperSpellId == id) return spell;
        return null;
    }

    /// <summary>The named keeper spell, or a throw naming what is missing. Deliberately loud
    /// (docs/HANDOFF.md): a rule that starts reading a new piece of the database breaks every
    /// synthetic database that does not supply it, and it should break at the seam with a name in
    /// the message rather than behaving differently in tests than on the real data.</summary>
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

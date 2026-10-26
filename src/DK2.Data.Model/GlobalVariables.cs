// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>One typed VARIABLES cell, before table merging or duplicate-id folding.</summary>
public abstract record KwdVariableEntry;

/// <summary>Creature availability pool entry (variable id 1).</summary>
public sealed record CreaturePoolEntry(int CreatureId, int Value, int PlayerId) : KwdVariableEntry;

/// <summary>Room/creature/trap/door/spell availability entry (variable id 2).</summary>
public sealed record AvailabilityEntry(ushort Kind, ushort PlayerId, int TypeId, int Value) : KwdVariableEntry;

/// <summary>Temple sacrifice recipe (variable id 43).</summary>
public sealed record SacrificeEntry(
    byte Type1, byte Id1, byte Type2, byte Id2, byte Type3, byte Id3,
    byte RewardType, byte SpeechId, int RewardValue) : KwdVariableEntry;

/// <summary>Per-creature, per-level stat override (variable ids 65 / 224).</summary>
public sealed record CreatureStatEntry(int StatId, int Value, int Level, bool FirstPerson) : KwdVariableEntry;

/// <summary>One entry of the research list (variable id 17), in the order research takes
/// them. <paramref name="Kind"/> is a research kind — 1 room, 2 door, 3 trap, 4 spell —
/// which is <b>not</b> an availability kind (docs/research/research-tree.md §1).
/// <paramref name="Cost"/> is research points; every shipped entry costs 1000.</summary>
public sealed record ResearchEntry(int Kind, int TypeId, int Cost) : KwdVariableEntry;

/// <summary>Id 77: the level sets one definition field of one creature type (<c>0x0051DD10</c>
/// looks the type up, <c>0x00494A70</c> stores <see cref="Value"/> into the field
/// <see cref="Selector"/> names, a <see cref="CreatureStatId"/>). Level 1 gives its Lord of the Land
/// Threat 125 and MeleeDamage 200.</summary>
public sealed record CreatureFieldOverrideEntry(int CreatureId, int Selector, int Value) : KwdVariableEntry;

/// <summary>Id 66: players <see cref="PlayerId"/> and <see cref="OtherPlayerId"/> start the level
/// allied. <c>0x0051DC90</c> writes 1 into both players' alliance bytes (<c>player+0x1AE</c>) at
/// each other's number; no record can break one (variables-corpus-sweep.md §Q84).</summary>
public sealed record AllianceEntry(int PlayerId, int OtherPlayerId) : KwdVariableEntry;

/// <summary>Plain global rule value — the vast majority of VARIABLES records.</summary>
public sealed record MiscVariable(VariableTypeId Id, int Value, int Unk1, int Unk2) : KwdVariableEntry;

/// <summary>
/// DK2's global game-rule table, as decoded from VARIABLES(230) records.
///
/// This is the single source of every balance number in the simulation. A
/// gameplay constant hardcoded in simulation code is a bug — read it from here.
/// See docs/research/kwd-variables.md.
/// </summary>
public sealed class GlobalVariables
{
    public List<CreaturePoolEntry> CreaturePool { get; } = [];
    public List<AvailabilityEntry> Availabilities { get; } = [];
    public List<SacrificeEntry> Sacrifices { get; } = [];
    public List<CreatureStatEntry> CreatureStats { get; } = [];

    /// <summary>Id 77 records in file order, global then level: each applied in turn, so a later
    /// record for the same field wins, as the handler's stores do.</summary>
    public List<CreatureFieldOverrideEntry> CreatureFieldOverrides { get; } = [];

    /// <summary>Id 66 records, global then level. Each only sets a pair, so order is immaterial.</summary>
    public List<AllianceEntry> Alliances { get; } = [];

    /// <summary>The research list, in file order: 29 entries in the shipped
    /// <c>GlobalVariables.kwd</c> and none in any level file.</summary>
    public List<ResearchEntry> Research { get; } = [];

    public Dictionary<VariableTypeId, MiscVariable> Misc { get; } = [];

    /// <summary>The value of a global rule variable. Throws if the data does not carry it.</summary>
    public int this[VariableTypeId id] => Misc.TryGetValue(id, out MiscVariable? v)
        ? v.Value
        : throw new KeyNotFoundException($"VARIABLES table carries no entry for {id} ({(int)id}).");

    public bool TryGet(VariableTypeId id, out int value)
    {
        if (Misc.TryGetValue(id, out MiscVariable? v)) { value = v.Value; return true; }
        value = 0;
        return false;
    }

    /// <summary>This table with one level's own VARIABLES applied on top.
    /// <para>Every shipped level carries its own <c>&lt;name&gt;Variables.kld</c>, and 26 ids are
    /// overridden by at least one: <c>MinimumImpThreshold</c> is 0 on four secret levels and the
    /// front end (ADR-0021), and level 1 doubles both payday variables.</para>
    /// <para>Plain values replace by id and new ids are added. Availability rows accumulate, the
    /// level's after the global ones (<see cref="AvailabilitiesOf"/>, ADR-0281). The other four
    /// lists replace wholesale when the level provides any and are kept when it does not:
    /// STRONGLY INFERRED. ADR-0040, Q82, docs/research/kwd-variables.md "The merge rules".</para>
    /// </summary>
    /// <summary>One player's availability table as DK2 leaves it after loading: every row for
    /// player 0 or <paramref name="playerId"/>, in file order, the last write to a kind and type
    /// winning (<c>0x0051CA60</c>). Ordered by kind, then type.</summary>
    public IEnumerable<AvailabilityEntry> AvailabilitiesOf(ushort playerId)
    {
        var table = new Dictionary<(ushort Kind, int TypeId), AvailabilityEntry>();
        foreach (AvailabilityEntry row in Availabilities)
        {
            if (row.PlayerId != 0 && row.PlayerId != playerId) continue;
            table[(row.Kind, row.TypeId)] = row with { PlayerId = playerId };
        }
        return table.Values.OrderBy(a => a.Kind).ThenBy(a => a.TypeId);
    }

    public GlobalVariables MergedWith(GlobalVariables? level)
    {
        var merged = new GlobalVariables();

        foreach (KeyValuePair<VariableTypeId, MiscVariable> entry in Misc)
            merged.Misc[entry.Key] = entry.Value;

        merged.CreaturePool.AddRange(CreaturePool);
        merged.Availabilities.AddRange(Availabilities);
        merged.Sacrifices.AddRange(Sacrifices);
        merged.CreatureStats.AddRange(CreatureStats);
        merged.Research.AddRange(Research);
        merged.CreatureFieldOverrides.AddRange(CreatureFieldOverrides);
        merged.Alliances.AddRange(Alliances);

        if (level is null) return merged;

        foreach (KeyValuePair<VariableTypeId, MiscVariable> entry in level.Misc)
            merged.Misc[entry.Key] = entry.Value;

        Replace(merged.CreaturePool, level.CreaturePool);
        merged.Availabilities.AddRange(level.Availabilities);
        merged.CreatureFieldOverrides.AddRange(level.CreatureFieldOverrides);
        merged.Alliances.AddRange(level.Alliances);
        Replace(merged.Sacrifices, level.Sacrifices);
        Replace(merged.CreatureStats, level.CreatureStats);
        // Untested for research: no shipped level carries a list, and DK2's handler only
        // appends, so how a level's list would combine with the global one is not settled.
        Replace(merged.Research, level.Research);

        return merged;

        static void Replace<T>(List<T> target, List<T> from)
        {
            if (from.Count == 0) return;
            target.Clear();
            target.AddRange(from);
        }
    }
}

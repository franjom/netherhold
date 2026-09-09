// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>Creature availability pool entry (variable id 1).</summary>
public sealed record CreaturePoolEntry(int CreatureId, int Value, int PlayerId);

/// <summary>Room/creature/trap/door/spell availability entry (variable id 2).</summary>
public sealed record AvailabilityEntry(ushort Kind, ushort PlayerId, int TypeId, int Value);

/// <summary>Temple sacrifice recipe (variable id 43).</summary>
public sealed record SacrificeEntry(
    byte Type1, byte Id1, byte Type2, byte Id2, byte Type3, byte Id3,
    byte RewardType, byte SpeechId, int RewardValue);

/// <summary>Per-creature, per-level stat override (variable ids 65 / 224).</summary>
public sealed record CreatureStatEntry(int StatId, int Value, int Level, bool FirstPerson);

/// <summary>Plain global rule value — the vast majority of VARIABLES records.</summary>
public sealed record MiscVariable(VariableTypeId Id, int Value, int Unk1, int Unk2);

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
    /// <para>Every shipped level carries its own <c>&lt;name&gt;Variables.kld</c> and <b>26 variable
    /// ids are overridden by at least one of them</b>, so reading only <c>GlobalVariables.kwd</c>
    /// plays a level with the wrong numbers: <c>MinimumImpThreshold</c> is <b>0</b> on four of the
    /// five secret levels and on the front end, where the global table hands out four free imps
    /// (ADR-0021), and level 1 doubles both payday variables, 600 to 1200.</para>
    /// <para><b>Two merge rules, because the file holds two kinds of thing.</b> Plain values
    /// replace by id and new ids are added — levels introduce <c>GameTicks</c>, <c>LevelRating</c>,
    /// <c>AverageTime</c> and two unnamed ids. The four lists replace wholesale when the level
    /// provides any and are kept when it does not: STRONGLY INFERRED, the global availability list
    /// being a player-0 template that a level's own players' lists cannot be unioned with.
    /// ADR-0040, Q82 and docs/research/kwd-variables.md "The merge rules".</para></summary>
    public GlobalVariables MergedWith(GlobalVariables? level)
    {
        var merged = new GlobalVariables();

        foreach (KeyValuePair<VariableTypeId, MiscVariable> entry in Misc)
            merged.Misc[entry.Key] = entry.Value;

        merged.CreaturePool.AddRange(CreaturePool);
        merged.Availabilities.AddRange(Availabilities);
        merged.Sacrifices.AddRange(Sacrifices);
        merged.CreatureStats.AddRange(CreatureStats);

        if (level is null) return merged;

        foreach (KeyValuePair<VariableTypeId, MiscVariable> entry in level.Misc)
            merged.Misc[entry.Key] = entry.Value;

        Replace(merged.CreaturePool, level.CreaturePool);
        Replace(merged.Availabilities, level.Availabilities);
        Replace(merged.Sacrifices, level.Sacrifices);
        Replace(merged.CreatureStats, level.CreatureStats);

        return merged;

        static void Replace<T>(List<T> target, List<T> from)
        {
            if (from.Count == 0) return;
            target.Clear();
            target.AddRange(from);
        }
    }
}

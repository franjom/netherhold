// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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

    /// <summary>
    /// This table with one level's own VARIABLES applied on top.
    ///
    /// <para><b>Every shipped level carries its own <c>&lt;name&gt;Variables.kld</c>,
    /// and 26 distinct variable ids are overridden by at least one of them.</b>
    /// Reading only <c>GlobalVariables.kwd</c> plays every level with the wrong
    /// numbers wherever it disagrees — and it disagrees about things that are
    /// already built: <c>MinimumImpThreshold</c> is <b>0</b> on four of the five
    /// secret levels and on the front end, so a keeper there gets no free imps
    /// at all, where the global table would hand out four (ADR-0021). Level 1
    /// doubles both payday variables, 600 to 1200.</para>
    ///
    /// <para><b>Two different merge rules, because the file holds two different
    /// kinds of thing.</b></para>
    ///
    /// <list type="number">
    /// <item><b>Plain values replace by id, and new ids are added.</b> A level's
    /// entry wins; a global the level is silent about is kept. That is what an
    /// override file is for, and it is the only reading that makes the secret
    /// levels' <c>MinimumImpThreshold</c> 0 mean anything. Levels also introduce
    /// ids the global table lacks at all — <c>GameTicks</c>,
    /// <c>LevelRating</c>, <c>AverageTime</c> and two unnamed ones.</item>
    /// <item><b>The four lists are replaced wholesale when the level provides
    /// any, and kept when it does not.</b> STRONGLY INFERRED, and the shipped
    /// data argues for it rather than merely permitting it: the global
    /// availability list is a **template keyed to player 0** (rooms AVAILABLE,
    /// traps/doors/spells RESEARCHABLE), while a level's list names **its own
    /// players** and its own states — level 1 gives its keeper three room
    /// entries, level 20 gives eleven. Unioning them would leave a level 1
    /// keeper holding the global template's "every room available" alongside its
    /// own "treasury unavailable", which is not a state the game can be
    /// in. Conversely level 1 ships no sacrifice recipes and no creature stat
    /// rows, and the temple's 44 recipes and the 320 stat rows are plainly
    /// game-wide, so silence there must mean "keep".</item>
    /// </list>
    ///
    /// <para>The one case this cannot distinguish is a level that means to *add*
    /// a single availability entry to the global set. No shipped level looks like
    /// that — every level that carries any availability carries a full set across
    /// the same five kinds — so the question does not arise on this data. See
    /// Q82.</para>
    /// </summary>
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

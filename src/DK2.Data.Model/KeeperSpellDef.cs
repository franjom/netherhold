// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>Keeper-spell costs, normal/upgraded shots, cooldown and validation selectors.
/// Loader provenance: research-tree.md §5 and keeper-spell-thunderbolt.md §1.
/// Fields outside those reads remain unparsed.</summary>
public sealed record KeeperSpellDef
{
    /// <summary>The spell's name in the data file's own words, e.g. "Create Imp".</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Mana to cast, once. CONFIRMED: the owner measured Create Imp's 1500 in
    /// the original without reference to the file, and this field holds exactly
    /// that.
    /// </summary>
    public required int ManaCost { get; init; }

    /// <summary>Mana per second the spell's result costs its keeper for as long as it exists, or
    /// zero for a spell whose effect is instantaneous.
    /// <para><b>This is what the red figure under the mana orb is made of.</b> CONFIRMED (owner,
    /// 2026-08-30) from two screenshots: four imps drain 28 a second and five drain 35, and Create
    /// Imp's value here is <b>7</b>. Non-zero for exactly the five spells whose effect persists —
    /// Create Imp 7, Summon Horny 2000, Call To Arms 90, Possession 25, OneShotReaper 125 — and
    /// zero for all eight instants, which is the shape a per-second upkeep has and a cast cost
    /// does not.</para>
    /// <para>The upkeep belongs to the <i>spell</i>, not to the creature it makes, which is why a
    /// long search of the CREATURES record for the Imp's 7 found nothing
    /// (docs/research/imps-and-mana.md).</para></summary>
    public required int ManaUpkeepPerSecond { get; init; }

    /// <summary>The spell's id, file <c>0xEE</c> (runtime <c>+0x75</c>): the type id its
    /// availability rows and research entries use. Create Imp is 4.</summary>
    public byte KeeperSpellId { get; init; }

    public int CooldownRaw { get; init; }
    public byte LocationSelector { get; init; }
    public byte TargetSelector { get; init; }

    /// <summary>The shot a cast fires, file <c>0xF0</c> (runtime <c>+0x42</c>).</summary>
    public byte ShotTypeId { get; init; }

    /// <summary>The shot's first datum, file <c>0xD0</c>. For Create Imp, the creature type.</summary>
    public int ShotData1 { get; init; }

    /// <summary>The shot's second datum, file <c>0xD4</c>. For Create Imp, the level.</summary>
    public int ShotData2 { get; init; }

    /// <summary>The shot an <b>upgraded</b> cast fires instead, file <c>0x113</c> (runtime
    /// <c>+0x4B</c>). Zero means the spell has no upgrade: research never targets it for one.</summary>
    public byte BonusShotTypeId { get; init; }

    /// <summary>The bonus shot's first datum, file <c>0x114</c>.</summary>
    public int BonusShotData1 { get; init; }

    /// <summary>The bonus shot's second datum, file <c>0x118</c>. Create Imp's is 4: an upgraded
    /// Create Imp makes a level-4 imp (owner, 2026-09-28).</summary>
    public int BonusShotData2 { get; init; }

    /// <summary>Whether research can upgrade this spell — the chooser's test of runtime
    /// <c>+0x4B</c> (<c>0x004C3000</c>). False for Summon Horny and OneShotReaper.</summary>
    public bool CanBeUpgraded => BonusShotTypeId != 0;

    /// <summary>The shot a cast fires and its data: the bonus ones when the caster's row is
    /// upgraded (<c>0x004BBD30</c>).</summary>
    public (byte ShotTypeId, int Data1, int Data2) Shot(bool upgraded) => upgraded
        ? (BonusShotTypeId, BonusShotData1, BonusShotData2)
        : (ShotTypeId, ShotData1, ShotData2);
}

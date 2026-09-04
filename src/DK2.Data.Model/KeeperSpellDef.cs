// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// One keeper spell from KEEPERSPELLS(?), the spells a keeper casts with their
/// own hand rather than through a creature.
///
/// <para><b>Deliberately partial.</b> Only three fields of a 406-byte record are
/// read, because only three are understood: the name, what the spell costs to
/// cast, and what its result costs to keep. The rest arrives with spells proper
/// at M7. Parsing a record badly is worse than not parsing it, so the unknown
/// bytes are left alone rather than guessed at.</para>
/// </summary>
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

    /// <summary>
    /// Mana per second the spell's result costs its keeper for as long as it
    /// exists, or zero for a spell whose effect is instantaneous.
    ///
    /// <para><b>This is what the red figure under the mana orb is made of.</b>
    /// CONFIRMED (2026-08-30) by the owner from two screenshots: four imps drain
    /// 28 a second and five drain 35, and Create Imp's value here is <b>7</b>.
    /// Non-zero for exactly the five spells whose effect persists — Create Imp
    /// 7, Summon Horny 2000, Call To Arms 90, Possession 25, OneShotReaper 125 —
    /// and zero for all eight instants, which is the shape a per-second upkeep
    /// has and a cast cost does not.</para>
    ///
    /// <para>See docs/research/imps-and-mana.md. The upkeep belongs to the
    /// <i>spell</i> and not to the creature it makes, which is why a long search
    /// of the CREATURES record for the Imp's 7 found nothing.</para>
    /// </summary>
    public required int ManaUpkeepPerSecond { get; init; }
}

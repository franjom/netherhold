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

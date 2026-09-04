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
/// The keeper spells' names as the shipped data spells them.
///
/// <para>These exist so that no rule contains a bare string literal, the same
/// way <see cref="RoomTypeId"/> exists so that no rule contains a bare terrain
/// id. They are the data file's internal ASCII names, not the localised display
/// text, and the record's own id field has not been identified yet — see the
/// note on <see cref="GameDatabase.KeeperSpells"/> and Q55.</para>
///
/// <para>Only the spells something reads are listed. The other nine arrive with
/// spells proper at M7.</para>
/// </summary>
public static class KeeperSpellName
{
    /// <summary>Buys an imp, and every imp alive charges its keeper upkeep.</summary>
    public const string CreateImp = "Create Imp";

    /// <summary>Summons a Horned Reaper, the most expensive thing a keeper can keep.</summary>
    public const string SummonHorny = "Summon Horny";

    /// <summary>Take a creature over in first person.</summary>
    public const string Possession = "Possession";
}

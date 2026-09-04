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

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Casts Create Imp: buy one imp at the dungeon heart for mana.
///
/// The third real player command, and <b>the first thing in this simulation that
/// spends mana</b>. Until now mana was income with no outgoings, which every
/// scenario run said out loud; a keeper can now turn it into workers.
///
/// <para>CONFIRMED that the spell exists, that it costs 1500 for the first and
/// rises by 1500 each time (owner, 2026-08-30), and that 1500 is the number on
/// DK2's own <c>Create Imp</c> record. What is HYPOTHESIS is the exact form of
/// the rise — see <see cref="ImpSupplyRules.CreateImpCost"/> and Q54.</para>
///
/// <para>Deliberately a bare command with no target. In the original a keeper
/// casts it onto a tile and the imp appears there; here it appears at the
/// dungeon heart, because where a cast lands is a UI question and because a
/// keeper with no heart has already lost (Q40). When keeper spells arrive at M7
/// this becomes one case of a general spell command rather than its own type.</para>
/// </summary>
public sealed record CreateImpCommand(byte PlayerId) : PlayerCommand(PlayerId);

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// DK2's reserved player ids. <b>Not every player is a keeper</b>, and until
/// this was worked out the project quietly assumed they were.
///
/// <para><b>CONFIRMED, from three independent directions.</b> The shipped
/// campaign's own data says it: across all 66 levels, <b>every dungeon-heart
/// tile belongs to a player between 3 and 7</b> and never to 0, 1 or 2, and
/// <b>every creature pool and every creature-availability entry names a player
/// between 3 and 7</b> and never below. Level 1 shows all four ids at once — its
/// hero gate is player 1, its unclaimed portal is player 2, its keeper's dungeon
/// heart is player 3, and unmined rock is player 0. And OpenKeeper's
/// <c>Player</c> constants, arrived at from the executable rather than from the
/// maps, are the same four numbers with the same meanings. All of it is asserted
/// in <c>PortalOnRealDataTests.TheReservedPlayerIdsAreWhatTheCampaignSays</c>.</para>
///
/// <para><b>Why it matters beyond bookkeeping.</b> A rule that loops over
/// players and treats each as a keeper is wrong four ways: it would run an
/// unclaimed portal's clock (<see cref="Simulation.Systems.PortalSystem"/>), pay
/// a payday to nobody, and hand mana or free imps to the neutral player. The
/// ids are also what makes "unclaimed" expressible at all — a neutral portal is
/// a portal owned by <see cref="Neutral"/>, not a portal owned by no one.</para>
///
/// <para><b>What this does not say.</b> That <see cref="Good"/> is one player
/// rather than a faction, or how the hero player's own structures behave.
/// Four shipped portals belong to player 1 and eleven hero portals belong to
/// player 2, and since neither id ever carries a creature pool, nothing comes
/// out of any of them here. What the original does with them is UNKNOWN and is
/// almost certainly level scripting rather than the portal clock (Q91).</para>
/// </summary>
public static class PlayerIds
{
    /// <summary>Terrain that belongs to nobody — unmined rock, undug dirt.</summary>
    public const byte Unowned = 0;

    /// <summary>The hero side. Hero gates and four shipped portals carry this.</summary>
    public const byte Good = 1;

    /// <summary>
    /// Neutral. This is what an <b>unclaimed room</b> belongs to: 167 of the
    /// campaign's portals and all eleven of its hero portals sit here until
    /// somebody takes them.
    /// </summary>
    public const byte Neutral = 2;

    /// <summary>
    /// The first keeper, and the lowest id that is one. Every shipped level has
    /// a player 3 with a dungeon heart; rival keepers run 4 to 7.
    /// </summary>
    public const byte FirstKeeper = 3;

    /// <summary>
    /// Whether this id is a keeper — someone who can hold a dungeon heart, be
    /// dealt a creature pool, and have a portal produce for them.
    /// </summary>
    public static bool IsKeeper(byte playerId) => playerId >= FirstKeeper;
}

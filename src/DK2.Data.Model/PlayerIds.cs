// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>DK2's reserved player ids. <b>Not every player is a keeper.</b>
/// <para><b>CONFIRMED from three independent directions.</b> Across all 66 shipped levels every
/// dungeon-heart tile, every creature pool and every creature-availability entry names a player
/// between 3 and 7, never 0, 1 or 2. Level 1 shows four ids at once — hero gate player 1,
/// unclaimed portal player 2, keeper's heart player 3, unmined rock player 0. And OpenKeeper's
/// <c>Player</c> constants, read from the executable rather than the maps, agree. Asserted by
/// <c>PortalOnRealDataTests.TheReservedPlayerIdsAreWhatTheCampaignSays</c>.</para>
/// <para>A rule that loops over players treating each as a keeper would run an unclaimed portal's
/// clock (<c>PortalSystem</c>, DK2.Simulation, not referenced from here), pay a payday to nobody
/// and hand mana or free imps to <see cref="Neutral"/>. UNKNOWN whether <see cref="Good"/> is one
/// player or a faction, and what the original does with the four player-1 and eleven player-2
/// portals, neither id ever carrying a creature pool (Q91).</para></summary>
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

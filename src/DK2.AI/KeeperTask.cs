// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.AI;

/// <summary>
/// The computer keeper's task machine, numbered as the original numbers it
/// (KEEPER_AI.md §5.2). The numbers are kept because the original's debug dump
/// prints them and a reader of both should not have to translate.
/// </summary>
public enum KeeperTask : byte
{
    /// <summary>Decide what to do next: attack, build, or dig gold.</summary>
    Decide = 0,

    /// <summary>Find a site for <see cref="KeeperAIState.CurrentRoomToBuild"/>.</summary>
    FindSite = 1,

    /// <summary>Tag the site's rectangle for digging.</summary>
    DigRectangle = 2,

    /// <summary>Place the room over the rectangle, one tile per action.</summary>
    PlaceRoom = 3,

    /// <summary>The task ended; check gold and go back to deciding.</summary>
    Done = 4,

    /// <summary>Set up a dig path from the nearest own room to a gold deposit.</summary>
    RouteToGold = 5,

    /// <summary>Tag the gold seam in an expanding square around the deposit.</summary>
    MineSeam = 6,

    /// <summary>Dig a path from the current point to the target with the line walker.</summary>
    DigPath = 7,

    /// <summary>Do nothing more this tick.</summary>
    Stop = 8,
}

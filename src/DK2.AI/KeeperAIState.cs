// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.AI;

/// <summary>
/// The runtime half of a computer keeper: what the original keeps in its
/// 1,273-byte struct besides the tuning parameters (docs/KEEPER_AI.md §2.3). Field
/// names follow the original's debug-dump labels so the two can be read side by
/// side. Lives outside the simulation's state hash by design (§12.1); the
/// keeper's decisions reach the simulation only as hashed commands.
///
/// <para>Only the fields the proactive foundation uses are here. The attack, battle,
/// event-ring and breach state arrive with the reactive half.</para>
/// </summary>
public sealed class KeeperAIState
{
    public KeeperTask Task;
    public KeeperTask NextTask;

    /// <summary>Ticks since the current task began; 1,680 abandons it.</summary>
    public int TaskTimeCounter;

    /// <summary>When it reaches zero, every gold tag the keeper placed comes off.</summary>
    public int UntagGoldCounter;

    /// <summary>The direction the site search currently walks (0 N, 1 W, 2 S, 3 E).</summary>
    public int SearchDirection;

    /// <summary>Fixed at creation: from the map centre toward the heart. Rooms with id below 6 use it.</summary>
    public int DefendDirection;

    /// <summary>The opposite of <see cref="DefendDirection"/>; every other room uses it.</summary>
    public int OpenDirection;

    /// <summary>How many directions the site search has tried for the current room.</summary>
    public int DirectionsTried;

    /// <summary>Where the site search starts from.</summary>
    public TilePos SearchOrigin;

    // --- the line walker's state (§5.2) ---
    public TilePos DigRoomAt;            // the target
    public TilePos DigRoomAtCurrent;     // where the walker is
    public TilePos WallHugStart;
    public TilePos WallHugEnd;
    public int WallHugDistance;
    public int WallHugHeading;
    public bool WallHug;
    public bool WallHugLeft;

    // --- the rectangle being dug and then built on (§5.2 tasks 2 and 3) ---
    public TilePos RectangleOrigin;      // top-left corner
    public int RectangleWidth;
    public int RectangleHeight;

    /// <summary>Nearest own tile to the rectangle, where a connecting corridor starts.</summary>
    public TilePos JoinPathStart;

    public byte CurrentRoomToBuild;
    public byte NextRoomToBuild;
    public bool NextRoomToBuildSet;
    public bool AllRoomsBuilt;

    /// <summary>The four nearest gold seams (centroid and tile count), refreshed by the gold step.</summary>
    public readonly List<GoldDeposit> GoldDeposits = new();
    public int CurrentGoldArea;

    /// <summary>Radius of the square being tagged around the current deposit (task 6).</summary>
    public int MineRadius;

    public void BeginTask(KeeperTask task, KeeperTask next = KeeperTask.Decide)
    {
        Task = task;
        NextTask = next;
        TaskTimeCounter = 0;
    }
}

/// <summary>A gold seam the scanner found: its centroid and how many tiles it has.</summary>
public readonly record struct GoldDeposit(TilePos Centre, int Tiles);

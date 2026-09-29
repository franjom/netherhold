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

    // --- the reactive half (§5.3, §5.4) ---

    /// <summary>Bit 3 of the flag word: set by every combat message the walk reaches, even one
    /// a full queue refuses; the battle step clears it and sets it again for every own creature
    /// fighting through a walk, and the tick's imp creation gives way to that step while it is
    /// on (ADR-0278, ADR-0283).</summary>
    public bool FightGoingOn;

    /// <summary>Bit 7 and the countdowns of §5.1 step 4 (<c>+0x4EF</c>, <c>+0x4F3</c>): a cast Call
    /// to Arms the keeper still counts as its own, the turns before the removal test takes it
    /// down on time, and the turns before it may call or attack again.</summary>
    public bool CallToArmsInUse;
    public int CallToArmsCounter;
    public int CannotAttackCounter;

    /// <summary>The stored call position (<c>+0x4E</c>, <c>+0x52</c>), written by the battle step
    /// before its later cast checks; the removal test counts enemies around it.</summary>
    public int CallToArmsX;
    public int CallToArmsY;

    /// <summary>Ours: the turn of the last cast; the removal test waits for the simulation to show
    /// the call, and a call never shown by then counts as a refused cast (ADR-0283).</summary>
    public long CallToArmsCastTurn = -1;

    /// <summary>Ours: battle spell choices the simulation cannot cast yet (Heal, Inferno, Chicken),
    /// counted and not issued.</summary>
    public int UnbuiltSpellChoices;

    /// <summary>Bit 6: an enemy's strength was found at an attacked position.</summary>
    public bool HaveBeenAttacked;

    /// <summary>The last enemy whose strength was found near an attack, for the attack planner.</summary>
    public byte AttackingPlayerId;

    /// <summary>The three-slot respond-to-attack ring and its residuals.</summary>
    public KeeperResponseQueue Responses { get; } = new();

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

/// <summary>A seam's corrected centroid and sum of its terrain gold values.</summary>
public readonly record struct GoldDeposit(TilePos Centre, int GoldAmount);

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
/// <para>Only the fields the built steps use are here: the proactive foundation, the
/// reactive half, the battle and the attack planner's flags and breaches.</para>
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

    /// <summary>Ours: battle spell choices the simulation cannot cast yet (Inferno, Chicken),
    /// counted and not issued.</summary>
    public int UnbuiltSpellChoices;

    /// <summary>Bit 6: an enemy's strength was found at an attacked position.</summary>
    public bool HaveBeenAttacked;

    /// <summary>The last enemy whose strength was found near an attack, for the attack planner.</summary>
    public byte AttackingPlayerId;

    // --- the attack planner (§5.5, ADR-0298) ---

    /// <summary><c>PlayersAttackedFlags</c> (<c>+0x9B</c>) and <c>FirstAttackFlag</c> (<c>+0x9D</c>): a bit
    /// per player id, set by the planner when it starts an attack; the front clears an attacked
    /// bit whose player is gone.</summary>
    public ushort PlayersAttackedFlags;
    public ushort FirstAttackFlag;

    /// <summary>The low nibble of <c>BlibBlubBlob</c> (<c>+0x4BA</c>): the player a tunnel is being
    /// dug toward, 0 for none; task 7 stores the tunnel where it meets enemy land and clears it.</summary>
    public byte AttackTarget;

    /// <summary><c>EnemyBreaches[16]</c> (<c>+0x9F</c>), by player id: the tunnels left to dig and
    /// the eight stored ones, empty when their start's X is zero.</summary>
    public readonly KeeperBreachState[] Breaches = Enumerable.Range(0, 16).Select(_ => new KeeperBreachState()).ToArray();

    /// <summary>Ours: the research move (<c>0x004FA8D0</c>) the tick would run and this build does
    /// not, counted where DK2 would call it.</summary>
    public int UnbuiltResearchMoves;

    /// <summary>The loaders copy <c>BreachAtXPoints</c>, kept to four bits, into every enemy's count.</summary>
    public void InitialiseBreaches(byte breachAtXPoints)
    {
        foreach (KeeperBreachState breach in Breaches) breach.TunnelsLeft = (byte)(breachAtXPoints & 0xF);
    }

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

    /// <summary>Ours: the tile task 7 last asked to bridge, whose result the next tick shows; DK2
    /// builds and steps the walker on one allowance (ADR-0304).</summary>
    public TilePos? BridgeRequestedAt;

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
        BridgeRequestedAt = null; // a span's walk is owed only to the path that asked for it
    }
}

/// <summary>A seam's corrected centroid and sum of its terrain gold values.</summary>
public readonly record struct GoldDeposit(TilePos Centre, int GoldAmount);

/// <summary>One enemy's breach record (65 bytes in DK2): <c>TunnelsLeft</c> and eight stored
/// tunnels, each a start and an end tile; a start whose X is zero is an empty slot.</summary>
public sealed class KeeperBreachState
{
    public const int Slots = 8;
    public byte TunnelsLeft;
    public readonly TilePos[] Start = new TilePos[Slots];
    public readonly TilePos[] End = new TilePos[Slots];

    public bool HasStoredTunnel
    {
        get
        {
            foreach (TilePos start in Start) if (start.X != 0) return true;
            return false;
        }
    }
}

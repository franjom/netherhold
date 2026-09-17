// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Steering;

/// <summary>What the behaviours ask of the world: the callbacks the original installs
/// (docs/research/steering-behaviours.md §1) and the route dispatcher (§7). The simulation
/// implements it in ADR-0157 phase 3; until then only tests do.</summary>
public interface ISteeringWorld
{
    /// <summary>The navigation byte of a tile (<c>0x004D16B0</c>); off the map it is solid.</summary>
    byte NavByte(int tileX, int tileY);

    /// <summary>Every <c>Obstacle</c> the gated enumerator (<c>0x004D12B0</c>) yields within
    /// <paramref name="range"/> of <paramref name="centre"/>, appended to <paramref name="into"/>.</summary>
    void CollectObstacles(Fixed16Vector centre, int range, List<SteeringObstacle> into);

    /// <summary>The crowd probe <c>0x004D15E0</c>: the signed sum of creature word <c>+0x200</c> over
    /// every creature on a tile of the axis-aligned square of side <paramref name="span"/> centred on
    /// <paramref name="point"/> (tiles taken by floor and ceiling of the square's edges). Capped at
    /// 1.0, it comes off the seek gain near a waypoint (§5.4).</summary>
    int CrowdWeight(Fixed16Vector point, int span);

    /// <summary>The route dispatcher (<c>0x004D0E10</c>) at the midpoint fraction: the waypoints
    /// in 16.16, ending on <paramref name="to"/>; false and empty when there is no route.</summary>
    bool Route(Fixed16Vector from, Fixed16Vector to, MoverProfile mover, List<Fixed16Vector> points);

    /// <summary>Every creature body the ungated enumerator (<c>0x004D1170</c>) yields within
    /// <paramref name="range"/> of <paramref name="centre"/>, the asking body included.</summary>
    void CollectNeighbours(Fixed16Vector centre, int range, List<SteeringNeighbour> into);

    /// <summary>The body of the entity remembered at <c>+0x537</c>, if any, with the reach in whole
    /// tiles that <c>0x00460F90</c> gives it (navigation-and-steering.md §3.2 step 3).</summary>
    SteeringBody? RelatedBody(SteeringMover mover, out int reachTiles);
}

/// <summary>A neighbouring creature as Separation sees it: its body and its size class 1..5.</summary>
public readonly record struct SteeringNeighbour(SteeringBody Body, int SizeClass);

/// <summary>What the <c>Obstacle</c> enumerator forwards per object: position, size and the two
/// flag bits steering reads, bit 0 being <c>SolidObstacle</c> (structure-blocks.md §5).</summary>
public readonly record struct SteeringObstacle(Fixed16Vector Pos, int Size, bool Solid, bool Flag2);

/// <summary>The per-creature inputs beyond the body: which arm of <c>Follow_Path</c> runs, the
/// heading it turns (2048 to a turn) at the definition's rate per tick, and what it may cross.</summary>
public sealed class SteeringMover
{
    public bool IsWorker { get; set; }

    public int Heading { get; set; }

    /// <summary>Definition <c>+0x6A0</c>: the KWD <c>u16</c> at <c>0xF20</c> over four ticks a second.</summary>
    public int TurnRate { get; set; }

    /// <summary>Definition <c>+0x624</c>, KWD <c>0xEA8</c>: 1 (Imp) to 5 (Bile Demon, Giant).</summary>
    public int SizeClass { get; set; } = 2;

    public MoverProfile Profile { get; set; } = MoverProfile.Walker;
}

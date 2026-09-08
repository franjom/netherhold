// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.AI;

public enum WalkOutcome
{
    /// <summary>Diggable rock met; the caller tags <see cref="WalkStep.Tile"/>.</summary>
    Dig,

    /// <summary>Water or lava met; the original lays a bridge here. No bridge command exists yet.</summary>
    Hazard,

    /// <summary>The walker moved over open ground and has nothing to tag this call.</summary>
    Moved,

    /// <summary>The current point is the target.</summary>
    Arrived,

    /// <summary>Ran off the map, or hugged an obstacle all the way round without finding a way on.</summary>
    Failed,
}

public readonly record struct WalkStep(WalkOutcome Outcome, TilePos Tile);

/// <summary>The original's corridor digger — STRONGLY INFERRED, docs/KEEPER_AI.md §5.2.</summary>
/// <remarks>
/// <para>From <see cref="KeeperAIState.DigRoomAtCurrent"/>, step one tile at a time
/// along whichever axis is further from <see cref="KeeperAIState.DigRoomAt"/>. The
/// first solid, water or lava tile met is handed back if it is diggable rock or a
/// hazard.</para>
/// <para>An undiggable obstacle is walked around: continue the straight line through
/// it to find the far side, trial-walk both ways round and take the one that gets
/// closer to the target, then follow the obstacle's edge (clockwise table, turning
/// toward the wall), digging any diggable rock met, until the far side is reached and
/// the straight walk resumes. Returning to where the hug began fails the task.</para>
/// </remarks>
public static class LineWalker
{
    /// <summary>Segments each trial walk may run before giving up, as in the original.</summary>
    public const int TrialSegments = 24;

    public static WalkStep Step(KeeperView view, KeeperAIState s)
    {
        if (!s.WallHug)
        {
            WalkStep straight = StraightWalk(view, s);
            if (straight.Outcome != WalkOutcome.Moved) return straight;
            // Moved into hugging mode; fall through and hug at once.
        }
        return Hug(view, s);
    }

    private static WalkStep StraightWalk(KeeperView view, KeeperAIState s)
    {
        TilePos pos = s.DigRoomAtCurrent;
        TilePos target = s.DigRoomAt;
        int stepDx = 0, stepDy = 0;

        while (true)
        {
            if (pos == target)
            {
                s.DigRoomAtCurrent = pos;
                return new WalkStep(WalkOutcome.Arrived, pos);
            }
            (stepDx, stepDy) = Toward(pos, target);
            pos = new TilePos(pos.X + stepDx, pos.Y + stepDy);
            if (!view.InBounds(pos)) return new WalkStep(WalkOutcome.Failed, pos);
            if (view.IsSolid(pos) || view.IsWater(pos) || view.IsLava(pos)) break;
        }

        if (view.IsDiggable(pos))
        {
            s.DigRoomAtCurrent = pos;
            return new WalkStep(WalkOutcome.Dig, pos);
        }
        if (view.IsWater(pos) || view.IsLava(pos))
        {
            s.DigRoomAtCurrent = pos;
            return new WalkStep(WalkOutcome.Hazard, pos);
        }

        // Undiggable rock. Continue the line through it to find the far side.
        TilePos exit = pos;
        while (true)
        {
            (int ex, int ey) = Toward(exit, target);
            exit = new TilePos(exit.X + ex, exit.Y + ey);
            if (!view.InBounds(exit)) return new WalkStep(WalkOutcome.Failed, exit);
            if (view.IsDiggable(exit) || !view.IsSolid(exit) || view.IsLava(exit)) break;
            if (exit == target) break;
        }

        s.WallHug = true;
        s.WallHugStart = pos;
        s.WallHugEnd = exit;
        s.WallHugDistance = KeeperView.DistanceSquared(pos, target);
        s.WallHugHeading = KeeperDirections.HeadingOf(stepDx, stepDy);
        s.DigRoomAtCurrent = pos;
        s.WallHugLeft = Trial(view, pos, exit, target, s.WallHugDistance, s.WallHugHeading);
        s.WallHugHeading = (s.WallHugHeading + (s.WallHugLeft ? 1 : -1)) & 3;
        return new WalkStep(WalkOutcome.Moved, pos);
    }

    private static WalkStep Hug(KeeperView view, KeeperAIState s)
    {
        TilePos pos = s.DigRoomAtCurrent;
        int heading = s.WallHugHeading;
        int guard = view.Width * view.Height * 4;

        while (pos != s.WallHugEnd && guard-- > 0)
        {
            int d = KeeperView.DistanceSquared(pos, s.DigRoomAt);
            if (d <= s.WallHugDistance) s.WallHugDistance = d;

            // Turn toward the wall first; if that is open ground, take it.
            int turn = (heading + (s.WallHugLeft ? -1 : 1)) & 3;
            TilePos next = Neighbour(pos, turn);
            int chosen;
            if (view.IsDiggable(next))
            {
                s.DigRoomAtCurrent = next;
                s.WallHugHeading = turn;
                return new WalkStep(WalkOutcome.Dig, next);
            }
            if (Open(view, next))
            {
                chosen = turn;
            }
            else
            {
                // Otherwise rotate away from the wall until something opens.
                int dir = heading;
                bool found = false;
                for (int i = 0; i < 4; i++)
                {
                    next = Neighbour(pos, dir);
                    if (view.IsDiggable(next))
                    {
                        s.DigRoomAtCurrent = next;
                        s.WallHugHeading = dir;
                        return new WalkStep(WalkOutcome.Dig, next);
                    }
                    if (Open(view, next))
                    {
                        found = true;
                        break;
                    }
                    dir = (dir + (s.WallHugLeft ? 1 : -1)) & 3;
                }
                if (!found)
                {
                    s.DigRoomAtCurrent = pos;
                    return new WalkStep(WalkOutcome.Failed, pos);
                }
                chosen = dir;
            }

            pos = Neighbour(pos, chosen);
            heading = chosen;
            if (pos == s.WallHugStart)
            {
                s.DigRoomAtCurrent = pos;
                return new WalkStep(WalkOutcome.Failed, pos);
            }
        }

        s.DigRoomAtCurrent = pos;
        s.WallHugHeading = heading;
        s.WallHug = false;
        return new WalkStep(guard > 0 ? WalkOutcome.Moved : WalkOutcome.Failed, pos);
    }

    /// <summary>
    /// Walk both ways round from <paramref name="start"/> toward <paramref name="end"/>,
    /// up to <see cref="TrialSegments"/> segments each (a segment ends where the
    /// hug would dig), and report whether the "+1" turn gets closer to the target.
    /// </summary>
    private static bool Trial(KeeperView view, TilePos start, TilePos end, TilePos target, int startDistance, int heading)
    {
        int plus = TrialOneWay(view, start, end, target, startDistance, heading, left: true);
        int minus = TrialOneWay(view, start, end, target, startDistance, heading, left: false);
        return plus < minus;
    }

    private static int TrialOneWay(KeeperView view, TilePos start, TilePos end, TilePos target, int startDistance, int heading, bool left)
    {
        int best = startDistance;
        TilePos pos = start;
        int h = (heading + (left ? 1 : -1)) & 3;
        int guard = view.Width * view.Height * 4;

        for (int segment = 0; segment < TrialSegments && pos != end && guard > 0; segment++)
        {
            while (pos != end && guard-- > 0)
            {
                best = Math.Min(best, KeeperView.DistanceSquared(pos, target));
                int turn = (h + (left ? -1 : 1)) & 3;
                TilePos next = Neighbour(pos, turn);
                int chosen = -1;
                if (view.IsDiggable(next) || Open(view, next))
                {
                    chosen = turn;
                }
                else
                {
                    int dir = h;
                    for (int i = 0; i < 4; i++)
                    {
                        next = Neighbour(pos, dir);
                        if (view.IsDiggable(next) || Open(view, next))
                        {
                            chosen = dir;
                            break;
                        }
                        dir = (dir + (left ? 1 : -1)) & 3;
                    }
                }
                if (chosen < 0) return best;
                pos = Neighbour(pos, chosen);
                h = chosen;
                if (pos == start) return best;
                if (view.IsDiggable(pos)) break; // a segment ends where digging would happen
            }
        }
        if (pos == end) return 0;
        return best;
    }

    private static bool Open(KeeperView view, TilePos p) =>
        view.InBounds(p) && !view.IsSolid(p) && !view.IsLava(p);

    private static TilePos Neighbour(TilePos p, int heading)
    {
        (int dx, int dy) = KeeperDirections.Clockwise[heading];
        return new TilePos(p.X + dx, p.Y + dy);
    }

    private static (int Dx, int Dy) Toward(TilePos from, TilePos to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dy) < Math.Abs(dx)) return (Math.Sign(dx), 0);
        return (0, Math.Sign(dy));
    }
}

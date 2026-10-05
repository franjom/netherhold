// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.AI;

public enum WalkOutcome
{
    /// <summary>Diggable rock met; the caller tags <see cref="WalkStep.Tile"/>.</summary>
    Dig,

    /// <summary>Water or lava met; the caller must choose and request a bridge.</summary>
    Hazard,

    /// <summary>The walker moved over open ground and has nothing to tag this call.</summary>
    Moved,

    /// <summary>The current point is the target.</summary>
    Arrived,

    /// <summary>Ran off the map, or hugged an obstacle back to where the hug began.</summary>
    Failed,
}

public readonly record struct WalkStep(WalkOutcome Outcome, TilePos Tile);

/// <summary>Task 7's line and wall walker, <c>0x00502230</c>, with its side chooser
/// <c>0x005027D0</c>. The rules, and where ours still differs, are in
/// computer-keeper-tasks.md §5.</summary>
/// <remarks>An undiggable obstacle starts a hug <b>from the cursor, not the obstacle</b>, and
/// coming back to that cursor fails the walk. Until 2026-10-05 the hug began on the obstacle
/// tile, which it never stands on, so the loop test could not fire.</remarks>
public static class LineWalker
{
    /// <summary>A trial stops after this many segments (<c>0x00502AE9</c>, <c>0x00502E42</c>).</summary>
    public const int TrialSegments = 25;

    public static WalkStep Step(KeeperView view, KeeperAIState s)
    {
        if (!s.WallHug)
        {
            WalkStep straight = StraightWalk(view, s);
            if (straight.Outcome != WalkOutcome.Moved) return straight;
            // A hug was started; DK2 hugs in the same call.
        }
        return Hug(view, s);
    }

    private static WalkStep StraightWalk(KeeperView view, KeeperAIState s)
    {
        TilePos pos = s.DigRoomAtCurrent;
        TilePos target = s.DigRoomAt;
        int stepDx, stepDy;

        while (true)
        {
            // DK2 returns 0 here without moving the cursor.
            if (pos == target) return new WalkStep(WalkOutcome.Arrived, pos);
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

        // Undiggable rock. Continue the line through it to find the exit.
        TilePos exit = pos;
        do
        {
            (int ex, int ey) = Toward(exit, target);
            exit = new TilePos(exit.X + ex, exit.Y + ey);
            if (!view.InBounds(exit)) return new WalkStep(WalkOutcome.Failed, exit);
        }
        while (!view.IsDiggable(exit) && view.IsSolid(exit) && !view.IsLava(exit));

        TilePos cursor = s.DigRoomAtCurrent;
        s.WallHug = true;
        s.WallHugStart = cursor;
        s.WallHugEnd = exit;
        s.WallHugDistance = KeeperView.DistanceSquared(pos, target);
        int heading = KeeperDirections.HeadingOf(stepDx, stepDy);
        s.WallHugLeft = ChooseSide(view, cursor, exit, target, s.WallHugDistance, heading);
        s.WallHugHeading = (heading + (s.WallHugLeft ? 1 : -1)) & 3;
        return new WalkStep(WalkOutcome.Moved, cursor);
    }

    private static WalkStep Hug(KeeperView view, KeeperAIState s)
    {
        TilePos pos = s.DigRoomAtCurrent;
        int heading = s.WallHugHeading;
        int rotate = s.WallHugLeft ? 1 : -1;
        int guard = view.Width * view.Height * 4; // DK2 has none; a cycle missing the start would spin

        while (pos != s.WallHugEnd)
        {
            if (guard-- <= 0) return new WalkStep(WalkOutcome.Failed, pos);
            int d = KeeperView.DistanceSquared(pos, s.DigRoomAt);
            if (d <= s.WallHugDistance) s.WallHugDistance = d;

            // Turn toward the wall first.
            int chosen = (heading - rotate) & 3;
            TilePos next = Neighbour(pos, chosen);
            if (view.IsDiggable(next)) return Dig(s, next, chosen);
            if (!HugOpen(view, next))
            {
                // Otherwise rotate away from the wall, from the current heading.
                chosen = heading;
                bool found = false;
                for (int i = 0; i < 4; i++)
                {
                    next = Neighbour(pos, chosen);
                    if (view.IsDiggable(next)) return Dig(s, next, chosen);
                    if (HugOpen(view, next))
                    {
                        found = true;
                        break;
                    }
                    chosen = (chosen + rotate) & 3;
                }
                if (!found) return new WalkStep(WalkOutcome.Failed, pos);
            }

            pos = next;
            heading = chosen;
            // DK2 leaves the cursor where it was and returns 0.
            if (pos == s.WallHugStart) return new WalkStep(WalkOutcome.Failed, pos);
        }

        s.DigRoomAtCurrent = pos;
        s.WallHugHeading = heading;
        s.WallHug = false;
        return new WalkStep(WalkOutcome.Moved, pos);
    }

    private static WalkStep Dig(KeeperAIState s, TilePos tile, int heading)
    {
        s.DigRoomAtCurrent = tile;
        s.WallHugHeading = heading;
        return new WalkStep(WalkOutcome.Dig, tile);
    }

    /// <summary>
    /// <c>0x005027D0</c>: true for the heading-plus-one side. Each side walks up to
    /// <see cref="TrialSegments"/> segments from <paramref name="start"/>; a segment ends where
    /// the hug would dig, and the next starts on that tile. A first side that loops back to the
    /// start chooses the other at once, a second that loops chooses the first. A first that
    /// reaches the exit bounds the second by its segment count; otherwise the smaller minimum
    /// squared distance to the target wins, ties to the second side.
    /// </summary>
    public static bool ChooseSide(KeeperView view, TilePos start, TilePos end, TilePos target, int distance, int heading)
    {
        int bound = 50; // 0x32, replaced by the first side's count when it arrives
        int first = Trial(view, start, end, target, distance, heading, plus: true, int.MaxValue,
            out bool firstLooped, out bool firstArrived, out int firstSegments);
        if (firstLooped) return false;
        if (firstArrived) bound = firstSegments;
        int second = Trial(view, start, end, target, distance, heading, plus: false, bound,
            out bool secondLooped, out _, out _);
        if (secondLooped) return true;
        if (second < 0) return true; // ran past the first side's segment count
        return first < second;
    }

    /// <summary>One side's walk: the minimum squared distance seen, 0 on reaching the exit, or
    /// -1 when the segment bound ran out first (only the second side has one).</summary>
    private static int Trial(KeeperView view, TilePos start, TilePos end, TilePos target, int distance,
        int heading, bool plus, int bound, out bool looped, out bool arrived, out int segments)
    {
        looped = false;
        arrived = false;
        int rotate = plus ? 1 : -1;
        int h = (heading + rotate) & 3;
        int best = distance;
        TilePos segmentStart = start;
        int guard = view.Width * view.Height * 4;

        for (segments = 0; ; )
        {
            if (segments > bound) return -1;
            TilePos pos = segmentStart;
            while (true)
            {
                if (pos == end)
                {
                    arrived = true;
                    return 0;
                }
                if (guard-- <= 0) return best;
                int d = KeeperView.DistanceSquared(pos, target);
                if (d <= best) best = d;

                int chosen = (h - rotate) & 3;
                TilePos next = Neighbour(pos, chosen);
                // Only the second side's first attempt also tests lava (0x00502C2A).
                if (view.IsDiggable(next) && (plus || !view.IsLava(next)))
                {
                    segmentStart = next;
                    h = chosen;
                    break;
                }
                if (!TrialOpen(view, next))
                {
                    chosen = h;
                    bool found = false;
                    bool dig = false;
                    for (int i = 0; i < 4; i++)
                    {
                        next = Neighbour(pos, chosen);
                        if (view.IsDiggable(next) && !view.IsLava(next))
                        {
                            dig = true;
                            break;
                        }
                        if (TrialOpen(view, next))
                        {
                            found = true;
                            break;
                        }
                        chosen = (chosen + rotate) & 3;
                    }
                    if (!found)
                    {
                        // A dig ends the segment on that tile; blocked all round, four turns bring
                        // the heading back and DK2 ends it on the tile ahead.
                        segmentStart = dig ? next : Neighbour(pos, chosen);
                        h = chosen;
                        break;
                    }
                }
                pos = next;
                h = chosen;
                if (pos == start)
                {
                    looped = true;
                    return best;
                }
            }
            if (++segments >= TrialSegments) return best;
        }
    }

    /// <summary>The hug's open test: terrain neither solid nor lava (<c>+0x17B</c> bits 0 and 8).</summary>
    private static bool HugOpen(KeeperView view, TilePos p) =>
        view.InBounds(p) && !view.IsSolid(p) && !view.IsLava(p);

    /// <summary>The trial's open test is the navigation query (world vtable <c>+0x208</c>), which
    /// ours stands in for with terrain solidity.</summary>
    private static bool TrialOpen(KeeperView view, TilePos p) =>
        view.InBounds(p) && !view.IsSolid(p);

    private static TilePos Neighbour(TilePos p, int heading)
    {
        (int dx, int dy) = KeeperDirections.Clockwise[heading];
        return new TilePos(p.X + dx, p.Y + dy);
    }

    /// <summary>DK2's step is <c>(diff &gt;&gt; 31) * 2 + 1</c>: never zero, +1 on a zero
    /// difference, so the exit search cannot stall on the target.</summary>
    private static (int Dx, int Dy) Toward(TilePos from, TilePos to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dy) < Math.Abs(dx)) return (dx < 0 ? -1 : 1, 0);
        return (0, dy < 0 ? -1 : 1);
    }
}

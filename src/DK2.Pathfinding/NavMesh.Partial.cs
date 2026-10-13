// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>DK2's per-tile rebuild (`0x004CE770`'s partial path, navigation-partial-rebuild.md,
/// Q591, ADR-0461): a changed rectangle of tiles is edited in place. Its sides are cut into the
/// mesh and marked as constraints, the inside is emptied of vertices, refilled from the bytes and
/// coarsened, and then the sides' points are unmarked and each one that has a single byte all round
/// is removed. The mesh afterwards depends on the history of edits, not only on the bytes. Each
/// walk is the original's transcribed through the translation in NavMesh.Edit.cs.</summary>
public sealed partial class NavMesh
{
    private readonly List<(int X, int Y)> _sidePoints = [];

    /// <summary>The original's retriangulation entry (`0x004CE770`) over the tiles
    /// <paramref name="x0"/>..<paramref name="x1"/> by <paramref name="y0"/>..<paramref name="y1"/>,
    /// far corners exclusive. A rectangle that does not keep one tile clear of every map edge, or
    /// the whole map, takes the whole-map path, which is <see cref="Build"/>; a 1x1 rectangle skips
    /// the emptying and takes its tile's byte; a rectangle under 4x4 of one byte is inserted as one;
    /// anything else is partitioned by the scanner, byte-0 rectangles included.</summary>
    public void Retriangulate(INavByteGrid grid, int x0, int y0, int x1, int y1)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        // The original returns on a -1 corner or an empty rectangle; any other corner outside the
        // map is widened below.
        if (x0 == -1 || y0 == -1 || x0 == x1 || y0 == y1) return;

        int width = grid.Width, height = grid.Height;
        bool single = x1 == x0 + 1 && y1 == y0 + 1;
        bool partial = x0 != 0 || y0 != 0 || x1 != width || y1 != height;
        if (x0 < 1 || y0 < 1 || x1 + 1 >= width || y1 + 1 >= height)
        {
            x0 = y0 = 0;
            x1 = width;
            y1 = height;
            partial = single = false;
        }

        if (!partial)
        {
            Reset(CanvasMin, CanvasMin, CanvasMax, CanvasMax);
            InsertRectangle(0, 0, width, height, NavBytes.Floor);
        }
        else
        {
            CutHorizontalSide(grid, x0, x1, y0);
            CutHorizontalSide(grid, x0, x1, y1);
            CutVerticalSide(grid, x0, y0, y1);
            CutVerticalSide(grid, x1, y0, y1);
            MarkSide(x0, y0, x0, y1);
            MarkSide(x0, y1, x1, y1);
            MarkSide(x1, y1, x1, y0);
            MarkSide(x1, y0, x0, y0);
            if (!single) EmptyRectangle(x0, y0, x1, y1);
        }

        int uniform = -1;
        if (single) uniform = grid.NavByte(x0, y0);
        else if (partial && x1 - x0 < 4 && y1 - y0 < 4)
        {
            uniform = grid.NavByte(x0, y0);
            for (int y = y0; y < y1 && uniform >= 0; y++)
                for (int x = x0; x < x1; x++)
                    if (grid.NavByte(x, y) != uniform) { uniform = -1; break; }
        }

        if (uniform < 0) InsertRectangles(grid, x0, y0, x1, y1, skipFloor: !partial);
        else InsertRectangle(x0, y0, x1, y1, (byte)uniform);
        CoarsenRectangle(x0, y0, x1, y1);

        if (!partial) return;
        _sidePoints.Clear();
        QueueSide(x0, y0, x0, y1);
        QueueSide(x0, y1, x1, y1);
        QueueSide(x1, y1, x1, y0);
        QueueSide(x1, y0, x0, y0);
        for (int i = _sidePoints.Count - 1; i >= 0; i--) RemoveIfRedundant(_sidePoints[i].X, _sidePoints[i].Y);
    }

    /// <summary>`0x004CDFE0`: the points of a horizontal side at <paramref name="y"/>, from
    /// <paramref name="x0"/> to <paramref name="x1"/>: both ends, and every corner where the pair of
    /// bytes above and below the side changes; then the side as one constraint.</summary>
    private void CutHorizontalSide(INavByteGrid grid, int x0, int x1, int y)
    {
        byte above = grid.NavByte(x0, y - 1), below = grid.NavByte(x0, y);
        int first = InsertPoint(x0, y);
        for (int x = x0 + 1; x < x1; x++)
        {
            if (grid.NavByte(x, y - 1) == above && grid.NavByte(x, y) == below) continue;
            InsertPoint(x, y);
            above = grid.NavByte(x, y - 1);
            below = grid.NavByte(x, y);
        }
        int last = InsertPoint(x1, y);
        InsertConstraint(first, last);
    }

    /// <summary>`0x004CDF20`: the same along a vertical side at <paramref name="x"/>, the pair of
    /// bytes being the tiles left and right of it.</summary>
    private void CutVerticalSide(INavByteGrid grid, int x, int y0, int y1)
    {
        byte left = grid.NavByte(x - 1, y0), right = grid.NavByte(x, y0);
        int first = InsertPoint(x, y0);
        for (int y = y0 + 1; y < y1; y++)
        {
            if (grid.NavByte(x, y) == right && grid.NavByte(x - 1, y) == left) continue;
            InsertPoint(x, y);
            left = grid.NavByte(x - 1, y);
            right = grid.NavByte(x, y);
        }
        int last = InsertPoint(x, y1);
        InsertConstraint(first, last);
    }

    /// <summary>`0x004CE380`: walks a side from (<paramref name="fx"/>, <paramref name="fy"/>) to
    /// (<paramref name="tx"/>, <paramref name="ty"/>) vertex by vertex, setting the constraint bit of
    /// each edge on the one triangle the edge finder names, the one on the original's right of the
    /// walk, outside the rectangle.</summary>
    private void MarkSide(int tx, int ty, int fx, int fy)
    {
        int x = fx, y = fy;
        while (x != tx || y != ty)
        {
            if (!TryEdgeToward(x, y, tx, ty, out int t, out int s))
                throw new InvalidOperationException($"no edge leaves ({x},{y}) toward ({tx},{ty})");
            MarkConstraint(t, EdgeLeaving(s));
            int q = CornerAfter(t, s);
            x = _px[q];
            y = _py[q];
        }
    }

    /// <summary>`0x004CE410`: the same walk after the fill, queueing each point it leaves and clearing
    /// the bit the marker set; the queue is then taken last first.</summary>
    private void QueueSide(int tx, int ty, int fx, int fy)
    {
        int x = fx, y = fy;
        while (x != tx || y != ty)
        {
            if (!TryEdgeToward(x, y, tx, ty, out int t, out int s))
                throw new InvalidOperationException($"no edge leaves ({x},{y}) toward ({tx},{ty})");
            _sidePoints.Add((x, y));
            ClearConstraint(t, EdgeLeaving(s));
            int q = CornerAfter(t, s);
            x = _px[q];
            y = _py[q];
        }
    }

    /// <summary>`0x004CE0C0`, transcribed loop for loop: from the edge leaving (x0, y0) toward (x1,
    /// y0), the triangle across it is entered and its far corner removed unless it lies on one of the
    /// rectangle's four lines, restarting from the start edge after each removal; then the walk turns
    /// and crosses edges without the constraint bit, examining a corner of each triangle it enters,
    /// until it comes back to the start triangle. The original's edge slot <c>next(b)</c> is our
    /// <c>Prev(b)</c> and its corner <c>prev(b)</c> our <c>Prev(b)</c> throughout.</summary>
    private void EmptyRectangle(int x0, int y0, int x1, int y1)
    {
        if (!TryEdgeToward(x0, y0, x1, y0, out int start, out int startSlot)) return;
        int startEdge = EdgeLeaving(startSlot);
        int cur = Neighbour(start, startEdge);
        if (cur == NoNeighbour) return;
        int slot = SlotToward(cur, start);
        int guard = 0;
        do
        {
            int prev;
            while (true)
            {
                while (true)
                {
                    int far = Prev(slot);
                    int v = Vertex(cur, far);
                    if (_px[v] == x0 || _px[v] == x1 || _py[v] == y0 || _py[v] == y1) break;
                    RemoveVertex(cur, far);
                    cur = Neighbour(start, startEdge);
                    if (cur == NoNeighbour) return;
                    slot = SlotToward(cur, start);
                    if (++guard > (4 * _triCount) + 64) throw new InvalidOperationException("emptying a rectangle did not converge");
                }
                slot = Prev(slot);
                int nb = Neighbour(cur, slot);
                if (nb == NoNeighbour) return;
                int back = SlotToward(nb, cur);
                prev = cur;
                if (!IsConstraint(nb, back)) break;
                if (nb == start) return;
            }
            int across;
            do
            {
                cur = Neighbour(prev, slot);
                if (cur == NoNeighbour) return;
                slot = Prev(SlotToward(cur, prev));
                across = Neighbour(cur, slot);
                if (across == NoNeighbour) return;
                prev = cur;
            } while (!IsConstraint(across, SlotToward(across, cur)));
        } while (Neighbour(cur, slot) != start);
    }

    /// <summary>The merge-back of one queued side point (`0x004CE770`'s queue loop): its fan is walked
    /// while each triangle carries the byte of the first; if the walk closes, the point has one byte
    /// all round and is removed. A point on a byte boundary, or whose fan is open, stays.</summary>
    private void RemoveIfRedundant(int x, int y)
    {
        if (!TryLocateVertex(x, y, out int t, out int s)) return;
        byte nav = _nav[t];
        int cur = t;
        int nb = Neighbour(t, EdgeLeaving(s));
        if (nb == NoNeighbour) return;
        while (_nav[nb] == nav)
        {
            int back = SlotToward(nb, cur);
            if (nb == t)
            {
                RemoveVertex(t, s);
                return;
            }
            int next = Neighbour(nb, EdgeLeaving(back));
            cur = nb;
            nb = next;
            if (nb == NoNeighbour) return;
        }
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>How a blended item's draw combines with the pixels under it (<see cref="BlendedItemOrder"/>).</summary>
public enum BlendedItemBlend : byte
{
    /// <summary>Adds its colour (DK2's ONE, ONE and SRCALPHA, ONE): two such draws leave the same pixels in either
    /// order.</summary>
    Additive,

    /// <summary>Any other blend (SRCALPHA, INVSRCALPHA, or a replace): its place in the order matters where it overlaps
    /// another item.</summary>
    Ordered,
}

/// <summary>Where an item may cover the screen, as a box in any fixed 2D measure of it (normalised device coordinates,
/// pixels): two items whose boxes do not overlap leave the same pixels in either order. <see cref="Everywhere"/> overlaps
/// every box.</summary>
public readonly record struct BlendedItemRect(float MinX, float MinY, float MaxX, float MaxY)
{
    public static readonly BlendedItemRect Everywhere = new BlendedItemRect(float.NegativeInfinity, float.NegativeInfinity,
        float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>Nothing: the start of a union.</summary>
    public static readonly BlendedItemRect Nowhere = new BlendedItemRect(float.PositiveInfinity, float.PositiveInfinity,
        float.NegativeInfinity, float.NegativeInfinity);

    public bool Overlaps(in BlendedItemRect other)
    {
        return MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;
    }

    /// <summary>A box in normalised device coordinates around everything within <paramref name="radius"/> of
    /// <paramref name="centre"/> under <paramref name="viewProjection"/> (column vectors, <see cref="ViewFrustum.ViewProjection"/>,
    /// in that frustum's frame); <see cref="Everywhere"/> when the sphere reaches the eye's plane. Each of x, y and w moves
    /// by at most its row's length times the radius, so the box holds every point of the sphere.</summary>
    public static BlendedItemRect OfSphere(in System.Numerics.Matrix4x4 viewProjection, System.Numerics.Vector3 centre, float radius)
    {
        System.Numerics.Matrix4x4 m = viewProjection;
        float w = m.M41 * centre.X + m.M42 * centre.Y + m.M43 * centre.Z + m.M44;
        float dw = radius * Length(m.M41, m.M42, m.M43);
        if (!(w - dw > 0f))
        {
            return Everywhere;
        }

        float x = m.M11 * centre.X + m.M12 * centre.Y + m.M13 * centre.Z + m.M14;
        float y = m.M21 * centre.X + m.M22 * centre.Y + m.M23 * centre.Z + m.M24;
        float dx = radius * Length(m.M11, m.M12, m.M13), dy = radius * Length(m.M21, m.M22, m.M23);
        float near = w - dw, far = w + dw;
        return new BlendedItemRect(Math.Min((x - dx) / near, (x - dx) / far), Math.Min((y - dy) / near, (y - dy) / far),
            Math.Max((x + dx) / near, (x + dx) / far), Math.Max((y + dy) / near, (y + dy) / far));
    }

    private static float Length(float x, float y, float z)
    {
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    public BlendedItemRect Union(in BlendedItemRect other)
    {
        return new BlendedItemRect(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
    }
}

/// <summary>One draw of <see cref="BlendedItemOrder"/>: <see cref="Count"/> items of one batch, starting at
/// <see cref="Start"/> in <see cref="BlendedItemOrder.Items"/>, back to front, keyed by <see cref="Distance"/> in the
/// measure of <see cref="BlendedRunSlot{TMaterial}.Distance"/>.</summary>
public readonly record struct BlendedItemSegment(int Batch, int Start, int Count, float Distance);

/// <summary>Blended items drawn as instanced batches beside the stream's runs (effect sprites, flames, flowers), cut into as
/// few draws as leave the pixels of DK2's one back-to-front list (0x0058B9D0, 0x00588D00) under a renderer that sorts whole
/// draws by distance (Godot's, plan step 3k). An item draws before a run keyed below its distance, after the rest, and
/// never crosses a run. Taken back to front, it joins its batch's latest draw between the same runs that it reaches by
/// moving earlier past only draws it cannot change (additive past additive, anything past boxes, <see cref="BlendedItemRect"/>,
/// it does not overlap); else it starts one. A draw's key is its first item's distance, held <see cref="KeyGap"/> inside its
/// neighbours, or spread evenly (at least <see cref="MinimumGap"/> apart) where the gap is too narrow. A thing the renderer
/// sorts itself (an object, the cage) is a barrier (<see cref="AddBarrier"/>), keyed at its own distance. Allocation-free once grown.</summary>
public sealed class BlendedItemOrder
{
    /// <summary>The least fall of the key from one draw to the next: a quarter of the runs' least fall
    /// (<see cref="BlendedRunSlots{TMaterial}.KeyStep"/>), so draws fit between two runs and stay far above a float's
    /// rounding at a level's distances.</summary>
    public const float KeyGap = BlendedRunSlots<object>.KeyStep / 4f;

    /// <summary>How many draws back an item looks for its batch's: a bound on the work per item.</summary>
    public const int LookBack = 64;

    /// <summary>The float steps a spread key keeps from its neighbours at the renderer's magnitude
    /// (<see cref="MinimumGap"/>): the offset and the renderer's own distance each round once.</summary>
    public const int GapUlps = 4;

    private float[] _runs = [];
    private int _runCount;
    private Entry[] _entries = [], _scratch = [];
    // Items, and items and barriers together.
    private int _count, _total;
    // By sorted position, the draw the entry went to (-1 a barrier); the output, draw by draw.
    private int[] _segmentOf = [], _items = [], _fill = [];
    private Draw[] _draws = [];
    private int _drawCount;
    private readonly List<BlendedItemSegment> _segments = new List<BlendedItemSegment>();

    private readonly struct Entry
    {
        public Entry(int index, int batch, float distance, BlendedItemBlend blend, in BlendedItemRect rect)
        {
            Index = index;
            Batch = batch;
            Distance = distance;
            Blend = blend;
            Rect = rect;
        }

        public readonly int Index, Batch;
        public readonly float Distance;
        public readonly BlendedItemBlend Blend;
        public readonly BlendedItemRect Rect;
    }

    private struct Draw
    {
        public int Batch, Gap, Count;
        public bool Ordered, Barrier;
        public float First, Key;
        public BlendedItemRect Rect;
    }

    // The batch of a barrier's entry and draw: no item's.
    private const int BarrierBatch = int.MinValue;

    /// <summary>The draws of the last <see cref="Order"/>, back to front.</summary>
    public IReadOnlyList<BlendedItemSegment> Segments
    {
        get
        {
            return _segments;
        }
    }

    /// <summary>The item indices (as <see cref="Add"/> returned them) of the last <see cref="Order"/>, draw by draw.</summary>
    public ReadOnlySpan<int> Items
    {
        get
        {
            return new ReadOnlySpan<int>(_items, 0, _count);
        }
    }

    /// <summary>How many items are added since <see cref="ClearItems"/>.</summary>
    public int ItemCount
    {
        get
        {
            return _count;
        }
    }

    /// <summary>How many runs are set.</summary>
    public int RunCount
    {
        get
        {
            return _runCount;
        }
    }

    /// <summary>The eye's distance from the renderer's sort pivot (Godot's origin): with the keys, the magnitude a sort
    /// depth is rounded at (<see cref="MinimumGap"/>).</summary>
    public float EyeDistance { get; set; }

    /// <summary>Grows the buffers to hold <paramref name="entries"/> items and barriers, <paramref name="draws"/> draws and
    /// <paramref name="runs"/> runs, so a level allocates nothing once open while it stays within them.</summary>
    public void EnsureCapacity(int entries, int draws, int runs)
    {
        if (_entries.Length < entries)
        {
            Resize(entries);
        }

        if (_draws.Length < draws)
        {
            Array.Resize(ref _draws, draws);
        }

        if (_fill.Length < draws)
        {
            Array.Resize(ref _fill, draws);
        }

        if (_segments.Capacity < draws)
        {
            _segments.Capacity = draws;
        }

        if (_runs.Length < runs)
        {
            Array.Resize(ref _runs, runs);
        }
    }

    /// <summary>The least gap two keys need to keep their order through the renderer's sort at
    /// <paramref name="key"/>: <see cref="GapUlps"/> float steps of <see cref="EyeDistance"/> plus the key.</summary>
    public float MinimumGap(float key)
    {
        float magnitude = Math.Abs(EyeDistance) + Math.Abs(key);
        int bits = BitConverter.SingleToInt32Bits(magnitude);
        return GapUlps * (BitConverter.Int32BitsToSingle(bits + 1) - magnitude);
    }

    /// <summary>Forgets the runs; set them again with <see cref="AddRun"/> after each re-sort of the stream.</summary>
    public void ClearRuns()
    {
        _runCount = 0;
    }

    /// <summary>Adds the next run's key, back to front: each must be below the one before
    /// (<see cref="BlendedRunSlot{TMaterial}.Distance"/> of the non-overlay runs of <see cref="BlendedRunSlots{TMaterial}.Drawn"/>).
    /// A tag overlay's run is not added: it draws before every blended thing.</summary>
    public void AddRun(float distance)
    {
        if (_runCount > 0 && !(distance < _runs[_runCount - 1]))
        {
            throw new ArgumentException("Run keys must fall back to front.", nameof(distance));
        }

        if (_runCount == _runs.Length)
        {
            Array.Resize(ref _runs, Math.Max(16, _runs.Length * 2));
        }

        _runs[_runCount++] = distance;
    }

    /// <summary>Forgets the items and barriers, keeping the runs.</summary>
    public void ClearItems()
    {
        _count = _total = 0;
        _drawCount = 0;
        _segments.Clear();
    }

    /// <summary>Adds an item of <paramref name="batch"/> (the renderer's draw: a material and its instances) at
    /// <paramref name="distance"/> from the eye, covering at most <paramref name="rect"/> of the screen; returns its index,
    /// counted from 0 since <see cref="ClearItems"/>.</summary>
    public int Add(int batch, float distance, BlendedItemBlend blend, in BlendedItemRect rect)
    {
        if (batch == BarrierBatch)
        {
            throw new ArgumentOutOfRangeException(nameof(batch));
        }

        if (_total == _entries.Length)
        {
            Resize(Math.Max(64, _entries.Length * 2));
        }

        _entries[_total++] = new Entry(_count, batch, distance, blend, rect);
        return _count++;
    }

    /// <summary><see cref="Add(int, float, BlendedItemBlend, in BlendedItemRect)"/> for an item that may cover the whole
    /// screen.</summary>
    public int Add(int batch, float distance, BlendedItemBlend blend)
    {
        return Add(batch, distance, blend, BlendedItemRect.Everywhere);
    }

    /// <summary>Adds a blended thing the renderer sorts on its own at <paramref name="distance"/> from the eye (in the
    /// renderer's own measure), covering at most <paramref name="rect"/> of the screen and blending as
    /// <paramref name="blend"/> (additive when every surface is ONE, ONE without a depth write), until
    /// <see cref="ClearItems"/>.</summary>
    public void AddBarrier(float distance, in BlendedItemRect rect, BlendedItemBlend blend = BlendedItemBlend.Ordered)
    {
        if (_total == _entries.Length)
        {
            Resize(Math.Max(64, _entries.Length * 2));
        }

        _entries[_total++] = new Entry(-1, BarrierBatch, distance, blend, rect);
    }

    private void Resize(int capacity)
    {
        Array.Resize(ref _entries, capacity);
        Array.Resize(ref _scratch, capacity);
        Array.Resize(ref _segmentOf, capacity);
        Array.Resize(ref _items, capacity);
    }

    /// <summary>Sorts the items and barriers back to front (equal distances in the order added), places them among the
    /// runs and cuts the items into <see cref="Segments"/>.</summary>
    public void Order()
    {
        _segments.Clear();
        _drawCount = 0;
        if (_count == 0)
        {
            return;
        }

        SortBackToFront();
        int run = 0, gapStart = 0;
        for (int i = 0; i < _total; i++)
        {
            ref readonly Entry entry = ref _entries[i];
            int passed = run;
            // A run draws before an item no farther than its key; an item never moves past one.
            while (run < _runCount && _runs[run] >= entry.Distance)
            {
                run++;
            }

            if (run != passed)
            {
                gapStart = _drawCount;
            }

            bool ordered = entry.Blend == BlendedItemBlend.Ordered;
            if (entry.Batch == BarrierBatch)
            {
                ref Draw barrier = ref NewDraw();
                barrier = new Draw
                {
                    Batch = BarrierBatch, Gap = run, Ordered = ordered, Barrier = true, First = entry.Distance,
                    Key = entry.Distance, Rect = entry.Rect,
                };
                _segmentOf[i] = -1;
                continue;
            }
            int target = -1;
            for (int d = _drawCount - 1; d >= Math.Max(gapStart, _drawCount - LookBack); d--)
            {
                ref readonly Draw draw = ref _draws[d];
                if (draw.Batch == entry.Batch)
                {
                    target = d;
                    break;
                }
                if ((ordered || draw.Ordered) && draw.Rect.Overlaps(entry.Rect))
                {
                    break;
                }
            }
            if (target < 0)
            {
                ref Draw made = ref NewDraw();
                made = new Draw
                {
                    Batch = entry.Batch, Gap = run, Ordered = ordered, First = entry.Distance, Rect = BlendedItemRect.Nowhere,
                };
                target = _drawCount - 1;
            }
            ref Draw into = ref _draws[target];
            into.Count++;
            into.Rect = into.Rect.Union(entry.Rect);
            _segmentOf[i] = target;
        }
        KeyDraws();
        // Each draw's items, back to front, from its start; a barrier draws nothing here.
        if (_fill.Length < _drawCount)
        {
            Array.Resize(ref _fill, Math.Max(_drawCount, _fill.Length * 2));
        }

        int start = 0;
        for (int d = 0; d < _drawCount; d++)
        {
            ref readonly Draw draw = ref _draws[d];
            _fill[d] = start;
            if (draw.Barrier)
            {
                continue;
            }

            _segments.Add(new BlendedItemSegment(draw.Batch, start, draw.Count, draw.Key));
            start += draw.Count;
        }
        for (int i = 0; i < _total; i++)
        {
            if (_segmentOf[i] >= 0)
            {
                _items[_fill[_segmentOf[i]]++] = _entries[i].Index;
            }
        }
    }

    private ref Draw NewDraw()
    {
        if (_drawCount == _draws.Length)
        {
            Array.Resize(ref _draws, Math.Max(32, _draws.Length * 2));
        }

        return ref _draws[_drawCount++];
    }

    // Keys each stretch of item draws between two fixed keys (a run's, a barrier's) below the fixed key or draw before
    // them and above the fixed key after them.
    private void KeyDraws()
    {
        int first = 0;
        while (first < _drawCount)
        {
            if (_draws[first].Barrier)
            {
                first++;
                continue;
            }
            int gap = _draws[first].Gap, end = first;
            while (end < _drawCount && _draws[end].Gap == gap && !_draws[end].Barrier)
            {
                end++;
            }

            float upper = first > 0 && _draws[first - 1].Gap == gap ? _draws[first - 1].Key
                : gap > 0 ? _runs[gap - 1] : float.PositiveInfinity;
            float lower = end < _drawCount && _draws[end].Gap == gap ? _draws[end].Key
                : gap < _runCount ? _runs[gap] : float.NegativeInfinity;
            float previous = upper;
            bool fits = true;
            for (int d = first; d < end; d++)
            {
                float key = Math.Min(_draws[d].First, previous - KeyGap);
                if (!(key - lower >= KeyGap))
                {
                    fits = false;
                }

                _draws[d].Key = key;
                previous = key;
            }
            if (!fits)
            {
                Spread(first, end, float.IsPositiveInfinity(upper) ? _draws[first].First + KeyGap : upper, lower);
            }

            first = end;
        }
    }

    // Spreads draws [first, end) evenly between two fixed keys, at least MinimumGap apart and from them; where that does
    // not fit, the last draws share the lowest key that keeps its gap above the lower key (or the midpoint): those draws
    // tie exactly, and Godot's unstable transparent sort may swap them. Rare (barriers ~1e-4 apart with items between).
    private void Spread(int first, int end, float top, float lower)
    {
        int n = end - first;
        float step = (top - lower) / (n + 1);
        float gap = MinimumGap(top);
        if (step >= gap)
        {
            for (int d = first; d < end; d++)
            {
                _draws[d].Key = top - step * (d - first + 1);
            }

            return;
        }
        float floor = lower + gap < top - gap ? lower + gap : (top + lower) * 0.5f;
        for (int d = first; d < end; d++)
        {
            _draws[d].Key = Math.Max(floor, top - gap * (d - first + 1));
        }
    }

    // A bottom-up merge sort, farthest first: stable, so equal distances keep the order added (Array.Sort with a comparer
    // allocates a delegate a call).
    private void SortBackToFront()
    {
        Entry[] from = _entries, to = _scratch;
        for (int width = 1; width < _total; width *= 2)
        {
            for (int left = 0; left < _total; left += 2 * width)
            {
                int middle = Math.Min(left + width, _total), right = Math.Min(left + 2 * width, _total);
                int i = left, j = middle, k = left;
                while (i < middle && j < right)
                {
                    to[k++] = from[j].Distance > from[i].Distance ? from[j++] : from[i++];
                }

                while (i < middle)
                {
                    to[k++] = from[i++];
                }

                while (j < right)
                {
                    to[k++] = from[j++];
                }
            }
            (from, to) = (to, from);
        }
        if (!ReferenceEquals(from, _entries))
        {
            Array.Copy(from, _entries, _total);
        }
    }
}

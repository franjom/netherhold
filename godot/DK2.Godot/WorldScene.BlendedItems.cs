// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using DK2.Presentation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The blended things drawn as instanced batches (plan step 3k): effect sprites, the wall flames and the status
/// flowers. Each frame they are added with their distance from the eye (<see cref="AddItem"/>; one wholly outside the
/// placed frustum is dropped); <see cref="DrawBlendedItems"/> adds the transparent things Godot sorts itself as barriers,
/// places the items among the blended runs and cuts them into draws (the shared <see cref="BlendedItemOrder"/>), and draws
/// each as one <see cref="RenderingServer"/> MultiMesh instance keyed as a run is (<see cref="RunSlot"/>): pivot at the
/// origin, sorting offset the eye's distance less the draw's key. Godot 4.8 sets a MultiMesh's buffer only whole
/// (MultimeshSetBuffer, no region update), so a draw's capacity follows its count (grown to a power of two, halved after
/// a sustained low count) and an unchanged draw is not uploaded. Allocation-free but when a pool grows.</summary>
public partial class WorldScene
{
    // Floats per instance: a Transform3D (12, rows), the colour (4) and the custom data (4).
    private const int ItemStride = 20;
    private const int FirstItemCapacity = 16, ItemEntries = 1024, ItemDrawsAtOpen = 512, ItemRunsAtOpen = 256;
    // Frames a draw's count must stay at a quarter of its capacity or less before the capacity halves.
    private const int ShrinkFrames = 120;
    private BlendedItemOrder _itemOrder = new();
    private List<ItemBatch> _itemBatches = new(64);
    private readonly List<ModelSlot> _transparentSlots = new(256);
    private float[] _itemData = new float[ItemEntries * ItemStride], _itemUpload = new float[ItemEntries * ItemStride];
    // Per item, its centre (Godot's frame) and radius: the draw's box, by which Godot culls it.
    private float[] _itemBounds = new float[ItemEntries * 4];
    private Vector3 _itemEye;
    private float _itemEyeDistance;
    private ViewFrustum _itemFrustum;
    private System.Numerics.Matrix4x4 _itemViewProjection;
    private Rid _itemScenario;
    private bool _itemsOpen;
    // For the exit report and the harness: what the item pipeline allocated in its first frames and after (and how much
    // of that a growing pool took), the most draws and items, the uploads made and skipped, and the time split.
    private const int ItemWarmUp = 60;
    private long _itemWarmUpBytes, _itemLateBytes, _itemGrowthBytes, _itemUploads, _itemUploadsSkipped;
    private int _itemFrames, _itemMaxDraws, _itemMaxItems, _itemDrawsMade;
    // What the frame's item steps allocated so far (BeginItems to DrawBlendedItems, the HUD's own work between them left
    // out), and the growth counter when the frame began.
    private long _itemFrameBytes, _itemFrameGrowth, _itemLateGrowthBytes;
    private readonly long[] _itemTicks = new long[(int)ItemStep.Count];
    private enum ItemStep { Effects, Flames, Flowers, Barriers, Order, Upload, Count }

    private sealed class PreviewItems
    {
        public BlendedItemOrder Order = new();
        public List<ItemBatch> Batches = new(64);
        public float[] Data = new float[ItemEntries * ItemStride], Upload = new float[ItemEntries * ItemStride];
        public float[] Bounds = new float[ItemEntries * 4];
        public Vector3 Eye;
        public float EyeDistance;
        public ViewFrustum Frustum;
        public System.Numerics.Matrix4x4 ViewProjection;
        public bool Open;
    }
    private readonly PreviewItems _previewItems = new();
    private bool _previewPass, _previewItemsDrawn;

    private void SwapPreviewItems()
    {
        (_itemOrder, _previewItems.Order) = (_previewItems.Order, _itemOrder);
        (_itemBatches, _previewItems.Batches) = (_previewItems.Batches, _itemBatches);
        (_itemData, _previewItems.Data) = (_previewItems.Data, _itemData);
        (_itemUpload, _previewItems.Upload) = (_previewItems.Upload, _itemUpload);
        (_itemBounds, _previewItems.Bounds) = (_previewItems.Bounds, _itemBounds);
        (_itemEye, _previewItems.Eye) = (_previewItems.Eye, _itemEye);
        (_itemEyeDistance, _previewItems.EyeDistance) = (_previewItems.EyeDistance, _itemEyeDistance);
        (_itemFrustum, _previewItems.Frustum) = (_previewItems.Frustum, _itemFrustum);
        (_itemViewProjection, _previewItems.ViewProjection) = (_previewItems.ViewProjection, _itemViewProjection);
        (_itemsOpen, _previewItems.Open) = (_previewItems.Open, _itemsOpen);
        _previewPass = !_previewPass;
    }

    private void BeginPreviewItems()
    {
        _previewItems.Open = PreviewPose.HasValue;
        if (PreviewPose is not ClassicCameraPose pose)
        {
            return;
        }

        _previewItems.Order.ClearItems();
        _previewItems.Eye = ToGodot(RightHandedFrame.Point(pose.Position));
        _previewItems.EyeDistance = pose.Position.Length();
        _previewItems.Order.EyeDistance = _previewItems.EyeDistance;
        _previewItems.Frustum = ViewFrustum.FromPose(pose, PreviewViewport.Width / (float)PreviewViewport.Height, .05f);
        _previewItems.ViewProjection = _previewItems.Frustum.ViewProjection;
    }

    /// <summary>A mesh drawn with a material: the instances of one sprite sheet, the flames, one flower icon.</summary>
    private sealed class ItemBatch(int id, Rid mesh, Rid material, BlendedItemBlend blend)
    {
        public readonly int Id = id;
        public readonly Rid Mesh = mesh, Material = material;
        public readonly BlendedItemBlend Blend = blend;
        public ItemDraw[] Draws = new ItemDraw[16];
        public int Made, Used;
    }

    /// <summary>One MultiMesh instance of a batch, shown for one segment of the order a frame, with the floats it last
    /// uploaded.</summary>
    private struct ItemDraw
    {
        public Rid Multimesh, Instance;
        public float[] Last;
        public int Capacity, Count, LowFrames;
        public float Offset;
        public Aabb Box;
        public bool Shown;
    }

    /// <summary>Sizes the item buffers for a level, so its frames allocate only when a pool outgrows them.</summary>
    private void OpenItems()
    {
        _itemOrder.EnsureCapacity(ItemEntries, ItemDrawsAtOpen, ItemRunsAtOpen);
        _itemScenario = GetWorld3D().Scenario;
    }

    /// <summary>A new batch of <paramref name="mesh"/> drawn with <paramref name="material"/>.</summary>
    private ItemBatch CreateItemBatch(Rid mesh, Rid material, BlendedItemBlend blend)
    {
        List<ItemBatch> registry = _previewPass ? _previewItems.Batches : _itemBatches;
        var batch = new ItemBatch(registry.Count, mesh, material, blend);
        registry.Add(batch);
        return batch;
    }

    /// <summary>Adds or removes a model whose surfaces draw in the transparent pass (<see cref="ModelSlot.ApplyLooks"/>).</summary>
    private void ListTransparent(ModelSlot slot, bool transparent)
    {
        if (transparent)
        {
            _transparentSlots.Add(slot);
        }
        else
        {
            _transparentSlots.Remove(slot);
        }
    }

    /// <summary>The runs' keys for the items, after each re-sort of the stream (<see cref="DrawBlended"/>): the tag overlays
    /// draw first by their priority and are left out.</summary>
    private void SetItemRuns(IReadOnlyList<BlendedRunSlot<ShaderMaterial>> drawn)
    {
        _itemOrder.ClearRuns();
        for (int i = 0; i < drawn.Count; i++)
        {
            if (!drawn[i].TagOverlay)
            {
                _itemOrder.AddRun(drawn[i].Distance);
            }
        }
    }

    /// <summary>Starts the frame's items, measured from the placed camera's eye, the one the runs are keyed for.</summary>
    private void BeginItems()
    {
        BeginPreviewItems();
        _itemFrameBytes = 0;
        _itemFrameGrowth = _itemGrowthBytes;
        _itemOrder.ClearItems();
        _itemsOpen = View is MinimapCameraView;
        if (View is not MinimapCameraView view)
        {
            return;
        }

        NVector3 eye = view.Position;
        _itemEye = ToGodot(RightHandedFrame.Point(eye));
        _itemEyeDistance = eye.Length();
        _itemOrder.EyeDistance = _itemEyeDistance;
        _itemFrustum = PlacedFrustum;
        _itemViewProjection = _itemFrustum.ViewProjection;
    }

    /// <summary>Adds an instance of <paramref name="batch"/>: its transform, colour and custom data, sorted by the distance
    /// from the eye to <paramref name="centre"/> (Godot's frame), covering at most the screen box of the sphere of
    /// <paramref name="radius"/> about it (<see cref="BlendedItemRect.OfSphere"/>), within which its order counts. A sphere
    /// wholly outside the frustum is not drawn.</summary>
    private void AddItem(ItemBatch batch, Vector3 centre, float radius, in Transform3D transform, Color colour, Color custom)
    {
        if (!_itemsOpen)
        {
            return;
        }

        if (_previewPass)
        {
            while (_itemBatches.Count <= batch.Id)
            {
                ItemBatch source = _previewItems.Batches[_itemBatches.Count];
                _itemBatches.Add(new ItemBatch(source.Id, source.Mesh, source.Material, source.Blend));
            }
            batch = _itemBatches[batch.Id];
        }
        NVector3 at = RightHandedFrame.Point(new NVector3(centre.X, centre.Y, centre.Z));
        for (int i = 0; i < 6; i++)
        {
            if (System.Numerics.Plane.DotCoordinate(_itemFrustum[i], at) < -radius)
            {
                return;
            }
        }

        int index = _itemOrder.Add(batch.Id, centre.DistanceTo(_itemEye), batch.Blend,
            BlendedItemRect.OfSphere(_itemViewProjection, at, radius));
        if (_itemData.Length < (index + 1) * ItemStride)
        {
            Array.Resize(ref _itemData, _itemData.Length * 2);
            Array.Resize(ref _itemBounds, _itemData.Length / ItemStride * 4);
        }
        _itemBounds[index * 4] = centre.X; _itemBounds[(index * 4) + 1] = centre.Y;
        _itemBounds[(index * 4) + 2] = centre.Z; _itemBounds[(index * 4) + 3] = radius;
        Span<float> row = _itemData.AsSpan(index * ItemStride, ItemStride);
        Basis b = transform.Basis;
        Vector3 o = transform.Origin;
        row[0] = b.X.X; row[1] = b.Y.X; row[2] = b.Z.X; row[3] = o.X;
        row[4] = b.X.Y; row[5] = b.Y.Y; row[6] = b.Z.Y; row[7] = o.Y;
        row[8] = b.X.Z; row[9] = b.Y.Z; row[10] = b.Z.Z; row[11] = o.Z;
        row[12] = colour.R; row[13] = colour.G; row[14] = colour.B; row[15] = colour.A;
        row[16] = custom.R; row[17] = custom.G; row[18] = custom.B; row[19] = custom.A;
    }

    /// <summary>Charges what the thread allocated since <paramref name="start"/> to the frame's item steps.</summary>
    private void ItemBytes(long start)
    {
        _itemFrameBytes += GC.GetAllocatedBytesForCurrentThread() - start;
    }

    /// <summary>Charges the time since <paramref name="start"/> to <paramref name="step"/>; returns now.</summary>
    private long ItemTime(ItemStep step, long start)
    {
        long now = Stopwatch.GetTimestamp();
        _itemTicks[(int)step] += now - start;
        return now;
    }

    /// <summary>Orders the frame's items among the runs and barriers and draws them, once every item is in (after the
    /// flowers and the cage, which the HUD's hover pass places). Hides the draws no segment took.</summary>
    public void DrawBlendedItems()
    {
        if (!_previewPass && (PreviewPose.HasValue || _previewItemsDrawn))
        {
            SwapPreviewItems();
            DrawBlendedItems();
            SwapPreviewItems();
            _previewItemsDrawn = PreviewPose.HasValue;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        foreach (ItemBatch batch in _itemBatches)
        {
            batch.Used = 0;
        }

        if (_itemsOpen)
        {
            AddItemBarriers();
            started = ItemTime(ItemStep.Barriers, started);
            _itemOrder.Order();
            started = ItemTime(ItemStep.Order, started);
            IReadOnlyList<BlendedItemSegment> segments = _itemOrder.Segments;
            ReadOnlySpan<int> items = _itemOrder.Items;
            for (int s = 0; s < segments.Count; s++)
            {
                BlendedItemSegment segment = segments[s];
                ItemBatch batch = _itemBatches[segment.Batch];
                if (batch.Used == batch.Made)
                {
                    MakeItemDraw(batch);
                }

                ref ItemDraw draw = ref batch.Draws[batch.Used++];
                Vector3 low = new(float.MaxValue, float.MaxValue, float.MaxValue), high = -low;
                // The upload buffer holds the segment before ShowItemDraw sizes the draw: a segment past every draw's
                // capacity so far (an uncapped additive burst) grows it here.
                if (_itemUpload.Length < segment.Count * ItemStride)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    int length = _itemUpload.Length;
                    while (length < segment.Count * ItemStride)
                    {
                        length *= 2;
                    }

                    Array.Resize(ref _itemUpload, length);
                    _itemGrowthBytes += GC.GetAllocatedBytesForCurrentThread() - before;
                }
                for (int i = 0; i < segment.Count; i++)
                {
                    int item = items[segment.Start + i];
                    Array.Copy(_itemData, item * ItemStride, _itemUpload, i * ItemStride, ItemStride);
                    var centre = new Vector3(_itemBounds[item * 4], _itemBounds[(item * 4) + 1], _itemBounds[(item * 4) + 2]);
                    var reach = new Vector3(_itemBounds[(item * 4) + 3], _itemBounds[(item * 4) + 3], _itemBounds[(item * 4) + 3]);
                    low = low.Min(centre - reach);
                    high = high.Max(centre + reach);
                }
                // The draw's box is its items': Godot culls by it (the camera's far plane culls a box past it), while
                // the pivot, not the box, keys the sort.
                var box = new Aabb(low, high - low);
                if (box != draw.Box)
                {
                    RenderingServer.MultimeshSetCustomAabb(draw.Multimesh, box);
                    draw.Box = box;
                }
                ShowItemDraw(ref draw, batch, segment.Count, segment.Distance);
            }
            _itemMaxDraws = Math.Max(_itemMaxDraws, segments.Count);
            _itemMaxItems = Math.Max(_itemMaxItems, items.Length);
        }
        foreach (ItemBatch batch in _itemBatches)
        {
            for (int d = batch.Used; d < batch.Made; d++)
            {
                ref ItemDraw draw = ref batch.Draws[d];
                if (!draw.Shown)
                {
                    continue;
                }

                RenderingServer.InstanceSetVisible(draw.Instance, false);
                draw.Shown = false;
            }
        }

        ItemTime(ItemStep.Upload, started);
        _itemsOpen = false;
        ItemBytes(allocated);
        long growth = _itemGrowthBytes - _itemFrameGrowth;
        if (_itemFrames++ < ItemWarmUp)
        {
            _itemWarmUpBytes += _itemFrameBytes;
        }
        else
        {
            _itemLateBytes += _itemFrameBytes - growth;
            _itemLateGrowthBytes += growth;
        }
    }

    private void MakeItemDraw(ItemBatch batch)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        if (batch.Made == batch.Draws.Length)
        {
            Array.Resize(ref batch.Draws, batch.Draws.Length * 2);
        }

        Rid multimesh = RenderingServer.MultimeshCreate();
        RenderingServer.MultimeshSetMesh(multimesh, batch.Mesh);
        // A custom box, so Godot never measures the buffer; set to the items' box each frame.
        RenderingServer.MultimeshSetCustomAabb(multimesh, _worldBox);
        Rid instance = RenderingServer.InstanceCreate2(multimesh, _itemScenario);
        RenderingServer.InstanceSetLayerMask(instance, _previewPass ? PreviewBlendedLayer : MainBlendedLayer);
        RenderingServer.InstanceGeometrySetCastShadowsSetting(instance, RenderingServer.ShadowCastingSetting.Off);
        RenderingServer.InstanceGeometrySetMaterialOverride(instance, batch.Material);
        batch.Draws[batch.Made++] = new ItemDraw
        {
            Multimesh = multimesh, Instance = instance, Last = [], Offset = float.NaN, Box = _worldBox, Shown = true,
        };
        _itemDrawsMade++;
        _itemGrowthBytes += GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Shows <paramref name="count"/> instances from the front of the upload buffer keyed at <paramref name="key"/>:
    /// the capacity follows the count, and the buffer goes up only when it differs from the draw's last.</summary>
    private void ShowItemDraw(ref ItemDraw draw, ItemBatch batch, int count, float key)
    {
        int capacity = draw.Capacity;
        if (count > capacity)
        {
            capacity = Math.Max(FirstItemCapacity, capacity);
            while (capacity < count)
            {
                capacity *= 2;
            }
        }
        draw.LowFrames = count <= draw.Capacity / 4 && draw.Capacity > FirstItemCapacity ? draw.LowFrames + 1 : 0;
        if (draw.LowFrames >= ShrinkFrames)
        {
            capacity = Math.Max(FirstItemCapacity, draw.Capacity / 2);
            draw.LowFrames = 0;
        }
        bool reallocated = capacity != draw.Capacity;
        if (reallocated)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            draw.Capacity = capacity;
            RenderingServer.MultimeshAllocateData(draw.Multimesh, capacity, RenderingServer.MultimeshTransformFormat.Transform3D,
                colorFormat: true, customDataFormat: true);
            if (draw.Last.Length < capacity * ItemStride)
            {
                draw.Last = new float[capacity * ItemStride];
            }

            if (_itemUpload.Length < capacity * ItemStride)
            {
                Array.Resize(ref _itemUpload, capacity * ItemStride);
            }

            _itemGrowthBytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        int floats = count * ItemStride;
        var fresh = new ReadOnlySpan<float>(_itemUpload, 0, floats);
        if (reallocated || count != draw.Count || !fresh.SequenceEqual(new ReadOnlySpan<float>(draw.Last, 0, floats)))
        {
            fresh.CopyTo(draw.Last);
            // The tail past the count is drawn by nobody (the visible count) but goes up with the buffer.
            RenderingServer.MultimeshSetBuffer(draw.Multimesh, new ReadOnlySpan<float>(draw.Last, 0, capacity * ItemStride));
            if (count != draw.Count || reallocated)
            {
                RenderingServer.MultimeshSetVisibleInstances(draw.Multimesh, count);
            }

            draw.Count = count;
            _itemUploads++;
        }
        else
        {
            _itemUploadsSkipped++;
        }

        float offset = _itemEyeDistance - key;
        if (offset != draw.Offset)
        {
            RenderingServer.InstanceSetPivotData(draw.Instance, offset, false);
            draw.Offset = offset;
        }
        if (!draw.Shown)
        {
            RenderingServer.InstanceSetVisible(draw.Instance, true);
            draw.Shown = true;
        }
    }

    /// <summary>The blended things Godot sorts on its own, by their box's centre, that the placed frustum does not cull:
    /// each shown model with a transparent surface (objects, structures, locks, creatures, effect models), the torch
    /// holders with one and the cage.</summary>
    private void AddItemBarriers()
    {
        if (_itemOrder.ItemCount == 0)
        {
            return;
        }

        for (int i = 0; i < _transparentSlots.Count; i++)
        {
            ModelSlot slot = _transparentSlots[i];
            if (slot.Node.Visible && slot.Node.IsVisibleInTree())
            {
                AddItemBarrier(slot.Node, slot.TransparentOrdered);
            }
        }
        for (int i = 0; i < _transparentTorches.Count; i++)
        {
            TorchSlot torch = _transparentTorches[i];
            if (torch.Node is { Visible: true } node)
            {
                AddItemBarrier(node, torch.TransparentOrdered);
            }
        }
        // The ribbon is ONE, ONE without a depth write (Shaders/Cage.gdshader).
        if (_cage is { Visible: true } cage)
        {
            AddItemBarrier(cage.Box, BlendedItemBlend.Additive);
        }
    }

    private void AddItemBarrier(GeometryInstance3D node, bool ordered)
    {
        Aabb local = node.CustomAabb;
        if (local.Size == Vector3.Zero)
        {
            local = node.GetAabb();
        }

        AddItemBarrier(node.GlobalTransform * local, ordered ? BlendedItemBlend.Ordered : BlendedItemBlend.Additive);
    }

    /// <summary>A box Godot sorts by its centre (use_aabb_center): a barrier at the centre's distance, covering the screen
    /// box of the sphere around the box; none where the frustum culls the box.</summary>
    private void AddItemBarrier(Aabb box, BlendedItemBlend blend)
    {
        Vector3 low = box.Position, high = box.End;
        // Godot's frame to the renderers': z flips, so the box's z bounds swap.
        var pick = new PickBox(new NVector3(low.X, low.Y, -high.Z), new NVector3(high.X, high.Y, -low.Z));
        if (_itemFrustum.Excludes(pick))
        {
            return;
        }

        Vector3 centre = box.GetCenter();
        _itemOrder.AddBarrier(centre.DistanceTo(_itemEye), BlendedItemRect.OfSphere(_itemViewProjection,
            RightHandedFrame.Point(new NVector3(centre.X, centre.Y, centre.Z)), box.Size.Length() * 0.5f), blend);
    }

    /// <summary>The item counters for the harness: the time split (mean ms a frame) and the allocation after warm-up.</summary>
    private void AddItemCounters(List<KeyValuePair<string, double>> into)
    {
        int frames = Math.Max(1, _itemFrames);
        string[] names = ["effects", "flames", "flowers", "barriers", "order", "upload"];
        for (int i = 0; i < names.Length; i++)
        {
            Add(into, "items." + names[i] + "Ms", Ms(_itemTicks[i]) / frames);
        }

        Add(into, "items.batches", _itemBatches.Count);
        Add(into, "items.drawsMade", _itemDrawsMade);
        Add(into, "items.maxDraws", _itemMaxDraws);
        Add(into, "items.maxItems", _itemMaxItems);
        Add(into, "items.lateBytes", _itemLateBytes);
        Add(into, "items.lateGrowthBytes", _itemLateGrowthBytes);
        Add(into, "items.uploads", _itemUploads);
        Add(into, "items.uploadsSkipped", _itemUploadsSkipped);
        AddLightningCounters(into);
    }

    private void FreeItems()
    {
        if (!_previewPass && _previewItems.Batches.Count > 0)
        {
            SwapPreviewItems(); FreeItems(); SwapPreviewItems();
        }
        int frames = Math.Max(1, _itemFrames);
        GD.Print("DK2.Godot blended items: ", _itemBatches.Count, " batches, ", _itemDrawsMade, " draws made; up to ", _itemMaxDraws,
            " draws and ", _itemMaxItems, " items a frame; ", _itemUploads, " uploads, ", _itemUploadsSkipped,
            " skipped unchanged; allocated ", _itemWarmUpBytes, " B in the first ", ItemWarmUp, " frames and ", _itemLateBytes,
            " B in ", Math.Max(0, _itemFrames - ItemWarmUp), " after, besides ", _itemLateGrowthBytes,
            " B after it loading art and growing pools; ms a frame: effects ",
            Milliseconds(_itemTicks[0] / frames), ", flames ", Milliseconds(_itemTicks[1] / frames), ", flowers ",
            Milliseconds(_itemTicks[2] / frames), ", barriers ", Milliseconds(_itemTicks[3] / frames), ", order ",
            Milliseconds(_itemTicks[4] / frames), ", upload ", Milliseconds(_itemTicks[5] / frames), "; ",
            _transparentSlots.Count, " transparent models and ", _transparentTorches.Count, " torch holders listed");
        foreach (ItemBatch batch in _itemBatches)
        {
            for (int d = 0; d < batch.Made; d++)
            {
                RenderingServer.FreeRid(batch.Draws[d].Instance);
                RenderingServer.FreeRid(batch.Draws[d].Multimesh);
            }
            batch.Made = 0;
        }
        _itemBatches.Clear();
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Core;
using DK2.Presentation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Draws status flowers from the scene frame, layering the sprite and mesh parts in their authored order.</summary>
public partial class WorldScene
{
    private readonly StatusFlowerLayer[] _flowerLayers = new StatusFlowerLayer[StatusFlowerLayers.MaxLayers];
    private readonly Dictionary<string, FlowerBatch> _flowerBatches = new Dictionary<string, FlowerBatch>();
    // The same batches by the icon name's reference: the shared layers hand out the same strings each frame, so this skips
    // hashing the name; a new reference falls back to the name and is remembered.
    private readonly Dictionary<string, FlowerBatch> _flowerByReference = new Dictionary<string, FlowerBatch>(ReferenceEqualityComparer.Instance);
    private Shader? _flowerShader;
    private QuadMesh? _flowerQuad;

    /// <summary>Adds this frame's flowers over the drawn creatures as blended items; call before
    /// <see cref="DrawBlendedItems"/>. <paramref name="art"/> reads an icon by its name under
    /// <see cref="StatusFlowerLayers.Folder"/>, once per icon.</summary>
    public void DrawFlowers(IReadOnlyList<StatusFlower> flowers, bool possessing, Func<string, RgbaImage> art, double seconds)
    {
        if (!_previewPass && PreviewPose.HasValue)
        {
            SwapPreviewItems(); DrawFlowers(flowers, possessing, art, seconds); SwapPreviewItems();
        }
        MinimapCameraView? activeView = _previewPass && PreviewPose is ClassicCameraPose preview
            ? MinimapCameraView.Of(preview, PreviewViewport) : View;
        if (activeView is not MinimapCameraView view || flowers.Count == 0)
        {
            if (!_previewPass)
            {
                HideUnusedDollarFlowers(0);
            }

            return;
        }
        int dollars = 0;
        long started = System.Diagnostics.Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        var forward = NVector3.Normalize(view.Forward);
        var right = NVector3.Normalize(NVector3.Cross(view.Up, forward));
        var up = NVector3.Cross(forward, right);
        Vector3 x = ToGodot(RightHandedFrame.Direction(right)), y = ToGodot(RightHandedFrame.Direction(up));
        Vector3 z = -ToGodot(RightHandedFrame.Direction(forward));
        for (int f = 0; f < flowers.Count; f++)
        {
            StatusFlower flower = flowers[f];
            if (!TryGetCreaturePosition(new CreatureQueryTarget(flower.CreatureId), out NVector3 feet))
            {
                continue;
            }

            NVector3 anchor = feet + NVector3.UnitY * (flower.Height.Raw / (float)Fp32.One);
            if (flower.Dollar)
            {
                // The same world mesh is visible to both cameras.
                bool visible = _creatures.TryGetValue(flower.CreatureId, out CreatureSlot? creature) && creature.Slot.Node.Visible;
                if (!_previewPass && visible && DrawDollarFlower(dollars, anchor, seconds))
                {
                    dollars++;
                }

                continue;
            }
            if (!_previewPass)
            {
                _flowerDollarClock.Reset();
            }
            // The owner's colour as a source_color uniform took it: made linear, as the art is sampled.
            Color tint = new Color(flower.Colour.Red / 255f, flower.Colour.Green / 255f, flower.Colour.Blue / 255f).SrgbToLinear();
            int count = StatusFlowerLayers.Build(flower, _flowerLayers);
            for (int i = 0; i < count; i++)
            {
                StatusFlowerLayer layer = _flowerLayers[i];
                (NVector3 at, float scale) = StatusFlowerRules.LayerPlacement(anchor, view.Position, view.Forward, layer.Index,
                    StatusFlowerLayers.CentreLift);
                float size = StatusFlowerRules.WorldSize(layer.Name, possessing) * scale;
                Vector3 centre = ToGodot(RightHandedFrame.Point(at));
                // The unit quad's corners are half a diagonal from its centre.
                AddItem(FlowerBatchFor(layer.Name, art).Items, centre, size * 0.70710678f,
                    new Transform3D(new Basis(x * size, y * size, z * size), centre), tint,
                    new Color(layer.Wedge, layer.Ring ? 1f : 0f, 0f, 0f));
            }
        }
        if (!_previewPass)
        {
            HideUnusedDollarFlowers(dollars);
        }

        ItemTime(ItemStep.Flowers, started);
        ItemBytes(allocated);
    }

    /// <summary>One icon's batch: its texture and material, loaded on first use.</summary>
    private sealed class FlowerBatch
    {
        public FlowerBatch(ImageTexture texture, ShaderMaterial material, ItemBatch items)
        {
            Texture = texture;
            Material = material;
            Items = items;
        }

        public readonly ImageTexture Texture;
        public readonly ShaderMaterial Material;
        public readonly ItemBatch Items;
    }

    private FlowerBatch FlowerBatchFor(string name, Func<string, RgbaImage> art)
    {
        if (_flowerByReference.TryGetValue(name, out FlowerBatch? batch))
        {
            return batch;
        }

        if (_flowerBatches.TryGetValue(name, out batch))
        {
            if (_flowerByReference.Count < 256)
            {
                _flowerByReference.Add(name, batch);
            }

            return batch;
        }
        RgbaImage rgba = art(name);
        using var image = Image.CreateFromData(rgba.Width, rgba.Height, false, Image.Format.Rgba8, rgba.Pixels);
        var texture = ImageTexture.CreateFromImage(image);
        _flowerShader ??= GD.Load<Shader>("res://Shaders/StatusFlower.gdshader");
        _flowerQuad ??= new QuadMesh { Size = Vector2.One };
        var material = new ShaderMaterial { Shader = _flowerShader };
        material.SetShaderParameter("art", texture);
        batch = new FlowerBatch(texture, material, CreateItemBatch(_flowerQuad.GetRid(), material.GetRid(), BlendedItemBlend.Ordered));
        _flowerBatches.Add(name, batch);
        _flowerByReference.Add(name, batch);
        return batch;
    }

    private void FreeFlowers()
    {
        FreeDollarFlowers();
        foreach (FlowerBatch batch in _flowerBatches.Values)
        {
            batch.Material.Dispose();
            batch.Texture.Dispose();
        }
        _flowerBatches.Clear();
        _flowerByReference.Clear();
        _flowerQuad?.Dispose();
        _flowerQuad = null;
    }
}

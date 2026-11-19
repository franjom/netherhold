// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Core;
using DK2.Presentation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The status flowers (Unity's <c>ClassicStatusFlowers</c>, plan step 3k): each layer
/// <see cref="StatusFlowerLayers.Build"/> gives a camera-facing quad at <see cref="StatusFlowerRules.LayerPlacement"/>, an
/// instance of its icon's batch among the blended items, so it joins DK2's one back-to-front list (0x0057F7E0 queues the
/// A_ sprite with batch state 0x200; 0x0058B9D0) as Unity's <c>TransparentTerrain.PlaceItems</c> places it: a blended run
/// or an effect nearer than the icon draws over it. The tint is the instance colour, the experience wedge and the ring
/// its custom data (<c>Shaders/StatusFlower.gdshader</c>). The dollar flower's mesh is not drawn (Unity's
/// <c>ClassicFlowerDollar</c>).</summary>
public partial class WorldScene
{
    private readonly StatusFlowerLayer[] _flowerLayers = new StatusFlowerLayer[StatusFlowerLayers.MaxLayers];
    private readonly Dictionary<string, FlowerBatch> _flowerBatches = new();
    // The same batches by the icon name's reference: the shared layers hand out the same strings each frame, so this skips
    // hashing the name; a new reference falls back to the name and is remembered.
    private readonly Dictionary<string, FlowerBatch> _flowerByReference = new(ReferenceEqualityComparer.Instance);
    private Shader? _flowerShader;
    private QuadMesh? _flowerQuad;

    /// <summary>Adds this frame's flowers over the drawn creatures as blended items; call before
    /// <see cref="DrawBlendedItems"/>. <paramref name="art"/> reads an icon by its name under
    /// <see cref="StatusFlowerLayers.Folder"/>, once per icon.</summary>
    public void DrawFlowers(IReadOnlyList<StatusFlower> flowers, bool possessing, Func<string, RgbaImage> art)
    {
        if (View is not MinimapCameraView view || flowers.Count == 0) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        NVector3 forward = NVector3.Normalize(view.Forward);
        NVector3 right = NVector3.Normalize(NVector3.Cross(view.Up, forward));
        NVector3 up = NVector3.Cross(forward, right);
        Vector3 x = ToGodot(RightHandedFrame.Direction(right)), y = ToGodot(RightHandedFrame.Direction(up));
        Vector3 z = -ToGodot(RightHandedFrame.Direction(forward));
        for (int f = 0; f < flowers.Count; f++)
        {
            StatusFlower flower = flowers[f];
            if (!TryGetCreaturePosition(new(flower.CreatureId), out NVector3 feet)) continue;
            NVector3 anchor = feet + NVector3.UnitY * (flower.Height.Raw / (float)Fp32.One);
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
        ItemTime(ItemStep.Flowers, started);
        ItemBytes(allocated);
    }

    /// <summary>One icon's batch: its texture and material, loaded on first use.</summary>
    private sealed class FlowerBatch(ImageTexture texture, ShaderMaterial material, ItemBatch items)
    {
        public readonly ImageTexture Texture = texture;
        public readonly ShaderMaterial Material = material;
        public readonly ItemBatch Items = items;
    }

    private FlowerBatch FlowerBatchFor(string name, Func<string, RgbaImage> art)
    {
        if (_flowerByReference.TryGetValue(name, out FlowerBatch? batch)) return batch;
        if (_flowerBatches.TryGetValue(name, out batch))
        {
            if (_flowerByReference.Count < 256) _flowerByReference.Add(name, batch);
            return batch;
        }
        RgbaImage rgba = art(name);
        using Image image = Image.CreateFromData(rgba.Width, rgba.Height, false, Image.Format.Rgba8, rgba.Pixels);
        ImageTexture texture = ImageTexture.CreateFromImage(image);
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

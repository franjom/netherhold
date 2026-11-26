// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Gauges masked on the GPU (audit item 11): each gauge slot is a sub-viewport the size of its image that
/// <c>Shaders/HudGauge.gdshader</c> fills once per fraction change, comparing each texel's <see cref="HudGaugeMask"/> rank
/// with the lit count. The canvas samples that texture exactly as it sampled the CPU-filled one, so a moving gauge makes
/// no texture and uploads nothing.</summary>
public partial class HudCanvas
{
    private static readonly StringName LitCount = "lit_count", Ranks = "ranks";
    private readonly Dictionary<HudImage, GaugeSlot> _gauges = new();
    private Shader? _gaugeShader;

    private sealed class GaugeSlot
    {
        public HudGaugeMask Mask = null!;
        public ImageTexture Source = null!, RankTexture = null!;
        public SubViewport Viewport = null!;
        public ShaderMaterial Material = null!;
        public Rid Item, Texture;
        public HudImage Current;
        public int Lit = -1;
    }

    private Resolved Gauge(HudImage image)
    {
        if (!_gauges.TryGetValue(image.Slot, out GaugeSlot? slot)) _gauges.Add(image.Slot, slot = NewGauge(image));
        if (slot.Current != image)
        {
            slot.Current = image;
            int lit = slot.Mask.LitCount(image.Gauge);
            if (lit != slot.Lit)
            {
                slot.Lit = lit;
                slot.Material.SetShaderParameter(LitCount, (float)lit);
                slot.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            }
        }
        return new Resolved(slot.Texture, new Vector2(slot.Mask.Width, slot.Mask.Height), false);
    }

    private GaugeSlot NewGauge(HudImage image)
    {
        DK2.Art.Textures.RgbaImage source = Catalogue(image.Path!);
        var mask = new HudGaugeMask(source.Width, source.Height);
        var slot = new GaugeSlot { Mask = mask, Current = image.Slot };
        using (var pixels = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, source.Pixels))
            slot.Source = ImageTexture.CreateFromImage(pixels);
        byte[] ranks = new byte[mask.Ranks.Length * 4];
        Buffer.BlockCopy(mask.Ranks, 0, ranks, 0, ranks.Length);
        using (var rankImage = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rf, ranks))
            slot.RankTexture = ImageTexture.CreateFromImage(rankImage);
        _gaugeShader ??= GD.Load<Shader>("res://Shaders/HudGauge.gdshader");
        slot.Material = new ShaderMaterial { Shader = _gaugeShader };
        slot.Material.SetShaderParameter(Ranks, slot.RankTexture);
        slot.Viewport = new SubViewport
        {
            Size = new Vector2I(source.Width, source.Height), TransparentBg = true, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(slot.Viewport);
        slot.Texture = slot.Viewport.GetTexture().GetRid();
        slot.Item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(slot.Item, slot.Viewport.World2D.Canvas);
        RenderingServer.CanvasItemSetMaterial(slot.Item, slot.Material.GetRid());
        RenderingServer.CanvasItemAddTextureRect(slot.Item, new Rect2(0, 0, source.Width, source.Height), slot.Source.GetRid());
        slot.Lit = mask.LitCount(image.Gauge);
        slot.Material.SetShaderParameter(LitCount, (float)slot.Lit);
        slot.Current = image;
        return slot;
    }

    private void ReleaseGauges()
    {
        foreach (GaugeSlot slot in _gauges.Values)
        {
            RenderingServer.FreeRid(slot.Item);
            slot.Viewport.QueueFree();
            slot.Material.Dispose(); slot.Source.Dispose(); slot.RankTexture.Dispose();
        }
        _gauges.Clear();
    }
}

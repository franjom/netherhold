// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Glow frames baked once per glowing label or image (audit item 11): the full-strength glow, which is quarter 4,
/// and a second texture holding quarters 1 to 3's alphas in its red, green and blue. A partial quarter is drawn through
/// <c>Shaders/HudGlow.gdshader</c> with the second texture as the draw's normal map, so the hardware filters the
/// quarter's alpha exactly as it filtered the separate quarter texture, and the colour, which every glow texel shares,
/// comes from the full texture as before. Two textures per glow, made together, instead of four.</summary>
public partial class HudCanvas
{
    private static readonly StringName Quarter = "quarter";
    private readonly Dictionary<HudLabel, GlowPack> _labelGlows = new();
    private readonly Dictionary<string, GlowPack> _imageGlows = new();
    private readonly List<HudLabel> _staleGlows = new();
    private readonly ShaderMaterial?[] _glowMaterials = new ShaderMaterial?[4];

    private sealed class GlowPack
    {
        public ImageTexture Full = null!, Quarters = null!;
        public CanvasTexture Partial = null!;

        public void Dispose() { Partial.Dispose(); Full.Dispose(); Quarters.Dispose(); }
    }

    private Resolved Glow(HudImage image)
    {
        GlowPack pack;
        if (image.Label != null)
        {
            if (!_labelGlows.TryGetValue(image.Label, out pack!))
            {
                _labelGlows.Add(image.Label, pack = Bake(HudLabelPixels.Render(image.Label, HudLabel.GlowPadding)));
            }
        }
        else if (!_imageGlows.TryGetValue(image.Path!, out pack!))
        {
            _imageGlows.Add(image.Path!, pack = Bake(HudLabelPixels.Pad(Catalogue(image.Path!), FrontEndScrollBar.GlowPadding)));
        }

        var size = new Vector2(pack.Full.GetWidth(), pack.Full.GetHeight());
        if (image.Pieces >= 4)
        {
            return new Resolved(pack.Full.GetRid(), size, false);
        }

        return new Resolved(pack.Partial.GetRid(), size, false, GlowMaterial(image.Pieces));
    }

    private static GlowPack Bake(RgbaImage padded)
    {
        RgbaImage full = HudLabelPixels.Glow(padded);
        byte[] quarters = new byte[full.Pixels.Length];
        for (int q = 1; q <= 3; q++)
        {
            byte[] alpha = HudLabelPixels.Quarter(full, q).Pixels;
            for (int at = 3; at < alpha.Length; at += 4)
            {
                quarters[at - 4 + q] = alpha[at];
            }
        }
        for (int at = 3; at < quarters.Length; at += 4)
        {
            quarters[at] = 255;
        }

        var pack = new GlowPack();
        using (var image = Image.CreateFromData(full.Width, full.Height, false, Image.Format.Rgba8, full.Pixels))
        {
            pack.Full = ImageTexture.CreateFromImage(image);
        }

        using (var image = Image.CreateFromData(full.Width, full.Height, false, Image.Format.Rgba8, quarters))
        {
            pack.Quarters = ImageTexture.CreateFromImage(image);
        }

        pack.Partial = new CanvasTexture { DiffuseTexture = pack.Full, NormalTexture = pack.Quarters };
        return pack;
    }

    private Rid GlowMaterial(int quarter)
    {
        ShaderMaterial? material = _glowMaterials[quarter];
        if (material == null)
        {
            material = _glowMaterials[quarter] = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/HudGlow.gdshader") };
            material.SetShaderParameter(Quarter, quarter);
        }
        return material.GetRid();
    }

    /// <summary>A label's glow is kept while the label is on the page, so the hover fade makes nothing.</summary>
    private void ReleaseUnusedGlows()
    {
        if (_labelGlows.Count == 0)
        {
            return;
        }

        _staleGlows.Clear();
        foreach (HudLabel label in _labelGlows.Keys)
        {
            if (!_labelsDrawn.Contains(label))
            {
                _staleGlows.Add(label);
            }
        }

        foreach (HudLabel label in _staleGlows) { _labelGlows[label].Dispose(); _labelGlows.Remove(label); }
    }

    private void ReleaseGlows()
    {
        foreach (GlowPack pack in _labelGlows.Values)
        {
            pack.Dispose();
        }

        foreach (GlowPack pack in _imageGlows.Values)
        {
            pack.Dispose();
        }

        _labelGlows.Clear(); _imageGlows.Clear();
        for (int q = 0; q < _glowMaterials.Length; q++) { _glowMaterials[q]?.Dispose(); _glowMaterials[q] = null; }
    }
}

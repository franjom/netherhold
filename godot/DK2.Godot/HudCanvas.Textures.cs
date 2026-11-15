// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The canvas's textures, one per image (variant applied, as Unity's <c>HudCanvas.Texture</c>). Godot blends
/// additively itself, so unlike Unity an image's pixels do not depend on its draw's blend.</summary>
public partial class HudCanvas
{
    private readonly Dictionary<HudImage, ImageTexture> _textures = new();
    private readonly Dictionary<HudImage, LiveTexture> _live = new();
    private readonly HashSet<HudImage> _textInUse = new();
    private readonly List<HudImage> _stale = new();
    /// <summary>Every label the shown frame draws in any form; its glow frames are kept while it is on the page.</summary>
    private readonly HashSet<HudLabel> _labelsDrawn = new();
    /// <summary>Each glowing label's full-strength glow, computed once; the quarters are cut from it.</summary>
    private readonly Dictionary<HudLabel, RgbaImage> _glowBase = new();
    private readonly List<HudLabel> _staleGlow = new();
    private int _catalogueCount;

    private sealed class LiveTexture
    {
        public RgbaImage Source = null!;
        public byte[] Pixels = null!;
        public Image Image = null!;
        public ImageTexture Texture = null!;
        public HudImage Current;
    }

    /// <summary>Decodes an image now rather than at the first frame that draws it (Unity's <c>HudCanvas.Preload</c>);
    /// <see cref="Load"/> must be set.</summary>
    public void Preload(HudImage image) => Texture(image);

    private Resolved Texture(HudImage image)
    {
        if (image.IsNone) return default;
        ImageTexture texture = image.IsLive ? Live(image) : Cached(image);
        // Set text keeps hard bitmap edges when the scale stretches it, as Unity's point-filtered text did.
        return new Resolved(texture.GetRid(), texture.GetSize(), image.Text != null);
    }

    private ImageTexture Cached(HudImage image)
    {
        if (_textures.TryGetValue(image, out ImageTexture? texture)) return texture;
        RgbaImage source;
        bool glow = image.Variant == HudImageVariant.Glow;
        if (image.Label != null && glow)
        {
            if (!_glowBase.TryGetValue(image.Label, out RgbaImage? full))
                _glowBase.Add(image.Label, full = HudLabelPixels.Glow(HudLabelPixels.Render(image.Label, HudLabel.GlowPadding)));
            source = HudLabelPixels.Quarter(full, image.Pieces);
            glow = false;
        }
        else if (image.Label != null) source = HudLabelPixels.Render(image.Label);
        else if (image.Text != null)
        {
            byte[] set = HudTextPixels.Render(image.Text, out int width, out int height);
            source = new RgbaImage(width, height, set);
        }
        else
        {
            source = Catalogue(image.Path!);
            _catalogueCount++;
            if (glow) source = HudLabelPixels.Pad(source, FrontEndScrollBar.GlowPadding);
        }
        if (glow) source = HudLabelPixels.GlowFrame(source, image.Pieces);
        byte[] pixels = HudImagePixels.Apply(source.Pixels, source.Width, source.Height, image);
        using Image decoded = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, pixels);
        texture = ImageTexture.CreateFromImage(decoded);
        _textures.Add(image, texture);
        return texture;
    }

    private RgbaImage Catalogue(string path) =>
        (Load ?? throw new InvalidOperationException("HudCanvas.Load is not set."))(path);

    /// <summary>A live image keeps one texture per <see cref="HudImage.Slot"/>, refilled in place when its parameter
    /// changes (a gauge's fraction), so a moving gauge creates no textures.</summary>
    private ImageTexture Live(HudImage image)
    {
        if (!_live.TryGetValue(image.Slot, out LiveTexture? live))
        {
            RgbaImage source = Catalogue(image.Path!);
            live = new LiveTexture { Source = source, Pixels = new byte[source.Pixels.Length] };
            HudImagePixels.Fill(source.Pixels, source.Width, source.Height, image, live.Pixels);
            live.Image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, live.Pixels);
            live.Texture = ImageTexture.CreateFromImage(live.Image);
            live.Current = image;
            _live.Add(image.Slot, live);
            return live.Texture;
        }
        if (live.Current == image) return live.Texture;
        live.Current = image;
        RgbaImage s = live.Source;
        HudImagePixels.Fill(s.Pixels, s.Width, s.Height, image, live.Pixels);
        live.Image.SetData(s.Width, s.Height, false, Image.Format.Rgba8, live.Pixels);
        live.Texture.Update(live.Image);
        return live.Texture;
    }

    /// <summary>A text or label texture no frame draws any more is freed, as Unity's canvas destroys it; a label's glow
    /// frames stay while the label is on the page, so the hover fade makes no textures.</summary>
    private void ReleaseUnusedText()
    {
        if (_glowBase.Count > 0)
        {
            _staleGlow.Clear();
            foreach (HudLabel label in _glowBase.Keys) if (!_labelsDrawn.Contains(label)) _staleGlow.Add(label);
            foreach (HudLabel label in _staleGlow) _glowBase.Remove(label);
        }
        if (_textures.Count == _textInUse.Count + _catalogueCount) return;
        _stale.Clear();
        foreach (HudImage image in _textures.Keys)
        {
            if (!image.IsGenerated || _textInUse.Contains(image)) continue;
            if (image.Variant == HudImageVariant.Glow && image.Label != null && _labelsDrawn.Contains(image.Label)) continue;
            _stale.Add(image);
        }
        foreach (HudImage image in _stale) { _textures[image].Dispose(); _textures.Remove(image); }
    }

    private void ReleaseTextures()
    {
        foreach (ImageTexture texture in _textures.Values) texture.Dispose();
        foreach (LiveTexture live in _live.Values) { live.Texture.Dispose(); live.Image.Dispose(); }
        _textures.Clear(); _live.Clear(); _glowBase.Clear(); _catalogueCount = 0;
    }
}

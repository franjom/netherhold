// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The canvas's textures, one per image (variant applied, as Unity's <c>HudCanvas.Texture</c>). Godot blends
/// additively itself, so unlike Unity an image's pixels do not depend on its draw's blend. Gauges, glows and set text
/// drawn one texel per pixel have their own paths (<c>HudCanvas.Gauges.cs</c>, <c>.Glows.cs</c>, <c>.Atlas.cs</c>).</summary>
public partial class HudCanvas
{
    private readonly Dictionary<HudImage, ImageTexture> _textures = new();
    private readonly HashSet<HudImage> _textInUse = new();
    private readonly List<HudImage> _stale = new();
    /// <summary>Every label the shown frame draws in any form; its glow is kept while it is on the page.</summary>
    private readonly HashSet<HudLabel> _labelsDrawn = new();
    private int _catalogueCount;

    /// <summary>Decodes an image now rather than at the first frame that draws it (Unity's <c>HudCanvas.Preload</c>);
    /// <see cref="Load"/> must be set.</summary>
    public void Preload(HudImage image) => Texture(image);

    private Resolved Texture(HudImage image)
    {
        if (image.IsNone) return default;
        if (image.IsLive) return Gauge(image);
        if (image.Variant == HudImageVariant.Glow) return Glow(image);
        ImageTexture texture = Cached(image);
        // Set text keeps hard bitmap edges when the scale stretches it, as Unity's point-filtered text did.
        return new Resolved(texture.GetRid(), texture.GetSize(), image.Text != null);
    }

    private ImageTexture Cached(HudImage image)
    {
        if (_textures.TryGetValue(image, out ImageTexture? texture)) return texture;
        RgbaImage source;
        if (image.Label != null) source = HudLabelPixels.Render(image.Label);
        else if (image.Text != null)
        {
            byte[] set = HudTextPixels.Render(image.Text, out int width, out int height);
            source = new RgbaImage(width, height, set);
        }
        else
        {
            source = Catalogue(image.Path!);
            _catalogueCount++;
        }
        byte[] pixels = HudImagePixels.Apply(source.Pixels, source.Width, source.Height, image);
        using Image decoded = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, pixels);
        texture = ImageTexture.CreateFromImage(decoded);
        _textures.Add(image, texture);
        return texture;
    }

    private RgbaImage Catalogue(string path) =>
        (Load ?? throw new InvalidOperationException("HudCanvas.Load is not set."))(path);

    /// <summary>A text or label texture no frame draws any more is freed, as Unity's canvas destroys it.</summary>
    private void ReleaseUnusedText()
    {
        if (_textures.Count == _textInUse.Count + _catalogueCount) return;
        _stale.Clear();
        foreach (HudImage image in _textures.Keys)
            if (image.IsGenerated && !_textInUse.Contains(image)) _stale.Add(image);
        foreach (HudImage image in _stale) { _textures[image].Dispose(); _textures.Remove(image); }
    }

    private void ReleaseTextures()
    {
        foreach (ImageTexture texture in _textures.Values) texture.Dispose();
        _textures.Clear(); _catalogueCount = 0;
    }
}

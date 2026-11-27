// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Textures;
using DK2.Data.Wad;

namespace DK2.Presentation;

/// <summary>The hand cursor as one <see cref="HudFrame"/> quad, for a renderer that draws it through its HUD canvas (the Godot
/// adapter, plan step 3e): the kind's sheet with its green keyed out, the frame's row, placed by <see cref="HandCursor.Place"/>
/// and scaled by the HUD factor about the pointer.</summary>
public static class HandCursorHud
{
    /// <summary>The kind's sprite sheet as a HUD image: its file name in Sprite.WAD, green keyed.</summary>
    public static HudImage Sheet(HandCursorKind kind)
    {
        return new(HandCursor.Sprite(kind).FileName, HudImageVariant.GreenKey);
    }

    /// <summary>Adds frame <paramref name="sheetFrame"/> of <paramref name="kind"/> to <paramref name="frame"/>, in the virtual
    /// units of <paramref name="screen"/> (a canvas presented at its scale and one pixel a unit).</summary>
    /// <param name="pointerPixels">The pointer in screen pixels from the top left.</param>
    /// <param name="hudFactor">The HUD scale factor (<see cref="HudScaleLayout.Factor"/>).</param>
    public static void Draw(HudFrame frame, HandCursorKind kind, int sheetFrame, Vector2 pointerPixels, HudScreen screen,
        float hudFactor)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (kind == HandCursorKind.Default)
        {
            return;
        }

        HandCursorSprite sprite = HandCursor.Sprite(kind);
        HandCursorPlacement placed = HandCursor.Place(kind, sheetFrame, pointerPixels, screen.Height);
        Vector2 origin = pointerPixels + (placed.Origin - pointerPixels) * hudFactor;
        Vector2 size = placed.Size * hudFactor;
        float rows = sprite.FrameCount;
        frame.Add(new HudDraw(HudDrawKind.Quad, Sheet(kind),
            new HudRect(screen.Virtual(origin.X), screen.Virtual(origin.Y), screen.Virtual(size.X), screen.Virtual(size.Y)),
            HudColor.White, Uv: new HudRect(0, sheetFrame / rows, 1, 1 / rows)));
    }
}

/// <summary>The hand cursor sheets of the install's Sprite.WAD, decoded by the engine's PNG reader and checked against the
/// native frame table (<see cref="HandCursor.Sprite"/>): FrameSize wide, FrameSize x FrameCount high.</summary>
public sealed class InstallHandCursorSheets : IDisposable
{
    private readonly WadArchive _archive;
    private readonly Func<byte[], RgbaImage> _decode;

    public InstallHandCursorSheets(string install, Func<byte[], RgbaImage> decodePng)
    {
        if (install is null)
        {
            throw new ArgumentNullException(nameof(install));
        }

        _decode = decodePng ?? throw new ArgumentNullException(nameof(decodePng));
        _archive = WadArchive.Open(Path.Combine(install, "Data", "Sprite.WAD"));
    }

    /// <summary>A sheet by its file name (<see cref="HandCursorSprite.FileName"/>).</summary>
    /// <exception cref="InvalidDataException">No cursor kind uses the name, or the sheet's size is not its table's.</exception>
    public RgbaImage Load(string fileName)
    {
        HandCursorSprite? sprite = null;
        foreach (HandCursorKind kind in Enum.GetValues(typeof(HandCursorKind)))
        {
            if (kind != HandCursorKind.Default && HandCursor.Sprite(kind).FileName == fileName) { sprite = HandCursor.Sprite(kind); break; }
        }

        if (sprite is not HandCursorSprite table)
        {
            throw new InvalidDataException("Not a hand cursor sheet: " + fileName);
        }

        RgbaImage image = _decode(_archive.Extract(fileName));
        if (image.Width != (int)table.FrameSize.X || image.Height != (int)table.FrameSize.Y * table.FrameCount)
        {
            throw new InvalidDataException("Cursor sheet does not match its native frame table: " + fileName);
        }

        return image;
    }

    public void Dispose()
    {
        _archive.Dispose();
    }
}

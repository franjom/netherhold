// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The full map's image (ADR-0805) as RGBA bytes, top row first, one pixel per tile: each cell of the minimap's
/// class grid in its palette colour, opaque, and the unknown class (1) black, as the full map draws it where the minimap
/// draws its rock backdrop.</summary>
public sealed class FullMapImage
{
    private const uint Black = 0xFF000000;
    private readonly byte[] _pixels;

    public FullMapImage(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Width = width; Height = height;
        _pixels = new byte[checked(width * height * 4)];
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Width x height RGBA pixels, rows top to bottom; transparent until the first <see cref="Update"/>.</summary>
    public ReadOnlySpan<byte> Pixels
    {
        get
        {
            return _pixels;
        }
    }

    /// <summary>Recolours every pixel from the grid and the palette (its neutral cycle included).</summary>
    /// <returns>Whether any pixel changed, so an unchanged image need not be uploaded.</returns>
    public bool Update(MinimapClassGrid grid, MinimapPalette palette)
    {
        if (grid is null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        if (palette is null)
        {
            throw new ArgumentNullException(nameof(palette));
        }

        if (grid.Width != Width || grid.Height != Height)
        {
            throw new ArgumentException("The grid is not the image's size.", nameof(grid));
        }

        ReadOnlySpan<MinimapColourClass> cells = grid.Cells;
        ReadOnlySpan<uint> colours = palette.Colours;
        ushort unknown = MinimapColourClass.Unknown.Index;
        bool changed = false;
        for (int i = 0, p = 0; i < cells.Length; i++, p += 4)
        {
            ushort colourClass = cells[i].Index;
            uint argb = colourClass == unknown ? Black : colours[colourClass] | Black;
            byte r = (byte)(argb >> 16), g = (byte)(argb >> 8), b = (byte)argb;
            if (_pixels[p] == r && _pixels[p + 1] == g && _pixels[p + 2] == b && _pixels[p + 3] == 0xFF)
            {
                continue;
            }

            _pixels[p] = r; _pixels[p + 1] = g; _pixels[p + 2] = b; _pixels[p + 3] = 0xFF;
            changed = true;
        }
        return changed;
    }
}

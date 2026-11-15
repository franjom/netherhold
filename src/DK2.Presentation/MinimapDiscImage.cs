// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The minimap's disc (ADR-0805) as RGBA bytes, top row first, straight alpha, at display resolution: DK2's
/// 128x128 tile raster (classes in their palette colours, class 1 and the raster outside the map the rock backdrop),
/// rotated by the camera's heading and cut to a circle with a one-pixel soft rim, then the markers and the Heart trail
/// over it. For an engine that uploads pixels; Unity draws the same disc on the GPU (<c>Resources/DK2Minimap.shader</c>,
/// passes 1 and 2, fed by <c>MinimapGpuMap</c>), so the two change together. The shader's per-pixel derivatives
/// (<c>fwidth</c>) are taken analytically here. Rules: minimap-rendering.md.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/minimap-rendering.md")]
public sealed class MinimapDiscImage
{
    /// <summary>The raster DK2 refreshes: 128x128, its centre at 64.</summary>
    private const float Raster = 128, Half = 64;
    private readonly byte[] _backdrop;
    private readonly int _backdropWidth, _backdropHeight;
    private readonly MinimapRasterPoint[] _trail = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
    private byte[] _pixels = Array.Empty<byte>();

    /// <param name="backdrop">The rock backdrop (<c>Map-BG.PNG</c>, 128x128), decoded to RGBA, top row first.</param>
    public MinimapDiscImage(RgbaImage backdrop)
    {
        if (backdrop is null) throw new ArgumentNullException(nameof(backdrop));
        if (backdrop.Width <= 0 || backdrop.Height <= 0 || backdrop.Pixels.Length < backdrop.Width * backdrop.Height * 4)
            throw new ArgumentException("The backdrop has no pixels.", nameof(backdrop));
        _backdrop = backdrop.Pixels; _backdropWidth = backdrop.Width; _backdropHeight = backdrop.Height;
    }

    /// <summary>The side of the last render, in pixels; 0 before the first.</summary>
    public int Diameter { get; private set; }
    /// <summary>Diameter x diameter RGBA pixels, rows top to bottom.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels.AsSpan(0, Diameter * Diameter * 4);

    /// <summary>Draws the disc.</summary>
    /// <param name="diameter">The side in pixels (<see cref="MinimapHud.DiscPixels"/>).</param>
    /// <param name="focus">The camera's focus, which the zoomed raster centres on.</param>
    /// <param name="heading">The camera's heading, which turns the raster under the disc.</param>
    /// <param name="heart">The viewer's Heart tile for the zoomed trail (<see cref="WorldView.MinimapHeartTile"/>).</param>
    /// <param name="tick">The captured world turn, which steps the trail.</param>
    public void Render(int diameter, MinimapClassGrid grid, MinimapPalette palette, IReadOnlyList<MinimapMarker> markers,
        MinimapZoom zoom, FpPosition3 focus, Dk2Angle heading, TilePos? heart, GameTick tick)
    {
        if (diameter <= 0 || diameter > 4096) throw new ArgumentOutOfRangeException(nameof(diameter));
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        if (palette is null) throw new ArgumentNullException(nameof(palette));
        if (markers is null) throw new ArgumentNullException(nameof(markers));
        if (_pixels.Length < diameter * diameter * 4) _pixels = new byte[diameter * diameter * 4];
        Diameter = diameter;
        float angle = heading.Value * (MathF.PI * 2 / 2048);
        var turn = new Turn(MathF.Sin(angle), MathF.Cos(angle), diameter);
        var map = new MapRaster(grid.Width, grid.Height, zoom, focus);
        DrawTiles(turn, map, grid, palette);
        for (int i = 0; i < markers.Count; i++)
        {
            MinimapMarker marker = markers[i];
            if (Place(marker, map, out Shape shape)) DrawMarker(turn, shape, marker.ColourArgb);
        }
        // After the ordinary markers: one opaque white raster pixel per dot (the shader's kind 6).
        int dots = heart.HasValue ? MinimapHeartTrail.Build(heart.Value, focus, zoom, tick, _trail) : 0;
        for (int i = 0; i < dots; i++)
            DrawMarker(turn, new Shape(ShapeKind.Solid, _trail[i].X + .5f, _trail[i].Y + .5f, 1, 1, 0), 0xFFFFFFFF);
    }

    /// <summary>A display pixel's raster point: the pixel centre in [-1, 1] across the disc (y down), turned by the
    /// heading, then 64 raster units a unit about the raster's centre. <see cref="MinimapNavigation"/> inverts it.</summary>
    private readonly struct Turn
    {
        public readonly float Sin, Cos;
        /// <summary>Raster units per display pixel.</summary>
        public readonly float Step;
        public readonly int Diameter;

        public Turn(float sin, float cos, int diameter) { Sin = sin; Cos = cos; Diameter = diameter; Step = Raster / diameter; }

        public float Disc(int pixel) => (pixel + .5f) * 2 / Diameter - 1;
        public float RasterX(float x, float y) => (Cos * x - Sin * y) * Half + Half;
        public float RasterY(float x, float y) => (Sin * x + Cos * y) * Half + Half;
        /// <summary>The display pixel whose centre a raster point falls on, unrounded.</summary>
        public float PixelX(float rx, float ry) => ((Cos * (rx - Half) + Sin * (ry - Half)) / Half + 1) * Diameter / 2 - .5f;
        public float PixelY(float rx, float ry) => ((-Sin * (rx - Half) + Cos * (ry - Half)) / Half + 1) * Diameter / 2 - .5f;
        /// <summary>A value's per-pixel change (<c>fwidth</c>) from its gradient in raster units.</summary>
        public float Width(float gx, float gy) => (MathF.Abs(Cos * gx + Sin * gy) + MathF.Abs(-Sin * gx + Cos * gy)) * Step;

        /// <summary>The disc's coverage at a point in [-1, 1]: one pixel of soft rim inside the unit circle.</summary>
        public float Coverage(float x, float y)
        {
            float radius = MathF.Sqrt(x * x + y * y);
            if (radius <= 0) return 1;
            float width = (MathF.Abs(x) + MathF.Abs(y)) / radius * (2f / Diameter);
            return 1 - SmoothStep(1 - width, 1, radius);
        }
    }

    /// <summary>Where the map sits in the raster: the integer whole-map fit, or the zoomed origin around the focus.</summary>
    private readonly struct MapRaster
    {
        public readonly int Width, Height, Level;
        public readonly MinimapMapBounds Fit;
        public readonly MinimapRasterPoint Origin;
        public readonly int FocusX, FocusY;

        public MapRaster(int width, int height, MinimapZoom zoom, FpPosition3 focus)
        {
            Width = width; Height = height; Level = zoom.Level;
            Fit = MinimapMapBounds.Fit(width, height);
            Origin = zoom.IsWholeMap ? default : zoom.RasterOrigin(focus);
            FocusX = zoom.IsWholeMap ? 0 : focus.X.Raw >> (12 - Level);
            FocusY = zoom.IsWholeMap ? 0 : focus.Y.Raw >> (12 - Level);
        }

        public bool WholeMap => Level < 0;
        /// <summary>A tile's raster corner in the whole-map fit.</summary>
        public int FitX(int tile) => tile * Fit.Width / Width + Fit.Left;
        public int FitY(int tile) => tile * Fit.Height / Height + Fit.Top;
    }

    private void DrawTiles(Turn turn, MapRaster map, MinimapClassGrid grid, MinimapPalette palette)
    {
        ReadOnlySpan<MinimapColourClass> cells = grid.Cells;
        ReadOnlySpan<uint> colours = palette.Colours;
        ushort unknown = MinimapColourClass.Unknown.Index;
        float scaleX = map.Width / (float)Math.Max(map.Fit.Width, 1), scaleY = map.Height / (float)Math.Max(map.Fit.Height, 1);
        int d = turn.Diameter;
        byte[] pixels = _pixels;
        for (int py = 0, p = 0; py < d; py++)
        {
            float y = turn.Disc(py);
            for (int px = 0; px < d; px++, p += 4)
            {
                float x = turn.Disc(px);
                float alpha = turn.Coverage(x, y);
                if (alpha <= 0) { pixels[p] = pixels[p + 1] = pixels[p + 2] = pixels[p + 3] = 0; continue; }
                float rx = turn.RasterX(x, y), ry = turn.RasterY(x, y);
                float tx, ty;
                if (map.WholeMap) { tx = (rx - map.Fit.Left) * scaleX; ty = (ry - map.Fit.Top) * scaleY; }
                else
                {
                    float step = 1 << map.Level;
                    tx = (MathF.Floor(rx) - map.Origin.X) / step; ty = (MathF.Floor(ry) - map.Origin.Y) / step;
                }
                ushort colourClass = unknown;
                if (tx >= 0 && ty >= 0 && tx < map.Width && ty < map.Height)
                    colourClass = cells[(int)ty * map.Width + (int)tx].Index;
                if (colourClass == unknown) Backdrop(rx, ry, pixels, p);
                else
                {
                    uint argb = colours[colourClass];
                    pixels[p] = (byte)(argb >> 16); pixels[p + 1] = (byte)(argb >> 8); pixels[p + 2] = (byte)argb;
                }
                pixels[p + 3] = (byte)(alpha * 255 + .5f);
            }
        }
    }

    /// <summary>The repeated rock at a raster point, bilinear as the backdrop texture is sampled (raster / 128 of it).
    /// Raster row 0 reads the PNG's top row, as minimap-rendering.md has Map-BG match the raster's indexing
    /// (0x0041ADA0, 0x0041C660); Unity's shader samples a <c>LoadImage</c> texture whose v=0 is the bottom row, so its rock
    /// is flipped vertically against this one.</summary>
    private void Backdrop(float rx, float ry, byte[] pixels, int p)
    {
        float u = rx / Raster * _backdropWidth - .5f, v = ry / Raster * _backdropHeight - .5f;
        float fu = MathF.Floor(u), fv = MathF.Floor(v);
        float wu = u - fu, wv = v - fv;
        int x0 = Wrap((int)fu, _backdropWidth), y0 = Wrap((int)fv, _backdropHeight);
        int x1 = Wrap(x0 + 1, _backdropWidth), y1 = Wrap(y0 + 1, _backdropHeight);
        int a = (y0 * _backdropWidth + x0) * 4, b = (y0 * _backdropWidth + x1) * 4;
        int c = (y1 * _backdropWidth + x0) * 4, e = (y1 * _backdropWidth + x1) * 4;
        for (int k = 0; k < 3; k++)
        {
            float top = _backdrop[a + k] + (_backdrop[b + k] - _backdrop[a + k]) * wu;
            float bottom = _backdrop[c + k] + (_backdrop[e + k] - _backdrop[c + k]) * wu;
            pixels[p + k] = (byte)(top + (bottom - top) * wv + .5f);
        }
    }

    private static int Wrap(int value, int size)
    {
        int r = value % size;
        return r < 0 ? r + size : r;
    }

    private enum ShapeKind : byte { Solid, Dot, Ring, Border }

    /// <summary>A marker's axis-aligned box in raster units and how it is filled.</summary>
    private readonly record struct Shape(ShapeKind Kind, float CentreX, float CentreY, float Width, float Height, int Radius);

    /// <summary>A marker's raster box, as the shader's marker pass places it: dots 3 across at the tile's raster corner,
    /// door strokes one unit wide along the tile's projected edge, rings sized by their radius, rectangles between their
    /// two corners. False for an empty box or a kind the minimap does not draw.</summary>
    private static bool Place(MinimapMarker marker, MapRaster map, out Shape shape)
    {
        shape = default;
        int tileX = marker.Position.X.Raw >> 12, tileY = marker.Position.Y.Raw >> 12;
        int originX, originY;
        if (map.WholeMap) { originX = map.FitX(tileX); originY = map.FitY(tileY); }
        else
        {
            originX = 64 + (marker.Position.X.Raw >> (12 - map.Level)) - map.FocusX;
            originY = 64 + (marker.Position.Y.Raw >> (12 - map.Level)) - map.FocusY;
        }
        int radius = marker.RadiusRasterPixels;
        if (!map.WholeMap && marker.ReportingDistanceTiles >= 0) radius = marker.ReportingDistanceTiles * (1 << map.Level);
        switch (marker.Kind)
        {
            case MinimapMarkerKind.VerticalDoor:
            case MinimapMarkerKind.HorizontalDoor:
            {
                bool vertical = marker.Kind == MinimapMarkerKind.VerticalDoor;
                int axisX = vertical ? 0 : 1, axisY = vertical ? 1 : 0;
                if (!map.WholeMap)
                {
                    originX -= axisX * ((marker.Position.X.Raw & 4095) >> (12 - map.Level));
                    originY -= axisY * ((marker.Position.Y.Raw & 4095) >> (12 - map.Level));
                }
                int spanX, spanY;
                if (map.WholeMap) { spanX = map.FitX(tileX + axisX) - originX; spanY = map.FitY(tileY + axisY) - originY; }
                else { spanX = axisX << map.Level; spanY = axisY << map.Level; }
                if (spanX * axisX + spanY * axisY <= 0) return false;
                float width = Math.Max(1 - axisX, spanX), height = Math.Max(1 - axisY, spanY);
                shape = new Shape(ShapeKind.Solid, originX + width * .5f, originY + height * .5f, width, height, 0);
                return true;
            }
            case MinimapMarkerKind.Ring:
            {
                float size = radius <= 1 ? radius * 2 + 1 : 2 * (radius + 1);
                ShapeKind kind = radius == 1 ? ShapeKind.Dot : radius > 1 ? ShapeKind.Ring : ShapeKind.Solid;
                shape = new Shape(kind, originX + .5f, originY + .5f, size, size, radius);
                return true;
            }
            case MinimapMarkerKind.Rectangle:
            {
                int endX = marker.EndPosition.X.Raw >> 12, endY = marker.EndPosition.Y.Raw >> 12;
                if (map.WholeMap) { endX = map.FitX(endX); endY = map.FitY(endY); }
                else
                {
                    endX = 64 + (marker.EndPosition.X.Raw >> (12 - map.Level)) - map.FocusX;
                    endY = 64 + (marker.EndPosition.Y.Raw >> (12 - map.Level)) - map.FocusY;
                }
                int width = Math.Max(endX - originX, 0), height = Math.Max(endY - originY, 0);
                if (width == 0 || height == 0) return false;
                shape = new Shape(ShapeKind.Border, originX + width * .5f, originY + height * .5f, width, height, 0);
                return true;
            }
            case MinimapMarkerKind.Creature:
            case MinimapMarkerKind.Trap:
                shape = new Shape(ShapeKind.Dot, originX + .5f, originY + .5f, 3, 3, 1);
                return true;
            default:
                // The full map's own kinds: the minimap's capture never lists them.
                return false;
        }
    }

    /// <summary>Blends one marker over the disc at display resolution: its coverage of each pixel whose centre falls in
    /// its box, times the disc's own coverage there; the disc's alpha is kept.</summary>
    private void DrawMarker(Turn turn, Shape shape, uint argb)
    {
        float halfW = shape.Width * .5f, halfH = shape.Height * .5f;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int corner = 0; corner < 4; corner++)
        {
            float rx = shape.CentreX + (corner == 1 || corner == 2 ? halfW : -halfW);
            float ry = shape.CentreY + (corner >= 2 ? halfH : -halfH);
            float x = turn.PixelX(rx, ry), y = turn.PixelY(rx, ry);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        int d = turn.Diameter;
        int left = Math.Max(0, (int)MathF.Floor(minX)), right = Math.Min(d - 1, (int)MathF.Ceiling(maxX));
        int top = Math.Max(0, (int)MathF.Floor(minY)), bottom = Math.Min(d - 1, (int)MathF.Ceiling(maxY));
        float r = (byte)(argb >> 16), g = (byte)(argb >> 8), b = (byte)argb;
        byte[] pixels = _pixels;
        for (int py = top; py <= bottom; py++)
        {
            float y = turn.Disc(py);
            for (int px = left; px <= right; px++)
            {
                float x = turn.Disc(px);
                float rx = turn.RasterX(x, y), ry = turn.RasterY(x, y);
                float lx = (rx - shape.CentreX) / halfW, ly = (ry - shape.CentreY) / halfH;
                if (lx < -1 || lx > 1 || ly < -1 || ly > 1) continue;
                float alpha = turn.Coverage(x, y) * Fill(turn, shape, lx, ly, rx, ry);
                if (alpha <= 0) continue;
                int p = (py * d + px) * 4;
                pixels[p] = (byte)(pixels[p] + (r - pixels[p]) * alpha + .5f);
                pixels[p + 1] = (byte)(pixels[p + 1] + (g - pixels[p + 1]) * alpha + .5f);
                pixels[p + 2] = (byte)(pixels[p + 2] + (b - pixels[p + 2]) * alpha + .5f);
            }
        }
    }

    /// <summary>A marker's own coverage at a point of its box, <paramref name="lx"/> and <paramref name="ly"/> in [-1, 1]
    /// across it: a filled circle with a half-pixel soft edge, a ring one raster unit thick, a one-unit border, or solid.</summary>
    private static float Fill(Turn turn, Shape shape, float lx, float ly, float rx, float ry)
    {
        switch (shape.Kind)
        {
            case ShapeKind.Dot:
            case ShapeKind.Ring:
            {
                float length = MathF.Sqrt(lx * lx + ly * ly);
                // The gradient of the length in raster units: local changes by 2 / size a raster unit.
                float width = length > 0 ? turn.Width(lx / length * 2 / shape.Width, ly / length * 2 / shape.Height) : 0;
                if (shape.Kind == ShapeKind.Dot)
                {
                    float edge = width * .5f;
                    return 1 - SmoothStep(1 - edge, 1 + edge, length);
                }
                float ring = MathF.Abs(length * (shape.Radius + 1) - shape.Radius) - .5f;
                float ringEdge = Math.Max(width * (shape.Radius + 1) * .5f, .001f);
                return 1 - SmoothStep(-ringEdge, ringEdge, ring);
            }
            case ShapeKind.Border:
            {
                float fromLeft = rx - (shape.CentreX - shape.Width * .5f), fromTop = ry - (shape.CentreY - shape.Height * .5f);
                float border = Math.Min(Math.Min(fromLeft, shape.Width - fromLeft), Math.Min(fromTop, shape.Height - fromTop));
                float edge = Math.Max(turn.Width(1, 0) * .5f, .001f);
                return 1 - SmoothStep(1 - edge, 1 + edge, border);
            }
            default:
                return 1;
        }
    }

    /// <summary>HLSL's smoothstep; a step at <paramref name="from"/> when the edges meet.</summary>
    private static float SmoothStep(float from, float to, float x)
    {
        if (to <= from) return x < from ? 0 : 1;
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

public enum MinimapQuadKind { Solid, Dot, Ring, Border }

public readonly record struct MinimapDiscQuad(MinimapQuadKind Kind, Vector2 Centre, Vector2 Size, int Radius, uint ColourArgb);

/// <summary>The minimap's ordered marker quads in its 128-unit raster. The independent pixel oracle remains
/// MinimapDiscImage; engine adapters only rotate and draw these quads.</summary>
public sealed class MinimapDiscGeometry
{
    private readonly List<MinimapDiscQuad> _quads = new List<MinimapDiscQuad>();
    private readonly MinimapRasterPoint[] _trail = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
    public IReadOnlyList<MinimapDiscQuad> Quads
    {
        get
        {
            return _quads;
        }
    }

    public void Build(MinimapClassGrid grid, IReadOnlyList<MinimapMarker> markers, MinimapZoom zoom,
        FpPosition3 focus, TilePos? heart, GameTick tick)
    {
        _quads.Clear();
        var map = new MapRaster(grid.Width, grid.Height, zoom, focus);
        for (int i = 0; i < markers.Count; i++)
        {
            if (Place(markers[i], map, out MinimapDiscQuad quad))
            {
                _quads.Add(quad);
            }
        }

        int dots = heart.HasValue ? MinimapHeartTrail.Build(heart.Value, focus, zoom, tick, _trail) : 0;
        for (int i = 0; i < dots; i++)
        {
            _quads.Add(new MinimapDiscQuad(MinimapQuadKind.Solid, new Vector2(_trail[i].X + .5f, _trail[i].Y + .5f), Vector2.One, 0, 0xFFFFFFFF));
        }
    }

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

        public bool WholeMap
        {
            get
            {
                return Level < 0;
            }
        }

        /// <summary>A tile's raster corner in the whole-map fit.</summary>
        public int FitX(int tile)
        {
            return tile * Fit.Width / Width + Fit.Left;
        }

        public int FitY(int tile)
        {
            return tile * Fit.Height / Height + Fit.Top;
        }
    }

    private static bool Place(MinimapMarker marker, MapRaster map, out MinimapDiscQuad shape)
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
        if (!map.WholeMap && marker.ReportingDistanceTiles >= 0)
        {
            radius = marker.ReportingDistanceTiles * (1 << map.Level);
        }

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
                if (spanX * axisX + spanY * axisY <= 0)
                    {
                        return false;
                    }

                    float width = Math.Max(1 - axisX, spanX), height = Math.Max(1 - axisY, spanY);
                shape = new MinimapDiscQuad(MinimapQuadKind.Solid, new Vector2(originX + width * .5f, originY + height * .5f), new Vector2(width, height), 0, marker.ColourArgb);
                return true;
            }
            case MinimapMarkerKind.Ring:
            {
                float size = radius <= 1 ? radius * 2 + 1 : 2 * (radius + 1);
                MinimapQuadKind kind = radius == 1 ? MinimapQuadKind.Dot : radius > 1 ? MinimapQuadKind.Ring : MinimapQuadKind.Solid;
                shape = new MinimapDiscQuad(kind, new Vector2(originX + .5f, originY + .5f), new Vector2(size, size), radius, marker.ColourArgb);
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
                if (width == 0 || height == 0)
                    {
                        return false;
                    }

                    shape = new MinimapDiscQuad(MinimapQuadKind.Border, new Vector2(originX + width * .5f, originY + height * .5f), new Vector2(width, height), 0, marker.ColourArgb);
                return true;
            }
            case MinimapMarkerKind.Creature:
            case MinimapMarkerKind.Trap:
                shape = new MinimapDiscQuad(MinimapQuadKind.Dot, new Vector2(originX + .5f, originY + .5f), new Vector2(3, 3), 1, marker.ColourArgb);
                return true;
            default:
                // The full map's own kinds: the minimap's capture never lists them.
                return false;
        }
    }

}

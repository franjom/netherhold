// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Core;

namespace DK2.Presentation.Tests;

/// <summary>The minimap's disc as Unity's DK2Minimap shader draws it (passes 1 and 2), on the CPU.</summary>
public sealed class MinimapDiscImageTests
{
    /// <summary>A 90x90 map fits the whole-map raster one unit a tile from (20, 20); a 128-pixel disc is one pixel a
    /// raster unit, so at heading 0 pixel (x, y) shows raster point (x + .5, y + .5).</summary>
    private const int Side = 90, Diameter = 128;
    private static readonly byte[] Rock = [10, 20, 30];
    private static readonly MinimapClassGrid Empty = new MinimapClassGrid(Side, Side);

    private static MinimapPalette Palette()
    {
        return new MinimapPalette(Enumerable.Range(0, 37).Select(i => 0x00400000u + (uint)i * 0x000101).ToArray());
    }

    private static MinimapDiscImage Disc()
    {
        byte[] pixels = new byte[128 * 128 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = Rock[0]; pixels[i + 1] = Rock[1]; pixels[i + 2] = Rock[2]; pixels[i + 3] = 255; }
        return new MinimapDiscImage(new RgbaImage(128, 128, pixels));
    }

    private static byte[] Pixel(MinimapDiscImage disc, int x, int y)
    {
        return disc.Pixels.Slice((y * disc.Diameter + x) * 4, 4).ToArray();
    }

    private static byte[] Opaque(uint argb)
    {
        return [(byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, 255];
    }

    private static FpPosition3 At(float x, float y)
    {
        return new FpPosition3(new Fp32((int)(x * 4096)), new Fp32((int)(y * 4096)), new Fp32(0));
    }

    private static void Render(MinimapDiscImage disc, MinimapClassGrid grid, IReadOnlyList<MinimapMarker>? markers = null,
        ushort heading = 0, MinimapZoom? zoom = null, FpPosition3 focus = default, TilePos? heart = null)
    {
        disc.Render(Diameter, grid, Palette(), markers ?? [], zoom ?? MinimapZoom.WholeMap, focus, new Dk2Angle(heading), heart,
            new GameTick(0));
    }

    [Fact]
    public void TheDiscIsOpaqueInsideTransparentOutsideAndSoftForOnePixelAtTheRim()
    {
        MinimapDiscImage disc = Disc();
        Render(disc, Empty);
        Assert.Equal(Diameter, disc.Diameter);
        Assert.Equal(Diameter * Diameter * 4, disc.Pixels.Length);
        Assert.Equal(new byte[4], Pixel(disc, 0, 0));
        Assert.Equal(new byte[4], Pixel(disc, Diameter - 1, Diameter - 1));
        Assert.Equal(255, Pixel(disc, 64, 64)[3]);
        Assert.Equal(255, Pixel(disc, 64, 2)[3]);
        byte rim = Pixel(disc, 64, 0)[3];   // its centre half a pixel inside the circle: half covered
        Assert.InRange((int)rim, 120, 135);
    }

    [Fact]
    public void ATileShowsItsClassColourAndTheUnknownClassAndTheRasterOffTheMapShowTheRock()
    {
        var grid = new MinimapClassGrid(Side, Side);
        grid.Set(new TilePos(44, 44), new MinimapColourClass(20));
        MinimapDiscImage disc = Disc();
        Render(disc, grid);
        Assert.Equal(Opaque(Palette().Colours[20]), Pixel(disc, 64, 64));    // raster (64.5, 64.5): tile (44, 44)
        Assert.Equal([.. Rock, 255], Pixel(disc, 65, 64));                   // tile (45, 44): unknown
        Assert.Equal([.. Rock, 255], Pixel(disc, 64, 10));                   // raster y 10.5: above the map
    }

    [Fact]
    public void TheHeadingTurnsTheRasterUnderTheDiscAsNavigationReadsIt()
    {
        var grid = new MinimapClassGrid(Side, Side);
        grid.Set(new TilePos(64, 44), new MinimapColourClass(20));   // raster (84..85, 64..65): east of the centre
        MinimapDiscImage disc = Disc();
        Render(disc, grid);
        Assert.Equal(Opaque(Palette().Colours[20]), Pixel(disc, 84, 64));
        Render(disc, grid, heading: 512);                            // a quarter turn: east comes up to the top
        Assert.Equal(Opaque(Palette().Colours[20]), Pixel(disc, 64, 43));
        Assert.Equal([.. Rock, 255], Pixel(disc, 84, 64));
        // MinimapNavigation sends a press there east of the centre too (reference point = the disc's place in 1024x768
        // units); its whole-map scale is DK2's own, not the drawn one, so only the direction is compared.
        var view = new MinimapNavigationView(Side, Side, MinimapZoom.WholeMap, default, new Dk2Angle(512));
        float scale = 169.6f / Diameter;
        Assert.True(MinimapNavigation.TryResolve(new System.Numerics.Vector2(12.8f + 64.5f * scale, 587.2f + 43.5f * scale), view,
            out TilePos target));
        Assert.InRange(target.X, 55, 70);
        Assert.InRange(target.Y, 43, 46);
    }

    [Fact]
    public void ZoomedTheRasterIsWholePixelsFromTheFocusOrigin()
    {
        var grid = new MinimapClassGrid(Side, Side);
        grid.Set(new TilePos(45, 44), new MinimapColourClass(20));
        grid.Set(new TilePos(22, 22), new MinimapColourClass(21));   // two pixels a tile at zoom 1
        FpPosition3 focus = At(44.5f, 44.5f);
        MinimapDiscImage disc = Disc();
        Render(disc, grid, zoom: new MinimapZoom(0), focus: focus);
        MinimapRasterPoint origin = new MinimapZoom(0).RasterOrigin(focus);
        Assert.Equal(Opaque(Palette().Colours[20]), Pixel(disc, origin.X + 45, origin.Y + 44));
        Render(disc, grid, zoom: new MinimapZoom(1), focus: At(22.5f, 22.5f));
        origin = new MinimapZoom(1).RasterOrigin(At(22.5f, 22.5f));
        Assert.Equal(Opaque(Palette().Colours[21]), Pixel(disc, origin.X + 44, origin.Y + 44));
        Assert.Equal(Opaque(Palette().Colours[21]), Pixel(disc, origin.X + 45, origin.Y + 45));
    }

    [Fact]
    public void ACreatureDotCoversItsTileCornerAndARectangleOnlyItsBorder()
    {
        MinimapDiscImage disc = Disc();
        var creature = new MinimapMarker(MinimapMarkerKind.Creature, At(44.5f, 44.5f), 0xFFFF0000);   // raster box 64..67
        var room = new MinimapMarker(MinimapMarkerKind.Rectangle, At(30, 30), 0xFF00FF00, EndPosition: At(40, 40));   // 50..60
        Render(disc, Empty, [creature, room]);
        Assert.Equal(Opaque(0xFFFF0000), Pixel(disc, 64, 64));                // the dot's centre, raster (64.5, 64.5)
        byte[] edge = Pixel(disc, 65, 65);                                    // its soft edge: part red, part rock
        Assert.InRange((int)edge[0], Rock[0] + 1, 254);
        Assert.Equal([.. Rock, 255], Pixel(disc, 68, 64));                   // beside it
        Assert.Equal(Opaque(0xFF00FF00), Pixel(disc, 50, 55));                // the border's left column
        Assert.Equal([.. Rock, 255], Pixel(disc, 55, 55));                   // inside it
    }

    [Fact]
    public void ADoorIsAOneUnitStrokeAlongItsTileEdge()
    {
        MinimapDiscImage disc = Disc();
        var door = new MinimapMarker(MinimapMarkerKind.VerticalDoor, At(44.5f, 44.5f), 0xFF0000FF);   // x 64, y 64..65
        Render(disc, Empty, [door]);
        Assert.Equal(Opaque(0xFF0000FF), Pixel(disc, 64, 64));
        Assert.Equal([.. Rock, 255], Pixel(disc, 65, 64));
        Assert.Equal([.. Rock, 255], Pixel(disc, 64, 65));
    }

    [Fact]
    public void ZoomedTheHeartTrailIsDrawnInWhite()
    {
        static int White(MinimapDiscImage disc)
        {
            int count = 0;
            ReadOnlySpan<byte> pixels = disc.Pixels;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] == 255 && pixels[i + 1] == 255 && pixels[i + 2] == 255)
                {
                    count++;
                }
            }

            return count;
        }
        MinimapDiscImage disc = Disc();
        Render(disc, Empty, zoom: new MinimapZoom(0), focus: At(10.5f, 10.5f));
        Assert.Equal(0, White(disc));
        Render(disc, Empty, zoom: new MinimapZoom(0), focus: At(10.5f, 10.5f), heart: new TilePos(80, 80));
        Assert.True(White(disc) > 0);
        Render(disc, Empty, focus: At(10.5f, 10.5f), heart: new TilePos(80, 80));   // the whole map draws no trail
        Assert.Equal(0, White(disc));
    }

    private static readonly byte[] RockPixel = [.. Rock, 255];

    [Fact]
    public void ARingOfRadiusFiveIsAOneUnitCircleAtFiveWithAHollowCentre()
    {
        MinimapDiscImage disc = Disc();
        var ring = new MinimapMarker(MinimapMarkerKind.Ring, At(44.5f, 44.5f), 0xFFFF0000, RadiusRasterPixels: 5);   // centre 64.5
        Render(disc, Empty, [ring]);
        Assert.Equal(Opaque(0xFFFF0000), Pixel(disc, 69, 64));   // raster (69.5, 64.5): distance 5
        Assert.Equal(Opaque(0xFFFF0000), Pixel(disc, 64, 59));   // distance 5 above
        Assert.Equal(RockPixel, Pixel(disc, 64, 64));             // the centre
        Assert.Equal(RockPixel, Pixel(disc, 66, 64));             // distance 2
        Assert.Equal(RockPixel, Pixel(disc, 71, 64));             // distance 7, past the ring
    }

    [Fact]
    public void ARingOfRadiusOneIsASoftDotAndOfRadiusZeroOneSolidUnit()
    {
        MinimapDiscImage disc = Disc();
        Render(disc, Empty, [new MinimapMarker(MinimapMarkerKind.Ring, At(44.5f, 44.5f), 0xFFFF0000, RadiusRasterPixels: 1)]);
        Assert.Equal(Opaque(0xFFFF0000), Pixel(disc, 64, 64));
        Assert.InRange((int)Pixel(disc, 65, 65)[0], Rock[0] + 1, 254);   // the dot's soft edge, three units across
        Render(disc, Empty, [new MinimapMarker(MinimapMarkerKind.Ring, At(44.5f, 44.5f), 0xFFFF0000, RadiusRasterPixels: 0)]);
        Assert.Equal(Opaque(0xFFFF0000), Pixel(disc, 64, 64));   // one raster unit, hard
        Assert.Equal(RockPixel, Pixel(disc, 65, 64));
        Assert.Equal(RockPixel, Pixel(disc, 65, 65));
    }

    [Fact]
    public void ATrapIsADotLikeACreature()
    {
        MinimapDiscImage disc = Disc();
        Render(disc, Empty, [new MinimapMarker(MinimapMarkerKind.Trap, At(44.5f, 44.5f), 0xFF00FF00)]);
        Assert.Equal(Opaque(0xFF00FF00), Pixel(disc, 64, 64));
        Assert.InRange((int)Pixel(disc, 65, 65)[1], Rock[1] + 1, 254);
        Assert.Equal(RockPixel, Pixel(disc, 68, 64));
    }

    [Fact]
    public void ZoomedADoorSpansOneTileFromItsTileEdgeAcrossOrDownTheRaster()
    {
        // Zoom 2 (4 units a tile), focus (44.5, 44.5): the door's position projects to 64, its tile edge 2 units back.
        var zoom = new MinimapZoom(2);
        FpPosition3 focus = At(44.5f, 44.5f);
        MinimapDiscImage disc = Disc();
        Render(disc, Empty, [new MinimapMarker(MinimapMarkerKind.HorizontalDoor, At(44.5f, 44.5f), 0xFF0000FF)], zoom: zoom, focus: focus);
        for (int x = 62; x <= 65; x++)
        {
            Assert.Equal(Opaque(0xFF0000FF), Pixel(disc, x, 64));   // x 62..66, y 64..65
        }

        Assert.Equal(RockPixel, Pixel(disc, 61, 64));
        Assert.Equal(RockPixel, Pixel(disc, 66, 64));
        Assert.Equal(RockPixel, Pixel(disc, 64, 65));
        Render(disc, Empty, [new MinimapMarker(MinimapMarkerKind.VerticalDoor, At(44.5f, 44.5f), 0xFF0000FF)], zoom: zoom, focus: focus);
        for (int y = 62; y <= 65; y++)
        {
            Assert.Equal(Opaque(0xFF0000FF), Pixel(disc, 64, y));   // x 64..65, y 62..66
        }

        Assert.Equal(RockPixel, Pixel(disc, 65, 64));
        Assert.Equal(RockPixel, Pixel(disc, 64, 66));
    }

    [Fact]
    public void ZoomedAReportingRingsRadiusIsItsTileDistanceTimesTheZoomStep()
    {
        var ring = new MinimapMarker(MinimapMarkerKind.Ring, At(44.5f, 44.5f), 0xFFFF0000, RadiusRasterPixels: 9, ReportingDistanceTiles: 2);
        MinimapDiscImage disc = Disc();
        Render(disc, Empty, [ring]);                                                   // whole map: radius 9
        Assert.Equal(Opaque(0xFFFF0000), Pixel(disc, 73, 64));
        Assert.Equal(RockPixel, Pixel(disc, 68, 64));
        Render(disc, Empty, [ring], zoom: new MinimapZoom(1), focus: At(44.5f, 44.5f));   // zoom 1: radius 2 x 2
        Assert.Equal(Opaque(0xFFFF0000), Pixel(disc, 68, 64));
        Assert.Equal(RockPixel, Pixel(disc, 73, 64));
    }

    [Fact]
    public void TheBackdropIsReadTopRowFirstAsTheRasterIsIndexed()
    {
        // Texel (x, y) is (y, x, 0); at heading 0 pixel (x, y) samples texel (x, y) exactly.
        byte[] pixels = new byte[128 * 128 * 4];
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++) { int p = (y * 128 + x) * 4; pixels[p] = (byte)y; pixels[p + 1] = (byte)x; pixels[p + 3] = 255; }
        }

        var disc = new MinimapDiscImage(new RgbaImage(128, 128, pixels));
        Render(disc, Empty);
        Assert.Equal(new byte[] { 10, 64, 0, 255 }, Pixel(disc, 64, 10));
        Assert.Equal(new byte[] { 100, 64, 0, 255 }, Pixel(disc, 64, 100));
        Assert.Equal(new byte[] { 64, 20, 0, 255 }, Pixel(disc, 20, 64));
    }
}

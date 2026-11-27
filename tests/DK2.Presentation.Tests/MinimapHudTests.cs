// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free minimap frame, corners, north sprite and hit areas (ADR-0805).</summary>
public sealed class MinimapHudTests
{
    private static readonly HudScreen FullHd = new(1920, 1080);
    /// <summary>The frame's clip: 179.2 x 176 at (8, 584) in 1024x768 units.</summary>
    private static readonly HudRect Clip = new(20, 1460, 448, 440);

    private static MinimapHud Hud()
    {
        return new(new MinimapPalette(new uint[37]), 64, 64);
    }

    private static HudDraw[] Build(MinimapHud hud, (float, float) pointer = default, ushort heading = 0)
    {
        var frame = new HudFrame();
        hud.Build(FullHd, pointer, new Dk2Angle(heading), frame);
        return frame.Draws.ToArray();
    }

    /// <summary>A 1024x768 point in virtual units.</summary>
    private static (float, float) At(float x, float y)
    {
        return (x * 2.5f, y * 2.5f);
    }

    [Fact]
    public void PossessionUsesItsEmptyCornerFrameAndKeepsItsDiscRoundOnWidescreen()
    {
        MinimapHud hud = Hud();
        hud.SetControls(ClassicHudAction.SelectSell, optionsVisible: true);
        var frame = new HudFrame();
        var layout = new HudScaleLayout(1920, 1080, 100);
        hud.BuildPossession(layout, new Dk2Angle(0), frame);
        HudDraw[] draws = frame.Draws.ToArray();
        HudDraw border = Assert.Single(draws, d => d.Image.Path == MinimapHud.PossessionFrameArt);
        Assert.Equal(20 * .5625f, border.Rect.X * layout.Screen.Scale, 3);
        Assert.Equal(448 * .5625f, border.Clip!.Value.Width * layout.Screen.Scale, 3);
        Assert.DoesNotContain(draws, d => d.Image.Path == MinimapHud.FrameArt || d.Image.Path!.Contains("mapbf"));
        HudRect disc = MinimapHud.PossessionDisc(layout);
        Assert.Equal(32 * .5625f, disc.X * layout.Screen.Scale, 3);
        Assert.Equal(424 * .5625f, disc.Width * layout.Screen.Scale, 3);
        Assert.Equal(424 * 1080f / 1920, disc.Height * layout.Screen.Scale, 3);
        Assert.Equal(disc.Width, disc.Height);
    }

    [Fact]
    public void PossessionMinimapDocksToTheVisibleBottomAtCustomUiScale()
    {
        var layout = new HudScaleLayout(1920, 1080, 150);
        var frame = new HudFrame();
        Hud().BuildPossession(layout, new Dk2Angle(0), frame);
        HudDraw border = Assert.Single(frame.Draws.ToArray(), d => d.Image.Path == MinimapHud.PossessionFrameArt);
        Assert.Equal(1920 / layout.Factor - 20, border.Clip!.Value.Y + border.Clip.Value.Height, 3);
        HudRect disc = MinimapHud.PossessionDisc(layout);
        Assert.True(disc.Y + disc.Height < 1920 / layout.Factor);
    }

    [Fact]
    public void TheFrameIsClippedToItsWindowAndTheNorthSpriteSitsOnTheRing()
    {
        HudDraw[] draws = Build(Hud());
        Assert.Equal(2, draws.Length);
        Assert.Equal(MinimapHud.FrameArt, draws[0].Image.Path);
        Assert.Equal(new HudRect(20, 1460, 512, 512), draws[0].Rect);
        Assert.Equal(Clip, draws[0].Clip);
        Vector2 north = MinimapNorth.SpriteOriginPixels(new Dk2Angle(0), 1080);
        Assert.Equal(MinimapHud.NorthArt, draws[1].Image.Path);
        Assert.Equal(new HudRect(FullHd.Virtual(north.X), FullHd.Virtual(north.Y), 20, 20), draws[1].Rect);
        Assert.NotEqual(draws[1].Rect, Build(Hud(), heading: 512)[1].Rect);
        Assert.Equal(new HudRect(32, 1468, 424, 424), MinimapHud.Disc);
    }

    [Fact]
    public void TheMagnifiersPlainArtIsTheSelectedFileAndTheOthersAreTheReverse()
    {
        Assert.Equal("GUI\\Map\\selected-mapbf-03", MinimapHud.CornerArt(MinimapCorner.BottomRight, false));
        Assert.Equal("GUI\\Map\\mapbf-03", MinimapHud.CornerArt(MinimapCorner.BottomRight, true));
        Assert.Equal("GUI\\Map\\mapbf-00", MinimapHud.CornerArt(MinimapCorner.TopLeft, false));
        Assert.Equal("GUI\\Map\\selected-mapbf-01", MinimapHud.CornerArt(MinimapCorner.TopRight, true));
        Assert.Equal(11, MinimapHud.Images().Count());
        Assert.Contains(new HudImage(MinimapHud.FootprintArt), MinimapHud.Images());
    }

    [Fact]
    public void ACornerShowsWhileHoveredAndStaysLitWhileItsToolIsSelectedOrOptionsIsOpen()
    {
        MinimapHud hud = Hud();
        Assert.DoesNotContain(Build(hud, At(100, 700)), d => d.Image.Path!.Contains("mapbf"));
        HudDraw hovered = Assert.Single(Build(hud, At(20, 600)), d => d.Image.Path == MinimapHud.CornerArt(MinimapCorner.TopLeft, false));
        Assert.Equal(new HudRect(20, 1460, 128, 128), hovered.Rect);
        Assert.Equal(Clip, hovered.Clip);

        hud.SetControls(ClassicHudAction.SelectSell, optionsVisible: true);
        HudDraw[] draws = Build(hud);
        HudDraw sell = Assert.Single(draws, d => d.Image.Path == MinimapHud.CornerArt(MinimapCorner.TopRight, true));
        Assert.Equal(20 + 79 * 1.6f * 2.5f, sell.Rect.X, 3);
        HudDraw options = Assert.Single(draws, d => d.Image.Path == MinimapHud.CornerArt(MinimapCorner.BottomLeft, true));
        Assert.Equal(1460 + 78 * 1.6f * 2.5f, options.Rect.Y, 3);
        Assert.Equal(4, draws.Length);   // frame, two corners, north
    }

    [Fact]
    public void TheMagnifierZoomsInOnThePrimaryButtonAndOutOnTheSecondary()
    {
        MinimapHud hud = Hud();
        (float x, float y) = At(160, 740);
        Assert.Equal(ClassicHudAction.ZoomIn, hud.CornerPress(x, y, true, false));
        Assert.Equal(ClassicHudAction.ZoomOut, hud.CornerPress(x, y, false, true));
        Assert.True(hud.ZoomPress(x, y, true, false));
        Assert.Equal(0, hud.Zoom.Level);
        Assert.True(hud.ZoomPress(x, y, false, true));
        Assert.True(hud.Zoom.IsWholeMap);
        Assert.False(hud.ZoomPress(At(100, 700).Item1, At(100, 700).Item2, true, false));
        Assert.Equal(ClassicHudAction.None, hud.CornerPress(At(20, 600).Item1, At(20, 600).Item2, false, true));
    }

    [Fact]
    public void ThePanelTakesThePointerFromItsTopEdgeToTheScreensBottomAndNothingWhileInputIsOff()
    {
        MinimapHud hud = Hud();
        Assert.True(hud.ContainsPointer(20, 1461));
        Assert.True(hud.ContainsPointer(479, 1920));
        Assert.False(hud.ContainsPointer(20, 1460));
        Assert.False(hud.ContainsPointer(480, 1700));
        Assert.False(hud.ContainsPointer(19, 1700));

        hud.SetVisibility(new HudVisibility(true, false));
        Assert.False(hud.ContainsPointer(100, 1700));
        Assert.Equal(ClassicHudAction.None, hud.CornerPress(At(20, 600).Item1, At(20, 600).Item2, true, false));
        Assert.False(hud.TryNavigate(At(100, 660).Item1, At(100, 660).Item2, default, new Dk2Angle(0), out _));
        Assert.Equal(ClassicHudAction.SelectQuery, hud.HoveredControl(At(20, 600).Item1, At(20, 600).Item2));

        hud.SetVisibility(new HudVisibility(false, false));
        Assert.Empty(Build(hud));
        Assert.Equal(ClassicHudAction.None, hud.HoveredControl(At(20, 600).Item1, At(20, 600).Item2));
    }

    [Fact]
    public void APressOnTheWholeMapResolvesTheTileItShows()
    {
        MinimapHud hud = Hud();
        (float x, float y) = At(95.2f, 672);   // the disc's centre
        var view = new MinimapNavigationView(64, 64, MinimapZoom.WholeMap, default, new Dk2Angle(0));
        Assert.Equal(MinimapNavigation.TryResolve(new Vector2(95.2f, 672), view, out TilePos expected),
            hud.TryNavigate(x, y, default, new Dk2Angle(0), out TilePos target));
        Assert.Equal(expected, target);
    }

    [Fact]
    public void APressGivesTheCornersActionOrTheFullMapForASecondaryPressOnTheMap()
    {
        MinimapHud hud = Hud();
        (float cx, float cy) = At(95.2f, 672);   // the disc's centre
        (float ox, float oy) = At(20, 740);      // the options corner
        (float mx, float my) = At(170, 745);     // the magnifier
        (float px, float py) = At(180, 700);     // the panel, off the map and the corners
        var heading = new Dk2Angle(256);
        Assert.Equal(ClassicHudAction.ToggleFullMap, hud.Press(cx, cy, false, true, default, heading));
        Assert.Equal(ClassicHudAction.None, hud.Press(cx, cy, true, false, default, heading));   // the camera's, asked apart
        Assert.Equal(ClassicHudAction.ToggleOptions, hud.Press(ox, oy, true, false, default, heading));
        Assert.Equal(ClassicHudAction.ZoomIn, hud.Press(mx, my, true, false, default, heading));
        Assert.Equal(ClassicHudAction.ZoomOut, hud.Press(mx, my, false, true, default, heading));
        Assert.Equal(ClassicHudAction.None, hud.Press(px, py, false, true, default, heading));
        Assert.Equal(ClassicHudAction.None, hud.Press(cx, cy, false, false, default, heading));

        hud.SetVisibility(new HudVisibility(true, false));
        Assert.Equal(ClassicHudAction.None, hud.Press(cx, cy, false, true, default, heading));
        Assert.Equal(ClassicHudAction.None, hud.Press(ox, oy, true, false, default, heading));
    }

    [Fact]
    public void ACameraFocusBecomesTheFixedPointMapPositionWithTheRowFromTheNegatedDepth()
    {
        Assert.Equal(new FpPosition3(new Fp32(14 * 4096 + 2048), new Fp32(28 * 4096 + 2048), new Fp32(0)),
            MinimapHud.FixedFocus(new Vector3(14.5f, 1, -28.5f)));
        Assert.Equal(new FpPosition3(new Fp32(409), new Fp32(0), new Fp32(0)), MinimapHud.FixedFocus(new Vector3(.1f, 7, 0)));
    }

    [Theory]
    [InlineData(1080, 1f, 239)]      // 169.6 * 1080 / 768 = 238.5, rounded up
    [InlineData(768, 1f, 170)]
    [InlineData(1080, 1.5f, 358)]
    [InlineData(1, .5f, 1)]
    [InlineData(100000, 2f, 4096)]
    public void TheDiscIsDrawnAtItsScreenSizeRoundedUpAndClamped(int height, float factor, int expected)
    {
        Assert.Equal(expected, MinimapHud.DiscPixels(height, factor));
    }

    private static readonly CameraViewport Viewport = new(1920, 1920, 1080);

    private static MinimapCameraView CameraAt(Vector3 focus, ushort yaw)
    {
        return MinimapCameraView.Of(new ClassicCamera(
        new CameraThing(new byte[84]) { CameraId = 3, Z = 4096, Zoom = 40960, MinZoom = 16384, MaxZoom = 53248, Lens = 8192, AnglePitch = 220, Flags = 0x26 },
        new Dk2Angle(yaw))
        { ReferenceScreenWidth = 1600 }.Pose(focus, Viewport), Viewport);
    }

    private static HudFrame Frame(MinimapHud hud)
    {
        var frame = new HudFrame();
        hud.Build(FullHd, default, new Dk2Angle(256), frame);
        return frame;
    }

    [Fact]
    public void TheCameraFootprintIsTheWholeMapsFirstDrawUnderTheFrame()
    {
        MinimapHud hud = Hud();
        MinimapCameraView view = CameraAt(new Vector3(32.5f, 1, -32.5f), 300);
        hud.SetCamera(view);
        HudFrame frame = Frame(hud);
        HudDraw footprint = frame.Draws[0];
        Assert.Equal(HudDrawKind.Triangles, footprint.Kind);
        Assert.Equal(new HudImage(MinimapHud.FootprintArt), footprint.Image);
        Assert.Equal(HudColor.White, footprint.Tint);
        Assert.Equal(MinimapHud.FrameArt, frame.Draws[1].Image.Path);
        // A centred camera's footprint lies inside the disc: the native ribbon, uncut.
        var corners = new Vector2[4];
        var ribbon = new Vector2[8];
        var expected = new HudVertex[MinimapCameraFootprint.TriangleVertices];
        Assert.True(view.TryFloorCorners(corners));
        Assert.True(MinimapCameraFootprint.TryBuild(corners, 64, 64, view.YawDegrees * (MathF.PI / 180f), ribbon));
        MinimapCameraFootprint.Triangles(ribbon, expected);
        Assert.Equal(expected, frame.Vertices(footprint).ToArray());
        hud.ZoomIn();
        Assert.DoesNotContain(Frame(hud).Draws.ToArray(), d => d.Kind == HudDrawKind.Triangles);
        hud.ZoomOut();
        Assert.Equal(HudDrawKind.Triangles, Frame(hud).Draws[0].Kind);
        hud.SetCamera(null);
        Assert.DoesNotContain(Frame(hud).Draws.ToArray(), d => d.Kind == HudDrawKind.Triangles);
        hud.SetCamera(view with { Forward = Vector3.UnitZ, Up = Vector3.UnitY });
        Assert.DoesNotContain(Frame(hud).Draws.ToArray(), d => d.Kind == HudDrawKind.Triangles);
    }

    [Fact]
    public void AFootprintLargerThanTheDiscIsCutToItsCircle()
    {
        var hud = new MinimapHud(new MinimapPalette(new uint[37]), 4, 4);
        hud.SetCamera(CameraAt(new Vector3(2, 1, -2), 300));
        HudFrame frame = Frame(hud);
        ReadOnlySpan<HudVertex> cut = frame.Vertices(frame.Draws[0]);
        Assert.NotEqual(MinimapCameraFootprint.TriangleVertices, cut.Length);
        float reach = 212 / MathF.Cos(MathF.PI / HudTriangles.CircleSides) + 1e-3f;
        foreach (HudVertex v in cut)
        {
            Assert.True(Vector2.Distance(new Vector2(v.X, v.Y), new Vector2(244, 1680)) <= reach);
        }
    }

    [Fact]
    public void SettingTheCameraAndBuildingAllocateNothing()
    {
        MinimapHud hud = Hud();
        MinimapCameraView view = CameraAt(new Vector3(32.5f, 1, -32.5f), 300);
        var frame = new HudFrame();
        hud.SetCamera(view);
        hud.Build(FullHd, default, new Dk2Angle(256), frame);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            hud.SetCamera(view with { Position = view.Position + new Vector3(i * .1f, 0, 0) });
            frame.Clear();
            hud.Build(FullHd, default, new Dk2Angle(256), frame);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

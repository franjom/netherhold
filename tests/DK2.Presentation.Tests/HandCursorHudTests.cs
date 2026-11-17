// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Numerics;
using DK2.Art.Textures;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The hand cursor's quad (<see cref="HandCursorHud"/>), its sheets' green key against Unity's
/// <c>ClassicHandCursor.Sheet</c> loop it came from (plan step 3e), and the install's sheets.</summary>
public sealed class HandCursorHudTests
{
    [Fact]
    public void TheGreenKeyIsUnitys()
    {
        var random = new Random(11);
        var pixels = new byte[4 * 4096];
        random.NextBytes(pixels);
        // Some pure greens, and near misses on each channel.
        for (int i = 0; i < pixels.Length; i += 4 * 7) { pixels[i] = 0; pixels[i + 1] = 255; pixels[i + 2] = 0; }
        for (int i = 4 * 3; i < pixels.Length; i += 4 * 11) { pixels[i] = 1; pixels[i + 1] = 255; pixels[i + 2] = 0; }
        for (int i = 4 * 5; i < pixels.Length; i += 4 * 13) { pixels[i] = 0; pixels[i + 1] = 254; pixels[i + 2] = 0; }
        for (int i = 4 * 6; i < pixels.Length; i += 4 * 17) { pixels[i] = 0; pixels[i + 1] = 255; pixels[i + 2] = 1; }
        // The last pixel is a pure green, so a loop that stops one short is caught.
        pixels[^4] = 0; pixels[^3] = 255; pixels[^2] = 0; pixels[^1] = 200;
        byte[] expected = (byte[])pixels.Clone();
        // Unity's loop over Color32 (r, g, b, a bytes).
        for (int i = 0; i < expected.Length / 4; i++)
            if (expected[i * 4] == 0 && expected[i * 4 + 1] == 255 && expected[i * 4 + 2] == 0) expected[i * 4 + 3] = 0;
        byte[] keyed = (byte[])pixels.Clone();
        HudImagePixels.KeyGreen(keyed);
        Assert.Equal(expected, keyed);
        Assert.NotEqual(pixels, keyed);
        // As a variant, the source is left alone.
        byte[] variant = HudImagePixels.Apply(pixels, 64, 64, new HudImage("x", HudImageVariant.GreenKey));
        Assert.Equal(expected, variant);
        Assert.NotSame(pixels, variant);
    }

    [Fact]
    public void TheQuadIsPlacedAsUnitysCursorPanelPlacesIt()
    {
        var frame = new HudFrame();
        var screen = new HudScreen(1920, 1080);
        var pointer = new Vector2(800, 300);
        HandCursorHud.Draw(frame, HandCursorKind.DropThing, 5, pointer, screen, 1.5f);
        HudDraw draw = Assert.Single(frame.Draws.ToArray());
        HandCursorPlacement placed = HandCursor.Place(HandCursorKind.DropThing, 5, pointer, 1080);
        Vector2 origin = pointer + (placed.Origin - pointer) * 1.5f;
        Assert.Equal(new HudImage("DropThing.png", HudImageVariant.GreenKey), draw.Image);
        Assert.Equal(origin.X, draw.Rect.X * screen.Scale, 3);
        Assert.Equal(origin.Y, draw.Rect.Y * screen.Scale, 3);
        Assert.Equal(placed.Size.X * 1.5f, draw.Rect.Width * screen.Scale, 3);
        Assert.Equal(placed.Size.Y * 1.5f, draw.Rect.Height * screen.Scale, 3);
        // Frame 5 of 14, top-down (Unity's bottom-up rect 1 - 6/14).
        Assert.Equal(new HudRect(0, 5 / 14f, 1, 1 / 14f), draw.Uv);
        Assert.Equal(HudColor.White, draw.Tint);
    }

    [Fact]
    public void TheDefaultCursorDrawsNothing()
    {
        var frame = new HudFrame();
        HandCursorHud.Draw(frame, HandCursorKind.Default, 0, new Vector2(1, 1), new HudScreen(800, 600), 1);
        Assert.Equal(0, frame.Count);
    }

    [InstallFact]
    public void EverySheetLoadsAtItsTableSize()
    {
        using var sheets = new InstallHandCursorSheets(Dk2InstallGate.Path, SizeOnly);
        foreach (HandCursorKind kind in Enum.GetValues<HandCursorKind>())
        {
            if (kind == HandCursorKind.Default) continue;
            HandCursorSprite sprite = HandCursor.Sprite(kind);
            RgbaImage image = sheets.Load(sprite.FileName);
            Assert.Equal(sprite.FrameSize.Y * sprite.FrameCount, image.Height);
        }
        Assert.Throws<InvalidDataException>(() => sheets.Load("Missing.png"));
        using var wrong = new InstallHandCursorSheets(Dk2InstallGate.Path, png => new RgbaImage(2, 2, new byte[16]));
        Assert.Throws<InvalidDataException>(() => wrong.Load("Point.png"));
    }

    /// <summary>A blank image of the PNG's IHDR size: enough to check the table, with no decoder.</summary>
    private static RgbaImage SizeOnly(byte[] png)
    {
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        return new RgbaImage(width, height, new byte[width * height * 4]);
    }
}

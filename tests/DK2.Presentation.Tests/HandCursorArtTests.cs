// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using DK2.Data.Wad;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Re-measures the claw tips in <see cref="HandCursor.Anchor"/> from the shipped
/// Sprite.WAD sheets: the opaque pixel furthest along (-1, 0.25), first in row order on a tie.</summary>
public sealed class HandCursorArtTests
{
    [InstallTheory]
    [InlineData(HandCursorKind.Point)]
    [InlineData(HandCursorKind.SmallPoint)]
    [InlineData(HandCursorKind.Pickup)]
    public void AnchorsAreTheClawTipsOfTheShippedSheet(HandCursorKind kind)
    {
        HandCursorSprite sprite = HandCursor.Sprite(kind);
        using WadArchive archive = WadArchive.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Sprite.WAD"));
        (int width, int height, byte[] alpha) = DecodeAlpha(archive.Extract(sprite.FileName));
        int frameHeight = (int)sprite.FrameSize.Y;
        Assert.Equal((int)sprite.FrameSize.X, width);
        Assert.Equal(frameHeight * sprite.FrameCount, height);
        // Every frame takes frame 0's tip: the resting claw, so the hand does not move about the pointer.
        Vector2 resting = Tip(alpha, width, 0, frameHeight);
        for (int frame = 0; frame < sprite.FrameCount; frame++)
            Assert.Equal(resting, HandCursor.Anchor(kind, frame));
    }

    private static Vector2 Tip(byte[] alpha, int width, int top, int height)
    {
        float best = float.NegativeInfinity;
        Vector2 tip = default;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (alpha[(top + y) * width + x] == 0) continue;
            float score = -x + 0.25f * y;
            if (score > best) { best = score; tip = new Vector2(x, y); }
        }
        return tip;
    }

    // The cursor sheets are 8-bit RGBA (colour type 6), Adam7-interlaced; only alpha is kept.
    private static (int Width, int Height, byte[] Alpha) DecodeAlpha(byte[] png)
    {
        int width = 0, height = 0, interlace = 0;
        using var idat = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(at + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                Assert.Equal(8, data[8]);
                Assert.Equal(6, data[9]);
                interlace = data[12];
            }
            else if (type == "IDAT") idat.Write(data);
            at += length + 12;
        }
        using var inflate = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        byte[] bytes = raw.ToArray();
        var alpha = new byte[width * height];
        int[][] passes = interlace == 0
            ? [[0, 0, 1, 1]]
            : [[0, 0, 8, 8], [4, 0, 8, 8], [0, 4, 4, 8], [2, 0, 4, 4], [0, 2, 2, 4], [1, 0, 2, 2], [0, 1, 1, 2]];
        int offset = 0;
        foreach (int[] pass in passes)
        {
            int passWidth = (width - pass[0] + pass[2] - 1) / pass[2];
            int passHeight = (height - pass[1] + pass[3] - 1) / pass[3];
            if (passWidth <= 0 || passHeight <= 0) continue;
            int stride = passWidth * 4;
            var previous = new byte[stride];
            var line = new byte[stride];
            for (int row = 0; row < passHeight; row++)
            {
                byte filter = bytes[offset];
                Array.Copy(bytes, offset + 1, line, 0, stride);
                offset += stride + 1;
                for (int i = 0; i < stride; i++)
                {
                    int left = i >= 4 ? line[i - 4] : 0, up = previous[i], upLeft = i >= 4 ? previous[i - 4] : 0;
                    line[i] = (byte)(line[i] + filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        4 => Paeth(left, up, upLeft),
                        _ => throw new InvalidDataException("PNG filter " + filter),
                    });
                }
                for (int column = 0; column < passWidth; column++)
                    alpha[(pass[1] + row * pass[3]) * width + pass[0] + column * pass[2]] = line[column * 4 + 3];
                (previous, line) = (line, previous);
            }
        }
        return (width, height, alpha);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}

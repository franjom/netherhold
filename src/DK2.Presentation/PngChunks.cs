// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>PNG chunk filtering for engines whose decoders colour-manage. DK2's PNGs are read as stored bytes (Unity's
/// <c>LoadImage</c> ignores colour-space chunks), but Godot's decoder corrects a gAMA chunk to sRGB: MapColours.png declares
/// gamma 0.22727, and its 231,24,24 decodes as 209,2,2.</summary>
public static class PngChunks
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>The PNG without its colour-space chunks (gAMA, cHRM, sRGB, iCCP); the other chunks, CRCs included, are
    /// copied as they are, up to and including IEND. The input itself is returned when it has none.</summary>
    /// <exception cref="InvalidDataException">No PNG signature, a chunk length over 2^31 - 1, a chunk running past the
    /// end, or no IEND.</exception>
    public static byte[] WithoutColourSpace(byte[] png)
    {
        if (png is null) throw new ArgumentNullException(nameof(png));
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG: the signature is missing.");
        var kept = new MemoryStream(png.Length);
        kept.Write(png, 0, Signature.Length);
        bool dropped = false;
        long at = Signature.Length;
        while (true)
        {
            if (at + 12 > png.Length) throw new InvalidDataException("The PNG ends without an IEND chunk.");
            uint length = (uint)(png[at] << 24 | png[at + 1] << 16 | png[at + 2] << 8 | png[at + 3]);
            if (length > int.MaxValue) throw new InvalidDataException("A PNG chunk's length is over 2^31 - 1.");
            long end = at + 12 + length;
            if (end > png.Length) throw new InvalidDataException("A PNG chunk runs past the end of the file.");
            ReadOnlySpan<byte> type = png.AsSpan((int)at + 4, 4);
            if (Is(type, "gAMA") || Is(type, "cHRM") || Is(type, "sRGB") || Is(type, "iCCP")) dropped = true;
            else kept.Write(png, (int)at, (int)(end - at));
            at = end;
            if (Is(type, "IEND")) break;
        }
        return dropped ? kept.ToArray() : png;
    }

    private static bool Is(ReadOnlySpan<byte> type, string name) =>
        type[0] == name[0] && type[1] == name[1] && type[2] == name[2] && type[3] == name[3];
}

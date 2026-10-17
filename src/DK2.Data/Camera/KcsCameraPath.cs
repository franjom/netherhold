// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using DK2.Data.Model;

namespace DK2.Data.Camera;

/// <summary>
/// Reads a Dungeon Keeper 2 camera path (<c>Paths.WAD</c>, <c>EnginePath%d.kcs</c>).
/// Layout from the loader 0x0044CFB0: a 16-byte header whose first dword is the
/// frame count (the other three are zero in every shipped file), then that many
/// 56-byte frames of 14 floats: position, a 3x3 rotation, field of view, view distance.
/// </summary>
public sealed class KcsCameraPath
{
    private const int HeaderSize = 16;
    private const int FrameSize = 56;

    private KcsCameraPath(List<KcsFrame> frames) =>
        Frames = new ReadOnlyCollection<KcsFrame>(frames);

    public IReadOnlyList<KcsFrame> Frames { get; }

    public static KcsCameraPath Read(ReadOnlySpan<byte> data, string sourceName = "<memory>")
    {
        if (data.Length < HeaderSize)
            throw Invalid(sourceName, "file is shorter than the 16-byte header");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        long expected = HeaderSize + (long)count * FrameSize;
        if (count == 0 || expected != data.Length)
            throw Invalid(sourceName, $"{count} frames need {expected} bytes, the file has {data.Length}");

        var frames = new List<KcsFrame>((int)count);
        Span<float> v = stackalloc float[FrameSize / 4];
        for (int i = 0; i < (int)count; i++)
        {
            ReadOnlySpan<byte> f = data.Slice(HeaderSize + i * FrameSize, FrameSize);
            for (int k = 0; k < v.Length; k++)
                v[k] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(f[(k * 4)..]));

            // The loader stores S * Mt * S with S = diag(-1, 1, 1): it transposes the
            // file's matrix and negates every element with exactly one index 0.
            var rotation = new KcsRotation(
                v[3], -v[6], -v[9],
                -v[4], v[7], v[10],
                -v[5], v[8], v[11]);
            frames.Add(new KcsFrame(v[0], v[1], v[2], rotation, v[12], v[13]));
        }

        return new KcsCameraPath(frames);
    }

    private static InvalidDataException Invalid(string source, string reason) =>
        new($"Invalid KCS camera path '{source}': {reason}.");
}

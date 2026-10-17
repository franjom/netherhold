// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Camera;
using DK2.Data.Wad;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KcsCameraPathTests
{
    [Fact]
    public void AFrameIsFourteenFloatsAfterASixteenByteHeader()
    {
        float[] values = [1.5f, -2f, -3f, 11, 12, 13, 21, 22, 23, 31, 32, 33, 0.75f, 12000f];

        KcsFrame frame = Assert.Single(KcsCameraPath.Read(File(values)).Frames);

        Assert.Equal((1.5f, -2f, -3f), (frame.X, frame.Y, frame.Z));
        Assert.Equal(0.75f, frame.FieldOfView);
        Assert.Equal(12000f, frame.ViewDistance);
    }

    [Fact]
    public void TheLoaderTransposesTheRotationAndNegatesTheElementsWithOneZeroIndex()
    {
        // 0x0044CFB0 stores S * Mt * S, S = diag(-1, 1, 1).
        float[] values = [0, 0, 0, 11, 12, 13, 21, 22, 23, 31, 32, 33, 1, 1];

        KcsRotation r = KcsCameraPath.Read(File(values)).Frames[0].Rotation;

        Assert.Equal(new KcsRotation(11, -21, -31, -12, 22, 32, -13, 23, 33), r);
    }

    [Fact]
    public void ACountThatDisagreesWithTheLengthIsRejected()
    {
        byte[] data = File([0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1]);
        BinaryPrimitives.WriteUInt32LittleEndian(data, 2);

        Assert.Throws<InvalidDataException>(() => KcsCameraPath.Read(data));
    }

    [InstallFact]
    public void EveryShippedPathParsesAndEveryFrameIsARotationBelowItsOrigin()
    {
        using var wad = WadArchive.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Paths.WAD"));
        WadEntry[] paths = wad.Entries
            .Where(e => e.Name.EndsWith(".KCS", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.Equal(80, paths.Length);
        foreach (WadEntry entry in paths)
        {
            foreach (KcsFrame f in KcsCameraPath.Read(wad.Extract(entry), entry.Name).Frames)
            {
                KcsRotation m = f.Rotation;
                float det = m.M00 * (m.M11 * m.M22 - m.M12 * m.M21)
                    - m.M01 * (m.M10 * m.M22 - m.M12 * m.M20)
                    + m.M02 * (m.M10 * m.M21 - m.M11 * m.M20);
                Assert.InRange(det, 0.999f, 1.001f);
                Assert.InRange(m.M00 * m.M01 + m.M10 * m.M11 + m.M20 * m.M21, -0.001f, 0.001f);
                Assert.InRange(m.M00 * m.M02 + m.M10 * m.M12 + m.M20 * m.M22, -0.001f, 0.001f);
                Assert.InRange(m.M01 * m.M02 + m.M11 * m.M12 + m.M21 * m.M22, -0.001f, 0.001f);
                Assert.InRange(m.M00 * m.M00 + m.M10 * m.M10 + m.M20 * m.M20, 0.999f, 1.001f);
                Assert.True(f.Z < 0, $"{entry.Name}: z {f.Z} would put the camera under one tile");
                Assert.InRange(f.FieldOfView, 0.3f, 2.6f);
            }
        }
    }

    private static byte[] File(float[] frame)
    {
        var data = new byte[16 + 56];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
        for (int i = 0; i < 14; i++)
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(16 + i * 4), frame[i]);
        return data;
    }
}

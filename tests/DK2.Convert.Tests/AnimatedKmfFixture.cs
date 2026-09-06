// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;

namespace DK2.Convert.Tests;

internal static class AnimatedKmfFixture
{
    public static byte[] TwoFrames(bool corruptSecondFrame = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        Tag("KMSH"); U32(0); U32(1);
        Tag("HEAD"); U32(0); U32(2); U32(0);
        Tag("MATL"); U32(0); U32(1);
        Tag("MAT2"); U32(0); Text("creature"); U32(1); Text("creature0");
        U32(0); writer.Write(1f); writer.Write(2f); Text("");
        Tag("ANIM"); U32(0); Tag("HEAD"); U32(0); Text("moving triangle");
        U32(1); U32(2); U32(3); U32(6); U32(0);
        Vector(0, 0, 0); writer.Write(1f); writer.Write(2f); U32(1);
        Tag("CTRL"); U32(0); U32(0);
        Tag("SPRS"); U32(0); Tag("SPHD"); U32(0); U32(1); U32(3); writer.Write(1f);
        Tag("SPRS"); U32(0); U32(0);
        Tag("POLY"); U32(0); writer.Write(new byte[] { 0, 1, 2 });
        Tag("VERT"); U32(0);
        Vertex(0, 0, 0); Vertex(1, 16384, 0); Vertex(2, 16384, 8192);
        Tag("ITAB"); U32(0); U32(0); U32(2); U32(4);
        Tag("GEOM"); U32(0);
        Geometry(512, 512, 512); Geometry(1023, 512, 512);
        Geometry(512, 1023, 512); Geometry(512, 512, 1023);
        Geometry(0, 512, 512); Geometry(512, 0, 512);
        Tag("VGEO"); U32(0);
        writer.Write((byte)0); writer.Write(corruptSecondFrame ? (byte)200 : (byte)1);
        writer.Write((byte)0); writer.Write((byte)1);
        writer.Write((byte)0); writer.Write((byte)1);
        return stream.ToArray();

        void Tag(string value) => writer.Write(Encoding.ASCII.GetBytes(value));
        void U32(uint value) => writer.Write(value);
        void Text(string value)
        {
            writer.Write(Encoding.ASCII.GetBytes(value));
            writer.Write((byte)0);
        }
        void Vector(float x, float y, float z)
        {
            writer.Write(x); writer.Write(y); writer.Write(z);
        }
        void Vertex(ushort geometry, ushort u, ushort v)
        {
            writer.Write(u); writer.Write(v); Vector(0, 1, 0); writer.Write(geometry);
        }
        void Geometry(uint x, uint y, uint z)
        {
            writer.Write((x << 20) | (y << 10) | z);
            writer.Write((byte)0);
        }
    }
}

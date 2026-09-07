// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Convert.Kmf;
using DK2.Data;
using DK2.Data.Wad;
using System.Numerics;
using System.Text;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class KmfReaderTests
{
    [Fact]
    public void StaticMeshFieldsAreDecodedWithoutAnInstall()
    {
        KmfModel model = KmfReader.Read(StaticTriangle());
        KmfPrimitive primitive = Assert.Single(Assert.IsType<KmfMesh>(model.Mesh).Primitives);
        KmfMaterial material = Assert.Single(model.Materials);
        Assert.Equal("stone", material.Name);
        Assert.Equal(["stone0"], material.TextureNames);
        Assert.Equal(new Vector3(1, 2, 3), primitive.Vertices[2].Position);
        Assert.Equal(new Vector2(0.5f, 0.25f), primitive.Vertices[2].TextureCoordinate);
        Assert.Equal([0, 1, 2], Assert.Single(primitive.IndicesByDetailLevel));
    }

    [InstallTheory]
    [InlineData("Imp-PoseFrame.kmf")]
    [InlineData("Gob-PoseFrame.kmf")]
    public void CreatureModelReadsGeometryAndTextures(string entryName)
    {
        string install = Dk2InstallGate.Path;
        KmfModel model = ReadFromInstall(install, entryName);
        Assert.Equal(KmfKind.Mesh, model.Kind);
        Assert.NotNull(model.Mesh);
        Assert.NotEmpty(model.Materials);
        Assert.All(model.Materials, material => Assert.NotEmpty(material.TextureNames));
        Assert.NotEmpty(model.Mesh.Primitives);
        Assert.All(model.Mesh.Primitives, AssertCompletePrimitive);
    }

    [InstallFact]
    public void AnimatedCreatureModelReadsEveryFrame()
    {
        string install = Dk2InstallGate.Path;
        KmfModel model = ReadFromInstall(install, "Imp-Idle2.kmf");
        Assert.Equal(KmfKind.Animation, model.Kind);
        Assert.True(model.FrameCount > 1);
        Assert.NotNull(model.Mesh);
        Assert.All(model.Mesh.Primitives, AssertCompletePrimitive);
        KmfAnimation animation = Assert.IsType<KmfAnimation>(model.Animation);
        Assert.True(animation.IsComplete, animation.DecodeFailure);
        Assert.Equal((int)animation.FrameCount, animation.Frames.Count);
        Assert.All(animation.Frames.SelectMany(frame => frame.Primitives), AssertCompletePrimitive);
    }

    [Fact]
    public void AnimatedGeometryExposesDistinctPositionsForEveryFrame()
    {
        KmfModel model = KmfReader.Read(AnimatedKmfFixture.TwoFrames());

        KmfAnimation animation = Assert.IsType<KmfAnimation>(model.Animation);
        Assert.True(animation.IsComplete);
        Assert.Equal(2u, animation.FrameCount);
        Assert.Equal(2, animation.Frames.Count);
        IReadOnlyList<KmfVertex> first = Assert.Single(animation.Frames[0].Primitives).Vertices;
        IReadOnlyList<KmfVertex> second = Assert.Single(animation.Frames[1].Primitives).Vertices;
        Assert.Equal(Vector3.Zero, first[0].Position);
        Assert.Equal(new Vector3(2, 0, 0), second[0].Position);
        Assert.False(first.Select(vertex => vertex.Position)
            .SequenceEqual(second.Select(vertex => vertex.Position)));
        Assert.Same(model.Mesh, animation.Frames[0]);
    }

    [InstallFact]
    public void TerrainWallReadsGeometryAndTextures()
    {
        string install = Dk2InstallGate.Path;
        KmfModel model = ReadFromInstall(install, "ROCK WALL.kmf");
        Assert.Equal(KmfKind.Mesh, model.Kind);
        Assert.NotNull(model.Mesh);
        Assert.NotEmpty(model.Materials.SelectMany(material => material.TextureNames));
        Assert.NotEmpty(model.Mesh.Primitives);
        Assert.All(model.Mesh.Primitives, AssertCompletePrimitive);
    }

    [Fact]
    public void BadSignatureIsRejected()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => KmfReader.Read("NOPE"u8));
        Assert.Contains("KMSH", error.Message, StringComparison.Ordinal);
    }

    [InstallFact]
    public void EveryShippedKmfHasARecognizedStructure()
    {
        string install = Dk2InstallGate.Path;
        using var wad = WadArchive.Open(Path.Combine(install, "Data", "Meshes.WAD"));
        foreach (WadEntry entry in wad.Entries.Where(entry =>
            entry.Name.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)))
        {
            KmfModel model = KmfReader.Read(wad.Extract(entry));
            Assert.True(model.Version > 0, entry.Name);
            if (model.Kind != KmfKind.Group) Assert.NotNull(model.Mesh);
        }
    }

    private static void AssertCompletePrimitive(KmfPrimitive primitive)
    {
        Assert.NotEmpty(primitive.Vertices);
        Assert.NotEmpty(primitive.IndicesByDetailLevel);
        Assert.NotEmpty(primitive.IndicesByDetailLevel[0]);
        Assert.All(primitive.IndicesByDetailLevel.SelectMany(level => level),
            index => Assert.InRange(index, 0, primitive.Vertices.Count - 1));
        Assert.Contains(primitive.Vertices, vertex => vertex.Position != default);
        Assert.Contains(primitive.Vertices, vertex => vertex.Normal != default);
    }

    private static KmfModel ReadFromInstall(string install, string entryName)
    {
        using var wad = WadArchive.Open(Path.Combine(install, "Data", "Meshes.WAD"));
        return KmfReader.Read(wad.Extract(entryName));
    }

    private static byte[] StaticTriangle()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        Tag("KMSH"); U32(0); U32(1);
        Tag("HEAD"); U32(0); U32(1); U32(0);
        Tag("MATL"); U32(0); U32(1);
        Tag("MAT2"); U32(0); Text("stone"); U32(1); Text("stone0");
        U32(0); writer.Write(1f); writer.Write(2f); Text("");
        Tag("MESH"); U32(0); Tag("HEAD"); U32(0); Text("triangle");
        U32(1); U32(3); Vector(0, 0, 0); writer.Write(1f); U32(1);
        Tag("CTRL"); U32(0); U32(0);
        Tag("SPRS"); U32(0); Tag("SPHD"); U32(0); U32(1); U32(3); writer.Write(1f);
        Tag("SPRS"); U32(0); U32(0); writer.Write(new byte[] { 0, 1, 2 });
        Vertex(0, 0, 0); Vertex(1, 16384, 0); Vertex(2, 16384, 8192);
        Tag("GEOM"); U32(0); Vector(0, 0, 0); Vector(1, 0, 0); Vector(1, 2, 3);
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
            writer.Write(geometry); writer.Write(u); writer.Write(v); Vector(0, 1, 0);
        }
    }
}

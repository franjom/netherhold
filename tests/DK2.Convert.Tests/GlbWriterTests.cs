// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using DK2.Art.Kmf;
using DK2.Convert.Gltf;
using DK2.Data;
using DK2.Data.Wad;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class GlbWriterTests
{
    [Fact]
    public void GeometryMaterialsAndTextureUrisRoundTrip()
    {
        KmfModel source = TriangleModel();
        byte[] bytes = GlbWriter.Write(source);
        using GlbDocument glb = GlbDocument.Load(bytes);

        JsonElement root = glb.Json.RootElement;
        Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());
        Assert.Equal("brick%20wall.png", root.GetProperty("images")[0].GetProperty("uri").GetString());
        Assert.Equal("shine%20map.png", root.GetProperty("images")[1].GetProperty("uri").GetString());
        JsonElement primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        Assert.Equal(0, primitive.GetProperty("material").GetInt32());

        int positionAccessor = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
        Assert.Equal(new Vector3(1, -3, 2), glb.Vector3(positionAccessor, 2));
        int indexAccessor = primitive.GetProperty("indices").GetInt32();
        Assert.Equal([2, 1, 0], glb.Bytes(indexAccessor));
    }

    [Fact]
    public void AnimationFramesBecomeDistinctMorphTargetsAndLinearWeightKeys()
    {
        KmfModel source = KmfReader.Read(AnimatedKmfFixture.TwoFrames());
        using GlbDocument glb = GlbDocument.Load(GlbWriter.Write(source));

        JsonElement root = glb.Json.RootElement;
        JsonElement primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        int targetAccessor = primitive.GetProperty("targets")[0].GetProperty("POSITION").GetInt32();
        Assert.Equal(new Vector3(2, 0, 0), glb.Vector3(targetAccessor, 0));

        JsonElement animation = Assert.Single(root.GetProperty("animations").EnumerateArray());
        JsonElement sampler = animation.GetProperty("samplers")[0];
        Assert.Equal("LINEAR", sampler.GetProperty("interpolation").GetString());
        Assert.Equal([0f, 1f / GlbWriter.AssumedAnimationFramesPerSecond],
            glb.Floats(sampler.GetProperty("input").GetInt32()));
        Assert.Equal([0f, 1f], glb.Floats(sampler.GetProperty("output").GetInt32()));
        Assert.Equal("weights", animation.GetProperty("channels")[0]
            .GetProperty("target").GetProperty("path").GetString());
    }

    /// <summary>Given a pass time (the descriptor's frames/FPS) a clip spans its first frame to
    /// its last over it (<c>0x004B41E0</c>, <c>0x00577F10</c>, Q442).</summary>
    [Fact]
    public void APassTimeSpansTheClipFromItsFirstFrameToItsLast()
    {
        KmfModel source = KmfReader.Read(AnimatedKmfFixture.KeyedAtFramesZeroAndTwo());
        using GlbDocument glb = GlbDocument.Load(GlbWriter.Write(source, passSeconds: 0.25f));

        JsonElement sampler = glb.Json.RootElement.GetProperty("animations")[0].GetProperty("samplers")[0];
        Assert.Equal([0f, 0.125f, 0.25f], glb.Floats(sampler.GetProperty("input").GetInt32()));
    }

    [Fact]
    public void LaterFrameDecodeFailureStillWritesFrameZeroGlb()
    {
        KmfModel source = KmfReader.Read(AnimatedKmfFixture.TwoFrames(corruptSecondFrame: true));
        KmfAnimation animation = Assert.IsType<KmfAnimation>(source.Animation);
        Assert.False(animation.IsComplete);
        Assert.Single(animation.Frames);
        Assert.NotNull(animation.DecodeFailure);

        using GlbDocument glb = GlbDocument.Load(GlbWriter.Write(source));

        Assert.False(glb.Json.RootElement.TryGetProperty("animations", out _));
        JsonElement primitive = glb.Json.RootElement.GetProperty("meshes")[0]
            .GetProperty("primitives")[0];
        Assert.False(primitive.TryGetProperty("targets", out _));
        int positionAccessor = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
        Assert.Equal(Vector3.Zero, glb.Vector3(positionAccessor, 0));
    }

    [InstallTheory]
    [InlineData("Imp-PoseFrame.kmf")]
    [InlineData("Gob-PoseFrame.kmf")]
    [InlineData("ROCK WALL.kmf")]
    [InlineData("Imp-Idle2.kmf")]
    public void ShippedModelsProduceRoundTripLoadableGlb(string entryName)
    {
        string install = Dk2InstallGate.Path;
        using var wad = WadArchive.Open(Path.Combine(install, "Data", "Meshes.WAD"));
        KmfModel source = KmfReader.Read(wad.Extract(entryName));

        using GlbDocument glb = GlbDocument.Load(GlbWriter.Write(source));

        Assert.Equal(source.Mesh!.Primitives.Count,
            glb.Json.RootElement.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength());
    }

    [InstallFact]
    public void EveryShippedMeshProducesARoundTripLoadableGlb()
    {
        string install = Dk2InstallGate.Path;
        using var wad = WadArchive.Open(Path.Combine(install, "Data", "Meshes.WAD"));
        foreach (WadEntry entry in wad.Entries.Where(entry =>
            entry.Name.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)))
        {
            KmfModel source = KmfReader.Read(wad.Extract(entry));
            if (source.Mesh is null) continue;
            using GlbDocument glb = GlbDocument.Load(GlbWriter.Write(source));
            Assert.NotEmpty(glb.Json.RootElement.GetProperty("meshes")[0]
                .GetProperty("primitives").EnumerateArray());
        }
    }

    private static KmfModel TriangleModel()
    {
        var vertices = new KmfVertex[]
        {
            new(new Vector3(0, 0, 0), Vector3.UnitY, Vector2.Zero),
            new(new Vector3(1, 0, 0), Vector3.UnitY, Vector2.UnitX),
            new(new Vector3(1, 2, 3), Vector3.UnitY, Vector2.One),
        };
        var primitive = new KmfPrimitive(0, 1, vertices,
            new IReadOnlyList<byte>[] { new byte[] { 0, 1, 2 } });
        var material = new KmfMaterial("brick", ["brick wall"], 0, 0, 0, "shine map");
        return new KmfModel(1, KmfKind.Mesh, [material],
            new KmfMesh("triangle", Vector3.Zero, 1, [primitive]));
    }

    private sealed class GlbDocument : IDisposable
    {
        private readonly byte[] _binary;
        public JsonDocument Json { get; }

        private GlbDocument(JsonDocument json, byte[] binary)
        {
            Json = json;
            _binary = binary;
            ValidateAccessors();
        }

        public static GlbDocument Load(byte[] bytes)
        {
            Assert.True(bytes.Length >= 28);
            Assert.Equal(0x46546c67u, U32(bytes, 0));
            Assert.Equal(2u, U32(bytes, 4));
            Assert.Equal((uint)bytes.Length, U32(bytes, 8));
            int jsonLength = checked((int)U32(bytes, 12));
            Assert.Equal(0x4e4f534au, U32(bytes, 16));
            Assert.Equal(0, jsonLength & 3);
            int binaryHeader = 20 + jsonLength;
            int binaryLength = checked((int)U32(bytes, binaryHeader));
            Assert.Equal(0x004e4942u, U32(bytes, binaryHeader + 4));
            Assert.Equal(bytes.Length, binaryHeader + 8 + binaryLength);
            JsonDocument json = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
            byte[] binary = bytes.AsSpan(binaryHeader + 8, binaryLength).ToArray();
            return new GlbDocument(json, binary);
        }

        public Vector3 Vector3(int accessorIndex, int element)
        {
            (JsonElement accessor, int offset) = Accessor(accessorIndex);
            Assert.Equal("VEC3", accessor.GetProperty("type").GetString());
            offset += element * 12;
            return new Vector3(F32(_binary, offset), F32(_binary, offset + 4), F32(_binary, offset + 8));
        }

        public byte[] Bytes(int accessorIndex)
        {
            (JsonElement accessor, int offset) = Accessor(accessorIndex);
            Assert.Equal(5121, accessor.GetProperty("componentType").GetInt32());
            return _binary.AsSpan(offset, accessor.GetProperty("count").GetInt32()).ToArray();
        }

        public float[] Floats(int accessorIndex)
        {
            (JsonElement accessor, int offset) = Accessor(accessorIndex);
            Assert.Equal(5126, accessor.GetProperty("componentType").GetInt32());
            Assert.Equal("SCALAR", accessor.GetProperty("type").GetString());
            int count = accessor.GetProperty("count").GetInt32();
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = F32(_binary, offset + i * 4);
            return result;
        }

        public void Dispose() => Json.Dispose();

        private void ValidateAccessors()
        {
            JsonElement root = Json.RootElement;
            Assert.Equal(_binary.Length, Align4(root.GetProperty("buffers")[0]
                .GetProperty("byteLength").GetInt32()));
            JsonElement views = root.GetProperty("bufferViews");
            foreach (JsonElement view in views.EnumerateArray())
            {
                int offset = view.GetProperty("byteOffset").GetInt32();
                int length = view.GetProperty("byteLength").GetInt32();
                Assert.InRange(offset, 0, _binary.Length);
                Assert.InRange(length, 0, _binary.Length - offset);
            }
            foreach (JsonElement accessor in root.GetProperty("accessors").EnumerateArray())
                Assert.InRange(accessor.GetProperty("bufferView").GetInt32(), 0, views.GetArrayLength() - 1);
        }

        private (JsonElement Accessor, int Offset) Accessor(int index)
        {
            JsonElement accessor = Json.RootElement.GetProperty("accessors")[index];
            JsonElement view = Json.RootElement.GetProperty("bufferViews")[
                accessor.GetProperty("bufferView").GetInt32()];
            return (accessor, view.GetProperty("byteOffset").GetInt32());
        }

        private static uint U32(byte[] bytes, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        private static float F32(byte[] bytes, int offset) =>
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)));
        private static int Align4(int value) => (value + 3) & ~3;
    }
}

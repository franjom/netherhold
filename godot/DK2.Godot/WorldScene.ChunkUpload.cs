// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Runtime.InteropServices;
using DK2.Art.Kmf;
using DK2.Presentation;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The opaque terrain chunks' upload (rendering audit item 12): <see cref="TerrainChunkScheduler"/>'s build
/// task gathers each rebuilt chunk's opaque surfaces and writes them in Godot's own vertex format
/// (<see cref="ChunkPacker"/>), so the frame's thread only hands the bytes to <see cref="RenderingServer.MeshAddSurface"/>.
/// The bytes are those <c>MeshAddSurfaceFromArrays</c> wrote from the same arrays (<see cref="TerrainStateCustomFormat"/>,
/// no attribute compression): positions, then Godot's octahedral normal and the tangent 4.8 adds for it, then UV and
/// CUSTOM0. One mesh and one instance per 16x16-tile chunk, as the <see cref="MeshInstance3D"/>s before.</summary>
public partial class WorldScene
{
    private ChunkPacker? _chunkPacker;
    private Rid[] _chunkMeshes = [], _chunkInstances = [];
    private readonly Godot.Collections.Dictionary _chunkSurface = new Godot.Collections.Dictionary();
    private static readonly Variant FormatKey = "format", PrimitiveKey = "primitive", VertexDataKey = "vertex_data",
        AttributeDataKey = "attribute_data", VertexCountKey = "vertex_count", IndexDataKey = "index_data",
        IndexCountKey = "index_count", AabbKey = "aabb";

    private void OpenChunkUpload(TerrainChunkScheduler terrain)
    {
        _chunkMeshes = new Rid[terrain.ChunkCount];
        _chunkInstances = new Rid[terrain.ChunkCount];
        _chunkPacker = new ChunkPacker(terrain.ChunkCount);
        _chunkSurface[FormatKey] = ChunkSurfaceFormat();
        _chunkSurface[PrimitiveKey] = (int)RenderingServer.PrimitiveType.Triangles;
        terrain.Packer = _chunkPacker;
    }

    /// <summary>The format Godot gives a <see cref="TerrainStateCustomFormat"/> surface with normals, read from a
    /// one-triangle surface it built (4.8 adds the tangent and its version bits; docs/TRAPS.md), with its layout checked
    /// against what <see cref="PackedSurface.Write"/> writes.</summary>
    private static ulong ChunkSurfaceFormat()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { Vector3.Zero, Vector3.Right, Vector3.Up };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Up, Vector3.Up, Vector3.Up };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[3];
        arrays[(int)Mesh.ArrayType.Custom0] = new float[12];
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2 };
        Rid mesh = RenderingServer.MeshCreate();
        RenderingServer.MeshAddSurfaceFromArrays(mesh, RenderingServer.PrimitiveType.Triangles, arrays, null, null,
            (RenderingServer.ArrayFormat)TerrainStateCustomFormat);
        ulong format = RenderingServer.MeshGetSurface(mesh, 0)[FormatKey].AsUInt64();
        RenderingServer.FreeRid(mesh);

        var layout = (RenderingServer.ArrayFormat)format;
        const int vertices = 4;
        if ((layout & RenderingServer.ArrayFormat.FlagCompressAttributes) != 0
            || (layout & RenderingServer.ArrayFormat.FormatTangent) == 0
            || RenderingServer.MeshSurfaceGetFormatVertexStride(layout, vertices) != PackedSurface.PositionStride
            || RenderingServer.MeshSurfaceGetFormatNormalTangentStride(layout, vertices) != PackedSurface.NormalStride
            || RenderingServer.MeshSurfaceGetFormatAttributeStride(layout, vertices) != PackedSurface.AttributeStride
            || RenderingServer.MeshSurfaceGetFormatOffset(layout, vertices, (int)Mesh.ArrayType.Normal) != vertices * PackedSurface.PositionStride
            || RenderingServer.MeshSurfaceGetFormatOffset(layout, vertices, (int)Mesh.ArrayType.Tangent) != (vertices * PackedSurface.PositionStride) + 4
            || RenderingServer.MeshSurfaceGetFormatOffset(layout, vertices, (int)Mesh.ArrayType.TexUV) != 0
            || RenderingServer.MeshSurfaceGetFormatOffset(layout, vertices, (int)Mesh.ArrayType.Custom0) != 8)
        {
            GD.PushError("DK2.Godot: the terrain chunk layout is not positions, then normal and tangent, then UV and four-float CUSTOM0; chunks draw wrong.");
        }

        return format;
    }

    /// <summary>Chunk <paramref name="chunk"/>'s packed opaque surfaces as its mesh, each with its key's material, in
    /// the order the chunk first used the keys; the instance is made with the first surface.</summary>
    private void UploadChunkSurfaces(int chunk)
    {
        Rid mesh = _chunkMeshes[chunk];
        if (mesh.IsValid)
        {
            RenderingServer.MeshClear(mesh);
        }

        List<PackedSurface> surfaces = _chunkPacker!.Surfaces(chunk, out int count);
        int added = 0;
        for (int i = 0; i < count; i++)
        {
            PackedSurface surface = surfaces[i];
            if (surface.IndexCount == 0)
            {
                continue;
            }

            if (!mesh.IsValid)
            {
                _chunkMeshes[chunk] = mesh = RenderingServer.MeshCreate();
            }
            // The packed arrays are copied into Godot's own and released here, not by a finalizer.
            using (Variant vertices = surface.VertexData, attributes = surface.AttributeData, indices = surface.IndexData)
            {
                _chunkSurface[VertexDataKey] = vertices;
                _chunkSurface[AttributeDataKey] = attributes;
                _chunkSurface[IndexDataKey] = indices;
            }
            _chunkSurface[VertexCountKey] = surface.VertexCount;
            _chunkSurface[IndexCountKey] = surface.IndexCount;
            _chunkSurface[AabbKey] = surface.Bounds;
            RenderingServer.MeshAddSurface(mesh, _chunkSurface);
            RenderingServer.MeshSurfaceSetMaterial(mesh, added++, MaterialFor(surface.Key).GetRid());
        }
        if (_chunkInstances[chunk].IsValid || added == 0)
        {
            return;
        }

        Rid instance = RenderingServer.InstanceCreate2(mesh, GetWorld3D().Scenario);
        RenderingServer.InstanceGeometrySetCastShadowsSetting(instance, RenderingServer.ShadowCastingSetting.Off);
        if (TorchShadowsActive)
        {
            RenderingServer.InstanceSetLayerMask(instance, ShadowCasterLayers);
        }

        _chunkInstances[chunk] = instance;
    }

    private void FreeChunkUpload()
    {
        for (int i = 0; i < _chunkInstances.Length; i++)
        {
            if (_chunkInstances[i].IsValid)
            {
                RenderingServer.FreeRid(_chunkInstances[i]);
            }
        }

        for (int i = 0; i < _chunkMeshes.Length; i++)
        {
            if (_chunkMeshes[i].IsValid)
            {
                RenderingServer.FreeRid(_chunkMeshes[i]);
            }
        }

        _chunkInstances = [];
        _chunkMeshes = [];
    }

    /// <summary>Packs a chunk's opaque keys on the scheduler's build task: each key's vertices gathered as the chunk's
    /// frame-thread gather did before (<see cref="SurfaceBuffer"/>, keys in first-use order), then written as Godot's
    /// bytes into the chunk's own <see cref="PackedSurface"/>s, which the frame's thread reads once the build is taken
    /// over. A key is opaque when its look is (the blended ones, with their shared materials, stay on the frame's
    /// thread, <see cref="MaterialFor"/>).</summary>
    private sealed class ChunkPacker(int chunkCount) : ITerrainChunkPacker
    {
        private readonly List<PackedSurface>?[] _chunks = new List<PackedSurface>?[chunkCount];
        private readonly int[] _counts = new int[chunkCount];
        private readonly Dictionary<TerrainMaterialKey, SurfaceBuffer> _surfaceOf = new Dictionary<TerrainMaterialKey, SurfaceBuffer>();
        private readonly List<SurfaceBuffer> _surfaces = new List<SurfaceBuffer>(), _spare = new List<SurfaceBuffer>();
        private readonly Dictionary<TerrainMaterialKey, bool> _opaque = new Dictionary<TerrainMaterialKey, bool>();
        private int[] _remap = new int[256], _stamp = new int[256];
        private int _generation;

        public List<PackedSurface> Surfaces(int chunk, out int count)
        {
            count = _counts[chunk];
            return _chunks[chunk] ??= new List<PackedSurface>();
        }

        public void PackChunk(int chunk, ReadOnlySpan<TerrainTileGeometry> tiles)
        {
            foreach (SurfaceBuffer surface in _surfaces)
            {
                _spare.Add(surface);
            }

            _surfaces.Clear();
            _surfaceOf.Clear();
            foreach (TerrainTileGeometry tile in tiles)
            {
                if (_remap.Length < tile.VertexCount)
                {
                    _remap = new int[tile.VertexCount * 2];
                    _stamp = new int[tile.VertexCount * 2];
                }
                for (int s = 0; s < tile.KeyCount; s++)
                {
                    TerrainMaterialKey key = tile.Keys[s];
                    if (!IsOpaque(key))
                    {
                        continue;
                    }

                    if (!_surfaceOf.TryGetValue(key, out SurfaceBuffer? surface))
                    {
                        surface = TakeSurface(key);
                        _surfaceOf.Add(key, surface);
                        _surfaces.Add(surface);
                    }
                    _generation++;
                    surface.Append(tile, tile.Triangles[s], _remap, _stamp, _generation);
                }
            }

            List<PackedSurface> packed = _chunks[chunk] ??= new List<PackedSurface>();
            while (packed.Count < _surfaces.Count)
            {
                packed.Add(new PackedSurface());
            }

            for (int i = 0; i < _surfaces.Count; i++)
            {
                packed[i].Write(_surfaces[i]);
            }

            _counts[chunk] = _surfaces.Count;
        }

        private bool IsOpaque(in TerrainMaterialKey key)
        {
            if (!_opaque.TryGetValue(key, out bool opaque))
            {
                _opaque.Add(key, opaque = TerrainMaterialLook.For(key).Blend == KmfBlend.Opaque);
            }

            return opaque;
        }

        private SurfaceBuffer TakeSurface(in TerrainMaterialKey key)
        {
            SurfaceBuffer surface;
            if (_spare.Count > 0)
            {
                surface = _spare[^1];
                _spare.RemoveAt(_spare.Count - 1);
            }
            else
            {
                surface = new SurfaceBuffer();
            }

            surface.Reset(key);
            return surface;
        }
    }

    /// <summary>One opaque surface of a chunk in Godot's uncompressed vertex format, as <c>_surface_set_data</c>
    /// (servers/rendering/rendering_server.cpp at c51ff06e8) writes it: the vertex buffer's positions as three floats,
    /// then each vertex's octahedral normal and the arbitrary tangent Godot derives from it (two 16-bit unorm pairs);
    /// the attribute buffer's UV and CUSTOM0 (four floats); 16-bit indices up to 65,536 vertices, 32-bit beyond; the
    /// bounds as Godot computes them.</summary>
    private sealed class PackedSurface
    {
        public const int PositionStride = 12, NormalStride = 8, AttributeStride = 24;

        /// <summary>Godot's <c>SMALL_VEC3</c> (<c>CMP_EPSILON</c>): a surface's bounds are at least this wide.</summary>
        private const float SmallSize = 0.00001f;

        private byte[] _vertices = [], _attributes = [], _indices = [];
        private int _vertexBytes, _attributeBytes, _indexBytes;

        public TerrainMaterialKey Key { get; private set; }
        public int VertexCount { get; private set; }
        public int IndexCount { get; private set; }
        public Aabb Bounds { get; private set; }
        public Span<byte> VertexData
        {
            get
            {
                return _vertices.AsSpan(0, _vertexBytes);
            }
        }

        public Span<byte> AttributeData
        {
            get
            {
                return _attributes.AsSpan(0, _attributeBytes);
            }
        }

        public Span<byte> IndexData
        {
            get
            {
                return _indices.AsSpan(0, _indexBytes);
            }
        }

        public void Write(SurfaceBuffer source)
        {
            int count = source.VertexCount;
            Key = source.Key;
            VertexCount = count;
            IndexCount = source.IndexCount;
            _vertexBytes = count * (PositionStride + NormalStride);
            _attributeBytes = count * AttributeStride;
            int indexSize = count <= 1 << 16 ? 2 : 4;
            _indexBytes = IndexCount * indexSize;
            if (_vertices.Length < _vertexBytes)
            {
                _vertices = new byte[_vertexBytes * 3 / 2];
            }

            if (_attributes.Length < _attributeBytes)
            {
                _attributes = new byte[_attributeBytes * 3 / 2];
            }

            if (_indices.Length < _indexBytes)
            {
                _indices = new byte[_indexBytes * 3 / 2];
            }

            // Godot memcpy's each value in the machine's order; these views do the same.
            ReadOnlySpan<NVector3> positions = source.Positions, normals = source.Normals;
            ReadOnlySpan<NVector2> uvs = source.Uvs, anchors = source.Anchors, states = source.StateSources;
            int normalBlock = count * PositionStride;
            positions.CopyTo(MemoryMarshal.Cast<byte, NVector3>(_vertices.AsSpan(0, normalBlock)));
            Span<uint> normalWords = MemoryMarshal.Cast<byte, uint>(_vertices.AsSpan(normalBlock, count * NormalStride));
            Span<float> attributes = MemoryMarshal.Cast<byte, float>(_attributes.AsSpan(0, _attributeBytes));
            NVector3 min = count > 0 ? positions[0] : default, max = min;
            for (int i = 0; i < count; i++)
            {
                // _compute_aabb_from_points: Vector3::min and max, a < b ? a : b per component.
                NVector3 p = positions[i];
                min = new NVector3(p.X < min.X ? p.X : min.X, p.Y < min.Y ? p.Y : min.Y, p.Z < min.Z ? p.Z : min.Z);
                max = new NVector3(p.X > max.X ? p.X : max.X, p.Y > max.Y ? p.Y : max.Y, p.Z > max.Z ? p.Z : max.Z);

                NVector3 n = normals[i];
                normalWords[2 * i] = OctahedralNormal(n);
                normalWords[(2 * i) + 1] = OctahedralTangent(n);

                int a = i * (AttributeStride / sizeof(float));
                attributes[a] = uvs[i].X;
                attributes[a + 1] = uvs[i].Y;
                attributes[a + 2] = anchors[i].X;
                attributes[a + 3] = anchors[i].Y;
                attributes[a + 4] = states[i].X;
                attributes[a + 5] = states[i].Y;
            }
            NVector3 size = max - min;
            Bounds = new Aabb(new Vector3(min.X, min.Y, min.Z), new Vector3(
                size.X > SmallSize ? size.X : SmallSize, size.Y > SmallSize ? size.Y : SmallSize, size.Z > SmallSize ? size.Z : SmallSize));

            ReadOnlySpan<int> indices = source.Indices;
            if (indexSize == 2)
            {
                Span<ushort> shorts = MemoryMarshal.Cast<byte, ushort>(_indices.AsSpan(0, _indexBytes));
                for (int i = 0; i < indices.Length; i++)
                {
                    shorts[i] = (ushort)indices[i];
                }
            }
            else
            {
                indices.CopyTo(MemoryMarshal.Cast<byte, int>(_indices.AsSpan(0, _indexBytes)));
            }
        }

        /// <summary><c>Vector3::octahedron_encode</c> of the normal, each half as Godot's 16-bit unorm.</summary>
        private static uint OctahedralNormal(NVector3 n)
        {
            Octahedron(n.X, n.Y, n.Z, out float u, out float v);
            return Unorm16(u) | ((uint)Unorm16(v) << 16);
        }

        /// <summary>The tangent Godot makes for a surface with normals and no tangents: <c>(n.z, -n.x, n.y)</c> crossed
        /// with the normalised normal, normalised, then <c>octahedron_tangent_encode(1)</c>, with Godot's (0, 1) to
        /// (1, 1) fix.</summary>
        private static uint OctahedralTangent(NVector3 n)
        {
            Normalise(n.X, n.Y, n.Z, out float nx, out float ny, out float nz);
            float ax = n.Z, ay = -n.X, az = n.Y;
            Normalise((ay * nz) - (az * ny), (az * nx) - (ax * nz), (ax * ny) - (ay * nx), out float tx, out float ty, out float tz);
            Octahedron(tx, ty, tz, out float u, out float v);
            const float bias = 1.0f / 32767.0f;
            v = v > bias ? v : bias;
            v = (v * 0.5f) + 0.5f;
            ushort first = Unorm16(u), second = Unorm16(v);
            if (first == 0 && second == 65535)
            {
                first = 65535;
            }

            return first | ((uint)second << 16);
        }

        private static void Octahedron(float x, float y, float z, out float u, out float v)
        {
            float sum = MathF.Abs(x) + MathF.Abs(y) + MathF.Abs(z);
            x /= sum;
            y /= sum;
            z /= sum;
            if (z >= 0.0f)
            {
                u = x;
                v = y;
            }
            else
            {
                u = (1.0f - MathF.Abs(y)) * (x >= 0.0f ? 1.0f : -1.0f);
                v = (1.0f - MathF.Abs(x)) * (y >= 0.0f ? 1.0f : -1.0f);
            }
            u = (u * 0.5f) + 0.5f;
            v = (v * 0.5f) + 0.5f;
        }

        /// <summary><c>Vector3::normalize</c>: zero when not finite or of zero length.</summary>
        private static void Normalise(float x, float y, float z, out float nx, out float ny, out float nz)
        {
            float lengthSquared = (x * x) + (y * y) + (z * z);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || lengthSquared == 0)
            {
                nx = ny = nz = 0;
                return;
            }
            float length = MathF.Sqrt(lengthSquared);
            nx = x / length;
            ny = y / length;
            nz = z / length;
        }

        /// <summary><c>(uint16_t)CLAMP(value * 65535, 0, 65535)</c>.</summary>
        private static ushort Unorm16(float value)
        {
            float scaled = value * 65535f;
            return (ushort)(scaled < 0f ? 0f : scaled > 65535f ? 65535f : scaled);
        }
    }
}

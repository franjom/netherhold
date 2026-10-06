// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace DK2.Convert.Kmf;

/// <summary>Reads static KMF meshes and every decodable frame of animated KMFs.</summary>
public static class KmfReader
{
    public static KmfModel Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Read(copy.ToArray());
    }

    public static KmfModel Read(ReadOnlySpan<byte> data)
    {
        var reader = new SpanReader(data);
        reader.Expect("KMSH"); reader.SkipSectionSize();
        uint version = reader.UInt32();
        reader.Expect("HEAD"); reader.SkipSectionSize();
        var kind = (KmfKind)reader.UInt32();
        if (kind is not (KmfKind.Mesh or KmfKind.Animation or KmfKind.Group))
            throw reader.Invalid($"Unsupported KMF kind {(uint)kind}.");
        _ = reader.UInt32(); // UNKNOWN header field.
        if (kind == KmfKind.Group)
            return new KmfModel(version, kind, [], null);

        IReadOnlyList<KmfMaterial> materials = ReadMaterials(ref reader);
        return kind == KmfKind.Mesh
            ? ReadStaticModel(ref reader, version, materials)
            : ReadAnimatedModel(ref reader, version, materials);
    }

    private static IReadOnlyList<KmfMaterial> ReadMaterials(ref SpanReader reader)
    {
        reader.Expect("MATL"); reader.SkipSectionSize();
        uint count = reader.Count("material");
        var materials = new List<KmfMaterial>((int)count);
        for (uint i = 0; i < count; i++)
        {
            reader.Expect("MAT2"); reader.SkipSectionSize();
            string name = reader.NullTerminatedAscii();
            uint textureCount = reader.Count("material texture");
            var textures = new List<string>((int)textureCount);
            for (uint texture = 0; texture < textureCount; texture++)
                textures.Add(reader.NullTerminatedAscii());
            materials.Add(new KmfMaterial(name, textures, reader.UInt32(),
                reader.Single(), reader.Single(), reader.NullTerminatedAscii()));
        }
        return materials;
    }

    private static KmfModel ReadStaticModel(ref SpanReader reader, uint version,
        IReadOnlyList<KmfMaterial> materials)
    {
        reader.Expect("MESH"); reader.SkipSectionSize();
        reader.Expect("HEAD"); reader.SkipSectionSize();
        string name = reader.NullTerminatedAscii();
        uint primitiveCount = reader.Count("mesh primitive");
        uint geometryCount = reader.Count("mesh geometry");
        Vector3 origin = reader.Vector3();
        float boundsScale = reader.Single();
        uint detailLevelCount = reader.Count("detail level");
        SkipControls(ref reader);
        List<SpriteData> sprites = ReadStaticSprites(ref reader, primitiveCount, detailLevelCount);
        reader.Expect("GEOM"); reader.SkipSectionSize();
        var positions = new Vector3[(int)geometryCount];
        for (int i = 0; i < positions.Length; i++) positions[i] = reader.Vector3();

        var primitives = new List<KmfPrimitive>(sprites.Count);
        foreach (SpriteData sprite in sprites)
        {
            ValidateSprite(ref reader, sprite, materials.Count);
            var vertices = new KmfVertex[sprite.Vertices.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                RawVertex vertex = sprite.Vertices[i];
                if (vertex.GeometryIndex >= positions.Length)
                    throw reader.Invalid($"Geometry index {vertex.GeometryIndex} is out of range.");
                vertices[i] = new KmfVertex(positions[vertex.GeometryIndex], vertex.Normal, vertex.Uv);
            }
            primitives.Add(new KmfPrimitive(sprite.MaterialIndex, sprite.DetailFactor,
                vertices, sprite.Indices));
        }
        return new KmfModel(version, KmfKind.Mesh, materials,
            new KmfMesh(name, origin, boundsScale, primitives));
    }

    private static KmfModel ReadAnimatedModel(ref SpanReader reader, uint version,
        IReadOnlyList<KmfMaterial> materials)
    {
        reader.Expect("ANIM"); reader.SkipSectionSize();
        reader.Expect("HEAD"); reader.SkipSectionSize();
        string name = reader.NullTerminatedAscii();
        uint primitiveCount = reader.Count("animation primitive");
        uint frameCount = reader.Count("animation frame");
        if (frameCount == 0) throw reader.Invalid("Animation has no frames.");
        uint indexTableCount = reader.Count("animation index table");
        uint geometryCount = reader.Count("animation geometry");
        uint frameMode = reader.UInt32();
        if (frameMode > 1) throw reader.Invalid($"Unknown animation frame mode {frameMode}.");
        Vector3 origin = reader.Vector3();
        float boundsScale = reader.Single();
        float geometryScale = reader.Single();
        uint detailLevelCount = reader.Count("detail level");
        SkipControls(ref reader);
        List<SpriteData> sprites = ReadAnimatedSprites(ref reader, primitiveCount, detailLevelCount);

        reader.Expect("ITAB"); reader.SkipSectionSize();
        uint tableChunkCount = checked((frameCount + 127) / 128);
        var indexTables = new uint[(int)tableChunkCount][];
        for (uint chunk = 0; chunk < tableChunkCount; chunk++)
        {
            var table = new uint[(int)indexTableCount];
            for (int i = 0; i < table.Length; i++) table[i] = reader.UInt32();
            indexTables[(int)chunk] = table;
        }

        reader.Expect("GEOM"); reader.SkipSectionSize();
        var positions = new Vector3[(int)geometryCount];
        var keyFrames = new byte[(int)geometryCount];
        for (int i = 0; i < positions.Length; i++)
        {
            uint packed = reader.UInt32();
            int xBits = (int)((packed >> 20) & 0x3ff);
            int yBits = (int)((packed >> 10) & 0x3ff);
            int zBits = (int)(packed & 0x3ff);
            positions[i] = new Vector3(
                (xBits - 512) / 511f,
                (yBits - 512) / 511f,
                (zBits - 512) / 511f) * geometryScale;
            keyFrames[i] = reader.Byte();
        }

        reader.Expect("VGEO"); reader.SkipSectionSize();
        var frameOffsets = new byte[(int)indexTableCount, (int)frameCount];
        for (int index = 0; index < (int)indexTableCount; index++)
            for (uint frame = 0; frame < frameCount; frame++)
                frameOffsets[index, (int)frame] = reader.Byte();

        foreach (SpriteData sprite in sprites) ValidateSprite(ref reader, sprite, materials.Count);
        KmfMesh frameZero = BuildFrame(0);
        var frames = new List<KmfMesh>(checked((int)frameCount)) { frameZero };
        string? decodeFailure = null;
        try
        {
            for (int frame = 1; frame < (int)frameCount; frame++) frames.Add(BuildFrame(frame));
        }
        catch (InvalidDataException exception)
        {
            decodeFailure = exception.Message;
        }
        return new KmfModel(version, KmfKind.Animation, materials,
            frameZero, new KmfAnimation(frameCount, frames, decodeFailure));

        // The drawer (0x00577F10) clamps the integer frame to [0, frames - 2] and evaluates the
        // last frame from there, with t carried past the key interval it names.
        KmfMesh BuildFrame(int frame)
        {
            int lookup = Math.Min(frame, Math.Max(0, (int)frameCount - 2));
            var primitives = new List<KmfPrimitive>(sprites.Count);
            foreach (SpriteData sprite in sprites)
            {
                var vertices = new KmfVertex[sprite.Vertices.Count];
                for (int i = 0; i < vertices.Length; i++)
                {
                    RawVertex vertex = sprite.Vertices[i];
                    if ((uint)vertex.GeometryIndex >= indexTableCount)
                        throw Invalid($"Animation table index {vertex.GeometryIndex} is out of range.");
                    uint geometryIndex;
                    try
                    {
                        geometryIndex = checked(indexTables[lookup / 128][vertex.GeometryIndex]
                            + frameOffsets[vertex.GeometryIndex, lookup]);
                    }
                    catch (OverflowException exception)
                    {
                        throw Invalid($"Animation geometry index overflow at frame {frame}.", exception);
                    }
                    if (geometryIndex >= positions.Length)
                        throw Invalid(
                            $"Animation geometry index {geometryIndex} is out of range at frame {frame}.");
                    vertices[i] = new KmfVertex(PositionAt(frame, lookup, (int)geometryIndex), vertex.Normal, vertex.Uv);
                }
                primitives.Add(new KmfPrimitive(sprite.MaterialIndex, sprite.DetailFactor,
                    vertices, sprite.Indices));
            }
            return new KmfMesh(name, origin, boundsScale, primitives);
        }

        // The engine's vertex evaluator (0x0057E5B0): each GEOM byte is the frame, within its
        // 128-frame block, at which that entry is exact; a frame between two keys lies on the line
        // from entry g to g + 1 (kmf-animation-playback.md §1). Equal keys, a backwards key and a
        // last entry hold g: equal keys read the table's unset entry 0, and the other two occur
        // in shipped KMFs only on the key's own frame, where DK2's numerator is 0 (ADR-0389).
        Vector3 PositionAt(int frame, int lookup, int g)
        {
            if (g + 1 >= positions.Length) return positions[g];
            int blockStart = lookup / 128 * 128;
            int keyA = blockStart + keyFrames[g];
            int span = keyFrames[g + 1] - keyFrames[g];
            if (span <= 0) return positions[g];
            float t = (frame - keyA) / (float)span;
            return positions[g] + (positions[g + 1] - positions[g]) * t;
        }

        InvalidDataException Invalid(string message, Exception? innerException = null) =>
            new($"Invalid KMF animation: {message}", innerException);
    }

    private static void SkipControls(ref SpanReader reader)
    {
        reader.Expect("CTRL"); reader.SkipSectionSize();
        reader.Skip(checked((int)reader.Count("control") * 8));
    }

    private static List<SpriteData> ReadStaticSprites(ref SpanReader reader,
        uint primitiveCount, uint detailLevelCount)
    {
        reader.Expect("SPRS"); reader.SkipSectionSize();
        List<SpriteHeader> headers = ReadSpriteHeaders(ref reader, primitiveCount, detailLevelCount);
        var sprites = new List<SpriteData>((int)primitiveCount);
        foreach (SpriteHeader header in headers)
        {
            reader.Expect("SPRS"); reader.SkipSectionSize();
            uint material = reader.UInt32();
            IReadOnlyList<IReadOnlyList<byte>> indices = ReadIndices(ref reader, header.TriangleCounts);
            var vertices = new List<RawVertex>((int)header.VertexCount);
            for (uint i = 0; i < header.VertexCount; i++)
            {
                ushort geometry = reader.UInt16();
                vertices.Add(new RawVertex(geometry, reader.Uv(), reader.Vector3()));
            }
            sprites.Add(new SpriteData(material, header.DetailFactor, indices, vertices));
        }
        return sprites;
    }

    private static List<SpriteData> ReadAnimatedSprites(ref SpanReader reader,
        uint primitiveCount, uint detailLevelCount)
    {
        reader.Expect("SPRS"); reader.SkipSectionSize();
        List<SpriteHeader> headers = ReadSpriteHeaders(ref reader, primitiveCount, detailLevelCount);
        var sprites = new List<SpriteData>((int)primitiveCount);
        foreach (SpriteHeader header in headers)
        {
            reader.Expect("SPRS"); reader.SkipSectionSize();
            uint material = reader.UInt32();
            reader.Expect("POLY"); reader.SkipSectionSize();
            IReadOnlyList<IReadOnlyList<byte>> indices = ReadIndices(ref reader, header.TriangleCounts);
            reader.Expect("VERT"); reader.SkipSectionSize();
            var vertices = new List<RawVertex>((int)header.VertexCount);
            for (uint i = 0; i < header.VertexCount; i++)
            {
                Vector2 uv = reader.Uv();
                Vector3 normal = reader.Vector3();
                vertices.Add(new RawVertex(reader.UInt16(), uv, normal));
            }
            sprites.Add(new SpriteData(material, header.DetailFactor, indices, vertices));
        }
        return sprites;
    }

    private static List<SpriteHeader> ReadSpriteHeaders(ref SpanReader reader,
        uint primitiveCount, uint detailLevelCount)
    {
        var headers = new List<SpriteHeader>((int)primitiveCount);
        for (uint i = 0; i < primitiveCount; i++)
        {
            reader.Expect("SPHD"); reader.SkipSectionSize();
            var triangleCounts = new uint[(int)detailLevelCount];
            for (int detail = 0; detail < triangleCounts.Length; detail++)
                triangleCounts[detail] = reader.Count("triangle");
            headers.Add(new SpriteHeader(triangleCounts, reader.Count("vertex"), reader.Single()));
        }
        return headers;
    }

    private static IReadOnlyList<IReadOnlyList<byte>> ReadIndices(ref SpanReader reader,
        IReadOnlyList<uint> triangleCounts)
    {
        var levels = new List<IReadOnlyList<byte>>(triangleCounts.Count);
        foreach (uint triangleCount in triangleCounts)
            levels.Add(reader.Bytes(checked((int)triangleCount * 3)));
        return levels;
    }

    private static void ValidateSprite(ref SpanReader reader, SpriteData sprite, int materialCount)
    {
        if (sprite.MaterialIndex >= materialCount)
            throw reader.Invalid($"Material index {sprite.MaterialIndex} is out of range.");
        foreach (byte index in sprite.Indices.SelectMany(level => level))
            if (index >= sprite.Vertices.Count)
                throw reader.Invalid($"Vertex index {index} is out of range.");
    }

    private sealed record SpriteHeader(uint[] TriangleCounts, uint VertexCount, float DetailFactor);
    private sealed record SpriteData(uint MaterialIndex, float DetailFactor,
        IReadOnlyList<IReadOnlyList<byte>> Indices, IReadOnlyList<RawVertex> Vertices);
    private sealed record RawVertex(int GeometryIndex, Vector2 Uv, Vector3 Normal);

    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;
        public SpanReader(ReadOnlySpan<byte> data) => _data = data;

        public byte Byte() { Require(1); return _data[_position++]; }
        public ushort UInt16()
        {
            Require(2); ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_position..]);
            _position += 2; return value;
        }
        public uint UInt32()
        {
            Require(4); uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data[_position..]);
            _position += 4; return value;
        }
        public float Single() => BitConverter.Int32BitsToSingle(unchecked((int)UInt32()));
        public Vector3 Vector3() => new(Single(), Single(), Single());
        public Vector2 Uv() => new(UInt16() / 32768f, UInt16() / 32768f);
        public uint Count(string name)
        {
            uint value = UInt32();
            if (value > int.MaxValue) throw Invalid($"{name} count {value} is too large.");
            return value;
        }
        public string NullTerminatedAscii()
        {
            int start = _position;
            while (_position < _data.Length && _data[_position] != 0) _position++;
            if (_position == _data.Length) throw Invalid("Unterminated ASCII string.");
            string value = Encoding.ASCII.GetString(_data[start.._position]); _position++; return value;
        }
        public byte[] Bytes(int count)
        {
            Require(count); byte[] value = _data.Slice(_position, count).ToArray();
            _position += count; return value;
        }
        public void Expect(string expected)
        {
            Require(4); string actual = Encoding.ASCII.GetString(_data.Slice(_position, 4));
            _position += 4;
            if (actual != expected) throw Invalid($"Expected chunk '{expected}', found '{actual}'.");
        }
        public void SkipSectionSize() => _ = UInt32();
        public void Skip(int count) { Require(count); _position += count; }
        public InvalidDataException Invalid(string message, Exception? innerException = null) =>
            new($"Invalid KMF at byte 0x{_position:x}: {message}", innerException);
        private void Require(int count)
        {
            if (count < 0 || _position > _data.Length - count)
                throw Invalid($"Need {count} bytes, only {_data.Length - _position} remain.");
        }
    }
}

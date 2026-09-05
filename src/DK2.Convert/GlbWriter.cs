// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using DK2.Convert.Kmf;

namespace DK2.Convert.Gltf;

/// <summary>Writes one parsed KMF mesh as a glTF 2.0 binary container.</summary>
public static class GlbWriter
{
    private const uint GlbMagic = 0x46546c67;
    private const uint JsonChunk = 0x4e4f534a;
    private const uint BinaryChunk = 0x004e4942;

    public static byte[] Write(KmfModel model, Func<string, string>? textureUri = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var output = new MemoryStream();
        Write(model, output, textureUri);
        return output.ToArray();
    }

    public static void Write(KmfModel model, Stream output,
        Func<string, string>? textureUri = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("Output stream is not writable.", nameof(output));
        KmfMesh mesh = model.Mesh ?? throw new NotSupportedException(
            $"KMF kind {model.Kind} does not contain directly writable mesh geometry.");
        textureUri ??= DefaultTextureUri;

        var binary = new BinaryBuilder();
        var primitives = new List<PrimitiveLayout>(mesh.Primitives.Count);
        foreach (KmfPrimitive primitive in mesh.Primitives)
            primitives.Add(binary.Add(primitive));

        TextureLayout textures = TextureLayout.Create(model.Materials, textureUri);
        byte[] json = BuildJson(model, mesh, primitives, binary.ByteLength, textures);
        byte[] bin = binary.ToArray();
        int jsonLength = Align4(json.Length);
        int binLength = Align4(bin.Length);
        uint totalLength = checked((uint)(12 + 8 + jsonLength + 8 + binLength));

        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header, GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], totalLength);
        output.Write(header);
        WriteChunk(output, json, jsonLength, JsonChunk, 0x20);
        WriteChunk(output, bin, binLength, BinaryChunk, 0);
    }

    private static byte[] BuildJson(KmfModel model, KmfMesh mesh,
        IReadOnlyList<PrimitiveLayout> primitives, int binaryLength, TextureLayout textures)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("asset");
            writer.WriteString("version", "2.0");
            writer.WriteString("generator", "DK2.Convert");
            writer.WriteEndObject();
            writer.WriteNumber("scene", 0);
            writer.WriteStartArray("scenes");
            writer.WriteStartObject();
            writer.WriteStartArray("nodes"); writer.WriteNumberValue(0); writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteStartArray("nodes");
            writer.WriteStartObject(); writer.WriteString("name", mesh.Name); writer.WriteNumber("mesh", 0);
            writer.WriteEndObject();
            writer.WriteEndArray();
            WriteMaterials(writer, model.Materials, textures);
            WriteTextures(writer, textures);
            writer.WriteStartArray("meshes");
            writer.WriteStartObject();
            writer.WriteString("name", mesh.Name);
            writer.WriteStartArray("primitives");
            foreach (PrimitiveLayout primitive in primitives) WritePrimitive(writer, primitive);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteStartArray("buffers");
            writer.WriteStartObject(); writer.WriteNumber("byteLength", binaryLength); writer.WriteEndObject();
            writer.WriteEndArray();
            WriteBufferViews(writer, primitives);
            WriteAccessors(writer, primitives);
            writer.WriteEndObject();
        }
        return json.ToArray();
    }

    private static void WriteMaterials(Utf8JsonWriter writer,
        IReadOnlyList<KmfMaterial> materials, TextureLayout textures)
    {
        if (materials.Count == 0) return;
        writer.WriteStartArray("materials");
        for (int i = 0; i < materials.Count; i++)
        {
            KmfMaterial material = materials[i];
            writer.WriteStartObject();
            writer.WriteString("name", material.Name);
            if (material.TextureNames.Count > 0)
            {
                writer.WriteStartObject("pbrMetallicRoughness");
                writer.WriteStartObject("baseColorTexture");
                writer.WriteNumber("index", textures.TextureIndexByMaterial[i][0]);
                writer.WriteEndObject();
                writer.WriteNumber("metallicFactor", 0);
                writer.WriteEndObject();
            }
            if (material.TextureNames.Count > 0 || textures.EnvironmentIndexByMaterial[i] is not null)
            {
                writer.WriteStartObject("extras");
                writer.WriteStartArray("dk2TextureAlternatives");
                foreach (int index in textures.TextureIndexByMaterial[i])
                    writer.WriteStringValue(textures.Uris[index]);
                writer.WriteEndArray();
                if (textures.EnvironmentIndexByMaterial[i] is int environmentIndex)
                    writer.WriteString("dk2EnvironmentTexture", textures.Uris[environmentIndex]);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteTextures(Utf8JsonWriter writer, TextureLayout textures)
    {
        if (textures.Uris.Count == 0) return;
        writer.WriteStartArray("textures");
        for (int i = 0; i < textures.Uris.Count; i++)
        {
            writer.WriteStartObject(); writer.WriteNumber("source", i); writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("images");
        foreach (string uri in textures.Uris)
        {
            writer.WriteStartObject(); writer.WriteString("uri", uri); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WritePrimitive(Utf8JsonWriter writer, PrimitiveLayout primitive)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("attributes");
        writer.WriteNumber("POSITION", primitive.PositionAccessor);
        writer.WriteNumber("NORMAL", primitive.NormalAccessor);
        writer.WriteNumber("TEXCOORD_0", primitive.UvAccessor);
        writer.WriteEndObject();
        if (primitive.IndexAccessor is int indexAccessor)
            writer.WriteNumber("indices", indexAccessor);
        else
            writer.WriteNumber("mode", 0); // No triangles in LOD 0: preserve it as points.
        writer.WriteNumber("material", primitive.MaterialIndex);
        writer.WriteEndObject();
    }

    private static void WriteBufferViews(Utf8JsonWriter writer,
        IReadOnlyList<PrimitiveLayout> primitives)
    {
        writer.WriteStartArray("bufferViews");
        foreach (PrimitiveLayout primitive in primitives)
        {
            View(primitive.PositionView, 34962);
            View(primitive.NormalView, 34962);
            View(primitive.UvView, 34962);
            if (primitive.IndexView is BufferView indices) View(indices, 34963);
        }
        writer.WriteEndArray();
        void View(BufferView view, int target)
        {
            writer.WriteStartObject(); writer.WriteNumber("buffer", 0);
            writer.WriteNumber("byteOffset", view.Offset); writer.WriteNumber("byteLength", view.Length);
            writer.WriteNumber("target", target); writer.WriteEndObject();
        }
    }

    private static void WriteAccessors(Utf8JsonWriter writer,
        IReadOnlyList<PrimitiveLayout> primitives)
    {
        writer.WriteStartArray("accessors");
        foreach (PrimitiveLayout primitive in primitives)
        {
            Accessor(primitive.PositionViewIndex, 5126, primitive.VertexCount, "VEC3",
                primitive.Minimum, primitive.Maximum);
            Accessor(primitive.NormalViewIndex, 5126, primitive.VertexCount, "VEC3");
            Accessor(primitive.UvViewIndex, 5126, primitive.VertexCount, "VEC2");
            if (primitive.IndexViewIndex is int indexView)
                Accessor(indexView, 5121, primitive.IndexCount, "SCALAR");
        }
        writer.WriteEndArray();
        void Accessor(int view, int componentType, int count, string type,
            Vector3? minimum = null, Vector3? maximum = null)
        {
            writer.WriteStartObject(); writer.WriteNumber("bufferView", view);
            writer.WriteNumber("componentType", componentType); writer.WriteNumber("count", count);
            writer.WriteString("type", type);
            if (minimum is Vector3 min) WriteVector("min", min);
            if (maximum is Vector3 max) WriteVector("max", max);
            writer.WriteEndObject();
        }
        void WriteVector(string name, Vector3 value)
        {
            writer.WriteStartArray(name); writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y); writer.WriteNumberValue(value.Z); writer.WriteEndArray();
        }
    }

    private static string DefaultTextureUri(string textureName)
    {
        string normalized = textureName.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        int dot = normalized.LastIndexOf('.');
        if (dot > slash) normalized = normalized[..dot];
        return string.Join('/', normalized.Split('/').Select(Uri.EscapeDataString)) + ".png";
    }

    private static void WriteChunk(Stream output, byte[] bytes, int paddedLength,
        uint type, byte padding)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)paddedLength));
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], type);
        output.Write(header); output.Write(bytes);
        for (int i = bytes.Length; i < paddedLength; i++) output.WriteByte(padding);
    }

    private static int Align4(int value) => checked((value + 3) & ~3);

    private sealed class BinaryBuilder
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _writer;
        private int _viewCount;
        private int _accessorCount;
        public BinaryBuilder() => _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        public int ByteLength => checked((int)_stream.Length);
        public byte[] ToArray() => _stream.ToArray();

        public PrimitiveLayout Add(KmfPrimitive primitive)
        {
            if (primitive.Vertices.Count == 0)
                throw new InvalidDataException("A glTF primitive cannot have no vertices.");
            Vector3 minimum = new(float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity);
            BufferView positions = AddView(() =>
            {
                foreach (KmfVertex vertex in primitive.Vertices)
                {
                    Vector3 value = ToGltf(vertex.Position); Write(value);
                    minimum = Vector3.Min(minimum, value); maximum = Vector3.Max(maximum, value);
                }
            });
            int positionView = _viewCount++; int positionAccessor = _accessorCount++;
            BufferView normals = AddView(() =>
            {
                foreach (KmfVertex vertex in primitive.Vertices) Write(ToGltf(vertex.Normal));
            });
            int normalView = _viewCount++; int normalAccessor = _accessorCount++;
            BufferView uvs = AddView(() =>
            {
                foreach (KmfVertex vertex in primitive.Vertices)
                { _writer.Write(vertex.TextureCoordinate.X); _writer.Write(vertex.TextureCoordinate.Y); }
            });
            int uvView = _viewCount++; int uvAccessor = _accessorCount++;
            IReadOnlyList<byte> sourceIndices = primitive.IndicesByDetailLevel.Count == 0
                ? [] : primitive.IndicesByDetailLevel[0];
            BufferView? indices = null; int? indexView = null; int? indexAccessor = null;
            if (sourceIndices.Count > 0)
            {
                indices = AddView(() =>
                {
                    for (int i = 0; i < sourceIndices.Count; i += 3)
                    {
                        _writer.Write(sourceIndices[i + 2]);
                        _writer.Write(sourceIndices[i + 1]);
                        _writer.Write(sourceIndices[i]);
                    }
                });
                indexView = _viewCount++; indexAccessor = _accessorCount++;
            }
            return new PrimitiveLayout((int)primitive.MaterialIndex, primitive.Vertices.Count,
                sourceIndices.Count, positions, normals, uvs, indices, positionView, normalView,
                uvView, indexView, positionAccessor, normalAccessor, uvAccessor, indexAccessor,
                minimum, maximum);
        }

        private BufferView AddView(Action write)
        {
            while ((_stream.Position & 3) != 0) _writer.Write((byte)0);
            int offset = checked((int)_stream.Position); write();
            return new BufferView(offset, checked((int)_stream.Position - offset));
        }
        private void Write(Vector3 value)
        { _writer.Write(value.X); _writer.Write(value.Y); _writer.Write(value.Z); }
        private static Vector3 ToGltf(Vector3 value) => new(value.X, -value.Z, value.Y);
    }

    private sealed record BufferView(int Offset, int Length);
    private sealed record PrimitiveLayout(int MaterialIndex, int VertexCount, int IndexCount,
        BufferView PositionView, BufferView NormalView, BufferView UvView, BufferView? IndexView,
        int PositionViewIndex, int NormalViewIndex, int UvViewIndex, int? IndexViewIndex,
        int PositionAccessor, int NormalAccessor, int UvAccessor, int? IndexAccessor,
        Vector3 Minimum, Vector3 Maximum);

    private sealed record TextureLayout(IReadOnlyList<string> Uris,
        IReadOnlyList<IReadOnlyList<int>> TextureIndexByMaterial,
        IReadOnlyList<int?> EnvironmentIndexByMaterial)
    {
        public static TextureLayout Create(IReadOnlyList<KmfMaterial> materials,
            Func<string, string> textureUri)
        {
            var uris = new List<string>();
            var byUri = new Dictionary<string, int>(StringComparer.Ordinal);
            var byMaterial = new List<IReadOnlyList<int>>(materials.Count);
            var environmentByMaterial = new List<int?>(materials.Count);
            foreach (KmfMaterial material in materials)
            {
                var indices = new List<int>(material.TextureNames.Count);
                foreach (string name in material.TextureNames)
                    indices.Add(Add(name));
                byMaterial.Add(indices);
                environmentByMaterial.Add(string.IsNullOrEmpty(material.EnvironmentTextureName)
                    ? null : Add(material.EnvironmentTextureName));
            }
            return new TextureLayout(uris, byMaterial, environmentByMaterial);

            int Add(string name)
            {
                string uri = textureUri(name);
                if (byUri.TryGetValue(uri, out int index)) return index;
                index = uris.Count; uris.Add(uri); byUri.Add(uri, index); return index;
            }
        }
    }
}

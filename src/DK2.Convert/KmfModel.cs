// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Convert.Kmf;

public enum KmfKind : uint { Mesh = 1, Animation = 2, Group = 3 }

public sealed record KmfMaterial(string Name, IReadOnlyList<string> TextureNames,
    uint Flags, float Brightness, float Shininess, string EnvironmentTextureName);

public sealed record KmfVertex(Vector3 Position, Vector3 Normal, Vector2 TextureCoordinate);

public sealed record KmfPrimitive(uint MaterialIndex, float DetailFactor,
    IReadOnlyList<KmfVertex> Vertices,
    IReadOnlyList<IReadOnlyList<byte>> IndicesByDetailLevel);

public sealed record KmfMesh(string Name, Vector3 Origin, float BoundsScale,
    IReadOnlyList<KmfPrimitive> Primitives);

public sealed record KmfAnimation(uint FrameCount, IReadOnlyList<KmfMesh> Frames,
    string? DecodeFailure = null)
{
    public bool IsComplete => DecodeFailure is null && Frames.Count == FrameCount;
}

public sealed record KmfModel(uint Version, KmfKind Kind,
    IReadOnlyList<KmfMaterial> Materials, KmfMesh? Mesh, KmfAnimation? Animation = null)
{
    public uint FrameCount => Animation?.FrameCount ?? 0;
}

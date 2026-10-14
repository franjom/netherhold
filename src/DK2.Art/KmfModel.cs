// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Art.Kmf;

public enum KmfKind : uint { Mesh = 1, Animation = 2, Group = 3 }

/// <summary>How a KMF material is blended over what is behind it (docs/research/kmf-materials.md).</summary>
public enum KmfBlend
{
    /// <summary>No blending; writes depth. Texture alpha is ignored.</summary>
    Opaque,

    /// <summary>SRCALPHA / INVSRCALPHA by the texture's alpha, no depth write, depth-sorted.</summary>
    AlphaBlend,

    /// <summary>ONE / ONE, no depth write, the material's colour forced to full white.</summary>
    Additive,
}

/// <summary>A MAT2 record. <paramref name="Flags"/> is the file's word; the loader
/// (<c>0x0057D390</c>) keeps only bits 0x01, 0x04, 0x08, 0x10, 0x20 and 0x40 of it.</summary>
public sealed record KmfMaterial(string Name, IReadOnlyList<string> TextureNames,
    uint Flags, float Brightness, float Shininess, string EnvironmentTextureName)
{
    /// <summary>File bit 0x01 (names say "mask"; the loader makes it engine flag 1).</summary>
    public const uint MaskFlag = 0x01;

    /// <summary>File bit 0x04 (names say "ADD"; engine flags 0x30, of which 0x10 is additive).</summary>
    public const uint AdditiveFlag = 0x04;

    /// <summary>File bits 0x08, 0x10, 0x20 (engine flags 2, 4, 8): alpha-blended.</summary>
    public const uint TranslucentFlags = 0x38;

    /// <summary>File bit 0x40 (engine flag 0x200): loads the environment texture as a second
    /// stage. Not drawn by this project yet.</summary>
    public const uint EnvironmentMapFlag = 0x40;

    /// <summary>The blend DK2's material constructor (<c>0x0057C420</c>) gives these flags. Its
    /// order: the additive bit first, then the mask and translucent bits each turn on
    /// SRCALPHA/INVSRCALPHA, which the state applier (<c>0x00589250</c>) issues after ONE/ONE,
    /// so a material with both (none ships) blends by alpha.</summary>
    public KmfBlend Blend =>
        (Flags & (MaskFlag | TranslucentFlags)) != 0 ? KmfBlend.AlphaBlend
        : (Flags & AdditiveFlag) != 0 ? KmfBlend.Additive
        : KmfBlend.Opaque;

    /// <summary>Whether the material carries DK2's environment-map stage (file bit 0x40).</summary>
    public bool IsEnvironmentMapped => (Flags & EnvironmentMapFlag) != 0;

    /// <summary>The light the material adds to every vertex, 0 to 1 a channel: the constructor
    /// stores <see cref="Brightness"/> × 255 as the material colour, which the vertex-colour loops
    /// add to the vertex's light; an additive material's colour is forced to 1.</summary>
    public float SelfIllumination => Blend == KmfBlend.Additive ? 1f : Math.Clamp(Brightness, 0f, 1f);
}

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

/// <summary>One member of a KMF group: a mesh entry's name (without <c>.kmf</c>) and its offset
/// from the group's origin, in the model's units (tiles).</summary>
public sealed record KmfGroupElement(string Name, Vector3 Position);

/// <summary>A KMF of kind <see cref="KmfKind.Group"/>: the parts an effect flings (<c>0x004347E0</c>
/// spawns one element per member at the instance's position plus the member's offset).</summary>
public sealed record KmfGroup(IReadOnlyList<KmfGroupElement> Elements);

public sealed record KmfModel(uint Version, KmfKind Kind,
    IReadOnlyList<KmfMaterial> Materials, KmfMesh? Mesh, KmfAnimation? Animation = null,
    KmfGroup? Group = null)
{
    public uint FrameCount => Animation?.FrameCount ?? 0;
}

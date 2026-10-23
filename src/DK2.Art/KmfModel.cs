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

    /// <summary>File bit 0x40 (engine flag 0x200): loads the environment texture
    /// (<see cref="EnvironmentTextureName"/>) for an extra additive pass,
    /// <see cref="HasEnvironmentPass"/>.</summary>
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

    /// <summary>Whether DK2 draws this material's triangles a second time with the environment
    /// texture: the static (<c>0x00581797</c>) and animated (<c>0x005851E0</c>) mesh queues add
    /// that pass, render state 0x741 (ONE/ONE, no depth write, depth-sorted), only for a material
    /// whose state carries 0x800 (file bit 0x40) and is not blended (none of state bits 0x21,
    /// unless the item's bit 0x400 is set: item <c>+0x50</c> in the static queue, <c>0x00581589</c>/
    /// <c>0x00581597</c>, item <c>+0x4C</c> in the animated one, <c>0x00584FD1</c>), and only while
    /// the EnvironmentMapping option is on (global <c>0x00764BE4</c>, default on, LIKELY).
    /// So an additive or alpha-blended material never gets one. CONFIRMED (kmf-materials.md §8).</summary>
    public bool HasEnvironmentPass => IsEnvironmentMapped && Blend == KmfBlend.Opaque;

    /// <summary>The environment pass's light scale: its vertex colour is the drawn thing's light
    /// (base colour plus point lights, <c>0x0057C190</c>) times material <c>+0x1D</c>, the
    /// descriptor's <see cref="Shininess"/>; brightness plays no part. CONFIRMED for the colour
    /// sum; that the texture is modulated by it is LIKELY (kmf-materials.md §8). The floor at 0 is
    /// ours: a Unity colour below 0 would subtract under ONE/ONE, where a packed vertex colour
    /// cannot; how DK2 packs a negative product is unread; the shipped examples read (§8) are 0.2 to 1.</summary>
    public float EnvironmentPassLight => Math.Max(Shininess, 0f);

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

    /// <summary>The CTRL section's tagged points, in file order.</summary>
    public IReadOnlyList<KmfControlPoint> ControlPoints { get; init; } = [];
}

/// <summary>A tagged point of a mesh (CTRL), found by <see cref="Id"/> (<c>0x0059D260</c>). In an
/// animation <see cref="Vertex"/> packs the primitive in its low word and the vertex within it in
/// its high word, the pair the animated draw's point getter (<c>0x005835E0</c>) hands the vertex
/// evaluator <c>0x0057E5B0</c> at the drawn frame (a Goblin's head is primitive 1, vertex 8, in
/// every clip but Eat's, whose two extra primitives make it 3). In a static mesh it is one index
/// into the resource's vertex array (<c>0x00580E40</c>), which ours does not keep.</summary>
public readonly record struct KmfControlPoint(uint Vertex, uint Id)
{
    /// <summary>Point 5000, the head: where the stun's dazed effect is tied (<c>0x004810E0</c>).</summary>
    public const uint Head = 5000;
}

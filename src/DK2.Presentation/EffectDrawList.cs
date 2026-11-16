// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The frame's light table of a tile, for sprites that take light (<see cref="ConstructionParticle.TakesLight"/>).</summary>
public delegate bool TileLightLookup(TilePos tile, out ClassicLightTable table);

/// <summary>A drawn effect colour; alpha is always opaque.</summary>
public readonly record struct EffectColour(byte Red, byte Green, byte Blue)
{
    public static EffectColour White => new(255, 255, 255);
}

/// <summary>How an effect element is drawn: a camera-facing or flat sprite, a mesh (art type 5, or a group
/// part's mesh entry), or an animated mesh (art type 6) whose frame <see cref="EffectDrawList.MeshFrame"/> picks.</summary>
public enum EffectGroupKind : byte { Sprite, Mesh, AnimatedMesh }

public enum EffectBlendFactor : byte { Zero, One, SourceAlpha, OneMinusSourceAlpha }

/// <summary>A sprite group's blend, <c>source·Source + destination·Destination</c>.</summary>
public readonly record struct EffectBlend(EffectBlendFactor Source, EffectBlendFactor Destination);

/// <summary>A sprite group's shape. <see cref="IsFlat"/>, art flag 0x100: DK2 draws it as a square lying flat at the
/// element (0x0059B880 → 0x0059C720), not facing the camera. The width and height per unit of scale are the
/// descriptor's /4096 (0x0059C360 multiplies scale +4 and +8 by them); <see cref="StandsOnPoint"/>, art flag 0x40,
/// lifts the sprite by half its height.</summary>
public readonly record struct EffectSpriteShape(bool IsFlat, bool StandsOnPoint, float WidthPerScale, float HeightPerScale)
{
    public static EffectSpriteShape Of(EffectElementDef definition, EffectSpriteResource resource) => new(
        IsFlat: (definition.ArtResource.Flags & 0x100) != 0,
        StandsOnPoint: (definition.ArtResource.Flags & 0x40) != 0,
        WidthPerScale: resource.WidthRaw / 4096f,
        HeightPerScale: resource.HeightRaw / 4096f);
}

/// <summary>A camera-facing sprite this frame: its centre after the lift and the screen offset, its size and colour.</summary>
public readonly record struct EffectBillboard(Vector3 Centre, float Width, float Height, EffectColour Colour);

/// <summary>The particles a frame draws, effects first and shot bodies after; the effects' count is taken once.</summary>
public readonly struct EffectParticleStream
{
    private readonly IReadOnlyList<ConstructionParticle> _effects;
    private readonly IReadOnlyList<ConstructionParticle> _shots;
    private readonly int _effectCount;

    internal EffectParticleStream(IReadOnlyList<ConstructionParticle> effects, IReadOnlyList<ConstructionParticle> shots)
    {
        _effects = effects;
        _shots = shots;
        _effectCount = effects.Count;
    }

    public int Count => _effectCount + _shots.Count;

    public ConstructionParticle this[int index] => index < _effectCount ? _effects[index] : _shots[index - _effectCount];
}

/// <summary>Quads in the render frame (<see cref="RenderWorld"/>), written per frame and reused: four vertices
/// each, with UVs, one colour a vertex and two triangles (0, 1, 2) and (0, 2, 3). UV v 0 is the atlas's first row,
/// the image's top. An engine uploads them as they are.</summary>
public sealed class EffectQuadBuffer
{
    private Vector3[] _vertices = new Vector3[16];
    private Vector2[] _uvs = new Vector2[16];
    private EffectColour[] _colours = new EffectColour[16];
    private int[] _indices = new int[24];

    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }
    public ReadOnlySpan<Vector3> Vertices => _vertices.AsSpan(0, VertexCount);
    public ReadOnlySpan<Vector2> Uvs => _uvs.AsSpan(0, VertexCount);
    public ReadOnlySpan<EffectColour> Colours => _colours.AsSpan(0, VertexCount);
    public ReadOnlySpan<int> Indices => _indices.AsSpan(0, IndexCount);

    public void Clear() => VertexCount = IndexCount = 0;

    internal void Add(Vector3 position, Vector2 uv, EffectColour colour)
    {
        if (VertexCount == _vertices.Length)
        {
            Array.Resize(ref _vertices, VertexCount * 2);
            Array.Resize(ref _uvs, VertexCount * 2);
            Array.Resize(ref _colours, VertexCount * 2);
        }
        _vertices[VertexCount] = position;
        _uvs[VertexCount] = uv;
        _colours[VertexCount++] = colour;
    }

    internal void CloseQuad(int first)
    {
        if (IndexCount + 6 > _indices.Length) Array.Resize(ref _indices, _indices.Length * 2);
        _indices[IndexCount++] = first; _indices[IndexCount++] = first + 1; _indices[IndexCount++] = first + 2;
        _indices[IndexCount++] = first; _indices[IndexCount++] = first + 2; _indices[IndexCount++] = first + 3;
    }
}

/// <summary>The effects' draw list: the effect model and the shot bodies with the adapters' styles, the frame's
/// particle stream and lights, and each particle's drawn colour, frame and quad. Render only: it reads particles and
/// light tables, never the world. The model's Emit runs on the runner's worker thread (ADR-0458), so the default
/// clock is a stopwatch, not an engine's main-thread time; everything else runs on the drawing thread.</summary>
public sealed class EffectDrawList
{
    /// <summary>The styles both adapters draw with (<see cref="RoomConstructionParticles"/>).</summary>
    public const TerrainDebrisStyle TerrainDebris = TerrainDebrisStyle.Crumbling;
    public const CaptureEffectStyle CaptureEffects = CaptureEffectStyle.Native;
    public const HeartSparkleStyle HeartSparkles = HeartSparkleStyle.PillarHeight;

    private const float Deg2Rad = (float)Math.PI * 2f / 360f;   // Unity's Mathf.Deg2Rad, which the quads were turned with

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private readonly List<EffectLightView> _lights = new();
    private readonly Dictionary<EffectElementDef, AssetRef> _keys = new(ReferenceComparer.Instance);

    public EffectDrawList(GameDatabase database, ulong seed, Func<EffectTime>? clock = null,
        Func<RoomConstructionEffect, bool>? visible = null,
        Func<ViewEffectRequest, bool>? effectVisible = null,
        Func<FpPosition3, Fp32>? floorHeight = null,
        Func<ViewEffectRequest, CaptureEdges>? captureEdges = null,
        Func<string, IReadOnlyList<EffectGroupPart>?>? groupParts = null,
        Func<int, uint, EffectAttachment>? attachment = null,
        Func<ShotId, ShotEffectAttachment>? shotAttachment = null,
        Func<ViewEffectRequest, bool>? cameraVisible = null,
        Func<TilePos, TerrainFlags>? terrainFlags = null,
        Func<TilePos, bool>? solidTile = null,
        Func<ShotState, bool>? shotVisible = null,
        Func<CreatureId, Fp32?>? creatureGroundHeight = null,
        PlayerId? viewer = null)
    {
        ShotBodies = new ShotBodyParticles(database, ShotSeed(seed), shotVisible);
        Model = new RoomConstructionParticles(database, seed, clock ?? ReadClock, visible, effectVisible, floorHeight,
            terrainDebrisStyle: TerrainDebris,
            captureEffectStyle: CaptureEffects, captureEdges: captureEdges,
            heartSparkleStyle: HeartSparkles,
            groupParts: groupParts, attachment: attachment,
            shotAttachment: shotAttachment, cameraVisible: cameraVisible,
            terrainFlags: terrainFlags, solidTile: solidTile, creatureGroundHeight: creatureGroundHeight,
            viewer: viewer);
    }

    public RoomConstructionParticles Model { get; }
    public ShotBodyParticles ShotBodies { get; }

    /// <summary>The frame's light tables; null without classic lighting.</summary>
    public TileLightLookup? TileLights { get; set; }

    /// <summary>The fraction of an effect update the last <see cref="Advance"/> left, which the frame draws at.</summary>
    public float Fraction { get; private set; }

    /// <summary>Effects first, then shot bodies.</summary>
    public EffectParticleStream Particles => new(Model.Particles, ShotBodies.Particles);

    /// <summary>The shot bodies' generator takes the seed's low 32 bits.</summary>
    internal static uint ShotSeed(ulong seed) => unchecked((uint)seed);

    private static EffectTime ReadClock() => new(Clock.ElapsedMilliseconds);

    /// <summary>Runs the effect updates due by now and returns the instances' lights, then the shot bodies', which the
    /// frame's lighting takes before it is prepared. The list is reused.</summary>
    public IReadOnlyList<EffectLightView> Advance()
    {
        Fraction = Model.Advance();
        Model.CollectLights(_lights);
        IReadOnlyList<EffectLightView> shots = ShotBodies.Lights;
        for (int i = 0; i < shots.Count; i++) _lights.Add(shots[i]);
        return _lights;
    }

    public void CaptureShots(IReadOnlyList<ShotState> shots, float fraction) => ShotBodies.Capture(shots, fraction);

    /// <summary>The key a particle's render resources are cached under: its parent's sprite, else its element's art.</summary>
    public AssetRef GroupKey(ConstructionParticle source)
    {
        if (!source.SpriteAsset.IsEmpty) return source.SpriteAsset;
        if (!_keys.TryGetValue(source.Definition, out AssetRef key))
            _keys.Add(source.Definition, key = AssetRef.ForEffectElement(source.Definition.Name));
        return key;
    }

    /// <param name="meshEntry">A group part's mesh entry, drawn in place of the definition's art.</param>
    public static EffectGroupKind Classify(EffectElementDef definition, string? meshEntry)
    {
        byte type = definition.ArtResource.Type;
        if (meshEntry == null && type == 6) return EffectGroupKind.AnimatedMesh;
        return meshEntry != null || type == 5 || type == 6 ? EffectGroupKind.Mesh : EffectGroupKind.Sprite;
    }

    public static EffectBlend Blend(EffectSpriteBlend blend) => new(
        blend == EffectSpriteBlend.Alpha ? EffectBlendFactor.SourceAlpha : EffectBlendFactor.One,
        blend == EffectSpriteBlend.Additive ? EffectBlendFactor.One
            : blend == EffectSpriteBlend.Alpha ? EffectBlendFactor.OneMinusSourceAlpha : EffectBlendFactor.Zero);

    /// <summary>An animated element's frame rate: the second dword of its descriptor read as frames a second is a
    /// HYPOTHESIS fitted to the Portal Beams (60 frames, 10, a six-second life; no reader of type-6 elements is
    /// read); 10 when the dword is out of range.</summary>
    public static int AuthoredFramesPerSecond(EffectElementDef definition)
    {
        ReadOnlySpan<byte> block = definition.ArtResource.Unknown_0044.Span;
        int rate = block.Length >= 8 ? BitConverter.ToInt32(block.Slice(4, 4)) : 0;
        return rate > 0 && rate <= 60 ? rate : 10;
    }

    /// <summary>An animated mesh's frame: shot bodies supply their game-tick phase; other mesh elements loop from
    /// birth at the authored rate, with one visual update per 50 ms.</summary>
    public static int MeshFrame(ConstructionParticle source, int frameCount, int framesPerSecond) =>
        source.MeshPhaseRaw.HasValue
            ? ShotBodyParticles.FrameIndex(new Fp32(source.MeshPhaseRaw.Value), frameCount)
            : (source.VisualUpdates * framesPerSecond / 20) % frameCount;

    /// <summary>A particle's drawn point between two updates, in the render frame.</summary>
    public static Vector3 Position(ConstructionParticle source, float fraction) => RenderWorld.Ground(
        PoseInterpolator.Tiles(source.PreviousXRaw, source.XRaw, fraction),
        PoseInterpolator.Tiles(source.PreviousYRaw, source.YRaw, fraction),
        PoseInterpolator.Tiles(source.PreviousHeightRaw, source.HeightRaw, fraction));

    /// <summary>The scale in tiles: a mesh's uniform scale, a sprite's size before its shape's width and height.</summary>
    public static float Size(ConstructionParticle source) => source.ScaleRaw / 4096f;

    public static EffectColour Tint(ConstructionParticle source) => new(source.Red, source.Green, source.Blue);

    /// <summary>A sprite's colour: an additive sprite's tint alone; a lit one's (art types 1 and 2) tint plus its tile
    /// table's radial light at its point (0x0057F3D0, 0x0057FB80; Q609, ADR-0665). The ambient DK2 adds is RGB 0 at
    /// the default setting (classic-lighting.md section 8) and is not added.</summary>
    public EffectColour SpriteColour(ConstructionParticle source, Vector3 position)
    {
        if (!source.TakesLight) return Tint(source);
        TileLightLookup? lights = TileLights;
        // Without classic lighting there are no tables: a lit sprite keeps its tint, and one whose tint is black
        // (the digits, a cannonball) draws its art plain rather than vanishing.
        if (lights == null)
            return (source.Red | source.Green | source.Blue) == 0 ? EffectColour.White : Tint(source);
        // A tile drawn without a table this frame has no lights, as DK2's empty list: base colour alone.
        if (!lights(new TilePos(source.XRaw >> 12, source.YRaw >> 12), out ClassicLightTable table)) return Tint(source);
        var (red, green, blue) = SpriteLighting.Lit(source.Red, source.Green, source.Blue, table.Lights, position);
        return new EffectColour(red, green, blue);
    }

    /// <summary>A camera-facing sprite at <paramref name="position"/> (<see cref="Position"/>): its colour there, then
    /// art flag 0x40's lift and a floating amount's digit offset.</summary>
    public EffectBillboard Billboard(ConstructionParticle source, Vector3 position, in EffectSpriteShape shape,
        Vector3 cameraRight, Vector3 cameraUp)
    {
        float size = Size(source);
        float width = size * shape.WidthPerScale, height = size * shape.HeightPerScale;
        EffectColour colour = SpriteColour(source, position);
        // 0x0059C360 with art flag 0x40 offsets the quad by half its height in camera space; that it lifts the
        // sprite onto its point (flames, splashes) is STRONGLY INFERRED from the quad's UVs (v grows with camera y,
        // D3D's v runs down) and the art.
        if (shape.StandsOnPoint) position += cameraUp * (height * 0.5f);
        // A floating amount's digit sits beside its neighbours in camera space (0x0059C360's +0x57).
        if (source.ScreenOffsetXRaw != 0) position += cameraRight * (source.ScreenOffsetXRaw / 4096f);
        return new EffectBillboard(position, width, height, colour);
    }

    /// <summary>A billboard's quad along the camera's right and up, its frame the particle's age through the sheet.
    /// 0x0057F3D0 gives the screen's upper corners the image's top row, so the lower corners take v 1.</summary>
    public static void AddBillboardQuad(EffectQuadBuffer quads, ConstructionParticle source, in EffectBillboard billboard,
        Vector3 right, Vector3 up, int spriteFrames)
    {
        var (u0, u1) = FrameSpan(source, spriteFrames);
        Vector3 position = billboard.Centre;
        float width = billboard.Width, height = billboard.Height;
        int first = quads.VertexCount;
        for (int corner = 0; corner < 4; corner++)
        {
            bool left = corner == 0 || corner == 3;
            quads.Add(position + right * (left ? -width * .5f : width * .5f) + up * (corner < 2 ? -height * .5f : height * .5f),
                new Vector2(left ? u0 : u1, corner < 2 ? 1f : 0f), billboard.Colour);
        }
        quads.CloseQuad(first);
    }

    /// <summary>One flat sprite (0x0059C720 → 0x0057FB80): a square of half-size scale / 4096 in the DK2 x-y plane at
    /// the element, its corners (-s,-s), (s,-s), (s,s), (-s,s) taking UVs (0,0), (1,0), (1,1), (0,1), one colour
    /// (<see cref="SpriteColour"/>). DK2 passes the angle -heading·π/1024 to a clockwise turn (0x0041C580 with the row
    /// transform 0x00594DB0), so the corners turn counter-clockwise by heading·π/1024 in the DK2 x-y plane. The frame is
    /// the particle's age through the stored frames, as the billboards' sheet takes it (DK2's frame pick for a flat
    /// sprite, 0x0057FF30, is unread).</summary>
    public void AddFlatQuad(EffectQuadBuffer quads, ConstructionParticle source, float fraction, int spriteFrames)
    {
        float x = Lerp(source.PreviousXRaw, source.XRaw, fraction);
        float y = Lerp(source.PreviousYRaw, source.YRaw, fraction);
        float heightRaw = Lerp(source.PreviousHeightRaw, source.HeightRaw, fraction);
        float height = heightRaw / 4096f;
        float half = source.ScaleRaw;
        float angle = MeshOrientation.LerpAngle(source.PreviousHeadingRaw * (360f / 2048f),
            source.HeadingRaw * (360f / 2048f), fraction) * Deg2Rad;
        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
        var (u0, u1) = FrameSpan(source, spriteFrames);
        int first = quads.VertexCount;
        EffectColour colour = SpriteColour(source, RenderWorld.Ground(x / 4096f, y / 4096f, height));
        for (int corner = 0; corner < 4; corner++)
        {
            float cx = corner == 0 || corner == 3 ? -half : half;
            float cy = corner < 2 ? -half : half;
            quads.Add(RenderWorld.Ground((x + cx * cos - cy * sin) / 4096f, (y + cx * sin + cy * cos) / 4096f, height),
                new Vector2(corner == 0 || corner == 3 ? u0 : u1, corner < 2 ? 0f : 1f), colour);
        }
        quads.CloseQuad(first);
    }

    /// <summary>The sheet columns of the particle's frame: its age through <paramref name="spriteFrames"/>.</summary>
    internal static (float U0, float U1) FrameSpan(ConstructionParticle source, int spriteFrames)
    {
        int frames = Math.Max(1, spriteFrames);
        float age = 1f - source.RemainingSpriteLifetimeFraction;
        int frame = Math.Min(frames - 1, (int)(age * frames));
        return (frame / (float)frames, (frame + 1) / (float)frames);
    }

    // Unity's Mathf.Lerp on raw positions, which the flat quads were drawn with.
    private static float Lerp(int previous, int current, float fraction)
    {
        float from = previous, to = current;
        return from + (to - from) * PoseInterpolator.Clamp01(fraction);
    }

    private sealed class ReferenceComparer : IEqualityComparer<EffectElementDef>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(EffectElementDef? x, EffectElementDef? y) => ReferenceEquals(x, y);
        public int GetHashCode(EffectElementDef obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

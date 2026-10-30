// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Read-only shot bodies in the existing sprite/mesh draw representation.</summary>
public sealed class ShotBodyParticles
{
    private readonly GameDatabase _database;
    private readonly Dk2Lcg _random;
    private readonly Dictionary<ShotId, ConstructionParticle> _bodies = [];
    private readonly Dictionary<byte, EffectElementDef> _definitions = [];
    private readonly List<ConstructionParticle> _particles = [];
    private readonly List<EffectLightView> _lights = [];
    private readonly HashSet<ShotId> _live = [];
    private readonly List<ShotId> _removed = [];
    private readonly Func<ShotState, bool>? _visible;

    public ShotBodyParticles(GameDatabase database, uint seed, Func<ShotState, bool>? visible = null)
    {
        _database = database;
        _random = new(seed);
        _visible = visible;
        foreach (ShotDef shot in database.Shots.Values)
            if (shot.MeshResource.Type is 1 or 2 or 3 or 5 or 6)
                _definitions.Add(shot.ShotId, new EffectElementDef([])
                {
                    Name = shot.Name, ArtResource = shot.MeshResource, ScaleRatio = Fp32.One,
                });
    }

    public IReadOnlyList<ConstructionParticle> Particles => _particles;
    public IReadOnlyList<EffectLightView> Lights => _lights;

    public void Capture(IReadOnlyList<ShotState> shots, float fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        _particles.Clear(); _lights.Clear(); _live.Clear();
        foreach (ShotState shot in shots)
        {
            if (shot.Health <= 0 || !_database.Shots.TryGetValue(shot.ShotTypeId, out ShotDef? definition)) continue;
            FpPosition3 point = Interpolate(shot.PreviousViewPosition ?? shot.ViewPosition, shot.ViewPosition, fraction);
            KwdLight light = definition.Light;
            if ((light.Flags & 1) != 0)
                _lights.Add(new(-shot.Id, light, new PlayerId(shot.OwnerPlayerId), point.X.Raw, point.Y.Raw, point.Z.Raw, 1, 1));
            if (!_definitions.TryGetValue(shot.ShotTypeId, out EffectElementDef? element)) continue;
            var id = new ShotId(shot.Id); _live.Add(id);
            if (!_bodies.TryGetValue(id, out ConstructionParticle? body))
                _bodies.Add(id, body = Create(element, definition));
            body.XRaw = body.PreviousXRaw = point.X.Raw;
            body.YRaw = body.PreviousYRaw = point.Y.Raw;
            body.HeightRaw = body.PreviousHeightRaw = point.Z.Raw;
            body.HeadingRaw = body.PreviousHeadingRaw = shot.ViewHeading.Value;
            body.VisualUpdates = shot.Age;
            SetPhase(body, shot.Age, fraction);
            if (_visible == null || _visible(shot)) _particles.Add(body);
        }
        _removed.Clear();
        foreach (ShotId id in _bodies.Keys) if (!_live.Contains(id)) _removed.Add(id);
        foreach (ShotId id in _removed) _bodies.Remove(id);
    }

    public static FpPosition3 Interpolate(FpPosition3 previous, FpPosition3 current, float fraction)
    {
        float t = Math.Clamp(fraction, 0, 1);
        return new(new(previous.X.Raw + (int)((current.X.Raw - previous.X.Raw) * t)),
            new(previous.Y.Raw + (int)((current.Y.Raw - previous.Y.Raw) * t)),
            new(previous.Z.Raw + (int)((current.Z.Raw - previous.Z.Raw) * t)));
    }

    public static int FrameIndex(Fp32 phase, int frameCount) => frameCount <= 1 ? 0
        : (int)((long)Math.Clamp(phase.Raw, 0, 4096) * (frameCount - 1) / 4096);

    private ConstructionParticle Create(EffectElementDef element, ShotDef shot)
    {
        KwdArtResource art = element.ArtResource;
        int scale = art.Type == 5 && art.Unknown_0044.Length >= 4
            ? BinaryPrimitives.ReadInt32LittleEndian(art.Unknown_0044.Span) : Fp32.One;
        int frames = Word(art, art.Type == 5 ? 4 : 8);
        int initialPhase = (art.Flags & 0x24) == 0x24
            ? _random.NextInt(Math.Max(1, frames)) * (Fp32.One / Math.Max(1, frames)) : 0;
        // Sprite types1/3 initialize white natively. Type2's untraced radial-light term is
        // approximated by neutral white in the existing unlit sprite renderer (shot-effects.md).
        byte colour = art.Type <= 3 ? (byte)255 : (byte)0;
        return new ConstructionParticle
        {
            Definition = element, SpriteAsset = AssetRef.ForShot(shot.Name), InitialScale = scale,
            ScaleTarget = scale, Health = 1, InitialHealth = 1, InitialPhase = initialPhase,
            FrameDuration = (art.Flags & 4) != 0 ? Math.Max(1, frames) : 0,
            Red = colour, Green = colour, Blue = colour,
        };
    }

    private static void SetPhase(ConstructionParticle body, int age, float fraction)
    {
        KwdArtResource art = body.Definition.ArtResource;
        double updates = age > 0 ? age - 1 + (double)fraction : 0;
        if ((art.Flags & 4) != 0)
        {
            int step = art.Type == 6 ? 512 : Fp32.One / Math.Max(1, body.FrameDuration);
            int phase = body.InitialPhase + (int)(updates * step);
            body.TexturePhaseRawOverride = (art.Flags & 0x80) != 0 ? Math.Min(4096, phase) : phase & 4095;
        }
        if (art.Type != 6) return;
        int frames = Word(art, 0), fps = Word(art, 4);
        int duration = fps == 0 ? 0 : (int)((long)frames * 4096 / fps);
        int rate = duration <= 0 ? 0 : (int)Math.Min(4095,
            4096L * 4096 / (duration * (long)SimulationTiming.OriginalTicksPerGameSecond));
        int meshPhase = (int)(updates * rate);
        body.MeshPhaseRaw = (art.Flags & 0x80) != 0 ? Math.Min(4096, meshPhase) : meshPhase & 4095;
    }

    private static int Word(KwdArtResource art, int offset) => art.Unknown_0044.Length >= offset + 2
        ? BinaryPrimitives.ReadUInt16LittleEndian(art.Unknown_0044.Span.Slice(offset, 2)) : 0;
}

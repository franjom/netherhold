// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;

namespace DK2.Presentation;

public readonly record struct PossessionForegroundSample(string? Resource, float Phase, byte Brightness);

/// <summary>The real-time foreground attack mesh, independent of the world camera (005075A0/00507710).</summary>
public sealed class PossessionForeground
{
    private static readonly string[] Names = ["", "Bite", "Claw_Slash", "Club_Smash", "Dark-FGBS_Slash",
        "King-FGBS_Slash", "L-Punch", "R-Punch", "L-Scythe_Slash", "R-Scythe_Slash", "Mistress_Slash",
        "Small_Slash", "Staff_Strike", "Sword_Slash", "Light_Arrow", "Dark_Arrow", "L-Bile_Smash",
        "R-Bile_Smash", "Hypnotise", "L-Small_Punch", "R-Small_Punch", "L-King-FGBS_Slash", "R-King-FGBS_Slash"];
    private readonly Dictionary<string, Clip?> _clips = new();
    private PossessionForegroundKind _kind;
    private long _start, _deadline;
    private long? _fade;
    private float _phase;
    private bool _alternate;

    public static string? Resource(PossessionForegroundKind kind) => (byte)kind is > 0 and < 23 ? Names[(byte)kind] : null;

    public void Begin(PossessionForegroundKind kind, ushort framesPerSecond, long milliseconds)
    {
        _kind = kind;
        _fade = null;
        _phase = 0;
        _start = milliseconds;
        _deadline = milliseconds + (kind == PossessionForegroundKind.None ? 0 : 22000 / framesPerSecond);
    }

    public void Fire(GameDatabase database, CreatureState creature, CreatureDef definition, PossessionControls controls,
        long milliseconds, long tick)
    {
        PossessionForegroundKind kind;
        if (controls.Melee)
        {
            if (!CombatRules.SwingMayBegin(database, creature, definition, tick)) return;
            if (definition.FirstPersonMelee2 != PossessionForegroundKind.None) _alternate = !_alternate;
            kind = _alternate ? definition.FirstPersonMelee2 : definition.FirstPersonMelee1;
        }
        else if ((controls.Selection & 6) == 4 && definition.SecondaryAbility == PossessionAbilityKind.Hypnosis)
            kind = definition.FirstPersonSecondary;
        else
        {
            if (controls.Slot == 3 || !SpellRules.CanCast(database, creature, definition, controls.Slot, tick)) return;
            kind = definition.FirstPersonSpell;
            if (kind == PossessionForegroundKind.None) return;
        }
        Begin(kind, definition.FirstPersonFramesPerSecond, milliseconds);
    }

    public PossessionForegroundSample Sample(long milliseconds)
    {
        string? resource = Resource(_kind);
        if (resource == null) return default;
        byte brightness = 255;
        if (_fade is long fade)
        {
            long elapsed = milliseconds - fade;
            if (elapsed > 500) { _kind = PossessionForegroundKind.None; return default; }
            brightness = unchecked((byte)(255 - elapsed * 255 / 500));
        }
        var sample = new PossessionForegroundSample(resource, _phase, brightness);
        if (_fade == null)
        {
            if (milliseconds > _deadline) { _fade = milliseconds; _phase = 4095 / 4096f; }
            else _phase = ((milliseconds - _start) * 4096 / (_deadline - _start)) / 4096f;
        }
        return sample;
    }

    public static Vector2 Project(Vector3 renderPosition, int width, int height) =>
        new(width / 2 + renderPosition.X * (width * 1000 / 640), height / 2 - renderPosition.Z * (width * 1000 / 640));

    public void Build(InstallArtProvider art, int width, int height, long milliseconds, HudFrame frame)
    {
        PossessionForegroundSample sample = Sample(milliseconds);
        if (sample.Resource == null) return;
        if (!_clips.TryGetValue(sample.Resource, out Clip? clip))
        {
            clip = art.TryResolveEntry(sample.Resource, out var asset) && art.TryLoadModel(asset, out KmfModel model)
                && model.Animation is { IsComplete: true } ? new Clip(model) : null;
            _clips.Add(sample.Resource, clip);
        }
        if (clip == null) return;
        clip.Animation.Sample(sample.Phase, loop: false, clip.Positions);
        for (int material = 0; material < clip.Model.Materials.Count; material++)
        {
            KmfMaterial texture = clip.Model.Materials[material];
            if (texture.TextureNames.Count == 0) continue;
            int[] indices = clip.Animation.Mesh.Triangles[material];
            if (clip.Triangles.Length < indices.Length) clip.Triangles = new HudVertex[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];
                Vector2 xy = Project(clip.Positions[index], width, height), uv = clip.Animation.Mesh.Uvs[index];
                clip.Triangles[i] = new(xy.X, xy.Y, uv.X, uv.Y);
            }
            int textureFrame = Math.Clamp((int)(texture.TextureNames.Count * sample.Phase), 0, texture.TextureNames.Count - 1);
            byte color = sample.Brightness;
            // The native type6 path consumes RGB, not the fourth fade byte.
            frame.AddTriangles(new(texture.TextureNames[textureFrame]), clip.Triangles.AsSpan(0, indices.Length),
                new(color, color, color, 255), texture.Blend == KmfBlend.Additive ? HudBlend.Additive : HudBlend.Alpha);
        }
    }

    private sealed class Clip(KmfModel model)
    {
        public KmfModel Model { get; } = model;
        public RenderAnimation Animation { get; } = new(model);
        public Vector3[] Positions { get; } = new Vector3[RenderMesh.FromModel(model).Positions.Count];
        public HudVertex[] Triangles = [];
    }
}

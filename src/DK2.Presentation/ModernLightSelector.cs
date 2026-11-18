// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>What a DK2 light source is, for the modern lights: a terrain tile's glow, a wall torch, or a moving source
/// (an object, an effect, the Hand, an entrance).</summary>
public enum ModernLightRole : byte
{
    Terrain,
    Torch,
    Dynamic,
}

/// <summary>A DK2 light source as a per-pixel point light, in the renderers' frame (<see cref="ClassicPointLight"/>'s):
/// a display colour no channel of which passes 1, an energy over linear light, a range in tiles, whether it casts
/// shadows this frame, and its source's flags. A slot of <see cref="ModernLightSelector.Slots"/> that holds no light is
/// <c>default</c>, so not <see cref="Active"/>.</summary>
public readonly record struct ModernPointLight(Vector3 Position, Vector3 Colour, float Energy, float Range, bool CastsShadow,
    ModernLightRole Role, ClassicLightFlags Flags = ClassicLightFlags.None, bool Active = true);

/// <summary>How a DK2 source maps to a point light whose falloff is <c>E (1 - (d/r)^4)^2 d^-a</c> over linear light
/// (Godot's omni light). DK2 adds <c>min(1, I cos (d/4 + 1/d)(1 - d^2/R^2))</c> times the colour to a gamma-space
/// texture (its distance binned to sixteenths of a tile squared); the linear equivalent is that factor to the power 2.2. A
/// least-squares fit over R 1.1 to 2.5 tiles, I 0.5 to 1.25 and d from 0.3 to R gives r = 0.886 R, E = 1.202 I^2.2 and
/// a = 1.861: 0.047 RMS, 0.075 at worst
/// (<see cref="Fitted"/>; <see cref="Scaled"/> tunes on top of it). The colour keeps DK2's magnitude, so a dim torch or a
/// fading effect stays dim; a channel over 1 (a torch's, the Hand's) is taken at a typical factor
/// (<see cref="TypicalFactor"/>), cut at 1 per channel as DK2 cuts it, and the colour then brought to a peak of 1.</summary>
public readonly record struct ModernLightMapping(float RangeScale, float EnergyScale, float Attenuation, float IntensityPower)
{
    /// <summary>The fit to DK2's falloff, untuned.</summary>
    public static readonly ModernLightMapping Fitted = new(0.886f, 1.202f, 1.861f, 2.2f);

    /// <summary>The most intensity that becomes energy. DK2 clamps a light's factor at 1, so intensity past it only widens
    /// the clamped core (the Hand's 255/128); an HDR light given all of it would blow out to white.</summary>
    public const float MaximumIntensity = 1.25f;

    /// <summary>The falloff factor a colour over 1 is taken at: a pool's middle, between its edge and its clamped core.</summary>
    public const float TypicalFactor = 0.75f;

    /// <summary>This mapping with its range and energy multiplied: the look's tuning over the fit.</summary>
    public ModernLightMapping Scaled(float range, float energy) =>
        this with { RangeScale = RangeScale * range, EnergyScale = EnergyScale * energy };

    public static ModernLightRole RoleOf(ClassicLightFlags flags) => flags switch
    {
        ClassicLightFlags.Terrain => ModernLightRole.Terrain,
        ClassicLightFlags.CheapTorch or ClassicLightFlags.NormalTorch => ModernLightRole.Torch,
        _ => ModernLightRole.Dynamic,
    };

    /// <summary>Whether a source lights anything: a positive radius, intensity and colour.</summary>
    public static bool Lights(in ClassicPointLight light) =>
        light.Radius > 0f && light.Intensity > 0f && (light.Colour.X > 0f || light.Colour.Y > 0f || light.Colour.Z > 0f);

    public ModernPointLight Map(in ClassicPointLight light, bool castsShadow)
    {
        Vector3 colour = Vector3.Max(light.Colour, Vector3.Zero);
        float peak = Math.Max(colour.X, Math.Max(colour.Y, colour.Z));
        if (peak > 1f)
        {
            colour = Vector3.Min(colour * TypicalFactor, Vector3.One);
            colour /= Math.Max(colour.X, Math.Max(colour.Y, colour.Z));
        }
        float energy = EnergyScale * MathF.Pow(Math.Min(light.Intensity, MaximumIntensity), IntensityPower);
        return new ModernPointLight(light.Position, colour, energy, light.Radius * RangeScale, castsShadow,
            RoleOf(light.Flags), light.Flags);
    }
}

/// <summary>The frame's modern lights from the sources a <see cref="ClassicWorldLightCache"/> prepared, terrain sources
/// and sources that light nothing left out (terrain sources sit under the tops they light, which only DK2's inverted top
/// normal reaches; an adapter draws them as glow from the classic tables): the <c>maxLights</c> that matter most to the
/// view (<see cref="Score"/>) in <see cref="Slots"/>. A light keeps its slot from frame to frame, matched by its flags and
/// the nearest last position within <see cref="MatchDistance"/>; at most <c>maxShadows</c> torches and moving sources
/// cast shadows, and a shadowed light keeps its shadow until another outscores it by <see cref="ShadowHysteresis"/>.
/// Allocation-free once its buffers have grown.</summary>
public sealed class ModernLightSelector
{
    /// <summary>How far a light may move in a frame and keep its slot, in tiles.</summary>
    public const float MatchDistance = 0.5f;

    /// <summary>How much a challenger must outscore the weakest shadowed light to take its shadow.</summary>
    public const float ShadowHysteresis = 1.25f;

    private readonly ModernLightMapping _mapping;
    private readonly ByScore _byScore = new();
    private ClassicPointLight[] _sources = Array.Empty<ClassicPointLight>();
    private int[] _order = Array.Empty<int>(), _slotOf = Array.Empty<int>();
    private bool[] _shadowed = Array.Empty<bool>(), _matched = Array.Empty<bool>();
    private readonly ModernPointLight[] _slots, _previous;
    private readonly bool[] _claimed;
    private int _previousSlotCount;

    public ModernLightSelector(int maxLights, int maxShadows, ModernLightMapping mapping)
    {
        if (maxLights < 0) throw new ArgumentOutOfRangeException(nameof(maxLights));
        if (maxShadows < 0 || maxShadows > maxLights) throw new ArgumentOutOfRangeException(nameof(maxShadows));
        MaxLights = maxLights;
        MaxShadows = maxShadows;
        _mapping = mapping;
        _slots = new ModernPointLight[maxLights];
        _previous = new ModernPointLight[maxLights];
        _claimed = new bool[maxLights];
    }

    public int MaxLights { get; }
    public int MaxShadows { get; }
    /// <summary>The point-light candidates the last <see cref="Select"/> saw (terrain and dark sources left out), before
    /// the cap.</summary>
    public int SourceCount { get; private set; }
    /// <summary>The lights in <see cref="Slots"/>.</summary>
    public int Count { get; private set; }
    public int ShadowCount { get; private set; }
    /// <summary>The slots up to the last one in use; an empty slot is <c>default</c>.</summary>
    public ReadOnlySpan<ModernPointLight> Slots => _slots.AsSpan(0, SlotCount);
    public int SlotCount { get; private set; }

    /// <summary>How much a source matters to a view centred on <paramref name="focus"/>: its brightness (luminance times
    /// intensity) times the area it reaches, over one plus its squared distance to the focus.</summary>
    public static float Score(in ClassicPointLight light, Vector3 focus)
    {
        float luminance = 0.299f * light.Colour.X + 0.587f * light.Colour.Y + 0.114f * light.Colour.Z;
        return luminance * light.Intensity * light.Radius * light.Radius / (1f + Vector3.DistanceSquared(light.Position, focus));
    }

    /// <summary>The frame's lights from <paramref name="cache"/>'s prepared sources, for a view centred on
    /// <paramref name="focus"/> (the renderers' frame).</summary>
    public void Select(ClassicWorldLightCache cache, Vector3 focus)
    {
        if (cache is null) throw new ArgumentNullException(nameof(cache));
        int count = cache.CopyPreparedSources(ref _sources);
        Select(_sources.AsSpan(0, count), focus);
    }

    /// <summary><see cref="Select(ClassicWorldLightCache, Vector3)"/> over a given source list.</summary>
    public void Select(ReadOnlySpan<ClassicPointLight> sources, Vector3 focus)
    {
        int count = sources.Length;
        if (_sources.Length < count || !sources.Overlaps(_sources)) CopyIn(sources);
        if (_order.Length < count)
        {
            int size = Math.Max(count, _order.Length * 2);
            _order = new int[size];
            _slotOf = new int[size];
            _shadowed = new bool[size];
            _matched = new bool[size];
            _byScore.Scores = new float[size];
        }
        int candidates = 0;
        for (int i = 0; i < count; i++)
        {
            ClassicPointLight light = _sources[i];
            if (ModernLightMapping.RoleOf(light.Flags) == ModernLightRole.Terrain || !ModernLightMapping.Lights(light)) continue;
            float score = Score(light, focus);
            // A NaN or non-positive score (a source past the float range) lights nothing worth a slot.
            if (!(score > 0f)) continue;
            _byScore.Scores[i] = score;
            _order[candidates++] = i;
        }
        SourceCount = candidates;
        SortByScore(candidates);
        int taken = Math.Min(candidates, MaxLights);
        Array.Copy(_slots, _previous, _previousSlotCount);
        AssignSlots(taken);
        ChooseShadows(taken);
        Array.Clear(_slots, 0, Math.Max(_previousSlotCount, SlotCount));
        int slotCount = 0;
        for (int i = 0; i < taken; i++)
        {
            int source = _order[i], slot = _slotOf[source];
            _slots[slot] = _mapping.Map(_sources[source], _shadowed[source]);
            slotCount = Math.Max(slotCount, slot + 1);
        }
        _previousSlotCount = SlotCount = slotCount;
        Count = taken;
    }

    /// <summary>Each chosen light, best first, takes the free last-frame slot of its flags nearest it within
    /// <see cref="MatchDistance"/>; the rest take the lowest free slots.</summary>
    private void AssignSlots(int taken)
    {
        Array.Clear(_claimed, 0, _claimed.Length);
        for (int i = 0; i < taken; i++)
        {
            int source = _order[i];
            ClassicPointLight light = _sources[source];
            int best = -1;
            float bestDistance = MatchDistance * MatchDistance;
            for (int slot = 0; slot < _previousSlotCount; slot++)
            {
                if (_claimed[slot] || !_previous[slot].Active || _previous[slot].Flags != light.Flags) continue;
                float distance = Vector3.DistanceSquared(_previous[slot].Position, light.Position);
                if (distance <= bestDistance) { best = slot; bestDistance = distance; }
            }
            _slotOf[source] = best;
            _matched[source] = best >= 0;
            if (best >= 0) _claimed[best] = true;
        }
        int free = 0;
        for (int i = 0; i < taken; i++)
        {
            int source = _order[i];
            if (_slotOf[source] >= 0) continue;
            while (_claimed[free]) free++;
            _slotOf[source] = free;
            _claimed[free] = true;
        }
    }

    /// <summary>The shadowed lights: last frame's that are still chosen, the best <c>maxShadows</c> of them, then the
    /// rest best first, each filling a free shadow or taking the weakest one's when it outscores it by
    /// <see cref="ShadowHysteresis"/>.</summary>
    private void ChooseShadows(int taken)
    {
        int shadows = 0;
        for (int i = 0; i < taken; i++)
        {
            int source = _order[i], slot = _slotOf[source];
            bool was = _matched[source] && _previous[slot].CastsShadow;
            _shadowed[source] = was && shadows < MaxShadows;
            if (_shadowed[source]) shadows++;
        }
        for (int i = 0; i < taken; i++)
        {
            int source = _order[i];
            if (_shadowed[source]) continue;
            if (shadows < MaxShadows)
            {
                _shadowed[source] = true;
                shadows++;
                continue;
            }
            int weakest = -1;
            for (int j = 0; j < taken; j++)
            {
                int other = _order[j];
                if (_shadowed[other] && (weakest < 0 || _byScore.Scores[other] < _byScore.Scores[weakest])) weakest = other;
            }
            if (weakest < 0 || _byScore.Scores[source] <= _byScore.Scores[weakest] * ShadowHysteresis) continue;
            _shadowed[weakest] = false;
            _shadowed[source] = true;
        }
        ShadowCount = shadows;
    }

    /// <summary>An insertion sort of the candidates (tens of torches and moving sources): <c>Array.Sort</c> with a
    /// comparer allocates a delegate a call.</summary>
    private void SortByScore(int count)
    {
        for (int i = 1; i < count; i++)
        {
            int item = _order[i], at = i;
            while (at > 0 && _byScore.Compare(_order[at - 1], item) > 0)
            {
                _order[at] = _order[at - 1];
                at--;
            }
            _order[at] = item;
        }
    }

    private void CopyIn(ReadOnlySpan<ClassicPointLight> sources)
    {
        if (_sources.Length < sources.Length) _sources = new ClassicPointLight[Math.Max(sources.Length, _sources.Length * 2)];
        sources.CopyTo(_sources);
    }

    /// <summary>Highest score first, the source order between equal scores.</summary>
    private sealed class ByScore
    {
        public float[] Scores = Array.Empty<float>();

        public int Compare(int x, int y)
        {
            int byScore = Scores[y].CompareTo(Scores[x]);
            return byScore != 0 ? byScore : x.CompareTo(y);
        }
    }
}

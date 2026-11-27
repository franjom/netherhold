// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Art;

/// <summary>An engine's side of a <see cref="CreatureAnimationPlayer{TResources}"/>: the meshes and materials it draws
/// with. <typeparamref name="TResources"/> is the engine's per-clip set (mesh templates, materials).</summary>
public interface ICreatureAnimationSurface<TResources>
{
    /// <summary>Install the engine resources for <paramref name="clips"/>, by <see cref="AnimationSegment"/>; null where the
    /// clip has no such segment.</summary>
    void Install(RenderAnimation?[] clips, TResources resources);

    /// <summary>Draw <paramref name="segment"/>'s mesh and materials from now on.</summary>
    void Show(AnimationSegment segment);

    /// <summary>The segment's vertices as just sampled, model-local in <see cref="RenderMesh"/>'s frame. The buffer is
    /// the player's and is rewritten by the next draw.</summary>
    void Upload(AnimationSegment segment, Vector3[] vertices);
}

/// <summary>A GPU renderer's optional surface: receives the shared clock's pose instead of CPU vertex uploads.</summary>
public interface ICreatureAnimationPoseSurface
{
    void Pose(AnimationSegment segment, float phase);
}

/// <summary>A tagged point of the drawn clip, model-local in <see cref="RenderMesh"/>'s frame.</summary>
public readonly record struct AnimationPoint(uint Id, Vector3 Local);

/// <summary>The art rules for starting a type-6 clip.</summary>
public static class CreatureAnimationArt
{
    /// <summary>The draw clock's turns per game second, as <see cref="RenderAnimationClock"/> counts them. Must equal
    /// <c>SimulationTiming.OriginalTicksPerGameSecond</c>, which DK2.Art cannot reference (pinned by a Presentation test).</summary>
    public const int TurnsPerSecond = 4;

    /// <summary>Whether the clip has <paramref name="segment"/>: the main one always, the start and end ones by the
    /// resource's flags.</summary>
    public static bool HasSegment(Dk2ArtResource resource, AnimationSegment segment)
    {
        return segment switch
        {
            AnimationSegment.Start => (resource.AnimationFlags & CreatureAnimationFlags.StartSegment) != 0,
            AnimationSegment.End => (resource.AnimationFlags & CreatureAnimationFlags.EndSegment) != 0,
            _ => true,
        };
    }

    public static CreatureAnimationTiming Timing(Dk2ArtResource resource)
    {
        return new CreatureAnimationTiming()
        {
            ResourceType = resource.Type,
            FrameCount = resource.FrameCount,
            FramesPerSecond = resource.FramesPerSecond,
            Flags = resource.AnimationFlags,
            StartSegmentFrames = resource.StartSegmentFrames,
            EndSegmentFrames = resource.EndSegmentFrames,
        };
    }

    /// <summary>The resource's movement scale when the clip follows a movement speed, else none.</summary>
    public static Fp32? AuthoredMovementScale(Dk2ArtResource resource, Fp32? movementSpeed)
    {
        return movementSpeed.HasValue ? new Fp32(resource.MovementScale) : null;
    }
}

/// <summary>Plays one creature's or object's type-6 clip on the draw clock: the turn catch-up, the deferred replacement,
/// the loop beat, the footstep marks and the sampling. Render only; it allocates a clock per started clip and retains
/// a buffer per fully sampled clip. Pose surfaces sample the full clip only on demand.</summary>
public sealed class CreatureAnimationPlayer<TResources>
{
    private const uint LightningPointLimit = 2000;
    private readonly ICreatureAnimationSurface<TResources> _surface;
    // Retain each fully sampled clip's buffer; pose-only draws and tagged points need none.
    private readonly Dictionary<RenderAnimation, Vector3[]> _buffers = new Dictionary<RenderAnimation, Vector3[]>();
    private readonly AnimationMarkWatcher _marks = new AnimationMarkWatcher();
    private RenderAnimation?[]? _clips;
    private RenderAnimationClock? _clock;
    private long _startTick;
    private long _advanced;
    private AnimationSegment? _drawnSegment;
    private bool _hasPending;
    private RenderAnimation?[]? _pendingClips;
    private TResources _pendingResources = default!;
    private int _resourceRevision;
    private int _mark1, _mark2;
    private float _drawnPhase;
    private bool _sampleCurrent;

    public CreatureAnimationPlayer(ICreatureAnimationSurface<TResources> surface)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    /// <summary>What the last <see cref="Draw"/> found of the clip's footstep marks: 0 none, 1 before the clip's wrap, 2 after it
    /// (<c>0x004B4630</c>, asked at each drawn frame by <c>0x0043CC30</c>).</summary>
    public int FootstepCrossing { get; private set; }

    /// <summary>A loop completed since the last <see cref="ConsumeBeat"/>.</summary>
    public bool BeatPending { get; private set; }

    /// <summary>Counts clip installs, including a deferred clip taking over.</summary>
    public int InstallationRevision { get; private set; }

    public AnimationSegment? DrawnSegment
    {
        get
        {
            return _drawnSegment;
        }
    }

    /// <summary>The clock, or null while stopped.</summary>
    public RenderAnimationClock? Clock
    {
        get
        {
            return _clock;
        }
    }

    public void ConsumeBeat()
    {
        BeatPending = false;
    }

    /// <summary>One turn of a Heart's health phase, outside the catch-up.</summary>
    public void AdvanceHealthPhase(Fp32 phaseStep)
    {
        if (_clock == null)
        {
            return;
        }

        long previousLoops = _clock.CompletedLoops;
        _clock.Advance(phaseStepOverride: phaseStep);
        if (_clock.CompletedLoops != previousLoops)
        {
            BeatPending = true;
        }
    }

    /// <summary>Starts <paramref name="clips"/> at <paramref name="start"/> on the draw clock. A deferred replacement
    /// first draws the playing clip up to now; while that clip runs on, the new one and its resources wait for the
    /// clock's <see cref="RenderAnimationClock.ResourceRevision"/> to change.</summary>
    public void Configure(RenderAnimation?[] clips, TResources resources, CreatureAnimationTiming timing, bool loop,
        double start, Fp32? authoredMovementScale = null, Fp32? movementSpeed = null,
        AnimationReplacement replacement = AnimationReplacement.Immediate, bool hold = false,
        Fp32? initialMovementSpeed = null, Fp32? phaseStepOverride = null, Fp32 initialPhase = default,
        FootstepMarkPair footstepMarks = default)
    {
        if (clips is null)
        {
            throw new ArgumentNullException(nameof(clips));
        }

        if (_clock != null && replacement == AnimationReplacement.Deferred)
        {
            Draw(start, movementSpeed, hold);
            int revision = _clock.ResourceRevision;
            _clock.Replace(timing, loop, replacement, authoredMovementScale, initialMovementSpeed);
            if (_clock.ResourceRevision == revision)
            {
                _pendingClips = clips;
                _pendingResources = resources;
                _hasPending = true;
                return;
            }
        }
        Stop();
        _clock = new RenderAnimationClock(timing, loop, authoredMovementScale, initialMovementSpeed, initialPhase);
        _resourceRevision = _clock.ResourceRevision;
        // The install normalizes the definition's marks by the clip's frame count (0x0048C5D0).
        _mark1 = FootstepMarkPair.Normalize(footstepMarks.First, timing.FrameCount);
        _mark2 = FootstepMarkPair.Normalize(footstepMarks.Second, timing.FrameCount);
        FootstepCrossing = 0;
        _marks.Reset();
        _startTick = (long)Math.Floor(start * CreatureAnimationArt.TurnsPerSecond);
        _advanced = 0;
        Install(clips, resources);
        Draw(start, movementSpeed, hold, phaseStepOverride);
    }

    /// <summary>Draws the clip at <paramref name="seconds"/>: advances the clock one turn for each turn since the start
    /// not yet advanced (the start's own turn included), then hands the pose or sampled vertices to the surface.</summary>
    public void Draw(double seconds, Fp32? movementSpeed = null, bool hold = false, Fp32? phaseStepOverride = null)
    {
        if (_clock == null)
        {
            return;
        }

        double tickTime = seconds * CreatureAnimationArt.TurnsPerSecond;
        long tick = (long)Math.Floor(tickTime);
        long previousLoops = _clock.CompletedLoops;
        int advancedNow = 0;
        while (_advanced <= tick - _startTick) { _clock.Advance(movementSpeed, hold, phaseStepOverride); _advanced++; advancedNow++; }
        if (_clock.CompletedLoops != previousLoops)
        {
            BeatPending = true;
        }

        float fraction = (float)(tickTime - tick);
        AnimationPose pose = _clock.Sample(fraction);
        FootstepCrossing = (_mark1 | _mark2) == 0 ? 0
            : _marks.Observe(mark1: _mark1, mark2: _mark2, previousPhase: _clock.PreviousPhase,
                phase: _clock.Phase, ticksAdvanced: advancedNow, fraction: fraction);
        if (_clock.ResourceRevision != _resourceRevision && _hasPending)
        {
            RenderAnimation?[] clips = _pendingClips!;
            TResources resources = _pendingResources;
            ClearPending();
            Install(clips, resources);
            _resourceRevision = _clock.ResourceRevision;
        }
        AnimationSegment segment = pose.Segment;
        if (_drawnSegment != segment)
        {
            _surface.Show(segment);
            _drawnSegment = segment;
        }
        _drawnPhase = pose.Phase;
        _sampleCurrent = false;
        if (_surface is ICreatureAnimationPoseSurface gpu)
        {
            gpu.Pose(segment, pose.Phase);
        }
        else if (TryGetSample(out Vector3[] sample))
        {
            _surface.Upload(segment, sample);
        }
    }

    /// <summary>The last drawn pose, sampled only on request and cached until the next draw.</summary>
    public bool TryGetSample(out Vector3[] vertices)
    {
        vertices = [];
        if (_clock == null || !_drawnSegment.HasValue || _clips == null)
        {
            return false;
        }

        int segment = (int)_drawnSegment.Value;
        RenderAnimation? clip = _clips[segment];
        if (clip == null)
        {
            return false;
        }

        if (!_buffers.TryGetValue(clip, out Vector3[]? buffer))
        {
            _buffers.Add(clip, buffer = new Vector3[clip.Mesh.Positions.Count]);
        }

        vertices = buffer;
        if (!_sampleCurrent)
        {
            clip.Sample(_drawnPhase, false, vertices);
        }

        _sampleCurrent = true;
        return true;
    }

    /// <summary>The drawn clip's tagged point <paramref name="id"/> (a KMF control point) as last drawn, model-local:
    /// the point world slot +0x10C finds (0x0059D260, ADR-0480).</summary>
    public bool TryGetControlPoint(uint id, out Vector3 local)
    {
        local = default;
        if (!_drawnSegment.HasValue || _clips == null)
        {
            return false;
        }

        int segment = (int)_drawnSegment.Value;
        RenderAnimation? clip = _clips[segment];
        if (clip == null || !clip.Mesh.ControlPoints.TryGetValue(id, out int index)
            || index >= clip.Mesh.Positions.Count)
        {
            return false;
        }

        local = clip.SampleVertex(_drawnPhase, index);
        return true;
    }

    /// <summary>The drawn clip's tagged points below id 2000, in file order with repeats, model-local as last drawn.</summary>
    public void CollectLightningPoints(List<AnimationPoint> points)
    {
        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        points.Clear();
        if (!_drawnSegment.HasValue || _clips == null)
        {
            return;
        }

        int segment = (int)_drawnSegment.Value;
        RenderAnimation? clip = _clips[segment];
        if (clip == null)
        {
            return;
        }

        IReadOnlyList<RenderControlPoint> tagged = clip.Mesh.TaggedPoints;
        for (int i = 0; i < tagged.Count; i++)
        {
            RenderControlPoint control = tagged[i];
            if (control.Id < LightningPointLimit && control.VertexIndex < clip.Mesh.Positions.Count)
            {
                points.Add(new AnimationPoint(control.Id, clip.SampleVertex(_drawnPhase, control.VertexIndex)));
            }
        }
    }

    /// <summary>Stops the clock and drops a waiting clip; the installed clips and their samples stay for a later
    /// <see cref="Configure"/>.</summary>
    public void Stop()
    {
        _clock = null;
        _mark1 = _mark2 = 0;
        FootstepCrossing = 0;
        BeatPending = false;
        ClearPending();
    }

    private void ClearPending()
    {
        _hasPending = false;
        _pendingClips = null;
        _pendingResources = default!;
    }

    private void Install(RenderAnimation?[] clips, TResources resources)
    {
        InstallationRevision++;
        _drawnSegment = null;
        _clips = clips;
        _sampleCurrent = false;
        _surface.Install(clips, resources);
    }
}

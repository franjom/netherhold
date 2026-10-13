// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Core;

namespace DK2.Art;

public enum AnimationSegment { Start, Main, End }
public enum AnimationReplacement { Immediate, Deferred }

public readonly record struct AnimationPose(AnimationSegment Segment, float Phase);

/// <summary>Presentation clock for type-6 playback at four turns per second.</summary>
public sealed class RenderAnimationClock
{
    private const int One = 4096;
    private const CreatureAnimationFlags StopLooping = (CreatureAnimationFlags)0x80;
    private CreatureAnimationTiming _timing;
    private bool _loop;
    private Fp32? _authoredMovementScale;
    private Fp32? _movementSpeed;
    private AnimationSegment _segment;
    private AnimationSegment _previousSegment;
    private int _phase;
    private int _previousPhase;
    private int _duration;
    private int _rate;
    private bool _complete;
    private (CreatureAnimationTiming Timing, bool Loop, Fp32? Scale, Fp32? InitialSpeed)? _pending;

    public int ResourceRevision { get; private set; }
    public long CompletedLoops { get; private set; }

    public RenderAnimationClock(CreatureAnimationTiming timing, bool loop, Fp32? authoredMovementScale = null,
        Fp32? initialMovementSpeed = null)
    {
        Install(timing, loop, authoredMovementScale, initialMovementSpeed);
    }

    public void Replace(CreatureAnimationTiming timing, bool loop, AnimationReplacement replacement,
        Fp32? authoredMovementScale = null, Fp32? initialMovementSpeed = null)
    {
        if (replacement == AnimationReplacement.Deferred && !_complete)
        {
            _pending = (timing, loop, authoredMovementScale, initialMovementSpeed);
            _loop = false;
            return;
        }
        Install(timing, loop, authoredMovementScale, initialMovementSpeed);
        ResourceRevision++;
    }

    private void Install(CreatureAnimationTiming timing, bool loop, Fp32? authoredMovementScale, Fp32? initialMovementSpeed)
    {
        _timing = timing;
        _loop = loop && (timing.Flags & StopLooping) == 0;
        _authoredMovementScale = authoredMovementScale;
        _movementSpeed = authoredMovementScale.HasValue ? initialMovementSpeed ?? new Fp32(One) : null;
        _pending = null;
        CompletedLoops = 0;
        _phase = _previousPhase = 0;
        _complete = false;
        _segment = Has(CreatureAnimationFlags.StartSegment) ? AnimationSegment.Start : AnimationSegment.Main;
        _previousSegment = _segment;
        SetDuration(_segment);
    }

    public void Advance(Fp32? movementSpeed = null, bool hold = false, Fp32? phaseStepOverride = null)
    {
        if (hold)
        {
            _previousSegment = _segment;
            _previousPhase = _phase;
            return;
        }
        AdvancePhase(phaseStepOverride);
        if (_authoredMovementScale.HasValue && movementSpeed.HasValue && !_complete)
        {
            Fp32 scalar = movementSpeed.Value.Raw == 0 ? _movementSpeed ?? new Fp32(1) : movementSpeed.Value;
            _movementSpeed = scalar;
            _rate = CalculateRate();
            if (movementSpeed.Value.Raw == 0) _movementSpeed = new Fp32(1);
        }
    }

    private void AdvancePhase(Fp32? phaseStepOverride)
    {
        _previousSegment = _segment;
        _previousPhase = _phase;
        if (_complete) { _previousPhase = _phase = One; _rate = 0; return; }
        int step = phaseStepOverride?.Raw ?? _rate;
        if (step == 0) return;
        _phase += step;
        if (_phase < One) return;
        if (_segment == AnimationSegment.Start)
        {
            Transition(AnimationSegment.Main);
            if (_phase <= One) return;
            if (!Has(CreatureAnimationFlags.EndSegment)) { _phase = One; return; }
            Transition(AnimationSegment.End);
            _phase = Math.Min(One, _phase);
            return;
        }
        if (_segment == AnimationSegment.Main)
        {
            if (_loop) { _phase -= One; CompletedLoops++; return; }
            if (Has(CreatureAnimationFlags.EndSegment))
            {
                Transition(AnimationSegment.End);
                _phase = Math.Min(One, _phase);
                return;
            }
        }
        _phase = One;
        _complete = true;
        if (_pending.HasValue)
        {
            var pending = _pending.Value;
            Install(pending.Timing, pending.Loop, pending.Scale, pending.InitialSpeed);
            ResourceRevision++;
        }
    }

    public AnimationPose Sample(float fraction)
    {
        if (float.IsNaN(fraction) || float.IsInfinity(fraction))
            throw new ArgumentOutOfRangeException(nameof(fraction));
        fraction = Math.Clamp(fraction, 0f, 1f);
        int current = _phase;
        if (_segment == _previousSegment && current < _previousPhase) current += One;
        float phase = _previousPhase + ((int)(_segment - _previousSegment) * One + current - _previousPhase) * fraction;
        AnimationSegment segment = _previousSegment;
        if (phase > 2 * One) { phase -= 2 * One; segment += 2; }
        else if (phase > One) { phase -= One; segment++; }
        if (segment > _segment) segment = _segment;
        return new AnimationPose(segment, Math.Clamp(phase / One, 0f, 1f));
    }

    private bool Has(CreatureAnimationFlags flag) => (_timing.Flags & flag) != 0;

    private void Transition(AnimationSegment segment)
    {
        int previousDuration = _duration;
        int overshoot = _phase - One;
        _segment = segment;
        SetDuration(segment);
        int ratio = previousDuration > 0 ? (int)((long)_duration * One / previousDuration) : 0;
        _phase = ratio > 0 ? (int)((long)overshoot * One / ratio) : 0;
    }

    private void SetDuration(AnimationSegment segment)
    {
        int frames = segment switch
        {
            AnimationSegment.Start => _timing.StartSegmentFrames,
            AnimationSegment.End => _timing.EndSegmentFrames,
            _ => _timing.FrameCount,
        };
        _duration = _timing.FramesPerSecond > 0 ? frames * One / _timing.FramesPerSecond : 0;
        _rate = CalculateRate();
    }

    private int CalculateRate()
    {
        long rate = _duration > 0 ? (long)One * One / (_duration * 4L) : 0;
        if (_authoredMovementScale.HasValue && _movementSpeed.HasValue)
        {
            int scale = _authoredMovementScale.Value.Raw;
            if (scale == 0) return 0;
            int perTick = scale / 4;
            if (perTick > 0)
            {
                long ratio = (long)_movementSpeed.Value.Raw * One / perTick;
                rate = rate * ratio >> 12;
            }
        }
        return (int)Math.Min(One - 1, rate);
    }
}

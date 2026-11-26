// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A heart's beat this frame: whether the gate let a pending beat through, which the renderer consumes and
/// DK2 sounds (0x150 at 0x0043CE85, <see cref="HeartBeatSound"/>), and the mana effect requested with it, if any.</summary>
public readonly record struct HeartBeat(bool Consumed, ViewEffectRequest? Effect);

/// <summary>The Dungeon Heart's pulse as the scene draws it: health changes queued from the tick thread and applied to
/// every heart of their owner at the next frame, and the beat that <see cref="HeartPulseGate"/> lets through. Render
/// only: it reads object views, never the world.</summary>
public sealed class HeartPulse
{
    private readonly HeartPulseGate _gate = new();
    private readonly ConcurrentQueue<(PlayerId Owner, Fp32 Step)> _advances = new();

    /// <summary>Queues a health change from any thread; the frame applies its phase step.</summary>
    public void QueueHealthChange(PlayerId owner, DungeonHeartVisual visual) => _advances.Enqueue((owner, visual.PhaseStep));

    /// <summary>The next queued change, oldest first; the renderer advances each heart of <paramref name="owner"/>
    /// (<see cref="IsHeartOf"/>) by <paramref name="step"/>.</summary>
    public bool TryTakeHealthAdvance(out PlayerId owner, out Fp32 step)
    {
        if (_advances.TryDequeue(out (PlayerId Owner, Fp32 Step) advance))
        {
            (owner, step) = advance;
            return true;
        }
        (owner, step) = (default, default);
        return false;
    }

    public static bool IsHeartOf(in ObjectView pose, PlayerId owner) => pose.HeartVisual.HasValue && pose.OwnerPlayerId == owner;

    public void ClearHealthAdvances()
    {
        while (_advances.TryDequeue(out _)) { }
    }

    /// <summary>A heart's beat: a heart with a pending beat that is on screen asks the gate, reading the clock only
    /// then; a beat let through requests the heart's mana effect at its position once the intro is over
    /// (<see cref="HeartPulseGate.CanEmit"/>) and the heart has one.</summary>
    public HeartBeat TryBeat(in ObjectView pose, bool beatPending, bool isVisible, Func<EffectTime> clock, GameTick tick)
    {
        if (!pose.HeartVisual.HasValue || !beatPending || !isVisible || !_gate.ConsumePendingBeat(clock())) return default;
        EffectId mana = pose.HeartVisual.Value.ManaEffect;
        if (!HeartPulseGate.CanEmit(tick) || mana == EffectId.None) return new HeartBeat(true, null);
        return new HeartBeat(true, new ViewEffectRequest(mana, new PlayerId(pose.OwnerPlayerId),
            new FpPosition3(new Fp32(pose.PosX), new Fp32(pose.PosY), new Fp32(pose.Height))));
    }
}

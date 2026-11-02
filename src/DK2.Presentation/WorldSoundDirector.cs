// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using System.Numerics;
using DK2.Data.Media;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Turns the simulation's thing sounds into sound requests. Each thing has one voice:
/// bit 31 set, its kind in bits 24-30 and its id below, so creature 42 stays <c>0x8000002A</c>.
/// Positions are in the view's space, <c>DungeonScene.Ground</c>'s (x, height, -y) in tiles.</summary>
public sealed class WorldSoundDirector(ISimulationEventSink? downstream = null) : ISimulationEventSink
{
    private readonly ConcurrentQueue<SoundRequest> _pending = new();
    public bool TryTake(out SoundRequest request) => _pending.TryDequeue(out request);
    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.ThingSound is { } sound && !string.IsNullOrEmpty(sound.Category))
            _pending.Enqueue(new(new SoundCategory(sound.Category.ToLowerInvariant()), new SoundId((uint)sound.Event),
                new Vector3(sound.Position.X.Raw / 4096f, sound.Position.Z.Raw / 4096f, -sound.Position.Y.Raw / 4096f),
                VoiceOf(sound.Source)));
        downstream?.Emit(simEvent);
    }

    private static SoundVoiceId VoiceOf(SoundSourceId source) =>
        new(0x80000000u | ((uint)source.Kind << 24) | ((uint)source.Value & 0x00FFFFFFu));
}

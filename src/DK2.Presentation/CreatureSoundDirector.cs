// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using System.Numerics;
using DK2.Data.Media;
using DK2.Simulation;

namespace DK2.Presentation;

public sealed class CreatureSoundDirector(ISimulationEventSink? downstream = null) : ISimulationEventSink
{
    private readonly ConcurrentQueue<SoundRequest> _pending = new();
    public bool TryTake(out SoundRequest request) => _pending.TryDequeue(out request);
    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.CreatureSound is { } sound && !string.IsNullOrEmpty(sound.Category))
            _pending.Enqueue(new(new SoundCategory(sound.Category.ToLowerInvariant()), new SoundId((uint)sound.Event),
                new Vector3(sound.Position.X.Raw / 4096f, sound.Position.Z.Raw / 4096f, sound.Position.Y.Raw / 4096f),
                new SoundVoiceId(0x80000000u | (uint)sound.Creature.Value)));
        downstream?.Emit(simEvent);
    }
}

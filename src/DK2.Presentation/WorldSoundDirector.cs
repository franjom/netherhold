// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using System.Numerics;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Turns the simulation's thing sounds into sound requests. Each thing has one voice:
/// bit 31 set, its kind in bits 24-30 and its id below, so creature 42 stays <c>0x8000002A</c>.
/// A request DK2 makes with handle 0 (<see cref="ThingSoundRequest.Voiced"/> false) has none.
/// Positions are in the view's space, <c>DungeonScene.Ground</c>'s (x, height, -y) in tiles. A request DK2
/// makes only for the local player's own thing (<see cref="ThingSoundRequest.ViewerOnlyFor"/>) plays only for
/// <paramref name="viewer"/>, as the heart's opening effects do.</summary>
public sealed class WorldSoundDirector(ISimulationEventSink? downstream = null, PlayerId? viewer = null)
    : ISimulationEventSink
{
    private readonly ConcurrentQueue<SoundRequest> _pending = new();
    public bool TryTake(out SoundRequest request) => _pending.TryDequeue(out request);
    private uint _alternator;

    public void Emit(in SimEvent simEvent)
    {
        // DK2's global counter for the chicken's step (0x006E4FD4): bit 0 is tested, then it counts, on every request.
        bool turn = simEvent.ThingSound is { Alternating: true } && (_alternator++ & 1) != 0;
        if (simEvent.ThingSound is { } sound && !string.IsNullOrEmpty(sound.Category)
            && (!sound.Alternating || turn)
            && (sound.ViewerOnlyFor is not { } owner || owner == viewer))
            _pending.Enqueue(new(new SoundCategory(sound.Category.ToLowerInvariant()), new SoundId((uint)sound.Event),
                new Vector3(sound.Position.X.Raw / 4096f, sound.Position.Z.Raw / 4096f, -sound.Position.Y.Raw / 4096f),
                sound.Voiced ? VoiceOf(sound.Source) : SoundVoiceId.None));
        downstream?.Emit(simEvent);
    }

    /// <summary>A creature's own voice, the one its simulation sounds use: DK2's handle at creature <c>+0x2E</c>.</summary>
    public static SoundVoiceId CreatureVoice(int creatureId) => VoiceOf(new SoundSourceId(SoundSourceKind.Creature, creatureId));

    private static SoundVoiceId VoiceOf(SoundSourceId source) =>
        new(0x80000000u | ((uint)source.Kind << 24) | ((uint)source.Value & 0x00FFFFFFu));
}

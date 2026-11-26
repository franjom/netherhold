// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A frame's effect requests in processing order, which decides how the shared
/// effect generator (<see cref="RoomConstructionParticles"/>, seeded) hands out its numbers: after the present, the loose
/// level gems' idle effects for the turns crossed (<see cref="AfterPresent"/>); then the scene frame's requests, a Heart's
/// mana effect and a gem carrier's sparkle (<see cref="RequestFromScene"/>); then the shots' bodies and the updates due
/// (<see cref="Advance"/>). A scene request or an advance out of order throws; a present after a frame that never
/// advanced (an exception mid-frame) abandons that frame. The simulation's own events reach the model as emitted.</summary>
public sealed class EffectFrame
{
    private readonly EffectDrawList _effects;
    private readonly LevelGemIdleEffects? _gems;
    private readonly List<ViewEffectRequest> _gemRequests = new();
    private Step _step = Step.Advanced;

    private enum Step { Presented, Advanced }

    public EffectFrame(EffectDrawList effects, LevelGemIdleEffects? gems)
    {
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _gems = gems;
    }

    /// <summary>Step 1, after the frame's present: the loose gems' idle effects. A frame left unadvanced is abandoned, so
    /// one exception mid-frame costs that frame only.</summary>
    public void AfterPresent(WorldState world)
    {
        _step = Step.Presented;
        if (_gems == null)
        {
            return;
        }

        _gemRequests.Clear();
        _gems.Collect(world, _gemRequests);
        for (int i = 0; i < _gemRequests.Count; i++)
        {
            _effects.Model.Request(_gemRequests[i]);
        }
    }

    /// <summary>Step 2, the scene frame's requests (a Heart's mana effect, a gem carrier's sparkle), in the frame's order.</summary>
    public void RequestFromScene(in ViewEffectRequest request)
    {
        if (_step != Step.Presented)
        {
            throw new InvalidOperationException("A scene request came before the present's.");
        }

        _effects.Model.Request(request);
    }

    /// <summary>Step 3: the shots' bodies at <paramref name="fraction"/>, then the updates due; returns the instances'
    /// lights (<see cref="EffectDrawList.Advance"/>).</summary>
    public IReadOnlyList<EffectLightView> Advance(IReadOnlyList<ShotState> shots, float fraction)
    {
        if (_step != Step.Presented)
        {
            throw new InvalidOperationException("The effects advanced before the present's requests.");
        }

        _step = Step.Advanced;
        _effects.CaptureShots(shots, fraction);
        return _effects.Advance();
    }
}

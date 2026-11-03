// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The sound of a Casino party's tile effect: <c>0x344</c>, handle 0, in the global effects category
/// (<c>GLOBAL\EFFECTS</c>, world <c>+0xA3BB</c>) at the effect's position (<c>0x004D6610</c>). DK2 requests it only when
/// the effect was created, so the host calls this when the effect model reports the effect created
/// (docs/research/sound-effects.md section 9).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects.md#9-workshop-casino-prison-and-portal")]
public static class PartyPopSound
{
    public const uint EventId = 0x344;

    public static SoundRequest? For(in ViewEffectRequest effect) =>
        effect.EffectId.Value != CasinoJackpotRules.PartyEffectId ? null
            : new SoundRequest(new SoundCategory("effects"), new SoundId(EventId),
                new Vector3(effect.Position.X.Raw / 4096f, effect.Position.Z.Raw / 4096f, -effect.Position.Y.Raw / 4096f));
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>A creature's footstep, which DK2's per-tile presentation pass asks for (<c>0x0043CD84</c>): the mark-crossing helper
/// returns 1 or 2 and the pass requests event <c>0x314</c> or <c>0x315</c> through <c>0x0048FF10</c>, which substitutes
/// <c>0x337</c> / <c>0x338</c> while the creature is grounded on water. The request is the creature's own: its handle,
/// its category and its position. Presentation only: no simulation state is read or written, and the throttle is the player's
/// (docs/research/sound-effects-continuous.md section 1).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-continuous.md#1-footsteps")]
public static class CreatureFootsteps
{
    public const uint Walk = 0x314, WalkAlternate = 0x315, Wade = 0x337, WadeAlternate = 0x338;

    /// <param name="crossing">The helper's result: 0 none, 1 before the clip's wrap, 2 after it.</param>
    public static SoundRequest? For(string category, int creatureId, int crossing, bool wading, Vector3 position)
    {
        if (crossing is not (1 or 2) || category.Length == 0) return null;
        uint id = (wading, crossing) switch
        {
            (false, 1) => Walk,
            (false, _) => WalkAlternate,
            (true, 1) => Wade,
            _ => WadeAlternate,
        };
        return new SoundRequest(new SoundCategory(category.ToLowerInvariant()), new SoundId(id), position,
            WorldSoundDirector.CreatureVoice(creatureId));
    }
}

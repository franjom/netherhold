// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Media;

namespace DK2.Presentation.Tests;

/// <summary>The footstep requests of the per-tile presentation pass: a mark crossing of 1 or 2 asks event <c>0x314</c> or
/// <c>0x315</c>, and the wading pair <c>0x337</c> / <c>0x338</c> while the creature is grounded on water (<c>0x0043CD93</c>,
/// <c>0x0048FF10</c>). Handle, category and position are the creature's own. Docs: sound-effects-continuous.md section 1.</summary>
public sealed class CreatureFootstepSoundTests
{
    private static readonly Vector3 Place = new(3.5f, 1f, -4.5f);

    [Theory]
    [InlineData(1, false, 0x314u)]
    [InlineData(2, false, 0x315u)]
    [InlineData(1, true, 0x337u)]
    [InlineData(2, true, 0x338u)]
    public void ACrossingAsksTheCreaturesPairInItsOwnCategoryWithItsOwnVoice(int crossing, bool wading, uint expected)
    {
        SoundRequest request = CreatureFootsteps.For("CREATURE_IMP", 42, crossing, wading, Place)!.Value;
        Assert.Equal(new SoundCategory("creature_imp"), request.Category);
        Assert.Equal(new SoundId(expected), request.Id);
        Assert.Equal(Place, request.World);
        Assert.Equal(new SoundVoiceId(0x8000002A), request.Voice);
    }

    [Fact]
    public void NoCrossingOrNoCategoryAsksNothing()
    {
        Assert.Null(CreatureFootsteps.For("CREATURE_IMP", 42, 0, false, Place));
        Assert.Null(CreatureFootsteps.For("", 42, 1, false, Place));
    }
}

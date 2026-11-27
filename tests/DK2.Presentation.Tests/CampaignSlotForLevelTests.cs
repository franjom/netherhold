// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class CampaignSlotForLevelTests
{
    [Theory]
    [InlineData("Level1", 1)]
    [InlineData("level6B", 7)]
    [InlineData("LEVEL20", 24)]
    [InlineData("Secret5", 30)]
    public void ALevelOnTheMapGivesItsSlotIgnoringCase(string name, int slot)
    {
        Assert.Equal(new CampaignSlot(slot), CampaignSlot.ForLevel(name));
    }

    [Theory]
    [InlineData("MyPetDungeon")]
    [InlineData("")]
    [InlineData(null)]
    public void ALevelOffTheMapGivesNone(string? name)
    {
        Assert.Equal(CampaignSlot.None, CampaignSlot.ForLevel(name));
    }
}

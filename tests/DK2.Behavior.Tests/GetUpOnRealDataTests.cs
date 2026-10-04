// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>State 257's hold from the shipped Getup animations (ADR-0348, Q527): every length the
/// Level 1–3 captures measured, as the longest 257 run of a type that ends in a room drop.</summary>
public class GetUpOnRealDataTests
{
    [InstallTheory]
    [InlineData(5, 51, 8)]   // Warlock
    [InlineData(7, 69, 11)]  // Goblin
    [InlineData(10, 29, 5)]  // Troll
    [InlineData(12, 84, 13)] // Firefly
    [InlineData(14, 16, 4)]  // Dwarf
    [InlineData(29, 22, 4)]  // Lord of the Land
    public void TheShippedGetupAnimationGivesTheCapturedGetUpLength(byte creatureId, int frames, int capturedTicks)
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level3");
        CreatureDef definition = db.CreatureById(creatureId);
        Assert.Equal((frames, 30), (definition.GetUpFrames, definition.GetUpFramesPerSecond));
        Assert.Equal(capturedTicks, DropLandingRules.GetUpTicks(definition));
    }
}

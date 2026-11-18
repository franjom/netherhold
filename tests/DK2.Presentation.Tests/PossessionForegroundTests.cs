// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Model;
using DK2.Data;
using DK2.TestSupport;
using DK2.Simulation;
using DK2.Art;
using DK2.Presentation;

namespace DK2.Presentation.Tests;

public sealed class PossessionForegroundTests
{
    [InstallFact]
    public void MeleeAlternationStartsWithTheSecondResourceAndReadinessRejectsARestart()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var definition = database.CreatureById(8) with
        { FirstPersonMelee1 = PossessionForegroundKind.LeftPunch, FirstPersonMelee2 = PossessionForegroundKind.RightPunch };
        var creature = new CreatureState { CreatureTypeId = 8, TimeLastUsedMelee = CreatureState.NeverUsed };
        var controls = new PossessionControls();
        var foreground = new PossessionForeground();
        foreground.Fire(database, creature, definition, controls, 0, 100);
        Assert.Equal("R-Punch", foreground.Sample(0).Resource);
        creature.TimeLastUsedMelee = 100;
        foreground.Fire(database, creature, definition, controls, 1, 100);
        Assert.Equal("R-Punch", foreground.Sample(1).Resource);
        foreground.Fire(database, creature, definition, controls, 2, 10000);
        Assert.Equal("L-Punch", foreground.Sample(2).Resource);
        controls.SelectFollowers();
        foreground.Begin(PossessionForegroundKind.None, 0, 3);
        foreground.Fire(database, creature, definition, controls, 3, 10000);
        Assert.Null(foreground.Sample(3).Resource);
    }

    [InstallFact]
    public void MeshFadeUsesRgbOnceRatherThanSquaringItThroughCanvasAlpha()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        using var art = InstallArtProvider.Open(Dk2InstallGate.Path, database);
        var foreground = new PossessionForeground();
        foreground.Begin(PossessionForegroundKind.SwordSlash, 22, 0);
        foreground.Sample(1001);
        var frame = new HudFrame();
        foreground.Build(art, 1600, 1200, 1251, frame);
        Assert.NotEmpty(frame.Draws.ToArray());
        Assert.All(frame.Draws.ToArray(), draw => Assert.Equal(new HudColor(128, 128, 128, 255), draw.Tint));
    }

    [InstallFact]
    public void ShippedCreatureResourcesMatchTheObservedClawAndBladeFamilies()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        foreach (var (name, kind) in new[] { ("Vampire", PossessionForegroundKind.ClawSlash),
            ("Skeleton", PossessionForegroundKind.SwordSlash), ("Mistress", PossessionForegroundKind.MistressSlash),
            ("Black Knight", PossessionForegroundKind.SwordSlash) })
        {
            var definition = database.Creatures.Values.First(c => c.Name == name);
            Assert.Equal(kind, definition.FirstPersonMelee1);
            Assert.True(definition.FirstPersonFramesPerSecond > 0);
        }
    }

    [Fact]
    public void FullScreenVisorIsDrawnBeforeThePortraitAndHealthPanel()
    {
        var frame = new HudFrame();
        new PossessionHud().Build(new HudScreen(1600, 1200),
            new PossessionHudSnapshot("portrait", 100, new(0), [], []) { Overlays = ["GUI\\Filters\\F-Black_Knight"] }, frame);
        var draws = frame.Draws.ToArray();
        Assert.True(Array.FindIndex(draws, d => d.Image.Path == "GUI\\Filters\\F-Black_Knight")
            < Array.FindIndex(draws, d => d.Image.Path == "portrait"));
    }

    [Fact]
    public void NativeHoldDeadlineIsStrictAndFadeStartsOnTheNextPresentedFrame()
    {
        var foreground = new PossessionForeground();
        foreground.Begin(PossessionForegroundKind.SwordSlash, 22, 100);
        Assert.Equal("Sword_Slash", foreground.Sample(1100).Resource);
        Assert.Equal((byte)255, foreground.Sample(1100).Brightness);
        Assert.Equal((byte)255, foreground.Sample(1101).Brightness);
        Assert.Equal((byte)128, foreground.Sample(1351).Brightness);
        Assert.Null(foreground.Sample(1602).Resource);
    }

    [Fact]
    public void ScreenMeshUsesAuthoredXyAtTheScreenCentreWithWidthDerivedScale()
    {
        Assert.Equal(new Vector2(1050, 1100), PossessionForeground.Project(new Vector3(.1f, .7f, -.2f), 1600, 1200));
        Assert.Equal(new Vector2(1260, 1140), PossessionForeground.Project(new Vector3(.1f, 0, -.2f), 1920, 1080));
    }

    [Fact]
    public void ZeroResourceClearsAndUnknownResourcesAreNotInvented()
    {
        var foreground = new PossessionForeground();
        foreground.Begin(PossessionForegroundKind.ClawSlash, 22, 0);
        Assert.Equal("Claw_Slash", foreground.Sample(100).Resource);
        foreground.Begin(PossessionForegroundKind.None, 0, 100);
        Assert.Null(foreground.Sample(100).Resource);
        Assert.Null(PossessionForeground.Resource((PossessionForegroundKind)255));
    }
}

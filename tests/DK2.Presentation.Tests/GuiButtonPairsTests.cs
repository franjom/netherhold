// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

/// <summary>The GUI buttons of action 0x72 (ADR-0670).</summary>
public sealed class GuiButtonPairsTests
{
    [Theory]
    [InlineData(BottomPanelChoiceKind.Room, GuiButtonCategory.Room)]
    [InlineData(BottomPanelChoiceKind.Spell, GuiButtonCategory.Spell)]
    [InlineData(BottomPanelChoiceKind.Door, GuiButtonCategory.Door)]
    [InlineData(BottomPanelChoiceKind.Trap, GuiButtonCategory.Trap)]
    public void APrimaryClickPostsItsCategory(BottomPanelChoiceKind kind, GuiButtonCategory category) =>
        Assert.Equal(new GuiButtonTarget(category, 7), GuiButtonPairs.ForSelection(kind, 7, clearsTool: false));

    [Fact]
    public void ARoomThatOnlyClearsTheToolPostsNothing()
    {
        // 0x00410860 posts only on its select branch; availability 4 takes the other.
        Assert.Null(GuiButtonPairs.ForSelection(BottomPanelChoiceKind.Room, 7, clearsTool: true));
        Assert.Null(GuiButtonPairs.ForSelection(BottomPanelChoiceKind.Special, 7, clearsTool: false));
    }

    [Theory]
    [InlineData(BottomPanelCategory.Creatures, 0xF8)]
    [InlineData(BottomPanelCategory.Rooms, 0xFB)]
    [InlineData(BottomPanelCategory.Spells, 0xFA)]
    [InlineData(BottomPanelCategory.Workshop, 0xF9)]
    public void ATabPostsItsWidgetId(BottomPanelCategory tab, byte id) =>
        Assert.Equal(new GuiButtonTarget(GuiButtonCategory.Widget, id), GuiButtonPairs.ForTab(tab));

    [Fact]
    public void ADoorRightClickPostsTheTrapCategory()
    {
        // 0x00416150 pushes 4 (0x00416180) where the primary door click 0x00410BC0 posts 3: a door of
        // kind k fires trap k's event. DK2's quirk, kept.
        Assert.Equal(GuiButtonTarget.Trap(2), GuiButtonPairs.ForSecondary(BottomPanelChoiceKind.Door, 2));
        Assert.Equal(GuiButtonTarget.Trap(2), GuiButtonPairs.ForSecondary(BottomPanelChoiceKind.Trap, 2));
        Assert.Equal(new GuiButtonTarget(GuiButtonCategory.Room, 2), GuiButtonPairs.ForSecondary(BottomPanelChoiceKind.Room, 2));
        Assert.Null(GuiButtonPairs.ForSecondary(BottomPanelChoiceKind.Spell, 2));
    }
}

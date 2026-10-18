// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
namespace DK2.Presentation.Tests;
public sealed class ClassicHudControlsTests
{
    [Fact]
    public void OptionsRejectsMapAndGuiTogglesUntilClosed()
    {
        var controls=new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        controls.Apply(ClassicHudAction.ToggleGui);
        Assert.True(controls.OptionsVisible);
        Assert.True(controls.GuiVisible);
        Assert.False(controls.FullMapVisible);
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.True(controls.FullMapVisible);
    }
    [Fact]
    public void HiddenGuiStillAllowsWorldInputAndOptionsRestoresGui()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleGui);
        Assert.False(controls.Visibility(new(true, true)).ShowNormalHud);
        Assert.True(controls.Visibility(new(true, true)).AllowOrdinaryInput);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(controls.GuiVisible);
        Assert.True(controls.OptionsVisible);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.False(controls.OptionsVisible);
    }
    [Theory]
    [InlineData(8,584,ClassicHudAction.SelectQuery)]
    [InlineData(155.2f,584,ClassicHudAction.SelectSell)]
    [InlineData(8,728,ClassicHudAction.ToggleOptions)]
    [InlineData(153.6f,729.6f,ClassicHudAction.ZoomIn)]
    [InlineData(40,600,ClassicHudAction.None)]
    public void CornerRegionsUseNativeHalfOpenBounds(float x,float y,ClassicHudAction expected)
        => Assert.Equal(expected, ClassicHudControlLayout.ActionAt(new Vector2(x,y), false));
    [Fact]
    public void OnlyMagnifierAcceptsSecondaryChannel()
    {
        Assert.Equal(ClassicHudAction.ZoomOut, ClassicHudControlLayout.ActionAt(new(160,740), true));
        Assert.Equal(ClassicHudAction.None, ClassicHudControlLayout.ActionAt(new(16,590), true));
    }
    [Fact]
    public void FullMapSuppressesNormalHudAndOnlyItsToggleIsDispatched()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleFullMap);
        controls.Apply(ClassicHudAction.ToggleGui);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(controls.GuiVisible);
        Assert.False(controls.OptionsVisible);
        Assert.False(controls.Visibility(new(true,true)).ShowNormalHud);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.True(controls.Visibility(new(true,true)).ShowNormalHud);
    }
}

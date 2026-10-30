// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;
using DK2.Presentation;

namespace DK2.Presentation.Tests;

public class OptionsPagesControllerTests
{
    [Fact]
    public void SoundVolumeAppliesLiveAndCancelRestoresEntryCapture()
    {
        var options = new OptionsPagesController(new GameSettings { MasterVolume = 73 });
        var applied = new List<int>(); options.Applied += s => applied.Add(s.MasterVolume);
        options.Enter(FrontEndPage.Sound);
        options.Edit(s => s with { MasterVolume = 14 });
        Assert.Equal(14, options.Current.MasterVolume);
        options.Cancel();
        Assert.Equal(73, options.Current.MasterVolume);
        Assert.Equal(new[] { 14, 73 }, applied);
    }

    [Fact]
    public void GraphicsAreAppliedOnlyByAccept()
    {
        var options = new OptionsPagesController(new GameSettings { Gamma = 30 });
        int applied = 0; options.Applied += _ => applied++;
        options.Enter(FrontEndPage.Graphics);
        options.Edit(s => s with { Gamma = 50 });
        Assert.Equal(30, options.Current.Gamma);
        options.Cancel(); Assert.Equal(0, applied);
        options.Enter(FrontEndPage.Graphics);
        options.Edit(s => s with { Gamma = 50 }); options.Accept();
        Assert.Equal(50, options.Current.Gamma); Assert.Equal(1, applied);
    }

    [Fact]
    public void SoundAcceptKeepsTheAlreadyPersistedLiveValues()
    {
        var options = new OptionsPagesController(new GameSettings());
        int saved = 0; options.SaveRequested += _ => saved++;
        options.Enter(FrontEndPage.Sound);
        options.Edit(s => s with { MusicVolume = 22 });
        Assert.Equal(1, saved); options.Accept();
        Assert.Equal(22, options.Current.MusicVolume); Assert.Equal(2, saved);
    }

    [Fact]
    public void ControlsApplyOnAcceptAndCancelRestoresSensitivityAndBindings()
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Controls);
        options.Edit(s => s with { GameSpeed = 16, MouseSensitivity = 20 });
        Assert.Equal(4, options.Current.GameSpeed); Assert.Equal(20, options.Current.MouseSensitivity);
        options.Cancel(); Assert.Equal(10, options.Current.MouseSensitivity);
        options.Enter(FrontEndPage.Controls); options.Edit(s => s with { GameSpeed = 16 }); options.Accept();
        Assert.Equal(16, options.Current.GameSpeed);
    }
    [Fact]
    public void SoundCancelRestoresEightCapturedValuesButKeepsTheSwitches()
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound);
        options.Edit(s => s with { SpeechSwitch = false, MusicSwitch = false, SfxSwitch = false, MasterVolume = 12 });
        options.Cancel();
        Assert.Equal(100, options.Current.MasterVolume);
        Assert.False(options.Current.SpeechSwitch); Assert.False(options.Current.MusicSwitch); Assert.False(options.Current.SfxSwitch);
    }

    [Theory]
    [InlineData(116)]
    [InlineData(118)]
    [InlineData(128)]
    [InlineData(135)]
    [InlineData(137)]
    [InlineData(674)]
    [InlineData(152)]
    public void SettingsWithoutRuntimeConsumersCannotBeChangedByWidgets(ushort widget)
    {
        var options = new OptionsPagesController(new GameSettings()); options.Enter(FrontEndPage.Graphics);
        Assert.False(options.Supports(widget));
        Assert.False(options.SetWidget(widget, 0));
        Assert.True(options.Pending.WaterTranslucency); Assert.True(options.Pending.EnvironmentMapping);
        Assert.Equal(3, options.Pending.ShadowLevel); Assert.True(options.Supports(126));
    }

    [Fact]
    public void CustomHudScaleUsesTheGraphicsCommitAndCancelTransaction()
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Graphics);
        Assert.True(options.Supports(900));
        Assert.True(options.SetWidget(900, 200));
        Assert.Equal(100, options.Current.UiScale);
        options.Accept(); Assert.Equal(200, options.Current.UiScale);
        options.Enter(FrontEndPage.Graphics); options.SetWidget(900, 50); options.Cancel();
        Assert.Equal(200, options.Current.UiScale);
    }

}

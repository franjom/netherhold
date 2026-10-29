// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Data.Tests;

public class GameSettingsFileTests
{
    [Fact]
    public void EverySettingAndBindingRoundTrips()
    {
        var original = new GameSettings { Gamma = 44, ShadowLevel = 1, SoundQuality = 2,
            MasterVolume = 27, SpeechVolume = 31, MusicVolume = 62, SoundEffectVolume = 83,
            SpeechSwitch = false, MusicSwitch = false, SfxSwitch = false, QSound = true,
            EnvironmentalEffects = false, WaterTranslucency = false, EnvironmentMapping = false,
            Headphones = true, InvertMouse = true, GameSpeed = 11, ScrollSpeed = 17,
            MouseSensitivity = 19, UiScale = 150,
            Bindings = new[] { new GameKeyBinding(GameAction.Heart, 0x20, KeyModifiers.Control) } };
        GameSettings restored = GameSettingsFile.Read(GameSettingsFile.Write(original) + "FutureOption=91\n");
        Assert.Equal(GameSettingsFile.Write(original), GameSettingsFile.Write(restored));
        Assert.Equal(0x20, restored.Binding(GameAction.Heart).ScanCode);
    }

    [Fact]
    public void ValuesAreClampedAndMalformedKeysRetainDefaults()
    {
        GameSettings s = GameSettingsFile.Read("Gamma=1\nShadowLevel=99\nMasterVolume=-1\nMusicVolume=120\nMouseSensitivity=99\nGameSpeed=-7\nScrollSpeed=1\nUiScale=1000\nSpeechVolume=oops\n");
        Assert.Equal(15, s.Gamma); Assert.Equal(3, s.ShadowLevel);
        Assert.Equal(0, s.MasterVolume); Assert.Equal(100, s.MusicVolume);
        Assert.Equal(20, s.MouseSensitivity); Assert.Equal(1, s.GameSpeed);
        Assert.Equal(5, s.ScrollSpeed); Assert.Equal(200, s.UiScale);
        Assert.Equal(new GameSettings().SpeechVolume, s.SpeechVolume);
    }

    [Fact]
    public void MissingFileReturnsDefaultsWithoutCreatingAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".settings");
        Assert.Equal(GameSettingsFile.Write(new GameSettings()), GameSettingsFile.Write(GameSettingsFile.Load(path)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SaveAndLoadUseTheRequestedFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".settings");
        GameSettingsFile.Save(path, new GameSettings { MusicVolume = 12 });
        Assert.Equal(12, GameSettingsFile.Load(path).MusicVolume);
    }
}

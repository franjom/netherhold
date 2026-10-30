// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text;
using DK2.Data.Campaign;

namespace DK2.Data.Settings;

/// <summary>Our UTF-8 key=value file; unknown or malformed entries retain defaults.</summary>
public static class GameSettingsFile
{
    public static GameSettings Load(string path) => File.Exists(path) ? Read(File.ReadAllText(path)) : new();
    public static void Save(string path, GameSettings settings) => ProfileFileStorage.Save(path, Encoding.UTF8.GetBytes(Write(settings)));

    public static string Write(GameSettings settings)
    {
        settings = settings.Clamped();
        var text = new StringBuilder();
        text.Append("Gamma=").Append(settings.Gamma.ToString()).Append('\n');
        text.Append("ShadowLevel=").Append(settings.ShadowLevel.ToString()).Append('\n');
        text.Append("SoundQuality=").Append(settings.SoundQuality.ToString()).Append('\n');
        text.Append("MasterVolume=").Append(settings.MasterVolume.ToString()).Append('\n');
        text.Append("SpeechVolume=").Append(settings.SpeechVolume.ToString()).Append('\n');
        text.Append("MusicVolume=").Append(settings.MusicVolume.ToString()).Append('\n');
        text.Append("SoundEffectVolume=").Append(settings.SoundEffectVolume.ToString()).Append('\n');
        text.Append("MouseSensitivity=").Append(settings.MouseSensitivity.ToString()).Append('\n');
        text.Append("GameSpeed=").Append(settings.GameSpeed.ToString()).Append('\n');
        text.Append("ScrollSpeed=").Append(settings.ScrollSpeed.ToString()).Append('\n');
        text.Append("UiScale=").Append(settings.UiScale.ToString()).Append('\n');
        text.Append("WaterTranslucency=").Append(settings.WaterTranslucency.ToString()).Append('\n');
        text.Append("EnvironmentMapping=").Append(settings.EnvironmentMapping.ToString()).Append('\n');
        text.Append("Headphones=").Append(settings.Headphones.ToString()).Append('\n');
        text.Append("SpeechSwitch=").Append(settings.SpeechSwitch.ToString()).Append('\n');
        text.Append("MusicSwitch=").Append(settings.MusicSwitch.ToString()).Append('\n');
        text.Append("SfxSwitch=").Append(settings.SfxSwitch.ToString()).Append('\n');
        text.Append("QSound=").Append(settings.QSound.ToString()).Append('\n');
        text.Append("EnvironmentalEffects=").Append(settings.EnvironmentalEffects.ToString()).Append('\n');
        text.Append("InvertMouse=").Append(settings.InvertMouse.ToString()).Append('\n');
        foreach (GameKeyBinding b in settings.Bindings.OrderBy(b => b.Action))
            text.Append("Key.").Append((int)b.Action).Append('=').Append(b.ScanCode).Append(',').Append((int)b.Modifiers).Append('\n');
        return text.ToString();
    }

    public static GameSettings Read(string text)
    {
        var settings = new GameSettings();
        var bindings = new List<GameKeyBinding>();
        foreach (string line in text.Split('\n'))
        {
            int split = line.IndexOf('=');
            if (split <= 0) continue;
            string key = line.Substring(0, split).Trim(), value = line.Substring(split + 1).Trim();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                settings = key switch
                {
                    "Gamma" => settings with { Gamma = number },
                    "ShadowLevel" => settings with { ShadowLevel = number },
                    "SoundQuality" => settings with { SoundQuality = number },
                    "MasterVolume" => settings with { MasterVolume = number },
                    "SpeechVolume" => settings with { SpeechVolume = number },
                    "MusicVolume" => settings with { MusicVolume = number },
                    "SoundEffectVolume" => settings with { SoundEffectVolume = number },
                    "MouseSensitivity" => settings with { MouseSensitivity = number },
                    "GameSpeed" => settings with { GameSpeed = number },
                    "ScrollSpeed" => settings with { ScrollSpeed = number },
                    "UiScale" => settings with { UiScale = number },
                    _ => settings,
                };
            if (bool.TryParse(value, out bool flag))
                settings = key switch
                {
                    "WaterTranslucency" => settings with { WaterTranslucency = flag },
                    "EnvironmentMapping" => settings with { EnvironmentMapping = flag },
                    "Headphones" => settings with { Headphones = flag },
                    "SpeechSwitch" => settings with { SpeechSwitch = flag },
                    "MusicSwitch" => settings with { MusicSwitch = flag },
                    "SfxSwitch" => settings with { SfxSwitch = flag },
                    "QSound" => settings with { QSound = flag },
                    "EnvironmentalEffects" => settings with { EnvironmentalEffects = flag },
                    "InvertMouse" => settings with { InvertMouse = flag },
                    _ => settings,
                };
            string[] pair = value.Split(',');
            if (key.StartsWith("Key.", StringComparison.Ordinal) && int.TryParse(key.Substring(4), out int action)
                && pair.Length == 2 && int.TryParse(pair[0], out int scan) && int.TryParse(pair[1], out int modifiers))
                bindings.Add(new((GameAction)action, scan, (KeyModifiers)modifiers));
        }
        return (settings with { Bindings = bindings }).Clamped();
    }
}

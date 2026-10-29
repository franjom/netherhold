// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using DK2.Data.Settings;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner
    {
        private GameSettings _gameSettings = new GameSettings();
        private OptionsPagesController _optionsPages;
        private string _settingsPath;
        private ClassicSettingsRendering _settingsRendering;
        public GameSettings Settings => _gameSettings;
        public OptionsPagesController OptionsPages => _optionsPages;
        public event Action<GameSettings> SettingsChanged;
        private void InitializeSettings()
        {
            string profile = string.IsNullOrWhiteSpace(campaignProfileFile)
                ? Path.Combine(Application.persistentDataPath, "campaign-profile.dk2p") : campaignProfileFile;
            _settingsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profile)), "game-settings.ini");
            _gameSettings = GameSettingsFile.Load(_settingsPath);
            _optionsPages = new OptionsPagesController(_gameSettings);
            _optionsPages.Applied += ApplyGameSettings;
            _optionsPages.SaveRequested += settings => GameSettingsFile.Save(_settingsPath, settings);
        }
        private void ApplyGameSettings(GameSettings settings)
        {
            _gameSettings = settings;
            classicScrollSpeed = settings.ScrollSpeed;
            if (_classicCameraControls != null) _classicCameraControls.ScrollSpeed = settings.ScrollSpeed;
            if (_settingsRendering == null) _settingsRendering = new ClassicSettingsRendering(transform);
            _settingsRendering.Apply(settings.Gamma);
            SettingsChanged?.Invoke(settings);
        }
    }
}

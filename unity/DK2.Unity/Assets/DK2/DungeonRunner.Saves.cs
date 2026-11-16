// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using DK2.Data;
using DK2.Data.Save;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner
    {
        [SerializeField] private string savedSessionDirectory = "";
        private SaveSlotsView _saves;
        private ClassicSaveLoadPanel _savePanel;
        private GameSimulation _resumeSimulation;
        private SavedSession _resumeSession;
        private SessionSaver _saver;
        private SessionLoader _loader;

        private void InitializeSaves(string install)
        {
            if (_hudSources == null) _hudSources = new InstallHudSources(install, _installArt);
            _saves = new SaveSlotsView(Folders.Saves);
            _savePanel = new ClassicSaveLoadPanel(transform, new InstallClassicOptionsAssets(_hudSources));
            _savePanel.HudAction += ApplyHudAction;
            _saver = new SessionSaver(_profiles, LoadFrontEndProfile, () => DateTime.UtcNow);
            _loader = new SessionLoader(_profiles, LoadFrontEndProfile, name => CampaignLevelData.Load(
                string.IsNullOrWhiteSpace(installDirectory) ? Dk2Install.Find() : installDirectory, name));
            _savePanel.Submit += () =>
            {
                if (_hudControls.OptionsPage == ClassicOptionsPage.Save) SaveSession();
                else if (_saves.Selected != null) LoadSession(_saves.Selected.Path);
            };
        }

        private void SaveSession()
        {
            try
            {
                WaitForTick();
                _saver.Save(_simulation, World.Carry, _context, _saves);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is UnauthorizedAccessException)
            { _savePanel.Error(ex.Message); }
        }

        private void LoadSession(string path)
        {
            try
            {
                WaitForTick();
                // Validated and rebuilt before the current scene is released; loaded as DK2 loads a save (ADR-0241: rooms re-chain).
                _pendingSession = _loader.Load(path);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException
                || ex is UnauthorizedAccessException || ex is NotSupportedException)
            { _savePanel.Error(ex.Message); }
        }

        private void DrawSavePanel(bool frontEnd)
        {
            if (frontEnd)
            {
                _savePanel?.Show(_saves, _frontEnd.Page == FrontEndPage.Load && !_frontEnd.PathPlaying,
                    _hudControls.OptionsPage == ClassicOptionsPage.Save, frontEnd: true);
                return;
            }
            (bool shown, bool saving) = _hudControls.SaveLoadPage(CurrentHud);
            _savePanel?.Show(_saves, shown, saving);
        }
    }
}

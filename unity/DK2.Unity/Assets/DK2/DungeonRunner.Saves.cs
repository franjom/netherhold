// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using DK2.Data;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Save;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Systems;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner
    {
        [SerializeField] private string savedSessionDirectory = "";
        private SaveSlotsView _saves;
        private ClassicSaveLoadPanel _savePanel;
        private GameSimulation _resumeSimulation, _pendingResumeSimulation;
        private SavedSession _resumeSession, _pendingResumeSession;

        private void InitializeSaves(string install)
        {
            if (_hudSources == null) _hudSources = new InstallHudSources(install, _installArt);
            string directory = string.IsNullOrWhiteSpace(savedSessionDirectory)
                ? Path.Combine(Application.persistentDataPath, "Saves") : savedSessionDirectory;
            _saves = new SaveSlotsView(directory);
            _savePanel = new ClassicSaveLoadPanel(transform, new InstallClassicOptionsAssets(_hudSources));
            _savePanel.Back += () => ApplyHudAction(ClassicHudAction.BackToOptions);
            _savePanel.Continue += () => ApplyHudAction(ClassicHudAction.ToggleOptions);
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
                if (_campaignProfile == null) LoadFrontEndProfile();
                if (_playedSlot != CampaignSlot.None) ApplyCampaignCarry();
                string path = SavedSessionFile.PathForName(_saves.Directory, _saves.Name);
                var header = new SavedSessionHeader(levelName, (byte)sessionMode, (ushort)frontEndReturnPage,
                    DateTime.UtcNow, _saves.Name);
                SavedSessionFile.Save(path, new SavedSession(header, WorldSnapshot.ToBytes(_simulation), _campaignProfile));
                _saves.Refresh();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is UnauthorizedAccessException)
            { _savePanel.Error(ex.Message); }
        }

        private void LoadSession(string path)
        {
            try
            {
                WaitForTick();
                SavedSession session = SavedSessionFile.Load(path);
                if (session.Header.GameMode != (byte)GameSessionMode.Campaign
                    || !Enum.IsDefined(typeof(FrontEndPage), session.Header.ReturnPageId))
                    throw new InvalidDataException("This saved session has an unsupported game mode or return page.");
                string install = string.IsNullOrWhiteSpace(installDirectory) ? Dk2Install.Find() : installDirectory;
                string name = session.Header.LevelName;
                GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, name);
                var level = GameDatabaseLoader.LoadCampaignLevel(install, name);
                var players = GameDatabaseLoader.LoadCampaignPlayers(install, name);
                var things = GameDatabaseLoader.LoadCampaignThings(install, name);
                var triggers = GameDatabaseLoader.LoadCampaignTriggers(install, name);
                if (level == null || players == null || things == null || triggers == null)
                    throw new InvalidDataException("The saved level is not available in this installation.");
                var tree = new TriggerTree(triggers, level.TriggerId, things.OfType<HeroPartyThing>(),
                    players.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
                // Validate before releasing the current scene. Our checkpoint format preserves its hash exactly.
                GameSimulation simulation = WorldSnapshot.FromBytes(session.Snapshot, database, SimulationSystems.Default(), tree);
                if (_campaignProfilePath == null) LoadFrontEndProfile();
                CampaignProfileFile.Save(_campaignProfilePath, session.Profile);
                _pendingResumeSession = session; _pendingResumeSimulation = simulation;
                _pendingSession = GameSessionRequest.Campaign(name, (FrontEndPage)session.Header.ReturnPageId);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException
                || ex is UnauthorizedAccessException || ex is NotSupportedException)
            { _savePanel.Error(ex.Message); }
        }

        private void DrawSavePanel(bool frontEnd)
        {
            bool shown = frontEnd ? _frontEnd.Page == FrontEndPage.Load && !_frontEnd.PathPlaying
                : _hudControls.OptionsVisible && _hudControls.OptionsPage is ClassicOptionsPage.Save or ClassicOptionsPage.Load;
            _savePanel?.Show(_saves, shown && (frontEnd || CurrentHud.ShowNormalHud), _hudControls.OptionsPage == ClassicOptionsPage.Save, frontEnd);
        }
    }
}

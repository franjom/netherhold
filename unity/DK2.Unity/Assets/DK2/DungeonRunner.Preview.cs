// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

#if UNITY_EDITOR
using System.IO;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>Editor-only previews of the level-end screens, from the DungeonRunner component's context
    /// menu in Play mode, so they can be looked at without winning a level. Remake tooling, not DK2.</summary>
    public sealed partial class DungeonRunner
    {
        private bool _previewingWinScreen;

        /// <summary>In a level: runs the win screen over this level's rewards, as if it had been won.</summary>
        [ContextMenu("DK2/Preview win screen")]
        private void PreviewWinScreen()
        {
            if (!Application.isPlaying || _frontEnd != null || _winScreen == null || _resultRecorded)
            {
                Debug.LogWarning("DK2: the win screen preview needs a level running in Play mode, before its result.");
                return;
            }
            _winScreen.Start(Time.unscaledTimeAsDouble * 1000d);
            _previewingWinScreen = !_winScreen.Done;
            if (!_previewingWinScreen) Debug.LogWarning($"DK2: {levelName} unlocks no rewards; its win screen is empty.");
        }

        partial void UpdateWinScreenPreview()
        {
            if (!_previewingWinScreen || _winScreen == null) return;
            // A real result takes the screen over; Done already clears the item, so nothing is closed.
            if (_resultRecorded) { _previewingWinScreen = false; return; }
            _winScreen.Update(Time.unscaledTimeAsDouble * 1000d, _scriptedView.PathPlaying, continueRequested: false);
            if (_winScreen.Done) _previewingWinScreen = false;
        }

        partial void DrawWinScreenPreview()
        {
            if (_previewingWinScreen && !_scriptedView.PathPlaying) DrawWinScreen();
        }

        /// <summary>Anywhere: returns to the front end as after a won level, with sample statistics for the
        /// played map, through a copy of the campaign profile and hi-score table in the temporary cache.
        /// The copies stay in use until Play mode stops; the real files are not written.</summary>
        [ContextMenu("DK2/Preview statistics page")]
        private void PreviewStatisticsPage()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("DK2: the statistics preview needs Play mode.");
                return;
            }
            string folder = Path.Combine(Application.temporaryCachePath, "dk2-preview");
            Directory.CreateDirectory(folder);
            string realProfile = string.IsNullOrWhiteSpace(campaignProfileFile)
                ? Path.Combine(Application.persistentDataPath, "campaign-profile.dk2p") : campaignProfileFile;
            CampaignProfile profile = File.Exists(realProfile)
                ? CampaignProfileFile.LoadOrCreate(realProfile, InstalledCampaignRegistry.ReadCurrentUser)
                : new CampaignProfile();
            CampaignSlot played = CampaignProgression.PlayedSlot(profile);
            if (played == CampaignSlot.None) { profile = profile with { LevelNumber = 1, SecretLevelNumber = 0 }; played = new CampaignSlot(1); }
            var block = new LevelStatisticsBlock
            {
                LevelWon = true, TimeTaken = 23 * 60 + 41, EnemyCreaturesKilled = 37, HeroesDestroyed = 12, RoomsTakenOver = 3,
                LandOwned = 412, GoldMined = 48250, ManaStored = 18000, ItemsManufactured = 9, CreaturesCommanded = 24,
                CreaturesConverted = 2, CreatureTraining = (4 << 12) | 0x400, SpecialsFound = 2, SpecialsMissed = 1,
            };
            profile = CampaignProgression.RecordResult(profile, played, won: true);
            profile = CampaignProgression.RecordStatistics(profile, PlayerIds.FirstKeeper, block, played, keeperRank: 60);
            string previewProfile = Path.Combine(folder, "campaign-profile.dk2p");
            CampaignProfileFile.Save(previewProfile, profile);
            string realScores = string.IsNullOrWhiteSpace(hiScoresFile)
                ? Path.Combine(Application.persistentDataPath, "HiScores.dat") : hiScoresFile;
            string previewScores = Path.Combine(folder, "HiScores.dat");
            if (File.Exists(realScores) && realScores != previewScores) File.Copy(realScores, previewScores, overwrite: true);
            else if (!File.Exists(realScores) && File.Exists(previewScores)) File.Delete(previewScores);
            campaignProfileFile = previewProfile;
            hiScoresFile = previewScores;
            Debug.Log($"DK2: previewing the statistics page for {played.LevelName} with a profile copy in {folder}.");
            LoadLevel(GameSessionRequest.FrontEnd(FrontEndPage.CampaignResults));
        }
    }
}
#endif

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Linq;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>A campaign level started from the front end: its verdict, and the way back.</summary>
    public sealed partial class DungeonRunner
    {
        private CampaignSlot _playedSlot;
        private bool _resultRecorded;
        private int _levelTicksPerSecond;
        private readonly bool[] _statisticsRecorded = new bool[8];

        private bool LaunchedFromFrontEnd => sessionMode == GameSessionMode.Campaign && frontEndReturnPage == FrontEndPage.CampaignResults;

        private void PrepareCampaignResult()
        {
            if (!LaunchedFromFrontEnd) return;
            LoadFrontEndProfile();
            CampaignSlot slot = CampaignProgression.PlayedSlot(_campaignProfile);
            // A profile that names another map (edited between sessions) records nothing.
            _playedSlot = string.Equals(slot.LevelName, levelName, System.StringComparison.OrdinalIgnoreCase) ? slot : CampaignSlot.None;
        }

        /// <summary>0x004BE630's local-player arms when the verdict lands; Space then posts action 1,
        /// whose world flag (+0x723B, 0x00510E90) ends the session and returns to the front end.</summary>
        /// <returns>True when the runner has been replaced and the frame must stop.</returns>
        private bool UpdateCampaignResult()
        {
            if (!LaunchedFromFrontEnd) return false;
            RecordLevelStatistics();
            byte status = _simulation.World.Player(new PlayerId((byte)viewingPlayerId)).Status;
            bool ended = PlayerStatus.HasEnded(status);
            _hudControls.UpdateResult(new HudResultState(HasResult: ended), _scriptedView.PathPlaying ? CameraModeId.ScriptedPath : default);
            if (!ended) return false;
            if (!_resultRecorded && _playedSlot != CampaignSlot.None)
            {
                _campaignProfile = CampaignProgression.RecordResult(_campaignProfile, _playedSlot, won: status == PlayerStatus.Won);
                CampaignProfileFile.Save(_campaignProfilePath, _campaignProfile);
            }
            _resultRecorded = true;
            if (_scriptedView.PathPlaying || !Input.GetKeyDown(KeyCode.Space)) return false;
            LoadLevel(GameSessionRequest.FrontEnd(frontEndReturnPage));
            return true;
        }

        /// <summary>0x004BE630's shared tail for each player as its verdict is sampled, in verdict order.</summary>
        private void RecordLevelStatistics()
        {
            if (_playedSlot == CampaignSlot.None) return;
            WorldState world = _simulation.World;
            bool any = false;
            for (int i = 0; i < world.PlayerCount && !any; i++)
            {
                ref PlayerState candidate = ref world.PlayerAtIndex(i);
                any = candidate.Statistics.Sampled && candidate.PlayerId.Value < _statisticsRecorded.Length
                    && !_statisticsRecorded[candidate.PlayerId.Value];
            }
            if (!any) return;
            var sampled = Enumerable.Range(0, world.PlayerCount).Select(i => world.PlayerAtIndex(i))
                .Where(p => p.Statistics.Sampled && p.PlayerId.Value < _statisticsRecorded.Length && !_statisticsRecorded[p.PlayerId.Value])
                .OrderBy(p => p.StatusTick.Value).ThenBy(p => p.PlayerId.Value).ToArray();
            if (sampled.Length == 0) return;
            GameDatabase database = _simulation.Database;
            foreach (PlayerState player in sampled)
            {
                _statisticsRecorded[player.PlayerId.Value] = true;
                LevelStatisticsBlock block = LevelStatisticsRules.Block(player, _levelTicksPerSecond);
                uint? rank = player.Status is PlayerStatus.Won or PlayerStatus.Lost
                    ? KeeperRank.Compute(block, LevelStatisticsRules.NativeEndTurn(player.Statistics), _levelTicksPerSecond,
                        database[VariableTypeId.AverageTime], database[VariableTypeId.LevelRating])
                    : (uint?)null;
                _campaignProfile = CampaignProgression.RecordStatistics(_campaignProfile, player.PlayerId, block, _playedSlot, rank);
            }
            CampaignProfileFile.Save(_campaignProfilePath, _campaignProfile);
        }

        private void DrawCampaignResultPrompt()
        {
            // Remake placeholder: DK2's in-level result presentation is unread (front-end plan F5).
            if (!LaunchedFromFrontEnd || !_resultRecorded || _scriptedView.PathPlaying) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            GUI.Label(new Rect(0, Screen.height - 60, Screen.width, 30), "Press Space to continue", style);
        }
    }
}

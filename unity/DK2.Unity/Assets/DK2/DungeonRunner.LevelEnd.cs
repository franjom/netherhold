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
        private CampaignCarry _carryApplied;

        private bool LaunchedFromFrontEnd => sessionMode == GameSessionMode.Campaign && frontEndReturnPage == FrontEndPage.CampaignResults;

        private void PrepareCampaignResult()
        {
            if (!LaunchedFromFrontEnd) return;
            LoadFrontEndProfile();
            CampaignSlot slot = CampaignProgression.PlayedSlot(_campaignProfile);
            // A profile that names another map (edited between sessions) records nothing.
            _playedSlot = string.Equals(slot.LevelName, levelName, System.StringComparison.OrdinalIgnoreCase) ? slot : CampaignSlot.None;
            if (_campaignProfile.TransferCreatureId == 0) return;
            // 0x0050C67D: the stored creature arrives before the first turn; both values clear whatever happens.
            TransferCreatureRules.Arrive(_simulation, new PlayerId((byte)viewingPlayerId),
                _campaignProfile.TransferCreatureId, _campaignProfile.TransferCreatureLevel);
            _campaignProfile = _campaignProfile with { TransferCreatureId = 0, TransferCreatureLevel = 0 };
            CampaignProfileFile.Save(_campaignProfilePath, _campaignProfile);
        }

        /// <summary>The profile writes specials 129 and 140 make in DK2 the moment they act.</summary>
        private void ApplyCampaignCarry()
        {
            CampaignCarry carry = _simulation.World.Carry;
            if (carry.HiddenLandFound == _carryApplied.HiddenLandFound && carry.TransferStored == _carryApplied.TransferStored
                && carry.TransferCreatureTypeId == _carryApplied.TransferCreatureTypeId
                && carry.TransferCreatureLevel == _carryApplied.TransferCreatureLevel) return;
            for (int i = _carryApplied.HiddenLandFound; i < carry.HiddenLandFound; i++)
                if (_campaignProfile.LevelNumber is >= 1 and <= 30)
                    _campaignProfile = CampaignProgression.LocateHiddenLand(_campaignProfile, new CampaignSlot(_campaignProfile.LevelNumber));
            if (carry.TransferStored)
                _campaignProfile = _campaignProfile with
                {
                    TransferCreatureId = carry.TransferCreatureTypeId, TransferCreatureLevel = carry.TransferCreatureLevel,
                };
            _carryApplied = carry;
            CampaignProfileFile.Save(_campaignProfilePath, _campaignProfile);
        }

        /// <summary>0x004BE630's local-player arms when the verdict lands; Space then posts action 1,
        /// whose world flag (+0x723B, 0x00510E90) ends the session and returns to the front end.</summary>
        /// <returns>True when the runner has been replaced and the frame must stop.</returns>
        private bool UpdateCampaignResult()
        {
            if (!LaunchedFromFrontEnd) return false;
            if (_playedSlot != CampaignSlot.None) ApplyCampaignCarry();
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

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>A campaign level started from the front end: its verdict, and the way back
    /// (<see cref="CampaignResultFlow"/>).</summary>
    public sealed partial class DungeonRunner
    {
        /// <summary>The level, how it started, the played slot and the carry already in the profile (saves read it too).</summary>
        private SessionContext _context;
        /// <summary>The level end, made with <see cref="_context"/>; null until the level starts.</summary>
        private CampaignResultFlow _resultFlow;

        private bool LaunchedFromFrontEnd => SessionContext.StartedFromFrontEnd(sessionMode, frontEndReturnPage);

        private void PrepareCampaignResult(bool resumed = false) =>
            _resultFlow.Prepare(_simulation, new PlayerId((byte)viewingPlayerId), resumed);

        /// <returns>True when the runner has been replaced and the frame must stop.</returns>
        private bool UpdateCampaignResult()
        {
            if (!LaunchedFromFrontEnd) return false;
            var frame = new CampaignResultFrame(new PlayerId((byte)viewingPlayerId), _scriptedView.PathPlaying,
                Time.unscaledTimeAsDouble * 1000d,
                _soundPlayer != null && _soundPlayer.SpeechEnabled ? _soundPlayer.SpeechBusy : (bool?)null,
                Input.GetKeyDown(KeyCode.Space), Input.GetKeyDown(KeyCode.Escape));
            if (!_resultFlow.Update(World, _simulation.Database, frame, _pendingCommands)) return false;
            LoadLevel(GameSessionRequest.FrontEnd(frontEndReturnPage));
            return true;
        }

        private void DrawCampaignResultPrompt()
        {
            if (_resultFlow == null || !_resultFlow.ResultRecorded) return;
            string prompt = _resultFlow.Prompt(World, new PlayerId((byte)viewingPlayerId), _scriptedView.PathPlaying);
            if (prompt == null) return;
            DrawWinScreen();
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            GUI.Label(new Rect(0, Screen.height - 60, Screen.width, 30), prompt, style);
        }
    }
}

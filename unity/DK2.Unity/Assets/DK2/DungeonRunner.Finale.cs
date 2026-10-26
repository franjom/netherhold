// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.IO;
using DK2.Core;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>The campaign win's view side (campaign-win-sequence.md §4-§6): the win screen, the
    /// camera following the Reaper, the finale's zoom, and Escape to carry on playing.</summary>
    public sealed partial class DungeonRunner
    {
        private WinScreen _winScreen;
        private bool _continueRequested;

        private void PrepareWinScreen(GameDatabase database)
        {
            _winScreen = null;
            _continueRequested = false;
            if (database.Level == null) return;
            string table = Path.Combine(_textFolder, "Speech.str");
            Dk2StringTable speech = File.Exists(table) ? Dk2StringTable.Open(table) : null;
            _winScreen = new WinScreen(WinScreen.RewardsOf(database.Level),
                id => speech != null && speech.TryResolve(id, out string text) ? text : null);
            _scriptedView.WinScreenRequested += () => _winScreen.Start(Time.unscaledTimeAsDouble * 1000d);
            _scriptedView.ZoomRequested += (zoom, _) => _classicCamera?.SetZoom(new Fp32(zoom));
        }

        /// <summary>Game action 17: the camera keeps the followed creature at its focus.</summary>
        private void FollowScriptedCreature()
        {
            if (_scriptedView.FollowedCreatureId == 0 || _glideMilliseconds > 0) return;
            if (_simulation.World.CreatureById(_scriptedView.FollowedCreatureId) is not CreatureState creature) return;
            _cameraFocus = new Vector3(creature.PosX / (float)Fp32.One, _cameraFocus.y, -creature.PosY / (float)Fp32.One);
        }

        /// <summary>The won campaign result's keys (<c>0x004054F0</c>) and the win screen's update
        /// (<c>0x0042C390</c>). Space leaves unless a continue is pending; Escape continues at once when
        /// the screen is done, else reveals the map and waits for it; on a secret level Escape does
        /// nothing. Returns true when the frame's keys were taken.</summary>
        private bool UpdateWinKeys()
        {
            var player = new PlayerId((byte)viewingPlayerId);
            double now = Time.unscaledTimeAsDouble * 1000d;
            bool secret = _campaignProfile.SecretLevelNumber != 0;
            if (_winScreen != null && _winScreen.Update(now, _scriptedView.PathPlaying, _continueRequested))
            {
                Continue(player);
                return true;
            }
            if (secret || _continueRequested || !Input.GetKeyDown(KeyCode.Escape)) return false;
            _pendingCommands.Add(new RevealMapCommand(player));
            if (_winScreen != null && _winScreen.Started && _winScreen.Done) Continue(player);
            else _continueRequested = true;
            return true;
        }

        private void Continue(PlayerId player)
        {
            _pendingCommands.Add(new ContinueAfterResultCommand(player));
            _winScreen?.Close();
            _continueRequested = false;
        }

        private void DrawWinScreen()
        {
            // Remake placeholder layout: DK2 centres the item's icon at 0.75 scale; its fields are unread.
            if (_winScreen == null || !_winScreen.Started || _winScreen.Current is not RewardItem item) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Screen.height / 24, wordWrap = true };
            GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, Screen.height * 0.1f), item.Name.Replace('_', ' '), style);
            if (_winScreen.Text is string text)
                GUI.Label(new Rect(Screen.width * 0.1f, Screen.height * 0.45f, Screen.width * 0.8f, Screen.height * 0.1f), text, style);
        }
    }
}

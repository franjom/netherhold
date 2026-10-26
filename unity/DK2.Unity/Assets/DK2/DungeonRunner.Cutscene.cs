// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.IO;
using DK2.Data.Text;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>The won-level title card and cutscene that run before the front end (0x0053B980).</summary>
    public sealed partial class DungeonRunner
    {
        // The voiced line is F8; until then the title holds for this long between DK2's two 250 ms waits.
        private const float TitleHoldWithoutSpeech = 2f;
        private CampaignCutscene _cutscene;
        private string _cutsceneTitle;
        private string _cutsceneInstall;
        private float _cutsceneTitleEnds;
        private int _cutsceneMovie = -1;
        private ClassicMoviePlayer _cutscenePlayer;

        private bool CutscenePlaying => _cutscene != null;

        private void BeginCutscene(string install)
        {
            _cutscene = CampaignCutscene.AfterLevel(_campaignProfile);
            if (_cutscene == null) return;
            _cutsceneInstall = install;
            _cutsceneTitle = null;
            if (_cutscene.TitleTextId != 0)
            {
                string table = Path.Combine(install, "Data", "Text", "Default", "Speech.str");
                if (File.Exists(table) && Dk2StringTable.Open(table).TryResolve(_cutscene.TitleTextId, out string title))
                    _cutsceneTitle = title;
            }
            _cutsceneTitleEnds = Time.unscaledTime + (_cutsceneTitle == null ? 0
                : 2 * CampaignCutscene.PauseMilliseconds / 1000f + TitleHoldWithoutSpeech);
            _cutsceneMovie = -1;
        }

        /// <summary>Runs the card and then each movie in turn; returns false once the front end may run.</summary>
        private bool UpdateCutscene()
        {
            if (_cutscene == null) return false;
            if (Time.unscaledTime < _cutsceneTitleEnds || _cutscenePlayer != null) return true;
            if (++_cutsceneMovie < _cutscene.Movies.Count)
            {
                _cutscenePlayer = ClassicMoviePlayer.Play(_cutscene.Movies[_cutsceneMovie], _cutsceneInstall);
                _cutscenePlayer.Finished += () => _cutscenePlayer = null;
                return true;
            }
            _cutscene = null;
            // The front end's clock starts when it first runs, as DK2's init returns only after the movie.
            _frontEndStarted = Time.realtimeSinceStartupAsDouble;
            return false;
        }

        private void DrawCutsceneTitle()
        {
            if (_cutscene == null || _cutscenePlayer != null || Time.unscaledTime >= _cutsceneTitleEnds) return;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (_cutsceneTitle == null) return;
            // Remake placeholder face and size: DK2 draws the title centred with its own font.
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Screen.height / 16 };
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), _cutsceneTitle, style);
        }
    }
}

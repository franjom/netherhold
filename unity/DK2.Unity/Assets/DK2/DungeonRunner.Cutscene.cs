// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.IO;
using DK2.Data.Text;
using DK2.Data.Media;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>The won-level title card and cutscene that run before the front end (0x0053B980).</summary>
    public sealed partial class DungeonRunner
    {
        private int _cutsceneTitlePhase;
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
            _cutsceneTitlePhase = _cutsceneTitle == null ? 3 : 0;
            _cutsceneTitleEnds = Time.unscaledTime + CampaignCutscene.PauseMilliseconds / 1000f;
            _cutsceneMovie = -1;
        }

        /// <summary>Runs the card and then each movie in turn; returns false once the front end may run.</summary>
        private bool UpdateCutscene()
        {
            if (_cutscene == null) return false;
            if (_cutsceneTitlePhase < 3)
            {
                if (Time.unscaledTime < _cutsceneTitleEnds) return true;
                if (_cutsceneTitlePhase == 0)
                {
                    _soundPlayer.QueueSpeech(SoundCategory.Mentor, new SoundId(_cutscene.TitleTextId + 1), clearWaiting: true);
                    _cutsceneTitlePhase = 1;
                    return true;
                }
                if (_cutsceneTitlePhase == 1)
                {
                    if (_soundPlayer.SpeechBusy) return true;
                    _cutsceneTitlePhase = 2;
                    _cutsceneTitleEnds = Time.unscaledTime + CampaignCutscene.PauseMilliseconds / 1000f;
                    return true;
                }
                _cutsceneTitlePhase = 3;
            }
            if (_cutscenePlayer != null) return true;
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
            if (_cutscene == null || _cutscenePlayer != null || _cutsceneTitlePhase >= 3) return;
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

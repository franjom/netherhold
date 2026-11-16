// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>The won-level title card and cutscene that run before the front end (0x0053B980), played by
    /// <see cref="CampaignCutsceneRunner"/>; this side is the speech queue, the movie player and the drawing.</summary>
    public sealed partial class DungeonRunner : ICampaignCutsceneHost
    {
        private CampaignCutsceneRunner _cutscenes;
        private ClassicMoviePlayer _cutscenePlayer;

        private void BeginCutscene(string install)
        {
            _cutscenes ??= new CampaignCutsceneRunner(this);
            _cutscenes.Begin(_profiles.Profile, install, id => CampaignCutsceneRunner.SpeechText(install, id),
                Time.unscaledTime);
        }

        /// <summary>Runs the card and then each movie in turn; returns false once the front end may run.</summary>
        private bool UpdateCutscene()
        {
            if (_cutscenes == null || !_cutscenes.Playing) return false;
            if (_cutscenes.Update(Time.unscaledTime)) return true;
            // The front end's clock starts when it first runs, as DK2's init returns only after the movie.
            _frontEndStarted = Time.realtimeSinceStartupAsDouble;
            return false;
        }

        bool ICampaignCutsceneHost.SpeechBusy => _soundPlayer.SpeechBusy;

        void ICampaignCutsceneHost.QueueSpeech(SoundId speech) =>
            _soundPlayer.QueueSpeech(SoundCategory.Mentor, speech, clearWaiting: true);

        bool ICampaignCutsceneHost.MoviePlaying => _cutscenePlayer != null;

        void ICampaignCutsceneHost.PlayMovie(string relativePath, string install)
        {
            _cutscenePlayer = ClassicMoviePlayer.Play(relativePath, install);
            _cutscenePlayer.Finished += () => _cutscenePlayer = null;
        }

        private void DrawCutsceneTitle()
        {
            // Black from the card until a movie's first picture is up, and between movies: the movie
            // decodes for a few frames, and nothing else hides the front end beneath.
            if (_cutscenes == null || !_cutscenes.Blackout(_cutscenePlayer != null && _cutscenePlayer.HasFrame)) return;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (_cutscenes.VisibleTitle is not string title) return;
            // DK2 draws the title centred in its own font; ours takes the subtitle font chain, about twice
            // the subtitle size (the remake's size, unread).
            DrawSubtitleText(SubtitleLayout.TitleBox(Screen.width, Screen.height), title, SubtitleLayout.TitleScale);
        }
    }
}

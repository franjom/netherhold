// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner
    {
        [SerializeField] private bool skipStartup;
        private static bool _startupShown;
        private FrontEndStartup _frontEndStartup;
        private readonly FrontEndAttract _attract = new FrontEndAttract();
        private double _startupStarted;
        private Texture2D _startupTitle;
        private ClassicMoviePlayer _frontEndMovie;
        private string _startupInstall;
        private Vector3 _lastFrontEndPointer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStartupRun() => _startupShown = false;

        private void BeginStartup(string install, IClassicFrontEndMenuAssets assets)
        {
            _startupInstall = install;
            _lastFrontEndPointer = Input.mousePosition;
            if (!skipStartup && !_startupShown && frontEndReturnPage == FrontEndPage.None)
            {
                _frontEndStartup = new FrontEndStartup();
                _startupStarted = Time.realtimeSinceStartupAsDouble;
                _startupTitle = assets.Image("TitleScreen/TitleScreen");
            }
            _startupShown = true;
        }

        private bool UpdateStartup()
        {
            if (_frontEndStartup == null) return false;
            FrontEndStartupStage stage = _frontEndStartup.Update(TimeSpan.FromSeconds(Time.realtimeSinceStartupAsDouble - _startupStarted));
            if (stage == FrontEndStartupStage.Done)
            {
                _frontEndStartup = null;
                _frontEndStarted = Time.realtimeSinceStartupAsDouble;
                return false;
            }
            if (stage != FrontEndStartupStage.Title && _frontEndMovie == null)
            {
                _frontEndMovie = ClassicMoviePlayer.Play(stage == FrontEndStartupStage.BullfrogIntro
                    ? ClassicMoviePlayer.BullfrogIntro : "Data/Movies/Intro.tgq", _startupInstall);
                _frontEndMovie.Finished += () => { _frontEndMovie = null; _frontEndStartup?.MovieFinished(); };
            }
            return true;
        }

        private bool UpdateAttract()
        {
            if (_frontEndMovie != null) return true;
            TimeSpan now = TimeSpan.FromSeconds(Math.Max(0, Time.realtimeSinceStartupAsDouble - _frontEndStarted));
            Vector3 pointer = Input.mousePosition;
            FrontEndMovie? movie = _attract.Update(now, _frontEnd.Page, pointer != _lastFrontEndPointer, Input.anyKeyDown);
            _lastFrontEndPointer = pointer;
            if (!movie.HasValue) return false;
            PlayFrontEndMovie(movie == FrontEndMovie.BullfrogIntro ? ClassicMoviePlayer.BullfrogIntro : "Data/Movies/Intro.tgq");
            return true;
        }

        private void PlayFrontEndMovie(string path)
        {
            if (_frontEndMovie != null) return;
            _frontEndMenu.SetVisible(false);
            _frontEndMovie = ClassicMoviePlayer.Play(path, _startupInstall);
            _frontEndMovie.Finished += () =>
            {
                _frontEndMovie = null;
                _attract.Reset(TimeSpan.FromSeconds(Math.Max(0, Time.realtimeSinceStartupAsDouble - _frontEndStarted)));
                _frontEndMenu?.SetVisible(true);
            };
        }

        private void DrawStartupTitle()
        {
            if (_frontEndStartup?.Stage != FrontEndStartupStage.Title || _startupTitle == null) return;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float scale = Mathf.Min(Screen.width / 640f, Screen.height / 480f);
            GUI.DrawTexture(new Rect((Screen.width - 640 * scale) / 2, (Screen.height - 480 * scale) / 2,
                640 * scale, 480 * scale), _startupTitle);
        }
    }
}

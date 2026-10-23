// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace DK2.Unity.Editor
{
    /// <summary>
    /// DK2/Movies/Play Bullfrog Intro. In Play Mode it plays the movie over the running scene. In Edit
    /// Mode it offers to save modified scenes, switches to an unsaved empty scene, enters Play Mode,
    /// plays the movie, leaves Play Mode when it ends, and reopens the scene that was open. No scene
    /// or asset is written.
    /// </summary>
    [InitializeOnLoad]
    public static class ClassicMovieMenu
    {
        private const string PendingKey = "DK2.Movie.Pending";
        private const string ReturnSceneKey = "DK2.Movie.ReturnScene";

        static ClassicMovieMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("DK2/Movies/Play Bullfrog Intro")]
        private static void PlayBullfrogIntro()
        {
            if (EditorApplication.isPlaying)
            {
                ClassicMoviePlayer.Play(ClassicMoviePlayer.BullfrogIntro);
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            SessionState.SetString(ReturnSceneKey, SceneManager.GetActiveScene().path);
            SessionState.SetBool(PendingKey, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                SessionState.EraseBool(PendingKey);
                ClassicMoviePlayer player = ClassicMoviePlayer.Play(ClassicMoviePlayer.BullfrogIntro);
                player.Finished += EditorApplication.ExitPlaymode;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseBool(PendingKey);
                string scene = SessionState.GetString(ReturnSceneKey, "");
                SessionState.EraseString(ReturnSceneKey);
                if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            }
        }
    }
}

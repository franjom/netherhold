// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using UnityEditor;
using UnityEngine;

namespace DK2.Unity.Editor
{
    public static class FrontEndPreview
    {
        private static DungeonRunner Runner => Object.FindAnyObjectByType<DungeonRunner>();

        [MenuItem("DK2/Front End/Open Front End")]
        public static void Open() => Runner.LoadLevel(GameSessionRequest.FrontEnd());
        [MenuItem("DK2/Front End/Open Front End", true)]
        public static bool CanOpen() => Application.isPlaying && Runner != null;

        [MenuItem("DK2/Front End/Preview/Options")]
        public static void Options() => Move(FrontEndPath.EntryToOptions, FrontEndState.OptionsReady);
        [MenuItem("DK2/Front End/Preview/Extras")]
        public static void Extras() => Move(FrontEndPath.EntryToExtras, FrontEndState.ExtrasReady);
        [MenuItem("DK2/Front End/Preview/Campaign Table")]
        public static void Campaign() => Move(FrontEndPath.EntryToTable, FrontEndState.CampaignMap);
        [MenuItem("DK2/Front End/Preview/Pet Dungeon")]
        public static void PetDungeon() => Move(FrontEndPath.EntryToPetDungeon, FrontEndState.PetDungeonReady);
        [MenuItem("DK2/Front End/Preview/Multiplayer")]
        public static void Multiplayer() => Move(FrontEndPath.EntryToMultiplayer, FrontEndState.MultiplayerReady);
        [MenuItem("DK2/Front End/Preview/Options", true)]
        [MenuItem("DK2/Front End/Preview/Extras", true)]
        [MenuItem("DK2/Front End/Preview/Campaign Table", true)]
        [MenuItem("DK2/Front End/Preview/Pet Dungeon", true)]
        [MenuItem("DK2/Front End/Preview/Multiplayer", true)]
        public static bool CanMove() => Application.isPlaying && Runner != null && Runner.FrontEnd != null && !Runner.FrontEnd.PathPlaying;

        private static void Move(FrontEndPath path, FrontEndState state)
        {
            // Each preview begins at Entry so the authored path's starting pose is correct.
            DungeonRunner next = Runner.LoadLevel(GameSessionRequest.FrontEnd());
            double deadline = EditorApplication.timeSinceStartup + 120;
            void WhenReady()
            {
                if (!Application.isPlaying || next == null || EditorApplication.timeSinceStartup > deadline)
                {
                    EditorApplication.update -= WhenReady;
                    return;
                }
                if (next.FrontEnd == null || next.FrontEnd.PathPlaying) return;
                EditorApplication.update -= WhenReady;
                next.FrontEnd.TryTransition(new FrontEndTransition(path, state));
            }
            EditorApplication.update += WhenReady;
        }
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Save;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DK2.Unity.Editor
{
    /// <summary>Editor-only helpers: builds the playable scene, and runs the whole
    /// presentation pipeline headlessly so a command can answer "it works in
    /// Unity". Compiling the adapter proves only that the C# is valid; the
    /// headless run is what proves <c>GameDatabaseLoader</c> can read a DK2
    /// install from inside Unity's Mono runtime, that the netstandard2.1
    /// assemblies load there, and that the presenter produces GameObjects — none
    /// of which can fail at compile time.</summary>
    public static class DungeonSetup
    {
        private const string ScenePath = "Assets/DK2/Scenes/Dungeon.unity";

        /// <summary>
        /// Creates the scene the runner lives in: a camera and one GameObject
        /// carrying <see cref="DungeonRunner"/>. Committed, so nobody has to
        /// assemble it by hand.
        /// </summary>
        [MenuItem("DK2/Create dungeon scene")]
        public static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
            camera.farClipPlane = 500f;

            // A single directional light, so the boxes have shaded faces and the
            // dungeon reads as geometry rather than as flat colour.
            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var runnerObject = new GameObject("Dungeon");
            runnerObject.AddComponent<DungeonRunner>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"DK2: wrote {ScenePath}");
        }

        /// <summary>Runs the whole pipeline once, inside Unity, and exits non-zero
        /// if any part of it fails. Skips cleanly when there is no DK2 install,
        /// like the install-gated tests. Invoked from the command line:
        /// <code>
        /// Unity.exe -batchmode -quit -nographics -projectPath unity/DK2.Unity \
        ///           -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest
        /// </code>
        /// </summary>
        public static void SmokeTest()
        {
            try
            {
                string install = Dk2Install.Find();
                if (install == null)
                {
                    Debug.Log("DK2 smoke test: skipped, no Dungeon Keeper 2 installation found.");
                    EditorApplication.Exit(0);
                    return;
                }

                GameDatabase database = GameDatabaseLoader.Load(install);
                LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level1")
                    ?? throw new InvalidOperationException("the install has no Level1 map.");

                byte[] players = map.Tiles.Select(t => t.PlayerId).Distinct().OrderBy(p => p).ToArray();
                WorldState world = WorldState.FromMap(database, map, players);
                var simulation = new GameSimulation(database, world, 1UL, SimulationSystems.Default());

                var root = new GameObject("DK2 smoke test");
                var assets = AssetProviderChain.Standard();
                var scene = new DungeonScene(root.transform, world.Width, world.Height);
                var presenter = new ScenePresenter(new WorldView(database, 1), assets, scene);

                presenter.Present(world);

                int afterFirstPresent = scene.TileObjects;
                ulong hashBefore = simulation.StateHash();

                for (int i = 0; i < 20; i++) simulation.Tick();
                presenter.Present(simulation.World);

                Debug.Log(
                    $"DK2 smoke test: {world.Width}x{world.Height} level, " +
                    $"{afterFirstPresent} tile objects, {scene.CreatureObjects} creatures, " +
                    $"art: {assets.Summarise()}, " +
                    $"hash {hashBefore:x16} -> {simulation.StateHash():x16}");

                // The presenter is supposed to build an object per tile. Anything
                // less means assets failed to resolve, which is the failure this
                // whole run exists to catch.
                if (afterFirstPresent != world.TileCount)
                    throw new InvalidOperationException(
                        $"expected {world.TileCount} tile objects, got {afterFirstPresent}.");

                Debug.Log("DK2 smoke test: OK");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"DK2 smoke test FAILED: {ex}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>The same run, but starting from a DK2 saved game instead of a
        /// campaign level. Asserts the read's alignment measure as well as the
        /// object count: a misread grid does not throw, it produces a plausible
        /// dungeon of noise, and a correct read of a played save leaves most of the
        /// terrain matching the level's design map where a wrong one leaves almost
        /// none. Skips cleanly with no install and with no saves — a fresh copy of
        /// DK2 has none.
        /// <code>
        /// Unity.exe -batchmode -quit -nographics -projectPath unity/DK2.Unity \
        ///           -executeMethod DK2.Unity.Editor.DungeonSetup.SaveSmokeTest
        /// </code>
        /// </summary>
        public static void SaveSmokeTest()
        {
            try
            {
                string install = Dk2Install.Find();
                if (install == null)
                {
                    Debug.Log("DK2 save smoke test: skipped, no Dungeon Keeper 2 installation found.");
                    EditorApplication.Exit(0);
                    return;
                }

                IReadOnlyList<string> saves = Dk2SaveReader.EnumerateSaves(install);
                if (saves.Count == 0)
                {
                    Debug.Log("DK2 save smoke test: skipped, the installation has no saved games.");
                    EditorApplication.Exit(0);
                    return;
                }

                GameDatabase database = GameDatabaseLoader.Load(install);

                foreach (string path in saves)
                {
                    SavedGame save = Dk2SaveReader.Load(install, path)
                        ?? throw new InvalidOperationException(
                            $"'{Path.GetFileName(path)}' names a level this install has no map for.");

                    LevelMap map = save.Map;
                    WorldState world = WorldState.FromMap(database, map, save.PlayerIds);

                    var root = new GameObject($"DK2 save smoke test: {Path.GetFileName(path)}");
                    var assets = AssetProviderChain.Standard();
                    var scene = new DungeonScene(root.transform, world.Width, world.Height);
                    var presenter = new ScenePresenter(new WorldView(database, 1), assets, scene);

                    presenter.Present(world);

                    Debug.Log(
                        $"DK2 save smoke test: {Path.GetFileName(path)} -> " +
                        $"level '{save.Header.LevelName}', {world.Width}x{world.Height}, " +
                        $"{scene.TileObjects} tile objects, " +
                        $"{save.TerrainAgreement:P1} of terrain unchanged from the design map, " +
                        $"owners [{string.Join(",", save.PlayerIds)}], " +
                        $"art: {assets.Summarise()}");

                    if (scene.TileObjects != world.TileCount)
                        throw new InvalidOperationException(
                            $"expected {world.TileCount} tile objects, got {scene.TileObjects}.");

                    // The check that separates a real dungeon from noise. See
                    // Dk2SaveReader.AgreementWithDesign for why half is the line.
                    if (save.TerrainAgreement <= 0.5)
                        throw new InvalidOperationException(
                            $"terrain agreed with the design map on only {save.TerrainAgreement:P1} " +
                            "of tiles — the grid was read at the wrong offset or stride.");

                    UnityEngine.Object.DestroyImmediate(root);
                }

                Debug.Log($"DK2 save smoke test: OK ({saves.Count} save(s))");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"DK2 save smoke test FAILED: {ex}");
                EditorApplication.Exit(1);
            }
        }
    }
}

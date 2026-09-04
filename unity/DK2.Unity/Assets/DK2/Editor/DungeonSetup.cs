// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Linq;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DK2.Unity.Editor
{
    /// <summary>
    /// Editor-only helpers: builds the playable scene, and runs the whole
    /// presentation pipeline headlessly so that "it works in Unity" is something
    /// a command can answer rather than something a person has to eyeball.
    ///
    /// <para><b>Why the headless check earns its place.</b> Compiling the adapter
    /// proves the C# is valid; it proves nothing about whether
    /// <c>GameDatabaseLoader</c> can read a DK2 install from inside Unity's Mono
    /// runtime, whether the netstandard2.1 assemblies actually load there, or
    /// whether the presenter produces GameObjects. Those are exactly the things
    /// that were never going to fail at compile time and were always going to
    /// fail on first Play. This runs them from the command line.</para>
    /// </summary>
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

        /// <summary>
        /// Runs the whole pipeline once, inside Unity, and exits non-zero if any
        /// part of it fails. Invoked from the command line:
        ///
        /// <code>
        /// Unity.exe -batchmode -quit -nographics -projectPath unity/DK2.Unity \
        ///           -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest
        /// </code>
        ///
        /// <para>Skips cleanly when there is no DK2 install, the same way the
        /// install-gated tests do — a machine without the game is not a
        /// failure.</para>
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
                var scene = new DungeonScene(root.transform, world.Width);
                var presenter = new ScenePresenter(new WorldView(database), assets, scene);

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
    }
}

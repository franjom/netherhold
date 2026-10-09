// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Art;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity.Editor
{
    /// <summary>The classic look's editor steps (ADR-0408, <c>docs/plans/2026-10-09-classic-look.md</c>):
    /// put the project on URP, and render step 0's pictures for the comparison with DK2.</summary>
    /// <remarks>Both run from the command line without a person, the second with graphics:
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath unity/DK2.Unity \
    ///           -executeMethod DK2.Unity.Editor.ClassicLookSetup.UseUrp
    /// Unity.exe -batchmode -quit -projectPath unity/DK2.Unity \
    ///           -executeMethod DK2.Unity.Editor.ClassicLookSetup.RenderStep0
    /// </code>
    /// URP's types are reached by name, so the adapter compiles without the URP package
    /// (<c>DK2.Unity.Compile</c> builds against the engine's modules only).</remarks>
    public static class ClassicLookSetup
    {
        private const string RenderingFolder = "Assets/DK2/Rendering";
        private const string PipelineAssetPath = RenderingFolder + "/DK2-URP.asset";
        private const string RendererAssetPath = RenderingFolder + "/DK2-URP-Renderer.asset";
        private const string UrpAssembly = "Unity.RenderPipelines.Universal.Runtime";
        private const string OutputFolder = "../../build/classic-look";

        [MenuItem("DK2/Classic look/Use the Universal Render Pipeline")]
        public static void UseUrp()
        {
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelineAssetPath);
                if (asset == null)
                {
                    Type assetType = Type.GetType($"UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, {UrpAssembly}")
                        ?? throw new InvalidOperationException("URP is not installed (Packages/manifest.json).");
                    Type rendererType = Type.GetType($"UnityEngine.Rendering.Universal.UniversalRendererData, {UrpAssembly}")
                        ?? throw new InvalidOperationException("URP has no UniversalRendererData.");
                    MethodInfo create = assetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .First(method => method.Name == "Create" && method.GetParameters().Length == 1);

                    if (!AssetDatabase.IsValidFolder(RenderingFolder))
                        AssetDatabase.CreateFolder("Assets/DK2", "Rendering");
                    // The renderer must be an asset of its own before the pipeline names it: given
                    // none, Create makes one in memory only, and URP then has no default renderer.
                    ScriptableObject renderer = ScriptableObject.CreateInstance(rendererType);
                    AssetDatabase.CreateAsset(renderer, RendererAssetPath);
                    asset = (RenderPipelineAsset)create.Invoke(null, new object[] { renderer });
                    AssetDatabase.CreateAsset(asset, PipelineAssetPath);
                }

                GraphicsSettings.defaultRenderPipeline = asset;
                int current = QualitySettings.GetQualityLevel();
                for (int level = 0; level < QualitySettings.names.Length; level++)
                {
                    QualitySettings.SetQualityLevel(level, applyExpensiveChanges: false);
                    QualitySettings.renderPipeline = asset;
                }
                QualitySettings.SetQualityLevel(current, applyExpensiveChanges: false);
                AssetDatabase.SaveAssets();

                Debug.Log($"DK2: the project renders through {asset.name} ({asset.GetType().Name}).");
                ExitIfBatch(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"DK2: could not switch to URP: {ex}");
                ExitIfBatch(1);
            }
        }

        /// <summary>
        /// Step 0's pictures: the pieces close up, and Level 1 from the runner's opening camera
        /// with its objects drawn from the install. Written to <c>build/classic-look/</c>.
        /// </summary>
        [MenuItem("DK2/Classic look/Render the step 0 pictures")]
        public static void RenderStep0()
        {
            // -executeMethod runs before the editor's first render loop has made URP's pipeline,
            // and a camera rendered then draws nothing; so wait for it, a bounded number of frames.
            _framesWaited = 0;
            EditorApplication.update += RenderWhenThePipelineExists;
        }

        private const int MaxFramesToWait = 600;
        private static int _framesWaited;

        private static void RenderWhenThePipelineExists()
        {
            if (RenderPipelineManager.currentPipeline == null && ++_framesWaited < MaxFramesToWait)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            EditorApplication.update -= RenderWhenThePipelineExists;
            Debug.Log($"DK2 step 0: waited {_framesWaited} editor frames for the render pipeline.");
            RenderNow();
        }

        private static void RenderNow()
        {
            // The editor draws a placeholder while a shader variant compiles in the background, and
            // a one-shot render would capture the placeholder.
            ShaderUtil.allowAsyncCompilation = false;
            PropertyInfo batcher = GraphicsSettings.currentRenderPipeline?.GetType().GetProperty("useSRPBatcher");
            if (batcher != null && batcher.CanWrite)
            {
                batcher.SetValue(GraphicsSettings.currentRenderPipeline, false);
                Debug.Log("DK2 step 0: SRP Batcher off for this render.");
            }
            GameObject root = null;
            try
            {
                string install = Dk2Install.Find()
                    ?? throw new InvalidOperationException("no Dungeon Keeper 2 installation found.");
                Directory.CreateDirectory(OutputFolder);

                root = new GameObject("DK2 classic look, step 0");
                GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
                using (InstallArtProvider art = InstallArtProvider.Open(install, database))
                {
                    var classic = new ClassicMeshes(art);
                    RenderPieces(art, classic, root.transform);
                    UnityEngine.Object.DestroyImmediate(root);

                    root = new GameObject("DK2 classic look, Level 1");
                    RenderLevel(install, database, art, classic, root.transform);
                }

                Debug.Log($"DK2: step 0 pictures written to {Path.GetFullPath(OutputFolder)}");
                ExitIfBatch(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"DK2: step 0 render FAILED: {ex}");
                ExitIfBatch(1);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>A gold pile, the rock wall's side, and the Lair's five wall pieces in a row along
        /// the north edges of seven tiles, seen from the south: LAIR4 and LAIR5 are the two ends, so
        /// a mirror swaps them.</summary>
        private static void RenderPieces(InstallArtProvider art, ClassicMeshes classic, Transform root)
        {
            AssetRef[] keys =
            {
                AssetRef.ForObject("Gold", AssetKind.Mesh),
                AssetRef.ForTerrain("Rock", AssetKind.Mesh),
                AssetCatalogue.WallPiece("Lair", 4),
                AssetCatalogue.WallPiece("Lair", 5),
                AssetCatalogue.WallPiece("Lair", 6),
                AssetCatalogue.WallPiece("Lair", 7),
                AssetCatalogue.WallPiece("Lair", 8),
            };
            for (int i = 0; i < keys.Length; i++)
            {
                if (!art.TryResolve(keys[i], out ResolvedAsset asset))
                    throw new InvalidOperationException($"'{keys[i]}' did not resolve in the install.");
                GameObject piece = classic.TryCreate(asset, keys[i].Key, root)
                    ?? throw new InvalidOperationException($"'{keys[i]}' did not build.");
                piece.transform.position = DungeonScene.Ground(i * 1.5f + 0.5f, 0.5f);
                foreach (Material material in piece.GetComponent<MeshRenderer>().sharedMaterials)
                {
                    Texture texture = material.mainTexture;
                    Debug.Log($"DK2 step 0: {keys[i]} material '{material.name}' shader {material.shader.name}, " +
                              (texture == null ? "no texture" : $"texture {texture.width}x{texture.height}") +
                              $", base map {(material.HasProperty("_BaseMap") ? (material.GetTexture("_BaseMap") == null ? "empty" : "set") : "absent")}");
                }
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(root, false);
            floor.transform.position = DungeonScene.Ground(5f, 1f);
            floor.transform.localScale = new Vector3(1.2f, 1f, 0.3f);

            Camera camera = MakeCamera(root, 40f);
            camera.transform.position = DungeonScene.Ground(5f, 9f, 3.5f);
            camera.transform.LookAt(DungeonScene.Ground(5f, 0.5f, 1f));
            AddLight(root);
            Save(camera, "step0-pieces.png");
        }

        private static void RenderLevel(string install, GameDatabase database, InstallArtProvider art,
            ClassicMeshes classic, Transform root)
        {
            LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level1")
                ?? throw new InvalidOperationException("the install has no Level1 map.");
            byte[] players = map.Tiles.Select(t => t.PlayerId).Distinct().OrderBy(p => p).ToArray();
            WorldState world = WorldState.FromMap(database, map, players);
            var things = GameDatabaseLoader.LoadCampaignThings(install, "Level1");
            if (things != null) ThingProjection.Place(world, database, things);

            AssetProviderChain assets = AssetProviderChain.Standard(install: art);
            var scene = new DungeonScene(root, world.Width, classic);
            var presenter = new ScenePresenter(new WorldView(database, (byte)PlayerIds.FirstKeeper), assets, scene,
                new TorchLayout(database));
            presenter.Present(world);
            Debug.Log($"DK2 step 0: Level 1, {scene.TileObjects} tiles, {scene.ObjectObjects} objects, {assets.Summarise()}");

            // The runner's opening camera (DungeonRunner.LookAt): pitch 55, facing north.
            Camera camera = MakeCamera(root, 60f);
            Vector3 centre = DungeonScene.Ground(world.Width / 2f, world.Height / 2f);
            float radius = 0.5f * Mathf.Sqrt(world.Width * (float)world.Width + world.Height * (float)world.Height);
            float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            camera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            camera.transform.position = centre - camera.transform.rotation * Vector3.forward * distance;
            camera.farClipPlane = distance * 3f;
            AddLight(root);
            Save(camera, "step0-level1.png");
        }

        private static Camera MakeCamera(Transform root, float fieldOfView)
        {
            var go = new GameObject("camera");
            go.transform.SetParent(root, false);
            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            return camera;
        }

        private static void AddLight(Transform root)
        {
            var go = new GameObject("light");
            go.transform.SetParent(root, false);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.4f);
        }

        private static void Save(Camera camera, string fileName)
        {
            const int width = 1600, height = 900;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.aspect = width / (float)height;
            // URP fulfils a render request into a texture; a bare Camera.Render() drew nothing here.
            var request = new RenderPipeline.StandardRequest { destination = target };
            bool requested = RenderPipeline.SupportsRenderRequest(camera, request);
            if (requested) RenderPipeline.SubmitRenderRequest(camera, request);
            else
            {
                camera.targetTexture = target;
                camera.Render();
            }
            Debug.Log($"DK2 step 0: {fileName} rendered by {(requested ? "a render request" : "Camera.Render")}; " +
                      $"pipeline asset {GraphicsSettings.currentRenderPipeline?.name ?? "none"}, " +
                      $"instance {RenderPipelineManager.currentPipeline?.GetType().Name ?? "none"}, " +
                      $"camera enabled {camera.isActiveAndEnabled}.");

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(Path.Combine(OutputFolder, fileName), image.EncodeToPNG());
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }

        /// <summary>Exits when started from the command line, batch or windowed, and not when run
        /// from the menu.</summary>
        private static void ExitIfBatch(int code)
        {
            if (Application.isBatchMode || Environment.GetCommandLineArgs().Contains("-executeMethod"))
                EditorApplication.Exit(code);
        }
    }
}

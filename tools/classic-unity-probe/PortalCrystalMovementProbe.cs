// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    public static class PortalCrystalMovementProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires the isolated movement probe project.");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                const string install = "E:/GogGames/Dungeon Keeper 2";
                var db = GameDatabaseLoader.Load(install);
                var art = InstallArtProvider.Open(install, db);
                var meshes = new ClassicMeshes(art);
                var root = new GameObject("portal movement fixture");
                var world = WorldState.FromMap(db,
                    new LevelMap(12, 12, Enumerable.Repeat(new MapTile(8, PlayerIds.FirstKeeper, 0, 0), 144).ToArray()),
                    Enumerable.Range(0, 8).Select(i => (byte)i));
                for (int y = 4; y <= 6; y++) for (int x = 4; x <= 6; x++)
                    RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), PlayerIds.FirstKeeper);
                var goblin = world.SpawnCreature(db, db.CreatureByName("Goblin").CreatureId, PlayerIds.FirstKeeper, new TilePos(6, 3));
                goblin.PosX = 6 * Fp32.One;
                var navigation = new WorldSteering(db, world);
                if (!navigation.TryRouteToStance(goblin, goblin.PosX, (int)(7.5 * Fp32.One), MoveOptions.ExactSpot))
                    throw new Exception("No portal route.");
                var simulation = new GameSimulation(db, world, 42, new ISimulationSystem[] { new CreatureMovementSystem() });
                var view = new WorldView(db, new PlayerId(PlayerIds.FirstKeeper));
                var scene = new DungeonScene(root.transform, world.Width, world.Height, meshes, creatureShadows: false);
                var presenter = new ScenePresenter(view, new AssetProviderChain(art, PlaceholderProvider.Instance), scene);
                var camera = new GameObject("probe camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                Vector3 focus = new Vector3(5.5f, .5f, -5.5f);
                camera.transform.position = focus + new Vector3(2.5f, 5, -3.75f); camera.transform.LookAt(focus);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                camera.allowHDR = false; camera.allowMSAA = false;
                var target = new RenderTexture(640, 480, 24); target.Create(); camera.targetTexture = target;
                var pixels = new Texture2D(640, 480, TextureFormat.RGBA32, false);
                string output = Environment.GetEnvironmentVariable("DK2_PORTAL_MOVEMENT_CAPTURE_OUTPUT")
                    ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../portal-movement-captures"));
                Directory.CreateDirectory(output);
                var csv = new StreamWriter(Path.Combine(output, "positions.csv")); csv.WriteLine("tick,x,y");
                int tick = 0;
                EditorApplication.CallbackFunction sample = null;
                sample = () =>
                {
                    try
                    {
                        if (tick != 0) simulation.Tick();
                        csv.WriteLine(tick + "," + goblin.PosX + "," + goblin.PosY);
                        presenter.Present(world);
                        if (tick % 5 == 0 || goblin.Body.PathFinished)
                        {
                            var transparent = root.GetComponentInChildren<TransparentTerrain>();
                            typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                                .Invoke(transparent, new object[] { default(ScriptableRenderContext), camera });
                            RenderPipeline.SubmitRenderRequest(camera,
                                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); pixels.Apply();
                            RenderTexture.active = null;
                            File.WriteAllBytes(Path.Combine(output, "portal-walk-" + tick + ".png"), pixels.EncodeToPNG());
                        }
                        if (!goblin.Body.PathFinished && ++tick <= 600) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                        if (!goblin.Body.PathFinished || goblin.Tile != new TilePos(6, 7)) throw new Exception("Goblin failed to finish its portal walk.");
                        Debug.Log("Portal movement preview completed after " + tick + " ticks.");
                        EditorApplication.update -= sample; csv.Dispose(); art.Dispose(); EditorApplication.Exit(0);
                    }
                    catch (Exception error) { EditorApplication.update -= sample; csv.Dispose(); Debug.LogException(error); EditorApplication.Exit(1); }
                };
                EditorApplication.update += sample; EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}

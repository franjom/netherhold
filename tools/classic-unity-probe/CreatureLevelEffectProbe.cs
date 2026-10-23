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
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    public static class CreatureLevelEffectProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires the isolated effect probe project.");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                const string install = "E:/GogGames/Dungeon Keeper 2";
                var db = GameDatabaseLoader.Load(install);
                var art = InstallArtProvider.Open(install, db);
                var meshes = new ClassicMeshes(art);
                var root = new GameObject("level up fixture");
                var map = new LevelMap(7, 7, Enumerable.Repeat(new MapTile(1, PlayerIds.Neutral, 0, 0), 49).ToArray());
                var world = WorldState.FromMap(db, map, Enumerable.Range(0, 8).Select(i => (byte)i));
                for (int y = 2; y <= 4; y++) for (int x = 2; x <= 4; x++)
                { world.Tile(x, y).TerrainId = 8; world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper; }
                var imp = world.SpawnCreature(db, db.CreatureByName("Imp").CreatureId, PlayerIds.FirstKeeper, new TilePos(3, 3));
                imp.Heading = 627;
                var view = new WorldView(db, new PlayerId(PlayerIds.FirstKeeper));
                var scene = new DungeonScene(root.transform, world.Width, world.Height, meshes, creatureShadows: false);
                new ScenePresenter(view, new AssetProviderChain(art, PlaceholderProvider.Instance), scene).Present(world);
                var camera = new GameObject("probe camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                Vector3 focus = new Vector3(3.5f, .3f, -3.5f);
                camera.transform.position = focus + new Vector3(1.5f, 3, -2.25f); camera.transform.LookAt(focus);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                camera.allowHDR = false; camera.allowMSAA = false;
                var target = new RenderTexture(640, 480, 24); target.Create(); camera.targetTexture = target;
                var pixels = new Texture2D(640, 480, TextureFormat.RGBA32, false);
                string output = Environment.GetEnvironmentVariable("DK2_LEVEL_UP_CAPTURE_OUTPUT")
                    ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../level-up-captures"));
                Directory.CreateDirectory(output);
                long milliseconds = 0;
                var effects = new ClassicConstructionEffects(root.transform, db,
                    new InstallEffectSpriteProvider(db, art.LoadTexture), 42,
                    floorHeight: point => view.GroundHeightAt(world, point), clock: () => new EffectTime(milliseconds), meshes: meshes);
                var sim = new GameSimulation(db, world, 42, new ISimulationSystem[] { new KeeperSpellSystem() });
                var events = new ListEventSink(); sim.ObserveWith(events);
                sim.Tick(InputFrame.Of(new GrantCreatureLevelCommand(PlayerIds.FirstKeeper, imp.Id)));
                var request = events.Events.Single(e => e.EffectRequest.HasValue).EffectRequest.Value;
                if (imp.Level != 2 || request.EffectId != new EffectId(339)) throw new Exception("Level-up effect missing.");
                effects.Model.Request(request);
                int step = 0, peak = 0;
                EditorApplication.CallbackFunction sample = null;
                sample = () =>
                {
                    try
                    {
                        milliseconds = step == 0 ? 0 : step * 50 + 1;
                        effects.Advance(); effects.Draw(); peak = Math.Max(peak, effects.Model.Particles.Count);
                        if (step % 5 == 0)
                        {
                            var transparent = root.GetComponentInChildren<TransparentTerrain>();
                            typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                                .Invoke(transparent, new object[] { default(ScriptableRenderContext), camera });
                            RenderPipeline.SubmitRenderRequest(camera,
                                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); pixels.Apply();
                            RenderTexture.active = null;
                            File.WriteAllBytes(Path.Combine(output, "level-up-" + step * 50 + ".png"), pixels.EncodeToPNG());
                            Debug.Log("Level-up sample " + milliseconds + "ms, particles " + effects.Model.Particles.Count);
                        }
                        if (++step <= 100) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                        if (peak == 0 || effects.Model.Particles.Count != 0) throw new Exception("Level-up particles missing or failed to expire.");
                        Debug.Log("Creature level effect verification passed; peak particles " + peak);
                        EditorApplication.update -= sample; effects.Dispose(); art.Dispose(); EditorApplication.Exit(0);
                    }
                    catch (Exception error) { EditorApplication.update -= sample; Debug.LogException(error); EditorApplication.Exit(1); }
                };
                EditorApplication.update += sample; EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}

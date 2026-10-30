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
    public static class ShotEffectsProbe
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
                var root = new GameObject("shot effects fixture");
                var map = new LevelMap(7, 7, Enumerable.Repeat(new MapTile(1, PlayerIds.Neutral, 0, 0), 49).ToArray());
                var world = WorldState.FromMap(db, map, Enumerable.Range(0, 8).Select(i => (byte)i));
                for (int y = 2; y <= 4; y++) for (int x = 2; x <= 4; x++)
                { world.Tile(x, y).TerrainId = 8; world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper; }
                var imp = world.SpawnCreature(db, db.CreatureByName("Goblin").CreatureId, PlayerIds.FirstKeeper, new TilePos(3, 3));
                imp.Heading = 627; imp.BurningTicks = 30;
                var view = new WorldView(db, new PlayerId(PlayerIds.FirstKeeper));
                var scene = new DungeonScene(root.transform, world.Width, world.Height, meshes, creatureShadows: false);
                var presenter = new ScenePresenter(view, new AssetProviderChain(art, PlaceholderProvider.Instance), scene);
                presenter.Present(world);
                var camera = new GameObject("probe camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                Vector3 focus = new Vector3(3.5f, .3f, -3.5f);
                camera.transform.position = focus + new Vector3(1.5f, 3, -2.25f); camera.transform.LookAt(focus);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                camera.allowHDR = false; camera.allowMSAA = false;
                var target = new RenderTexture(640, 480, 24); target.Create(); camera.targetTexture = target;
                var pixels = new Texture2D(640, 480, TextureFormat.RGBA32, false);
                string output = Environment.GetEnvironmentVariable("DK2_SHOT_EFFECTS_CAPTURE_OUTPUT")
                    ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../shot-effects-captures"));
                Directory.CreateDirectory(output);
                long milliseconds = 0;
                var effects = new ClassicConstructionEffects(root.transform, db,
                    new InstallEffectSpriteProvider(db, art.LoadTexture), 42,
                    floorHeight: point => view.GroundHeightAt(world, point), clock: () => new EffectTime(milliseconds), meshes: meshes,
                    creatureGroundHeight: id => world.CreatureById(id.Value) is CreatureState creature
                        ? view.CreatureEffectGround(world, creature) : (Fp32?)null);
                var sim = new GameSimulation(db, world, 42, new ISimulationSystem[] { new CreatureStatusSystem() });
                sim.ObserveWith(effects.Model);
                effects.Model.Request(new ViewEffectRequest(new EffectId(107), PlayerIds.FirstKeeper,
                    new FpPosition3(new Fp32(imp.PosX), new Fp32(imp.PosY), new Fp32(4096))));
                var lightning = new ClassicLightning(root.transform, art.LoadTexture, effects.Model, 42);
                effects.Model.Request(new ViewEffectRequest(new EffectId(4), PlayerIds.FirstKeeper,
                    new FpPosition3(new Fp32(imp.PosX + 7000), new Fp32(imp.PosY), new Fp32(10000))));
                var bodies = new[] { new ShotState { Id = 9001, ShotTypeId = 1, Health = 100, PosX = imp.PosX - 5000, PosY = imp.PosY },
                    new ShotState { Id = 9002, ShotTypeId = 7, Health = 100, PosX = imp.PosX + 5000, PosY = imp.PosY } };
                int step = 0, peak = 0; Color32[] burningOnly = null;
                EditorApplication.CallbackFunction sample = null;
                sample = () =>
                {
                    try
                    {
                        milliseconds = step <= 1 ? 0 : step * 50 + 1;
                        if (step % 5 == 0) sim.Tick();
                        presenter.Present(world); lightning.BeginFrame(camera);
                        scene.DrawCreatureFrame(world.CurrentTick, 1);
                        if (step == 1) scene.DrawHoverOverlay(new WorldHoverHit(WorldHoverKind.Creature, imp.Tile, imp.Id));
                        foreach (var body in bodies) body.Age = step / 5 + 1;
                        effects.CaptureShots(bodies, 1);
                        scene.SetEffectLights(effects.Advance()); effects.Draw();
                        lightning.DrawEffects(); lightning.EndFrame();
                        peak = Math.Max(peak, effects.Model.Particles.Count);
                        if (step == 0 || step == 1 || step == 20 || step == 50 || step == 100 || step == 140 || step == 180)
                        {
                            var transparent = root.GetComponentInChildren<TransparentTerrain>();
                            typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                                .Invoke(transparent, new object[] { default(ScriptableRenderContext), camera });
                            RenderPipeline.SubmitRenderRequest(camera,
                                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); pixels.Apply();
                            RenderTexture.active = null;
                            if (step == 0) burningOnly = pixels.GetPixels32();
                            if (step == 1)
                            {
                                Color32[] hovered = pixels.GetPixels32();
                                Vector3 centre = camera.WorldToScreenPoint(focus + Vector3.up * .5f);
                                int changed = 0;
                                for (int y = (int)centre.y - 30; y <= (int)centre.y + 30; y++)
                                    for (int x = (int)centre.x - 15; x <= (int)centre.x + 15; x++)
                                        if (!hovered[y * 640 + x].Equals(burningOnly[y * 640 + x])) changed++;
                                if (changed != 0) throw new Exception("Hover doubled the burning overlay: " + changed + " changed pixels.");
                                Debug.Log("Burning plus hover pixel equality verification passed.");
                            }
                            File.WriteAllBytes(Path.Combine(output, "shot-effects-" + step * 50 + ".png"), pixels.EncodeToPNG());
                            Debug.Log("Shot effects sample " + milliseconds + "ms, particles " + effects.Model.Particles.Count);
                        }
                        if (++step <= 180) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                        if (peak == 0) throw new Exception("Shot effect particles missing.");
                        Debug.Log("Shot effects verification passed; peak particles " + peak);
                        EditorApplication.update -= sample; lightning.Dispose(); effects.Dispose(); art.Dispose(); EditorApplication.Exit(0);
                    }
                    catch (Exception error) { EditorApplication.update -= sample; Debug.LogException(error); EditorApplication.Exit(1); }
                };
                EditorApplication.update += sample; EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}

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
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    public static class PortalEntranceProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires the isolated effect probe project.");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var db = GameDatabaseLoader.LoadForCampaignLevel("E:/GogGames/Dungeon Keeper 2", "Level8");
                var art = InstallArtProvider.Open("E:/GogGames/Dungeon Keeper 2", db);
                var meshes = new ClassicMeshes(art);
                var root = new GameObject("portal fixture");
                var map = GameDatabaseLoader.LoadCampaignMap("E:/GogGames/Dungeon Keeper 2", "Level8");
                var world = WorldState.FromMap(db, map, Enumerable.Range(0, 8).Select(i => (byte)i));
                var portal = RoomIndex.Build(world, db).First(room => room.Definition.TypeId == RoomTypeId.Portal);
                var at = new TilePos((portal.MinX + portal.MaxX) / 2, (portal.MinY + portal.MaxY) / 2);
                var view = new WorldView(db, new PlayerId(PlayerIds.FirstKeeper));
                var scene = new DungeonScene(root.transform, world.Width, world.Height, meshes, creatureShadows: false);
                new ScenePresenter(view, new AssetProviderChain(art, PlaceholderProvider.Instance), scene).Present(world);
                var camera = new GameObject("probe camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                Vector3 focus = new Vector3(at.X + .5f, 1, -at.Y - .5f);
                camera.transform.position = focus + new Vector3(3, 6, -4.5f);
                camera.transform.LookAt(focus);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                camera.allowHDR = false; camera.allowMSAA = false;
                var target = new RenderTexture(640, 480, 24); target.Create(); camera.targetTexture = target;
                var pixels = new Texture2D(640, 480, TextureFormat.RGBA32, false);
                var output = Environment.GetEnvironmentVariable("DK2_PORTAL_CAPTURE_OUTPUT")
                    ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../portal-captures"));
                Directory.CreateDirectory(output);
                long milliseconds = 0;
                ushort effectId = ushort.TryParse(Environment.GetEnvironmentVariable("DK2_PORTAL_PROBE_EFFECT"), out ushort selected)
                    ? selected : (ushort)181;
                byte owner = byte.TryParse(Environment.GetEnvironmentVariable("DK2_PORTAL_PROBE_OWNER"), out byte selectedOwner)
                    ? selectedOwner : PlayerIds.FirstKeeper;
                if (effectId == 94) camera.fieldOfView = 30;
                Color32[] baseline = null;
                int maximumBuildupPixels = 0;
                var effects = new ClassicConstructionEffects(root.transform, db,
                    new InstallEffectSpriteProvider(db, art.LoadTexture), 42,
                    floorHeight: point => view.GroundHeightAt(world, point),
                    clock: () => new EffectTime(milliseconds), meshes: meshes);
                effects.Model.Request(ViewEffectRequest.AtTileCentre(new EffectId(effectId),
                    new PlayerId(owner), at, new Fp32(0)));
                int step = 0;
                EditorApplication.CallbackFunction sample = null;
                sample = () =>
                {
                    try
                    {
                        milliseconds = step == 0 ? 0 : step * 50 + 1;
                        effects.Advance(); effects.Draw();
                        if (step % 20 == 0)
                        {
                            var transparent = root.GetComponentInChildren<TransparentTerrain>();
                            typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                                .Invoke(transparent, new object[] { default(ScriptableRenderContext), camera });
                            if (Environment.GetEnvironmentVariable("DK2_PORTAL_EFFECT_ONLY") == "1")
                                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                                    renderer.enabled = renderer.sharedMaterial.shader.name == "DK2/ClassicEffect";
                            RenderPipeline.SubmitRenderRequest(camera,
                                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); pixels.Apply();
                            RenderTexture.active = null;
                            if (effectId == 94)
                            {
                                Color32[] current = pixels.GetPixels32();
                                if (baseline == null) baseline = current;
                                int brightened = 0;
                                for (int y = 140; y < 340; y++)
                                    for (int x = 240; x < 400; x++)
                                    {
                                        int pixel = y * 640 + x;
                                        if (current[pixel].r > baseline[pixel].r + 50
                                            && current[pixel].r > 200 && current[pixel].g > 60) brightened++;
                                    }
                                maximumBuildupPixels = Math.Max(maximumBuildupPixels, brightened);
                                Debug.Log("Visible buildup pixels " + brightened);
                            }
                            string prefix = effectId == 181 && owner == PlayerIds.FirstKeeper
                                ? "portal-" : "portal-" + effectId + "-owner" + owner + "-";
                            File.WriteAllBytes(Path.Combine(output, prefix + step * 50 + ".png"), pixels.EncodeToPNG());
                            Debug.Log("Portal sample " + milliseconds + "ms, particles " + effects.Model.Particles.Count);
                            if (effectId == 94 && effects.Model.Particles.Count != 0)
                                Debug.Log("Buildup height " + effects.Model.Particles.Min(p => p.HeightRaw)
                                    + ".." + effects.Model.Particles.Max(p => p.HeightRaw)
                                    + " scale " + effects.Model.Particles.Max(p => p.ScaleRaw));
                        }
                        if (++step <= 150) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                        if (effects.Model.Particles.Count != 0) throw new Exception("Portal arrival failed to expire.");
                        if (effectId == 94 && maximumBuildupPixels < 300)
                            throw new Exception("Portal buildup is hidden: only " + maximumBuildupPixels + " visible pixels.");
                        effects.Model.Request(ViewEffectRequest.AtTileCentre(new EffectId(159),
                            new PlayerId(PlayerIds.FirstKeeper), at, new Fp32(0)));
                        effects.Model.UpdateNow(); effects.Advance(); effects.Draw();
                        var groups = (System.Collections.IDictionary)typeof(ClassicConstructionEffects)
                            .GetField("_groups", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(effects);
                        bool beam = false;
                        foreach (object group in groups.Values)
                            if (group != null && group.GetType().GetField("Frames").GetValue(group) is Mesh[] frames)
                            { Debug.Log("Animated effect frames " + frames.Length); beam |= frames.Length == 60; }
                        if (!beam) throw new Exception("Portal's sixty-frame Lightbeams mesh was not rendered.");
                        Debug.Log("Portal entrance verification passed.");
                        EditorApplication.update -= sample;
                        effects.Dispose(); art.Dispose();
                        EditorApplication.Exit(0);
                    }
                    catch (Exception error) { EditorApplication.update -= sample; Debug.LogException(error); EditorApplication.Exit(1); }
                };
                EditorApplication.update += sample;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}

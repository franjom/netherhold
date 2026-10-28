// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace DK2.Unity.Editor
{
    public static class PortalPerformanceProbe
    {
        private static readonly string[] Modes = { "baseline", "full", "no-light", "no-geometry", "frozen", "full-repeat", "baseline-after" };
        private static readonly string[] Markers = { "DK2.TransparentTerrain.Sort", "DK2.TransparentTerrain.DepthOrder", "DK2.TransparentTerrain.UploadRuns" };
        private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires the isolated probe project.");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                const string install = "E:/GogGames/Dungeon Keeper 2";
                var db = GameDatabaseLoader.LoadForCampaignLevel(install, "level2");
                var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "level2"), new byte[] { 0, 1, 2, 3 }, fogOfWarEnabled: false);
                var portal = RoomIndex.Build(world, db).First(r => r.Definition.TypeId == RoomTypeId.Portal);
                var at = new TilePos((portal.MinX + portal.MaxX) / 2, (portal.MinY + portal.MaxY) / 2);
                var art = InstallArtProvider.Open(install, db);
                var meshes = new ClassicMeshes(art, classicVertexLighting: true, nativeInitialAmbient: true);
                var root = new GameObject("arrival performance fixture");
                var view = new WorldView(db, new PlayerId(3));
                var lattice = new TerrainLattice(db, world, new PlayerId(3));
                var scene = new DungeonScene(root.transform, world.Width, world.Height, meshes, lattice, creatureShadows: false);
                new ScenePresenter(view, new AssetProviderChain(art, PlaceholderProvider.Instance), scene).Present(world);
                var camera = new GameObject("arrival performance camera").AddComponent<Camera>();
                camera.tag = "MainCamera"; camera.allowHDR = false; camera.allowMSAA = false;
                camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor;
                Vector3 focus = new Vector3(at.X + .5f, 1, -at.Y - .5f);
                camera.transform.position = focus + new Vector3(1, .5f, -1.7f); camera.transform.LookAt(focus);
                camera.fieldOfView = 45;
                var target = new RenderTexture(1920, 1080, 24); target.Create(); camera.targetTexture = target;
                var transparent = root.GetComponentInChildren<TransparentTerrain>();
                var beforeCamera = (Action<ScriptableRenderContext, Camera>)Delegate.CreateDelegate(typeof(Action<ScriptableRenderContext, Camera>), transparent,
                    typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance));
                var recorders = Markers.Select(n => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, n)).ToArray();
                string output = Environment.GetEnvironmentVariable("DK2_PORTAL_PERFORMANCE_OUTPUT")
                    ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../arrival-performance-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "metadata.txt"),
                    "Unity=" + Application.unityVersion + "\nCPU=" + SystemInfo.processorType + "\nGPU=" + SystemInfo.graphicsDeviceName +
                    "\nAPI=" + SystemInfo.graphicsDeviceType + "\nLevel=level2\nEffect=165\nResolution=1920x1080\nFog=False\nSimulation=Not running\n" +
                    "Camera=" + camera.transform.position + "\nRotation=" + camera.transform.eulerAngles +
                    "\nSamples=60 after15warmup perphase\nClock=2000ms preadvance then16ms percallback; frozen holds2000\n" +
                    "Timings=CPU stopwatch scopes, render submission is not GPU time or FPS\nMarkerLatency=recent completed editor frames, exclude phase warmup\n");
                var csv = new StreamWriter(Path.Combine(output, "frames.csv"));
                csv.WriteLine("mode,frame,warmup,effect_ms,particles,advance_ms,lighting_ms,draw_ms,sort_ms,render_submit_ms,vertices,triangles,active_runs,sort_marker_ms,depth_marker_ms,upload_marker_ms,logged_problems");
                int loggedProblems = 0;
                Application.LogCallback countProblems = (message, stack, kind) =>
                { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Warning) loggedProblems++; };
                Application.logMessageReceived += countProblems;
                var watch = new Stopwatch();
                long milliseconds = 0;
                ClassicConstructionEffects effects = null;
                int phase = -1, frame = 75;
                EditorApplication.CallbackFunction sample = null;
                sample = () =>
                {
                    try
                    {
                        if (frame == 75)
                        {
                            effects?.Dispose();
                            if (++phase == Modes.Length)
                            {
                                csv.Dispose(); foreach (var recorder in recorders) recorder.Dispose();
                                Application.logMessageReceived -= countProblems;
                                EditorApplication.update -= sample;
                                Debug.Log("Portal performance research complete: " + output); EditorApplication.Exit(0); return;
                            }
                            milliseconds = 0;
                            effects = new ClassicConstructionEffects(root.transform, db, new InstallEffectSpriteProvider(db, art.LoadTexture), 42,
                                floorHeight: p => view.GroundHeightAt(world, p), clock: () => new EffectTime(milliseconds), meshes: meshes);
                            if (phase != 0 && phase != 6)
                            {
                                effects.Model.Request(ViewEffectRequest.AtTileCentre(new EffectId(165), new PlayerId(3), at, new Fp32(0)));
                                for (milliseconds = 51; milliseconds <= 2000; milliseconds += 50) effects.Advance();
                            }
                            frame = 0;
                        }
                        milliseconds = 2001 + (phase == 4 ? 0 : frame * 16);
                        int initialProblems = loggedProblems;
                        watch.Restart(); var lights = effects.Advance(); watch.Stop(); double advance = watch.Elapsed.TotalMilliseconds;
                        watch.Restart();
                        scene.SetEffectLights(phase == 2 || phase == 3 ? Array.Empty<EffectLightView>() : lights);
                        scene.PrepareClassicLighting(camera, 24); watch.Stop(); double lighting = watch.Elapsed.TotalMilliseconds;
                        watch.Restart();
                        if (phase != 3) effects.Draw();
                        else { transparent.BeginEffectFrame(); transparent.EndEffectFrame(); }
                        watch.Stop(); double draw = watch.Elapsed.TotalMilliseconds;
                        watch.Restart(); beforeCamera(default(ScriptableRenderContext), camera); watch.Stop(); double sort = watch.Elapsed.TotalMilliseconds;
                        watch.Restart();
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        watch.Stop(); double render = watch.Elapsed.TotalMilliseconds;
                        int runs = root.GetComponentsInChildren<MeshRenderer>().Count(r => r.gameObject.name.StartsWith("transparent", StringComparison.OrdinalIgnoreCase));
                        csv.WriteLine(string.Join(",", new object[] { Modes[phase], frame, frame < 15 ? 1 : 0, milliseconds, effects.Model.Particles.Count,
                            advance.ToString("R", CultureInfo.InvariantCulture), lighting.ToString("R", CultureInfo.InvariantCulture), draw.ToString("R", CultureInfo.InvariantCulture),
                            sort.ToString("R", CultureInfo.InvariantCulture), render.ToString("R", CultureInfo.InvariantCulture),
                            ((ICollection)Field(transparent, "_positions")).Count, ((ICollection)Field(transparent, "_triangles")).Count, runs,
                            Counter(recorders[0]), Counter(recorders[1]), Counter(recorders[2]), loggedProblems - initialProblems }));
                        if (frame == 45)
                        {
                            RenderTexture.active = target;
                            var pixels = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
                            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply(); RenderTexture.active = null;
                            File.WriteAllBytes(Path.Combine(output, Modes[phase] + ".png"), pixels.EncodeToPNG());
                            UnityEngine.Object.DestroyImmediate(pixels);
                        }
                        if (++frame == 75) { csv.Flush(); Debug.Log("Arrival phase complete: " + Modes[phase]); }
                    }
                    catch (Exception error) { csv.Dispose(); EditorApplication.update -= sample; Debug.LogException(error); EditorApplication.Exit(1); }
                };
                EditorApplication.update += sample;
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static string Counter(ProfilerRecorder recorder) => recorder.Valid && recorder.Count > 0
            ? (recorder.LastValue * 1e-6).ToString("R", CultureInfo.InvariantCulture) : "NaN";
    }
}

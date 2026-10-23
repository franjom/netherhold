// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
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
using DK2.Simulation.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity.Editor
{
    public static class ClassicCameraReferenceProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires the isolated effect probe project.");
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Camera comparison requires a graphics device.");
                long frameTicks = (long)Math.Round((1f / 60f) * (double)TimeSpan.TicksPerSecond);
                if (TimeSpan.FromTicks(frameTicks).Ticks != 166667)
                    throw new Exception("Camera frame timing lost sub-millisecond precision.");
                Debug.Log("Camera timing: FromSeconds=" + TimeSpan.FromSeconds(1f / 60f).Ticks +
                    "; tick construction=" + frameTicks);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                const string install = "E:/GogGames/Dungeon Keeper 2";
                var db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
                var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1"),
                    Enumerable.Range(0, 8).Select(i => (byte)i));
                bool heart = Environment.GetEnvironmentVariable("DK2_CAMERA_REFERENCE_HEART") == "1";
                if (heart)
                {
                    var simulation = new GameSimulation(db, world, 1, new ISimulationSystem[] { new DungeonHeartSystem() });
                    for (int i = 0; i < 41; i++) simulation.Tick();
                }
                using var art = InstallArtProvider.Open(install, db);
                var root = new GameObject("camera reference");
                var view = new WorldView(db, new PlayerId(PlayerIds.FirstKeeper));
                var meshes = new ClassicMeshes(art);
                var lattice = new TerrainLattice(db, world, new PlayerId(PlayerIds.FirstKeeper));
                var scene = new DungeonScene(root.transform, world.Width, world.Height, meshes, lattice, creatureShadows: false);
                var presenter = new ScenePresenter(view, new AssetProviderChain(art, PlaceholderProvider.Instance), scene);
                presenter.Present(world);
                var camera = new GameObject("reference camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = false; camera.allowMSAA = false;
                bool portrait = Environment.GetEnvironmentVariable("DK2_CAMERA_REFERENCE_PORTRAIT") == "1";
                bool letterbox = Environment.GetEnvironmentVariable("DK2_CAMERA_REFERENCE_LETTERBOX") == "1";
                string size = Environment.GetEnvironmentVariable("DK2_CAMERA_REFERENCE_SIZE");
                string[] dimensions = size?.Split('x');
                int width = dimensions == null ? (portrait ? 1065 : 1600) : int.Parse(dimensions[0]);
                int height = dimensions == null ? (portrait ? 1080 : 1200) : int.Parse(dimensions[1]);
                var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
                var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
                if (letterbox) camera.rect = new Rect(0, .125f, 1, .75f);
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../camera-captures"));
                Directory.CreateDirectory(output);
                if (heart || portrait || letterbox || size != null)
                {
                    output = Path.Combine(output, (heart ? "heart" : "gold") + "-" + (size ?? (portrait ? "portrait" : "4x3"))
                        + (letterbox ? "-letterbox" : ""));
                    Directory.CreateDirectory(output);
                }
                var profile = GameDatabaseLoader.LoadCameraPresets(install, "Level1").Single(p => p.CameraId == 3);
                var model = new ClassicCamera(profile, ClassicCamera.LevelStartYaw) { ReferenceScreenWidth = 1600 };
                foreach (int zoom in new[] { profile.Zoom, profile.MaxZoom - 1, profile.MinZoom })
                {
                    model.SetZoom(new Fp32(zoom));
                    var focus = new System.Numerics.Vector3(14.5f, 1, heart ? -28.5f : -24.5f);
                    var pose = model.Pose(focus, new CameraViewport(width, camera.pixelWidth, camera.pixelHeight));
                    camera.transform.position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
                    camera.transform.rotation = Quaternion.LookRotation(new Vector3(pose.Forward.X, pose.Forward.Y, pose.Forward.Z),
                        new Vector3(pose.Up.X, pose.Up.Y, pose.Up.Z));
                    camera.ResetProjectionMatrix(); camera.fieldOfView = pose.VerticalFieldOfView;
                    camera.farClipPlane = pose.FarClipDistance;
                    var projection = camera.projectionMatrix; projection[1, 2] = -pose.ProjectionCentreShift;
                    camera.projectionMatrix = projection;
                    Vector3 axis = camera.WorldToScreenPoint(new Vector3(focus.X, focus.Y, focus.Z));
                    float expectedY = camera.pixelRect.y + camera.pixelHeight * (1f - model.ProjectionCentre);
                    if (Mathf.Abs(axis.x - width / 2f) > .01f || Mathf.Abs(axis.y - expectedY) > .01f)
                        throw new Exception("Unity's rendered axis differs from the presentation model: " + axis);
                    float expectedHorizontalScale = 2f * 640f / 1600f;
                    if (Mathf.Abs(camera.projectionMatrix[0, 0] - expectedHorizontalScale) > .00001f)
                        throw new Exception("Viewport cropping changed the native horizontal projection scale.");
                    for (int warmup = 0; warmup < 3; warmup++)
                    {
                        typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                            .Invoke(root.GetComponentInChildren<TransparentTerrain>(), new object[] { default(ScriptableRenderContext), camera });
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    }
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                    if (pixels.GetPixels32().Count(p => p.r > 20 || p.g > 20 || p.b > 20) < 10000)
                        throw new Exception("Camera reference capture is blank.");
                    File.WriteAllBytes(Path.Combine(output, "gold-zoom-" + zoom + ".png"), pixels.EncodeToPNG());
                    if (zoom == profile.Zoom) ExportWallMarkers(world, db, lattice, camera, output, height);
                }
                RenderTexture.active = null; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(root);
                Debug.Log("Classic camera reference verification passed: three zooms, rendered axis and visible pixels.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void ExportWallMarkers(WorldState world, GameDatabase database, TerrainLattice lattice,
            Camera camera, string output, int height)
        {
            using var csv = new StreamWriter(Path.Combine(output, "wall-markers.csv"));
            csv.WriteLine("tile_x,tile_y,world_x,world_y,world_z,pixel_x,pixel_y");
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    var tile = world.Tile(new TilePos(x, y));
                    if ((database.TerrainById(tile.TerrainId).Flags & TerrainFlags.PlayerColouredWall) == 0) continue;
                    var p = lattice.Deform(new System.Numerics.Vector3(x + .5f, y + .5f, -2), x, y);
                    var point = new Vector3(p.X, -p.Z, -p.Y);
                    Vector3 pixel = camera.WorldToScreenPoint(point);
                    csv.WriteLine(FormattableString.Invariant(
                        $"{x},{y},{point.x:R},{point.y:R},{point.z:R},{pixel.x:R},{height - pixel.y:R}"));
                }
        }
    }
}

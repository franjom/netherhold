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
    public static class TutorialHighlightProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires the isolated effect probe project.");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                const string install = "E:/GogGames/Dungeon Keeper 2";
                var db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
                var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1"),
                    Enumerable.Range(0, 8).Select(i => (byte)i));
                ThingProjection.Place(world, db, GameDatabaseLoader.LoadCampaignThings(install, "Level1")
                    .OfType<ActionPointThing>().Select(p => p with { TriggerId = 0 }));
                var records = GameDatabaseLoader.LoadCampaignTriggers(install, "Level1").OfType<ActionTriggerRecord>().ToArray();
                using var art = InstallArtProvider.Open(install, db);
                var meshes = new ClassicMeshes(art);
                var root = new GameObject("tutorial fixture");
                var view = new WorldView(db, new PlayerId(PlayerIds.FirstKeeper));
                var scene = new DungeonScene(root.transform, world.Width, world.Height, meshes, creatureShadows: false);
                var presenter = new ScenePresenter(view, new AssetProviderChain(art, PlaceholderProvider.Instance), scene);
                var camera = new GameObject("probe camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                Vector3 focus = new Vector3(14.5f, 1.4f, -24.5f);
                camera.transform.position = focus + new Vector3(3, 5, -4);
                camera.transform.LookAt(focus); camera.fieldOfView = 35;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                camera.allowHDR = false; camera.allowMSAA = false;
                var target = new RenderTexture(640, 480, 24); target.Create(); camera.targetTexture = target;
                var pixels = new Texture2D(640, 480, TextureFormat.RGBA32, false);
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../tutorial-captures"));
                Directory.CreateDirectory(output);
                for (int warmup = 0; warmup < 3; warmup++) Capture("warmup-" + warmup);
                Color32[] baseline = Capture("off");
                RunAction(records.Single(r => r.TriggerId == 65456));
                view.AdvanceTerrainHighlights(new EffectTime(201));
                Color32[] on = Capture("on");
                int red = 0;
                for (int i = 0; i < on.Length; i++)
                    if (on[i].r > baseline[i].r + 50 && on[i].r > on[i].g + 50) red++;
                if (red < 300) throw new Exception("Tutorial guidance is invisible: " + red + " red pixels.");
                view.AdvanceTerrainHighlights(new EffectTime(402));
                if (!baseline.SequenceEqual(Capture("pulse-off"))) throw new Exception("Tutorial pulse did not restore terrain.");
                view.AdvanceTerrainHighlights(new EffectTime(603));
                RunAction(records.First(r => r.Type == 16 && r.Parameters["ActionPointId"] == 4 && r.Parameters["Available"] == 0));
                if (!baseline.SequenceEqual(Capture("cleared"))) throw new Exception("Script clear left red guidance.");
                Debug.Log("Tutorial highlight verification passed: original Level1 action, " + red + " red pixels, pulse and clear.");
                EditorApplication.Exit(0);

                void RunAction(ActionTriggerRecord original)
                {
                    var action = new ActionTriggerRecord(Array.Empty<byte>()) { Body = original.Body,
                        Type = original.Type, TriggerId = 0xFFFF, Parameters = original.Parameters };
                    new TriggerExecutor(new TriggerTree(new[] { action }, 0xFFFF), world.Triggers, world, db).Run(1);
                }
                Color32[] Capture(string name)
                {
                    presenter.Present(world);
                    var transparent = root.GetComponentInChildren<TransparentTerrain>();
                    typeof(TransparentTerrain).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(transparent, new object[] { default(ScriptableRenderContext), camera });
                    RenderPipeline.SubmitRenderRequest(camera,
                        new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); pixels.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
                    return pixels.GetPixels32();
                }
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}

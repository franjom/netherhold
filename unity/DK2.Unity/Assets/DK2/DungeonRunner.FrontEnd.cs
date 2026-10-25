// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner
    {
        private FrontEndSession _frontEnd;
        private ClassicFrontEndMenu _frontEndMenu;
        private IReadOnlyList<FrontEndProp> _frontEndScenery;
        private double _frontEndStarted;

        public GameSessionMode SessionMode => sessionMode;
        public FrontEndSession FrontEnd => _frontEnd;
        public ClassicFrontEndMenu FrontEndMenu => _frontEndMenu;
        public string LoadedLevelName => levelName;
        public string StartupFailure => _failure;

        /// <summary>Starts a named session on a fresh runtime hierarchy, preserving the runner's settings.</summary>
        public DungeonRunner LoadLevel(GameSessionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!Application.isPlaying) throw new InvalidOperationException("Session switching requires Play mode.");
            WaitForTick();
            var next = new GameObject(request.Mode == GameSessionMode.FrontEnd ? "DK2 Front End" : "DK2 " + request.LevelName);
            next.SetActive(false);
            next.transform.SetParent(transform.parent, false);
            next.transform.localPosition = transform.localPosition;
            next.transform.localRotation = transform.localRotation;
            next.transform.localScale = transform.localScale;
            var runner = next.AddComponent<DungeonRunner>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), runner);
            runner.sessionMode = request.Mode;
            runner.levelName = request.LevelName;
            runner.frontEndReturnPage = request.ReturnPage;
            runner.saveFile = "";
            runner.enabled = true;
            gameObject.SetActive(false);
            // Shader globals belong to one scene; release them before the replacement publishes its buffers.
            ReleaseRuntimeResources();
            Destroy(gameObject);
            next.SetActive(true);
            return runner;
        }

        private void StartFrontEnd(string install, WorldState world)
        {
            var paths = CameraPathLoader(install);
            _frontEnd = new FrontEndSession(path => paths((int)path), frontEndReturnPage);
            _frontEndStarted = Time.realtimeSinceStartupAsDouble;
            _frontEnd.Update(TimeSpan.Zero);
            _frontEndMenu = new ClassicFrontEndMenu(transform, _frontEnd, new InstallClassicFrontEndMenuAssets(install));
            _frontEndMenu.Model.QuitRequested += QuitFrontEnd;
            _scene.SetCreatureFrameTime(GameTick.Zero, 0);
            _presenter.Present(world);
            _startup.Step("front-end.present");
            Cursor.visible = true;
            UpdateFrontEnd();
        }

        private void UpdateFrontEnd()
        {
            double elapsed = Math.Max(0, Time.realtimeSinceStartupAsDouble - _frontEndStarted);
            _frontEnd.Update(TimeSpan.FromSeconds(elapsed));
            _frontEndMenu?.Refresh();
            if (_frontEnd.Pose is CameraPathPose pose) ApplyFrontEndCamera(pose);
            double turns = elapsed * SimulationTiming.OriginalTicksPerGameSecond;
            var tick = new GameTick((long)Math.Floor(turns));
            float fraction = (float)(turns - Math.Floor(turns));
            _scene.DrawCreatureFrame(tick, fraction);
            _scene.SetObjectLights(_worldView.LightObjects(_simulation.World), 1);
            ApplyClassicLook();
            if (_frontEnd.Pose is CameraPathPose lightingPose) _scene.PrepareClassicLighting(Camera.main, lightingPose.ViewDistance);
            _scene.AdvanceTorchAnimation();
            _constructionEffects?.Draw();
            string settled = _startup?.Frame(Time.unscaledDeltaTime * 1000d, _installArt);
            if (settled != null) Debug.Log(settled);
        }

        private static void QuitFrontEnd()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void ApplyFrontEndCamera(CameraPathPose pose)
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            camera.rect = new Rect(0, 0, 1, 1);
            int width = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            int height = camera.targetTexture != null ? camera.targetTexture.height : Screen.height;
            if (width <= 0 || height <= 0) return;
            camera.orthographic = false;
            camera.ResetProjectionMatrix();
            camera.fieldOfView = pose.VerticalFieldOfView(width, height);
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + .01f, pose.ViewDistance);
            camera.transform.SetPositionAndRotation(new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z),
                Quaternion.LookRotation(new Vector3(pose.Forward.X, pose.Forward.Y, pose.Forward.Z),
                    new Vector3(pose.Up.X, pose.Up.Y, pose.Up.Z)));
        }

        private void DrawFrontEndDiagnostics()
        {
            _frameOverlay.Draw();
            GUI.Label(new Rect(10, 10, 1200, 22), $"Front end (mode 5) · path {_frontEnd.Path} · page {_frontEnd.Page}");
        }
    }
}

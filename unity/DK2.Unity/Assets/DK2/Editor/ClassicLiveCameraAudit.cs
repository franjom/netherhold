// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity.Editor
{
    public static class ClassicLiveCameraAudit
    {
        private static double _lastCapture = double.NegativeInfinity;
        private static int _captures;
        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            string request = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/camera-live-audit.request"));
            if (!File.Exists(request)) return;
            File.Delete(request);
            Request();
        }

        [MenuItem("DK2/Diagnostics/Record live camera")]
        public static void Request()
        {
            _lastCapture = double.NegativeInfinity;
            _captures = 0;
            RenderPipelineManager.endCameraRendering -= CaptureWhenPlaying;
            RenderPipelineManager.endCameraRendering += CaptureWhenPlaying;
        }

        private static void CaptureWhenPlaying(ScriptableRenderContext context, Camera camera)
        {
            if (!EditorApplication.isPlaying || camera != Camera.main) return;
            DungeonRunner runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            if (runner == null) return;
            object scripted = typeof(DungeonRunner).GetField("_scriptedView", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(runner);
            if (scripted == null || (bool)scripted.GetType().GetProperty("PathPlaying").GetValue(scripted)) return;
            if (EditorApplication.timeSinceStartup - _lastCapture < 1) return;
            _lastCapture = EditorApplication.timeSinceStartup;
            if (++_captures >= 16) RenderPipelineManager.endCameraRendering -= CaptureWhenPlaying;
            Capture();
        }

        private static void Capture()
        {
            if (!EditorApplication.isPlaying) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            DungeonRunner runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            var record = new CameraRecord
            {
                screenWidth = Screen.width, screenHeight = Screen.height,
                pixelWidth = camera.pixelWidth, pixelHeight = camera.pixelHeight,
                aspect = camera.aspect, fieldOfView = camera.fieldOfView,
                physical = camera.usePhysicalProperties, rect = camera.rect,
                eye = camera.transform.position, euler = camera.transform.eulerAngles,
                focus = runner == null ? Vector3.zero : (Vector3)typeof(DungeonRunner)
                    .GetField("_cameraFocus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runner),
                projection = Enumerable.Range(0, 16).Select(i => camera.projectionMatrix[i]).ToArray(),
                worldToCamera = Enumerable.Range(0, 16).Select(i => camera.worldToCameraMatrix[i]).ToArray(),
            };
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/camera-live-audit"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".json");
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(output)) writer.Write(JsonUtility.ToJson(record, true));
            Debug.Log("Live camera audit: " + path);
        }

        [Serializable]
        private sealed class CameraRecord
        {
            public int screenWidth, screenHeight, pixelWidth, pixelHeight;
            public float aspect, fieldOfView;
            public bool physical;
            public Rect rect;
            public Vector3 eye, euler, focus;
            public float[] projection, worldToCamera;
        }
    }
}

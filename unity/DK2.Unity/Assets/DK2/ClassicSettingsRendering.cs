// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Reflection;
using DK2.Data.Settings;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    /// <summary>
    /// The display settings: brightness and multisampling. Brightness is the gamma curve of
    /// <see cref="DisplaySettings.BrightnessExponent"/> over the main camera's finished image:
    /// the image is copied and redrawn through the curve by one full-screen triangle, and nothing
    /// is drawn at the default gamma. The project renders in Gamma colour
    /// space, so the curve is on display-encoded values. The UI Toolkit HUD draws after the camera
    /// and keeps its brightness. (A URP Volume's postExposure did nothing here: the renderer has no
    /// post-processing data, so URP runs no post-processing.)
    /// </summary>
    public sealed class ClassicSettingsRendering : IDisposable
    {
        private static readonly int SourceId = Shader.PropertyToID("_DK2BrightnessSource"), ExponentId = Shader.PropertyToID("_Exponent");

        private readonly Material _curve;
        private readonly CommandBuffer _commands = new CommandBuffer { name = "DK2 brightness" };
        // The URP asset is shared with the editor: its sample count is restored on Dispose so a
        // Play session never leaves a changed asset to be saved.
        private readonly RenderPipelineAsset _pipeline = GraphicsSettings.currentRenderPipeline;
        private readonly PropertyInfo _msaa;
        private readonly int _originalMsaa = 1;
        private bool _drawing;

        public ClassicSettingsRendering()
        {
            Shader shader = Resources.Load<Shader>("Shaders/ClassicExposure");
            if (shader != null) _curve = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            else Debug.LogWarning("DK2 display settings: Shaders/ClassicExposure is missing; brightness will not change");
            // UniversalRenderPipelineAsset.msaaSampleCount, by reflection: the adapter compiles without URP's assemblies.
            _msaa = _pipeline != null ? _pipeline.GetType().GetProperty("msaaSampleCount") : null;
            if (_msaa != null) _originalMsaa = (int)_msaa.GetValue(_pipeline);
            else Debug.LogWarning("DK2 display settings: the render pipeline has no msaaSampleCount; MSAA will not change");
        }

        /// <summary>Whether the curve is drawn, and its exponent (30 / Gamma; 1 when not drawn).</summary>
        public bool DrawsBrightness => _drawing;
        public float BrightnessExponent { get; private set; } = 1;

        public void Apply(GameSettings settings)
        {
            float? exponent = _curve != null ? DisplaySettings.BrightnessExponent(settings.Gamma) : null;
            bool draw = exponent.HasValue;
            BrightnessExponent = exponent ?? 1;
            if (draw) _curve.SetFloat(ExponentId, BrightnessExponent);
            if (draw != _drawing)
            {
                if (draw) RenderPipelineManager.endCameraRendering += AfterCamera;
                else RenderPipelineManager.endCameraRendering -= AfterCamera;
                _drawing = draw;
            }

            if (_msaa != null && (int)_msaa.GetValue(_pipeline) != settings.Msaa) _msaa.SetValue(_pipeline, settings.Msaa);
        }

        private void AfterCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != Camera.main) return;
            RenderTexture texture = camera.targetTexture;
            RenderTargetIdentifier target = texture != null
                ? new RenderTargetIdentifier(texture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            _commands.Clear();
            // The whole target, so the triangle reads the source at its own pixel position.
            _commands.GetTemporaryRT(SourceId, texture != null ? texture.width : Screen.width,
                texture != null ? texture.height : Screen.height, 0, FilterMode.Point, RenderTextureFormat.ARGB32);
            _commands.Blit(target, SourceId);
            _commands.SetRenderTarget(target);
            _commands.SetViewport(camera.pixelRect);
            _commands.DrawProcedural(Matrix4x4.identity, _curve, 0, MeshTopology.Triangles, 3);
            _commands.ReleaseTemporaryRT(SourceId);
            context.ExecuteCommandBuffer(_commands);
            context.Submit();
        }

        public void Dispose()
        {
            if (_drawing) RenderPipelineManager.endCameraRendering -= AfterCamera;
            _drawing = false;
            if (_msaa != null) _msaa.SetValue(_pipeline, _originalMsaa);
            _commands.Release();
            if (_curve != null) UnityEngine.Object.Destroy(_curve);
        }
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Reflection;
using DK2.Data.Settings;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    /// <summary>
    /// The display settings: brightness and multisampling. Brightness multiplies the main camera's
    /// finished image by gamma/30 (0.5..2), our brightness proxy, not native gamma-ramp emulation.
    /// It is one full-screen triangle drawn over that image with a multiplying blend, and nothing
    /// at the default gamma. The project renders in Gamma colour space, so the multiply is on
    /// display-encoded values and the top of the slider clips bright pixels to white. The UI
    /// Toolkit HUD draws after the camera and is not scaled. (A URP Volume's postExposure did
    /// nothing here: the renderer has no post-processing data, so URP runs no post-processing.)
    /// </summary>
    public sealed class ClassicSettingsRendering : IDisposable
    {
        private const int DefaultGamma = 30;
        private static readonly int ScaleId = Shader.PropertyToID("_Scale"), DstBlendId = Shader.PropertyToID("_DstBlend");

        private readonly Material _exposure;
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
            if (shader != null) _exposure = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            else Debug.LogWarning("DK2 display settings: Shaders/ClassicExposure is missing; brightness will not change");
            // UniversalRenderPipelineAsset.msaaSampleCount, by reflection: the adapter compiles without URP's assemblies.
            _msaa = _pipeline != null ? _pipeline.GetType().GetProperty("msaaSampleCount") : null;
            if (_msaa != null) _originalMsaa = (int)_msaa.GetValue(_pipeline);
            else Debug.LogWarning("DK2 display settings: the render pipeline has no msaaSampleCount; MSAA will not change");
        }

        public void Apply(GameSettings settings)
        {
            float scale = settings.Gamma / (float)DefaultGamma;
            bool draw = _exposure != null && settings.Gamma != DefaultGamma;
            if (draw)
            {
                // Darken: dst * scale. Brighten: dst * (scale - 1) + dst.
                bool brighten = scale > 1;
                _exposure.SetFloat(ScaleId, brighten ? scale - 1 : scale);
                _exposure.SetFloat(DstBlendId, (float)(brighten ? BlendMode.One : BlendMode.Zero));
            }
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
            _commands.Clear();
            _commands.SetRenderTarget(camera.targetTexture != null
                ? new RenderTargetIdentifier(camera.targetTexture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
            _commands.SetViewport(camera.pixelRect);
            _commands.DrawProcedural(Matrix4x4.identity, _exposure, 0, MeshTopology.Triangles, 3);
            context.ExecuteCommandBuffer(_commands);
            context.Submit();
        }

        public void Dispose()
        {
            if (_drawing) RenderPipelineManager.endCameraRendering -= AfterCamera;
            _drawing = false;
            if (_msaa != null) _msaa.SetValue(_pipeline, _originalMsaa);
            _commands.Release();
            if (_exposure != null) UnityEngine.Object.Destroy(_exposure);
        }
    }
}

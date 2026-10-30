// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Data.Settings;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>URP owns display correction. Exposure is our brightness proxy, not native gamma-ramp emulation.</summary>
    public sealed class ClassicSettingsRendering : IDisposable
    {
        private readonly Transform _parent;
        private GameObject _owner;
        private ScriptableObject _profile;
        private object _exposure;
        private Component _cameraData;
        private bool _originalPost;
        public ClassicSettingsRendering(Transform parent) { _parent = parent; }
        public void Apply(int gamma)
        {
            if (_exposure == null)
            {
                // Resolve the installed SRP assemblies at runtime so the compile harness needs no copied package DLLs.
                Type volumeType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
                Type profileType = Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
                Type colourType = Type.GetType("UnityEngine.Rendering.Universal.ColorAdjustments, Unity.RenderPipelines.Universal.Runtime");
                if (volumeType == null || profileType == null || colourType == null) return;
                _owner = new GameObject("DK2 display settings"); _owner.transform.SetParent(_parent, false);
                Component volume = _owner.AddComponent(volumeType);
                _profile = ScriptableObject.CreateInstance(profileType);
                volumeType.GetProperty("isGlobal").SetValue(volume, true);
                volumeType.GetField("sharedProfile").SetValue(volume, _profile);
                object colour = profileType.GetMethod("Add", new[] { typeof(Type), typeof(bool) }).Invoke(_profile, new object[] { colourType, true });
                _exposure = colourType.GetField("postExposure").GetValue(colour);
                _exposure.GetType().GetProperty("overrideState").SetValue(_exposure, true);
                Type cameraType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (cameraType != null && Camera.main != null)
                {
                    _cameraData = Camera.main.GetComponent(cameraType) ?? Camera.main.gameObject.AddComponent(cameraType);
                    _originalPost = (bool)cameraType.GetProperty("renderPostProcessing").GetValue(_cameraData);
                    cameraType.GetProperty("renderPostProcessing").SetValue(_cameraData, true);
                }
            }
            _exposure.GetType().GetProperty("value").SetValue(_exposure, Mathf.Log(gamma / 30f, 2));
        }
        public void Dispose()
        {
            if (_cameraData != null) _cameraData.GetType().GetProperty("renderPostProcessing").SetValue(_cameraData, _originalPost);
            if (_owner != null) UnityEngine.Object.Destroy(_owner);
            if (_profile != null) UnityEngine.Object.Destroy(_profile);
        }
    }
}

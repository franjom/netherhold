// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Runtime.InteropServices;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    // Shader globals belong to the one active DungeonScene; concurrent world views need per-camera binding.
    internal sealed class ClassicTorchLightBuffer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Entry
        {
            public Vector4 PositionRadiusSquared;
            public Vector4 ColourIntensity;
        }

        private static readonly int SourcesId = Shader.PropertyToID("_DK2TorchSources");
        private static readonly int WidthId = Shader.PropertyToID("_DK2TorchGridWidth");
        private static readonly int CountId = Shader.PropertyToID("_DK2TorchGridCount");
        private readonly Entry[] _entries;
        private readonly GraphicsBuffer _buffer;
        private readonly int _width;
        private int _firstDirty, _lastDirty = -1;

        public ClassicTorchLightBuffer(int width, int height)
        {
            _width = width;
            _entries = new Entry[checked(width * height)];
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _entries.Length, Marshal.SizeOf<Entry>());
            _buffer.SetData(_entries);
            // RenderSink_Init starts native ambient at zero (classic-lighting.md section8).
            Shader.SetGlobalVector("_DK2AmbientColour", Vector4.zero);
            Shader.SetGlobalFloat("_DK2InitialAmbientPreview", 0);
            Publish();
        }

        public void Set(int tileIndex, in TorchView torch)
        {
            ClassicPointLight light = ClassicPointLight.FromTorch(torch);
            _entries[tileIndex] = new Entry
            {
                PositionRadiusSquared = new Vector4(light.Position.X, light.Position.Y, light.Position.Z, light.Radius * light.Radius),
                ColourIntensity = new Vector4(light.Colour.X, light.Colour.Y, light.Colour.Z, light.Intensity),
            };
            Dirty(tileIndex);
        }

        public void Remove(int tileIndex)
        {
            _entries[tileIndex] = default;
            Dirty(tileIndex);
        }

        private void Dirty(int tileIndex)
        {
            _firstDirty = _lastDirty < 0 ? tileIndex : Math.Min(_firstDirty, tileIndex);
            _lastDirty = Math.Max(_lastDirty, tileIndex);
        }

        public void Flush()
        {
            if (_lastDirty >= 0)
            {
                _buffer.SetData(_entries, _firstDirty, _firstDirty, _lastDirty - _firstDirty + 1);
                _lastDirty = -1;
            }
            Publish();
        }

        private void Publish()
        {
            Shader.SetGlobalBuffer(SourcesId, _buffer);
            Shader.SetGlobalInt(WidthId, _width);
            Shader.SetGlobalInt(CountId, _entries.Length);
        }

        public void Clear()
        {
            Array.Clear(_entries, 0, _entries.Length);
            _firstDirty = 0;
            _lastDirty = _entries.Length - 1;
            Flush();
        }

        public void Dispose()
        {
            Shader.SetGlobalInt(CountId, 0);
            _buffer.Dispose();
        }
    }
}

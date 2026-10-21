// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>Which tiles take Unity's stand-in sky and sun fill (ADR-0522): explored ones, or
    /// ones whose terrain light is submitted. DK2 adds no fill that we know of (inferred from the
    /// owner's screenshots, where tiles away from the explored area stay dark), so this
    /// approximates its 0x0043C310 terrain-light gate; the gate's camera-mode admission, and Sight
    /// or scan admission of a tile without a terrain light, are not mirrored. Shader globals
    /// belong to the one active DungeonScene.</summary>
    internal sealed class ClassicTileFillBuffer : IDisposable
    {
        private static readonly int WeightsId = Shader.PropertyToID("_DK2TileFill");
        private static readonly int WidthId = Shader.PropertyToID("_DK2TileFillWidth");
        private static readonly int CountId = Shader.PropertyToID("_DK2TileFillCount");
        private readonly float[] _weights;
        private readonly GraphicsBuffer _buffer;
        private readonly int _width;
        private int _firstDirty, _lastDirty = -1;

        public ClassicTileFillBuffer(int width, int height)
        {
            _width = width;
            _weights = new float[checked(width * height)];
            Array.Fill(_weights, 1f);
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _weights.Length, sizeof(float));
            _buffer.SetData(_weights);
            Publish();
        }

        public void Set(int tileIndex, bool lit)
        {
            float weight = lit ? 1f : 0f;
            if (_weights[tileIndex] == weight) return;
            _weights[tileIndex] = weight;
            _firstDirty = _lastDirty < 0 ? tileIndex : Math.Min(_firstDirty, tileIndex);
            _lastDirty = Math.Max(_lastDirty, tileIndex);
        }

        public void Flush()
        {
            if (_lastDirty >= 0)
            {
                _buffer.SetData(_weights, _firstDirty, _firstDirty, _lastDirty - _firstDirty + 1);
                _lastDirty = -1;
            }
            Publish();
        }

        public void Clear()
        {
            Array.Fill(_weights, 1f);
            _firstDirty = 0;
            _lastDirty = _weights.Length - 1;
            Flush();
        }

        private void Publish()
        {
            Shader.SetGlobalBuffer(WeightsId, _buffer);
            Shader.SetGlobalInt(WidthId, _width);
            Shader.SetGlobalInt(CountId, _weights.Length);
        }

        public void Dispose()
        {
            Shader.SetGlobalInt(CountId, 0);
            _buffer.Dispose();
        }
    }
}

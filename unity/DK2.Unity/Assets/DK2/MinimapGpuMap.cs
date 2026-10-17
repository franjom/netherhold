// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    public sealed class MinimapGpuMap : IDisposable
    {
        private struct Upload { public uint Position, ColourClass; }
        private readonly Upload[] _uploads;
        private readonly GraphicsBuffer _buffer;
        private readonly Material _material;
        private readonly Texture2D _palette;
        private readonly CommandBuffer _commands = new CommandBuffer { name = "DK2 minimap" };
        private readonly Color32[] _colours = new Color32[MinimapColourClass.Count];
        private RenderTexture _disc;
        private bool _dirty = true;
        private float _angle = float.NaN;
        public RenderTexture Classes { get; }
        public RenderTexture Disc => _disc;
        public int LastUploadedTiles { get; private set; }

        public MinimapGpuMap(int width, int height, MinimapPalette palette, Texture backdrop)
        {
            if (width <= 0 || width > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0 || height > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(height));
            Shader shader = Resources.Load<Shader>("DK2Minimap");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Minimap shader unavailable.");
            _material = new Material(shader);
            _uploads = new Upload[checked(width * height)];
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _uploads.Length, sizeof(uint) * 2);
            Classes = Target(width, height, linear: true);
            Classes.filterMode = FilterMode.Point;
            _palette = new Texture2D(MinimapColourClass.Count, 1, TextureFormat.RGBA32, false);
            _palette.filterMode = FilterMode.Point; _palette.wrapMode = TextureWrapMode.Clamp;
            _material.SetBuffer("_Changes", _buffer);
            _material.SetTexture("_Classes", Classes);
            _material.SetTexture("_Palette", _palette);
            _material.SetTexture("_Backdrop", backdrop);
            _material.SetVector("_GridSize", new Vector4(width, height, 0, 0));
            UpdatePalette(palette);
        }

        private static RenderTexture Target(int width, int height, bool linear)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            target.wrapMode = TextureWrapMode.Clamp;
            target.Create();
            return target;
        }

        public void UpdatePalette(MinimapPalette palette)
        {
            for (int i = 0; i < _colours.Length; i++)
            {
                uint colour = palette.Colours[i];
                _colours[i] = new Color32((byte)(colour >> 16), (byte)(colour >> 8), (byte)colour, 255);
            }
            _palette.SetPixels32(_colours); _palette.Apply(false, false); _dirty = true;
        }

        public void UploadChanges(MinimapClassGrid grid)
        {
            if (grid.Width != Classes.width || grid.Height != Classes.height)
                throw new ArgumentException("Minimap grid dimensions changed.", nameof(grid));
            var changes = grid.Changes;
            LastUploadedTiles = changes.Length;
            if (changes.Length == 0) return;
            for (int i = 0; i < changes.Length; i++)
            {
                var change = changes[i];
                _uploads[i] = new Upload { Position = (uint)(change.Position.X | change.Position.Y << 16),
                    ColourClass = change.ColourClass.Index };
            }
            _buffer.SetData(_uploads, 0, 0, changes.Length);
            _commands.Clear(); _commands.SetRenderTarget(Classes);
            _commands.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 6, changes.Length);
            Graphics.ExecuteCommandBuffer(_commands);
            grid.ClearChanges(); _dirty = true;
        }

        public void Render(float headingDegrees, int diameter)
        {
            diameter = Mathf.Clamp(diameter, 1, 4096);
            if (_disc == null || _disc.width != diameter)
            {
                if (_disc != null) { _disc.Release(); UnityEngine.Object.Destroy(_disc); }
                _disc = Target(diameter, diameter, linear: false); _dirty = true;
            }
            if (!_dirty && _angle == headingDegrees) return;
            _angle = headingDegrees; _dirty = false;
            _material.SetFloat("_Angle", headingDegrees * Mathf.Deg2Rad);
            _commands.Clear(); _commands.SetRenderTarget(_disc);
            _commands.DrawProcedural(Matrix4x4.identity, _material, 1, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_commands);
        }

        public void Dispose()
        {
            _buffer.Dispose(); _commands.Dispose();
            Classes.Release(); UnityEngine.Object.Destroy(Classes);
            if (_disc != null) { _disc.Release(); UnityEngine.Object.Destroy(_disc); }
            UnityEngine.Object.Destroy(_palette); UnityEngine.Object.Destroy(_material);
        }
    }
}

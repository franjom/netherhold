// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Presentation;
using DK2.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DK2.Unity
{
    public sealed class MinimapGpuMap : IDisposable
    {
        private struct Upload { public uint Position, ColourClass; }
        private struct MarkerUpload { public int X, Y; public uint Kind, Colour; public int EndX, EndY; }
        private MarkerUpload[] _markers = new MarkerUpload[64];
        private GraphicsBuffer _markerBuffer;
        private int _markerCount;
        private int _renderMarkerCount;
        private bool _markersDirty = true;
        private readonly MinimapRasterPoint[] _trail = new MinimapRasterPoint[MinimapHeartTrail.MaximumDots];
        private TilePos? _heart;
        private GameTick _trailTick;
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
        public Texture PaletteTexture => _palette;
        public RenderTexture Disc => _disc;
        public int LastUploadedTiles { get; private set; }
        private MinimapZoom _zoom = MinimapZoom.WholeMap;
        private FpPosition3 _focus;
        public void SetHeartTrail(TilePos? heart, GameTick tick)
        {
            if (_heart == heart && ((_trailTick.Value ^ tick.Value) & 3) == 0) return;
            _heart = heart; _trailTick = tick;
            if (!_zoom.IsWholeMap) { _dirty = true; _markersDirty = true; }
        }
        public void SetView(MinimapZoom zoom, FpPosition3 focus)
        {
            if (_zoom == zoom && (zoom.IsWholeMap || _focus == focus)) return;
            _zoom = zoom; _focus = focus;
            _markersDirty = true;
            _material.SetInt("_Zoom", zoom.Level);
            if (!zoom.IsWholeMap)
            {
                MinimapRasterPoint origin = zoom.RasterOrigin(focus);
                _material.SetVector("_RasterOrigin", new Vector4(origin.X, origin.Y, 0, 0));
                _material.SetVector("_FocusPixel", new Vector4(focus.X.Raw >> (12 - zoom.Level),
                    focus.Y.Raw >> (12 - zoom.Level), 0, 0));
            }
            _dirty = true;
        }

        public MinimapGpuMap(int width, int height, MinimapPalette palette, Texture backdrop)
        {
            if (width <= 0 || width > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0 || height > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(height));
            Shader shader = Resources.Load<Shader>("DK2Minimap");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Minimap shader unavailable.");
            _material = new Material(shader);
            _material.SetInt("_Zoom", -1);
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
            MinimapMapBounds bounds = MinimapMapBounds.Fit(width, height);
            _material.SetVector("_MapRect", new Vector4(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            _markerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _markers.Length, sizeof(uint) * 6);
            _material.SetBuffer("_Markers", _markerBuffer);
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
            // A render texture's contents do not survive a device loss; refill it whole.
            if (!Classes.IsCreated())
            {
                Classes.Create(); _material.SetTexture("_Classes", Classes); grid.MarkAllChanged();
            }
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
            if (_disc == null || _disc.width != diameter || !_disc.IsCreated())
            {
                if (_disc != null) { _disc.Release(); UnityEngine.Object.Destroy(_disc); }
                _disc = Target(diameter, diameter, linear: false); _dirty = true;
            }
            if (!_dirty && _angle == headingDegrees) return;
            _angle = headingDegrees; _dirty = false;
            _material.SetFloat("_Angle", headingDegrees * Mathf.Deg2Rad);
            if (_markersDirty) UpdateMarkerBuffer();
            _commands.Clear(); _commands.SetRenderTarget(_disc);
            _commands.DrawProcedural(Matrix4x4.identity, _material, 1, MeshTopology.Triangles, 3);
            if (_renderMarkerCount > 0)
                _commands.DrawProcedural(Matrix4x4.identity, _material, 2, MeshTopology.Triangles, 6, _renderMarkerCount);
            Graphics.ExecuteCommandBuffer(_commands);
        }

        public void UploadMarkers(IReadOnlyList<MinimapMarker> markers)
        {
            if (markers.Count + MinimapHeartTrail.MaximumDots > _markers.Length)
            {
                _markers = new MarkerUpload[Mathf.NextPowerOfTwo(markers.Count + MinimapHeartTrail.MaximumDots)];
                _markerBuffer.Dispose();
                _markerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _markers.Length, sizeof(uint) * 6);
                _material.SetBuffer("_Markers", _markerBuffer);
            }
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].RadiusRasterPixels < 0 || markers[i].RadiusRasterPixels > ushort.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(markers), "Marker radius must fit the packed raster extent.");
                _markers[i] = new MarkerUpload { X = markers[i].Position.X.Raw, Y = markers[i].Position.Y.Raw,
                    Kind = (uint)markers[i].Kind | (markers[i].ReportingDistanceTiles >= 0 ? 256u : 0)
                        | (uint)markers[i].RadiusRasterPixels << 16, Colour = markers[i].ColourArgb,
                    EndX = markers[i].ReportingDistanceTiles >= 0 ? markers[i].ReportingDistanceTiles : markers[i].EndPosition.X.Raw,
                    EndY = markers[i].EndPosition.Y.Raw };
            }
            _markerCount = markers.Count;
            _markersDirty = true;
            _dirty = true;
        }

        private void UpdateMarkerBuffer()
        {
            int dots = _heart.HasValue ? MinimapHeartTrail.Build(_heart.Value, _focus, _zoom, _trailTick, _trail) : 0;
            for (int i = 0; i < dots; i++)
                // Shader kind 6 takes raster coordinates directly and draws one pixel, after ordinary markers.
                _markers[_markerCount + i] = new MarkerUpload {
                    X = _trail[i].X, Y = _trail[i].Y, Kind = 6, Colour = 0xFFFFFFFF };
            _renderMarkerCount = _markerCount + dots;
            if (_renderMarkerCount > 0) _markerBuffer.SetData(_markers, 0, 0, _renderMarkerCount);
            _markersDirty = false;
        }

        public void Dispose()
        {
            _buffer.Dispose(); _markerBuffer.Dispose(); _commands.Dispose();
            Classes.Release(); UnityEngine.Object.Destroy(Classes);
            if (_disc != null) { _disc.Release(); UnityEngine.Object.Destroy(_disc); }
            UnityEngine.Object.Destroy(_palette); UnityEngine.Object.Destroy(_material);
        }
    }
}

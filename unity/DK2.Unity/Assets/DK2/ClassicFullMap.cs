// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Art.Textures;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace DK2.Unity
{
    /// <summary>The full map's Unity side: <see cref="FullMapHud"/> (ADR-0805) drawn by two <see cref="HudCanvas"/>es,
    /// the backdrop under and the markers and legend over the engine's own map image (a blit of the minimap's class
    /// texture) and camera preview. The panel is in screen pixels.</summary>
    public sealed class ClassicFullMap : IDisposable
    {
        private static readonly RgbaImage Blank = new RgbaImage(1, 1, new byte[4]);
        private readonly FullMapHud _hud;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly Image _map;
        private readonly Material _material;
        private readonly RenderTexture _target;
        private readonly DungeonMinimap _source;
        private readonly HudCanvas _back, _front;
        private readonly HudFrame _backFrame = new HudFrame(), _frontFrame = new HudFrame();
        private readonly Image _preview;
        private readonly Camera _previewCamera;
        private RenderTexture _previewTarget;
        private IClassicResourceAssets _roomAssets;
        private bool _visible;
        public Camera PreviewCamera => _visible && _previewTarget != null ? _previewCamera : null;
        public void PreparePreview()
        {
            if (_visible) PreparePreview(Screen.width, Screen.height);
        }
        public PanelSettings Panel => _settings;
        public ClassicFullMap(Transform parent, DungeonMinimap source)
        {
            _source = source;
            _hud = new FullMapHud(source.Palette, source.MapWidth, source.MapHeight);
            Shader shader = Resources.Load<Shader>("DK2FullMap");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Full-map shader unavailable.");
            _material = new Material(shader);
            _material.SetTexture("_Classes",source.ClassTexture); _material.SetTexture("_Palette",source.PaletteTexture);
            _target = new RenderTexture(source.MapWidth,source.MapHeight,0,RenderTextureFormat.ARGB32)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _target.Create();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.sortingOrder = 20;
            _owner = new GameObject("Dungeon full map"); _owner.transform.SetParent(parent,false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _back = new HudCanvas(_ => Blank, null); _root.Add(_back);
            _map = new Image { image = _target, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            _map.style.position = Position.Absolute; _root.Add(_map);
            _preview = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            _preview.style.position = Position.Absolute; _root.Add(_preview);
            var previewObject = new GameObject("Full map preview camera");
            previewObject.transform.SetParent(_owner.transform, false);
            _previewCamera = previewObject.AddComponent<Camera>();
            _previewCamera.enabled = false;
            _previewCamera.clearFlags = CameraClearFlags.SolidColor;
            _previewCamera.backgroundColor = Color.black;
            _previewCamera.nearClipPlane = .05f;
            _front = new HudCanvas(path => _roomAssets != null ? _roomAssets.Image(path) : Blank, null);
            _root.Add(_front);
            SetVisible(false);
        }
        public void SetLegendAssets(IClassicOptionsAssets assets, Func<byte, string> playerName, IClassicResourceAssets roomAssets)
        {
            _roomAssets = roomAssets;
            _hud.SetLegend(assets != null ? assets.Font : (Func<int, BitmapFont>)null, playerName, roomAssets != null);
        }
        public void Capture(WorldState world, GameDatabase database, PlayerId viewer)
        {
            if (_visible) _hud.Capture(world, database, viewer, ResolvePointer(Input.mousePosition));
        }
        public void SetVisible(bool visible)
        {
            if (visible && !_visible) { _back.Invalidate(); _front.Invalidate(); }
            _visible=visible; _hud.SetVisible(visible); _root.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            _previewCamera.enabled = visible;
        }
        public void Draw(Vector2Int viewport=default)
        {
            if (!_visible) return;
            int width=viewport.x>0?viewport.x:Screen.width, height=viewport.y>0?viewport.y:Screen.height;
            ClassicFullMapLayout layout = _hud.Layout(width, height);
            PreparePreview(width, height);
            _map.style.left=layout.Origin.X; _map.style.top=layout.Origin.Y;
            _map.style.width=layout.Size.X; _map.style.height=layout.Size.Y;
            Graphics.Blit(null,_target,_material);
            var screen = new HudScreen(width, height);
            _backFrame.Clear(); _frontFrame.Clear();
            _hud.Build(screen, _backFrame, _frontFrame);
            _back.Present(_backFrame, screen.Scale, 1);
            _front.Present(_frontFrame, screen.Scale, 1);
        }
        private void PreparePreview(int width, int height)
        {
            FullMapPreview preview = _hud.Layout(width, height).Preview(width, height,
                new System.Numerics.Vector2(Input.mousePosition.x, height - Input.mousePosition.y));
            int pw = (int)preview.Size.X, ph = (int)preview.Size.Y;
            if (_previewTarget == null || _previewTarget.width != pw || _previewTarget.height != ph)
            {
                if (_previewTarget != null) { _previewTarget.Release(); UnityEngine.Object.Destroy(_previewTarget); }
                _previewTarget = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32);
                _previewTarget.Create(); _previewCamera.targetTexture = _previewTarget; _preview.image = _previewTarget;
            }
            _preview.style.left = preview.Origin.X; _preview.style.top = preview.Origin.Y;
            _preview.style.width = pw; _preview.style.height = ph;
            _previewCamera.transform.position = new Vector3(preview.Pose.Position.X, preview.Pose.Position.Y, preview.Pose.Position.Z);
            _previewCamera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            _previewCamera.fieldOfView = preview.Pose.VerticalFieldOfView;
            _previewCamera.farClipPlane = preview.Pose.FarClipDistance;
            _previewCamera.aspect = pw / (float)ph;
        }
        public TilePos ResolvePointer(Vector3 screenPosition) =>
            _hud.ResolvePointer(Screen.width, Screen.height, new System.Numerics.Vector2(screenPosition.x, Screen.height - screenPosition.y));
        public void Dispose()
        {
            _back.Dispose(); _front.Dispose();
            _target.Release(); UnityEngine.Object.Destroy(_target); UnityEngine.Object.Destroy(_material);
            if (_previewTarget != null) { _previewTarget.Release(); UnityEngine.Object.Destroy(_previewTarget); }
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}

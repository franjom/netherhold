// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
namespace DK2.Unity
{
    public sealed class ClassicFullMap : IDisposable
    {
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly Image _map;
        private readonly Material _material;
        private readonly RenderTexture _target;
        private readonly DungeonMinimap _source;
        private bool _visible;
        private readonly List<MinimapMarker> _markers = new List<MinimapMarker>();
        private readonly List<Texture2D> _legendTextures = new List<Texture2D>();
        private readonly List<Image> _legendLabels = new List<Image>();
        private readonly List<PlayerId> _legendPlayers = new List<PlayerId>();
        private readonly VisualElement _overlay;
        private readonly Image _preview;
        private readonly Camera _previewCamera;
        private RenderTexture _previewTarget;
        private IClassicOptionsAssets _textAssets;
        private Func<byte, string> _playerName;
        private ClassicFullMapLayout _layout;
        private int _legendHeight;
        private readonly Dictionary<RoomTypeId, Texture2D> _roomTextures = new Dictionary<RoomTypeId, Texture2D>();
        private IClassicResourceAssets _roomAssets;
        private MinimapMarker? _roomMarker;
        private Vector2 _viewport;
        public Camera PreviewCamera => _visible && _previewTarget != null ? _previewCamera : null;
        public void PreparePreview()
        {
            if (_visible) PreparePreview(Screen.width, Screen.height);
        }
        public PanelSettings Panel => _settings;
        public ClassicFullMap(Transform parent, DungeonMinimap source)
        {
            _source = source;
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
            _root.style.backgroundColor = Color.black;
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
            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.style.position = Position.Absolute;
            _overlay.style.width = Length.Percent(100); _overlay.style.height = Length.Percent(100);
            _overlay.generateVisualContent += DrawMarkers; _root.Add(_overlay);
            SetVisible(false);
        }
        public void SetLegendAssets(IClassicOptionsAssets assets, Func<byte, string> playerName, IClassicResourceAssets roomAssets)
        { _textAssets = assets; _playerName = playerName; _roomAssets = roomAssets; }
        public void Capture(WorldState world, GameDatabase database, PlayerId viewer)
        {
            if (!_visible) return;
            _markers.Clear();
            _roomMarker = null;
            TilePos hovered = ResolvePointer(Input.mousePosition);
            if (hovered.X > 0 && hovered.Y > 0 && hovered.X < world.Width - 1 && hovered.Y < world.Height - 1)
            {
                RoomInstance room = RoomIndex.RoomAt(world, database, world.TileIndex(hovered));
                if (room != null)
                {
                    var centre = new TilePos(room.CentreFieldX, room.CentreFieldY);
                    bool explored = world.Contains(centre) && (!world.FogOfWarEnabled || world.Tile(centre).IsExploredBy(viewer));
                    if (room.Definition.TypeId is RoomTypeId type
                        && ClassicFullMapMarkers.TryRoom(type, centre, explored, out var marker)) _roomMarker = marker;
                }
            }
            foreach (CreatureState creature in world.Creatures)
            {
                var tile = new TilePos(creature.PosX >> 12, creature.PosY >> 12);
                bool explored = world.Contains(tile) && (!world.FogOfWarEnabled || world.Tile(tile).IsExploredBy(viewer));
                var position = new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ));
                if (ClassicFullMapMarkers.TryCreature(position, creature.OwnerPlayerId, creature.InHand, explored,
                    _source.Palette, out MinimapMarker marker)) _markers.Add(marker);
            }
            if (_textAssets != null && _legendHeight != Screen.height)
            {
                _legendHeight = Screen.height;
                foreach (Texture2D texture in _legendTextures) UnityEngine.Object.Destroy(texture);
                foreach (Image label in _legendLabels) label.RemoveFromHierarchy();
                _legendTextures.Clear(); _legendLabels.Clear(); _legendPlayers.Clear();
                for (int i = 0; i < world.PlayerCount; i++)
                {
                    PlayerId player = world.PlayerAtIndex(i).PlayerId;
                    if (player.Value < PlayerIds.FirstKeeper) continue;
                    Texture2D texture = ClassicOptionsPanel.TextTexture(_textAssets.Font(Screen.height), _playerName(player.Value));
                    var label = new Image { image = texture, pickingMode = PickingMode.Ignore };
                    label.style.position = Position.Absolute; _root.Add(label);
                    _legendTextures.Add(texture); _legendLabels.Add(label); _legendPlayers.Add(player);
                }
            }
        }
        public void SetVisible(bool visible)
        {
            _visible=visible; _root.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            _previewCamera.enabled = visible;
        }
        public void Draw(Vector2Int viewport=default)
        {
            if (!_visible) return;
            int width=viewport.x>0?viewport.x:Screen.width, height=viewport.y>0?viewport.y:Screen.height;
            _viewport = new Vector2(width, height);
            ClassicFullMapLayout layout = ClassicFullMapLayout.Fit(width,height,_source.MapWidth,_source.MapHeight);
            _layout = layout;
            PreparePreview(width, height);
            _map.style.left=layout.Origin.X; _map.style.top=layout.Origin.Y;
            _map.style.width=layout.Size.X; _map.style.height=layout.Size.Y;
            Graphics.Blit(null,_target,_material);
            _overlay.MarkDirtyRepaint();
            for (int i = 0; i < _legendLabels.Count; i++)
            {
                Image label = _legendLabels[i]; Texture2D texture = _legendTextures[i];
                label.style.left = width * 474 / 640;
                label.style.top = height * 274 / 480 + (_legendPlayers[i].Value - 3) * (texture.height + height * 4 / 480);
                label.style.width = texture.width; label.style.height = texture.height;
                // 0x00404C14: legend names use Number + 0x1A; creature squares use + 0x11.
                label.tintColor = Colour(_source.Palette.Colours[26 + _legendPlayers[i].Value]);
            }
        }
        private void PreparePreview(int width, int height)
        {
            ClassicFullMapLayout layout = ClassicFullMapLayout.Fit(width,height,_source.MapWidth,_source.MapHeight);
            FullMapPreview preview = layout.Preview(width, height,
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
        private void DrawMarkers(MeshGenerationContext context)
        {
            if (_roomMarker is MinimapMarker room && _roomAssets != null)
            {
                if (!_roomTextures.TryGetValue(room.RoomType, out Texture2D texture))
                {
                    var image = _roomAssets.Image(ClassicFullMapMarkers.RoomArt(room.RoomType));
                    texture = MinimapFrameVisual.Decode(image);
                    _roomTextures.Add(room.RoomType, texture);
                }
                float width = (int)_viewport.x * 32 / 640, height = (int)_viewport.y * 32 / 480;
                float x = (int)(room.Position.X.Raw / (float)Fp32.One * _layout.Size.X / _layout.MapWidth - width / 2) + _layout.Origin.X;
                float y = (int)(room.Position.Y.Raw / (float)Fp32.One * _layout.Size.Y / _layout.MapHeight - height / 2) + _layout.Origin.Y;
                MeshWriteData icon = context.Allocate(4, 6, texture);
                icon.SetNextVertex(new Vertex { position = new Vector3(x, y, Vertex.nearZ), tint = Color.white, uv = new Vector2(0, 1) });
                icon.SetNextVertex(new Vertex { position = new Vector3(x + width, y, Vertex.nearZ), tint = Color.white, uv = new Vector2(1, 1) });
                icon.SetNextVertex(new Vertex { position = new Vector3(x + width, y + height, Vertex.nearZ), tint = Color.white, uv = new Vector2(1, 0) });
                icon.SetNextVertex(new Vertex { position = new Vector3(x, y + height, Vertex.nearZ), tint = Color.white, uv = new Vector2(0, 0) });
                icon.SetNextIndex(0); icon.SetNextIndex(1); icon.SetNextIndex(2);
                icon.SetNextIndex(2); icon.SetNextIndex(3); icon.SetNextIndex(0);
            }
            if (_markers.Count == 0) return;
            MeshWriteData mesh = context.Allocate(_markers.Count * 4, _markers.Count * 6);
            float size = 3 * Screen.height / 768f;
            for (int i = 0; i < _markers.Count; i++)
            {
                MinimapMarker marker = _markers[i];
                float x = (int)(marker.Position.X.Raw / (float)Fp32.One * _layout.Size.X / _layout.MapWidth - 1) + _layout.Origin.X;
                float y = (int)(marker.Position.Y.Raw / (float)Fp32.One * _layout.Size.Y / _layout.MapHeight - 1) + _layout.Origin.Y;
                Color32 colour = Colour(marker.ColourArgb);
                mesh.SetNextVertex(new Vertex { position = new Vector3(x, y, Vertex.nearZ), tint = colour });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x + size, y, Vertex.nearZ), tint = colour });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x + size, y + size, Vertex.nearZ), tint = colour });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x, y + size, Vertex.nearZ), tint = colour });
                ushort first = (ushort)(i * 4);
                mesh.SetNextIndex(first); mesh.SetNextIndex((ushort)(first + 1)); mesh.SetNextIndex((ushort)(first + 2));
                mesh.SetNextIndex((ushort)(first + 2)); mesh.SetNextIndex((ushort)(first + 3)); mesh.SetNextIndex(first);
            }
        }
        private static Color32 Colour(uint argb) => new Color32((byte)(argb >> 16),(byte)(argb >> 8),(byte)argb,(byte)(argb >> 24));
        public TilePos ResolvePointer(Vector3 screenPosition) =>
            ClassicFullMapLayout.Fit(Screen.width,Screen.height,_source.MapWidth,_source.MapHeight)
                .ResolveTile(new System.Numerics.Vector2(screenPosition.x,Screen.height-screenPosition.y));
        public void Dispose()
        {
            _target.Release(); UnityEngine.Object.Destroy(_target); UnityEngine.Object.Destroy(_material);
            if (_previewTarget != null) { _previewTarget.Release(); UnityEngine.Object.Destroy(_previewTarget); }
            foreach (Texture2D texture in _legendTextures) UnityEngine.Object.Destroy(texture);
            foreach (Texture2D texture in _roomTextures.Values) UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}

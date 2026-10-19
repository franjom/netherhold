// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    public sealed class MinimapFrameVisual : IDisposable
    {
        private const float FrameTexel = 204.8f / 128, CornerSize = 32 * FrameTexel;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly VisualElement _root;
        private readonly Image[] _corners = new Image[4];
        private readonly Texture2D[] _cornerTextures = new Texture2D[8];

        public MinimapFrameVisual(VisualElement parent, IMinimapAssets assets)
        {
            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute;
            _root.style.left = 8; _root.style.bottom = 8;
            _root.style.width = 179.2f; _root.style.height = 176;
            _root.style.overflow = Overflow.Hidden;
            parent.Add(_root);
            // The padded texture extends past the native clip; size interpretation is STRONGLY INFERRED (minimap-rendering.md).
            Add(assets.Frame, left: 0, bottom: -28.8f, width: 204.8f, height: 204.8f);
            for (int i = 0; i < 4; i++)
            {
                for (int active = 0; active < 2; active++)
                {
                    Texture2D texture = Decode(assets.Corner((MinimapCorner)i, active != 0));
                    _cornerTextures[i * 2 + active] = texture; _textures.Add(texture);
                }
                // Measured, not read: each 32-texel corner is drawn at the frame's 1.6 px a texel
                // over the frame's own corner art, at texels (0,0), (79,0), (0,78) and (79,78),
                // found by matching the sprites against gui/map/map. A candidate sprite renderer,
                // 0x0059B0C0, sizes sprites by resource size times render scale; whether the live
                // minimap uses it is open. The owner confirmed the result (2026-10-19).
                var corner = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                corner.style.position = Position.Absolute;
                corner.style.width = CornerSize; corner.style.height = CornerSize;
                corner.style.left = i == 1 || i == 3 ? 79 * FrameTexel : 0;
                corner.style.top = i >= 2 ? 78 * FrameTexel : 0;
                corner.style.display = DisplayStyle.None;
                _corners[i] = corner; _root.Add(corner);
            }
        }

        public void UpdateControls(System.Numerics.Vector2 pointer, ClassicHudAction selected, bool optionsVisible)
        {
            ClassicHudAction hover = ClassicHudControlLayout.ActionAt(pointer, false);
            for (int i = 0; i < 4; i++)
            {
                ClassicHudAction action = i == 0 ? ClassicHudAction.SelectQuery : i == 1 ? ClassicHudAction.SelectSell
                    : i == 2 ? ClassicHudAction.ToggleOptions : ClassicHudAction.ZoomIn;
                bool active = i == 2 ? optionsVisible : i < 2 && selected == action;
                bool visible = active || hover == action;
                _corners[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible) _corners[i].image = _cornerTextures[i * 2 + (active ? 1 : 0)];
            }
        }

        public static Texture2D Decode(RgbaImage image)
        {
            int row = checked(image.Width * 4);
            var bottomUp = new byte[image.Pixels.Length];
            for (int y = 0; y < image.Height; y++)
                Buffer.BlockCopy(image.Pixels, y * row, bottomUp, (image.Height - 1 - y) * row, row);
            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(bottomUp); texture.Apply();
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private void Add(RgbaImage source, float left, float bottom, float width, float height)
        {
            Texture2D texture = Decode(source); _textures.Add(texture);
            var image = new Image { image = texture, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute;
            image.style.left = left; image.style.bottom = bottom;
            image.style.width = width; image.style.height = height;
            _root.Add(image);
        }

        public static bool ContainsPointer(Vector3 position)
        {
            float scale = Screen.height / 768f;
            return position.x >= 8 * scale && position.x < 192 * scale
                && position.y >= 0 && position.y < 184 * scale;
        }

        public void Dispose()
        {
            _root.RemoveFromHierarchy();
            foreach (Texture2D texture in _textures) UnityEngine.Object.Destroy(texture);
        }
    }
}

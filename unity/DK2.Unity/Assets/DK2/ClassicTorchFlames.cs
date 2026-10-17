// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using DK2.Art;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Core;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DK2.Unity
{
    /// <summary>Install-backed, unlit additive wall flames sharing one presentation clock.</summary>
    internal sealed class ClassicTorchFlames : IDisposable
    {
        private readonly Transform _root;
        private readonly TerrainLattice _lattice;
        private readonly Dictionary<int, (SpriteRenderer Renderer, TorchView Torch, int PhaseOffsetRaw)> _drawn =
            new Dictionary<int, (SpriteRenderer, TorchView, int)>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly List<Sprite> _frames = new List<Sprite>();
        private readonly Material _material;
        private readonly Dk2Lcg _random = new Dk2Lcg(1);
        private readonly ClassicTorchFlameAnimation _animation = new ClassicTorchFlameAnimation();

        public ClassicTorchFlames(Transform root, TerrainLattice lattice, IEffectSpriteProvider provider)
        {
            _root = root;
            _lattice = lattice;
            if (!provider.TryResolve(ClassicTorchFlame.Asset, out ResolvedAsset asset)
                || !provider.TryGetSprite(asset, out EffectSpriteResource sprite)) return;
            Shader shader = Shader.Find("DK2/ClassicEffect");
            if (shader == null) throw new InvalidOperationException("The classic effect shader is unavailable.");
            _material = new Material(shader) { name = "wall flame" };
            _material.SetInt("_DstBlend", (int)BlendMode.One);
            for (int frame = 0; frame < sprite.StoredFrameCount; frame++)
            {
                if (!provider.TryLoadFrame(asset, frame, out RgbaImage image)) break;
                var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
                { name = "wall flame " + frame, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                texture.LoadRawTextureData(image.Pixels);
                texture.Apply(false, true);
                _textures.Add(texture);
                _frames.Add(Sprite.Create(texture, new Rect(0, 0, image.Width, image.Height),
                    new Vector2(0.5f, 0.5f), image.Width / (ClassicTorchFlame.SizeRaw / 4096f)));
            }
        }

        public void Set(in TorchView torch)
        {
            if (torch.Mesh.IsEmpty)
            {
                Remove(torch.FloorTileIndex);
                return;
            }
            if (_frames.Count == 0) return;
            if (!_drawn.TryGetValue(torch.FloorTileIndex, out var drawn))
            {
                var go = new GameObject("wall flame " + torch.FloorTileIndex);
                go.transform.SetParent(_root, false);
                go.layer = _root.gameObject.layer;
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                drawn = (renderer, torch, _random.NextInt(4096));
            }
            _drawn[torch.FloorTileIndex] = (drawn.Renderer, torch, drawn.PhaseOffsetRaw);
        }

        public void Remove(int tile)
        {
            if (_drawn.TryGetValue(tile, out var drawn)) Object.Destroy(drawn.Renderer.gameObject);
            _drawn.Remove(tile);
        }

        public void Advance() => _animation.Advance();

        public void Draw(Camera camera)
        {
            if (camera == null || _frames.Count == 0) return;
            foreach (var drawn in _drawn.Values)
            {
                FpPosition3 raw = ClassicTorchFlame.Position(drawn.Torch);
                var point = new System.Numerics.Vector3(raw.X.Raw / 4096f, raw.Y.Raw / 4096f, -raw.Z.Raw / 4096f);
                if (_lattice != null) point = _lattice.Deform(point, drawn.Torch.Wall.X, drawn.Torch.Wall.Y);
                drawn.Renderer.transform.localPosition = new Vector3(point.X, -point.Z, -point.Y);
                drawn.Renderer.transform.rotation = camera.transform.rotation;
                drawn.Renderer.sprite = _frames[_animation.Frame(_frames.Count, drawn.PhaseOffsetRaw)];
            }
        }

        public void Clear()
        {
            foreach (var drawn in _drawn.Values) Object.Destroy(drawn.Renderer.gameObject);
            _drawn.Clear();
        }

        public void Dispose()
        {
            Clear();
            foreach (Sprite frame in _frames) Object.Destroy(frame);
            foreach (Texture2D texture in _textures) Object.Destroy(texture);
            if (_material != null) Object.Destroy(_material);
        }
    }
}

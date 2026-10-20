// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Art.Textures;
using DK2.Core;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DK2.Unity
{
    /// <summary>Draws <see cref="StatusFlowers"/> as camera-facing quads over each shown creature
    /// (status-flowers.md §2): the ring <c>GUI\moods\H-00</c> tinted with the owner's colour, then the
    /// level badge <c>SL-01..SL-10</c> untinted over it. Pass 1: no blink, no cycling centre.</summary>
    public sealed class ClassicStatusFlowers : IDisposable
    {
        /// <summary>World width of a flower at native scale 0x400, in tiles. Eyeballed from the
        /// owner's DK2 screenshot (about a third of a tile); the native world size is not established.</summary>
        public const float SizeTiles = 0.4f;

        /// <summary>Native scale 0x200 against 0x400 while possessing.</summary>
        public const float PossessedScale = 0.5f;

        // The ring index's argument is unread (status-flowers.md §3); H-00 is a stand-in.
        private const string RingTexture = "GUI\\moods\\H-00";
        private const string LevelTexturePrefix = "GUI\\moods\\SL-";
        private const int Levels = 10;

        // Draws the badge this far nearer the camera than the ring, so sorting and depth put it on top.
        private const float BadgeLift = 0.01f;

        private readonly List<Object> _owned = new List<Object>();
        private readonly Dictionary<StatusFlowerColour, Mesh> _ringQuads = new Dictionary<StatusFlowerColour, Mesh>();
        private readonly Mesh _whiteQuad;
        private readonly Material _ring;
        private readonly Material[] _levels = new Material[Levels];
        private readonly float[] _levelScale = new float[Levels];

        public ClassicStatusFlowers(IClassicResourceAssets assets)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            Shader shader = Shader.Find("DK2/ClassicEffect");
            if (shader == null) throw new InvalidOperationException("The classic effect shader is unavailable.");
            RgbaImage ringImage = assets.Image(RingTexture);
            _ring = CreateMaterial(shader, ringImage, "status flower ring");
            for (int level = 1; level <= Levels; level++)
            {
                RgbaImage image = assets.Image(LevelTexturePrefix + level.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                _levels[level - 1] = CreateMaterial(shader, image, "status flower level " + level);
                // Same texel density as the ring: the badge's drawn size is not read.
                _levelScale[level - 1] = image.Width / (float)ringImage.Width;
            }
            _whiteQuad = Quad(Color.white);
        }

        /// <summary>Draws this frame's flowers. Creatures the scene does not show (fog, removed,
        /// inactive) get none. Call after the creatures are posed, with the world settled.</summary>
        public void Draw(Camera camera, DungeonScene scene, IReadOnlyList<StatusFlower> flowers, bool hudVisible, bool possessing)
        {
            if (camera == null || scene == null || !hudVisible) return;
            Quaternion facing = camera.transform.rotation;
            Vector3 towardCamera = -camera.transform.forward * BadgeLift;
            float size = SizeTiles * (possessing ? PossessedScale : 1f);
            for (int i = 0; i < flowers.Count; i++)
            {
                StatusFlower flower = flowers[i];
                if (!scene.TryGetCreaturePosition(new CreatureQueryTarget(flower.CreatureId), out Vector3 feet)) continue;
                Vector3 centre = feet + Vector3.up * (flower.Height.Raw / (float)Fp32.One);
                Graphics.RenderMesh(Params(_ring), RingQuad(flower.Colour), 0,
                    Matrix4x4.TRS(centre, facing, Vector3.one * size));
                int level = Mathf.Clamp(flower.Level, 1, Levels) - 1;
                Graphics.RenderMesh(Params(_levels[level]), _whiteQuad, 0,
                    Matrix4x4.TRS(centre + towardCamera, facing, Vector3.one * (size * _levelScale[level])));
            }
        }

        public void Dispose()
        {
            foreach (Object owned in _owned) if (owned != null) Object.Destroy(owned);
            _owned.Clear();
            _ringQuads.Clear();
        }

        private static RenderParams Params(Material material) => new RenderParams(material)
        { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };

        private Mesh RingQuad(StatusFlowerColour colour)
        {
            if (!_ringQuads.TryGetValue(colour, out Mesh mesh))
            {
                mesh = Quad(new Color32(colour.Red, colour.Green, colour.Blue, 255));
                _ringQuads.Add(colour, mesh);
            }
            return mesh;
        }

        private Material CreateMaterial(Shader shader, RgbaImage image, string name)
        {
            Texture2D texture = MinimapFrameVisual.Decode(image);
            texture.name = name;
            // Alpha-blended (SrcAlpha, OneMinusSrcAlpha), unlit, ZWrite Off, depth-tested. The
            // native sprite blend is open (status-flowers.md §3).
            var material = new Material(shader) { name = name, mainTexture = texture };
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _owned.Add(texture); _owned.Add(material);
            return material;
        }

        private Mesh Quad(Color32 colour)
        {
            var mesh = new Mesh { name = "status flower quad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0),
            });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetColors(new[] { colour, colour, colour, colour });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            return mesh;
        }
    }
}

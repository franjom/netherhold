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
    /// (status-flowers.md §2): the petal ring <c>GUI\moods\H-0n</c> in the owner's colour, then the
    /// centre: the level badge at half colour with a full-colour experience wedge, or a mood, job
    /// or status icon at full colour; then, over a hero that trigger action 42 marked, the Objective
    /// ring (0x00596770).</summary>
    public sealed class ClassicStatusFlowers : IDisposable
    {
        /// <summary>World width of a flower at native scale 0x400, in tiles. Eyeballed from the
        /// owner's DK2 screenshots (0.4, then 10%, 10% and 20% smaller on the owner's comparisons);
        /// the native world size is not established.</summary>
        public const float SizeTiles = 0.2592f;

        /// <summary>Native scale 0x200 against 0x400 while possessing.</summary>
        public const float PossessedScale = 0.5f;

        private const string Folder = "GUI\\moods\\";

        // Draws the centre this far nearer the camera than the ring, so sorting and depth put it on top.
        private const float CentreLift = 0.01f;

        private static readonly string[] Rings = { "H-00", "H-01", "H-02", "H-03", "H-04", "H-05" };

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int WedgeId = Shader.PropertyToID("_Wedge");

        private readonly IClassicResourceAssets _assets;
        private readonly Shader _shader;
        private readonly List<Object> _owned = new List<Object>();
        private readonly Dictionary<string, (Material Material, float Scale)> _art = new Dictionary<string, (Material, float)>();
        private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        private readonly Mesh _quad;
        private readonly int _ringWidth;

        public ClassicStatusFlowers(IClassicResourceAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _shader = Shader.Find("DK2/ClassicEffect");
            if (_shader == null) throw new InvalidOperationException("The classic effect shader is unavailable.");
            _ringWidth = assets.Image(Folder + "H-00").Width;
            _quad = Quad();
        }

        /// <summary>Draws this frame's flowers. Creatures the scene does not show (fog, removed,
        /// inactive) get none. Call after the creatures are posed, with the world settled.</summary>
        public void Draw(Camera camera, DungeonScene scene, IReadOnlyList<StatusFlower> flowers, bool hudVisible, bool possessing)
        {
            if (camera == null || scene == null || !hudVisible) return;
            Quaternion facing = camera.transform.rotation;
            Vector3 towardCamera = -camera.transform.forward * CentreLift;
            float size = SizeTiles * (possessing ? PossessedScale : 1f);
            int block = 0;
            for (int i = 0; i < flowers.Count; i++)
            {
                StatusFlower flower = flowers[i];
                if (!scene.TryGetCreaturePosition(new CreatureQueryTarget(flower.CreatureId), out Vector3 feet)) continue;
                Vector3 centre = feet + Vector3.up * (flower.Height.Raw / (float)Fp32.One);
                var colour = new Color32(flower.Colour.Red, flower.Colour.Green, flower.Colour.Blue, 255);
                (Material ring, float ringScale) = Art(Rings[Mathf.Clamp(flower.Ring, 0, 5)], ring: true);
                Graphics.RenderMesh(Params(ring, Block(ref block, colour, -1)), _quad, 0,
                    Matrix4x4.TRS(centre, facing, Vector3.one * (size * ringScale)));
                (Material middle, float scale) = Art(flower.Centre, ring: false);
                Graphics.RenderMesh(Params(middle, Block(ref block, colour, flower.ExperienceArcs ? flower.Experience : -1)), _quad, 0,
                    Matrix4x4.TRS(centre + towardCamera, facing, Vector3.one * (size * scale)));
                if (flower.Objective == null) continue;
                (Material objective, float objectiveScale) = Art(flower.Objective, ring: false);
                Graphics.RenderMesh(Params(objective, Block(ref block, colour, -1)), _quad, 0,
                    Matrix4x4.TRS(centre + towardCamera * 2, facing, Vector3.one * (size * objectiveScale)));
            }
        }

        public void Dispose()
        {
            foreach (Object owned in _owned) if (owned != null) Object.Destroy(owned);
            _owned.Clear();
            _art.Clear();
        }

        private MaterialPropertyBlock Block(ref int index, Color colour, float wedge)
        {
            if (index == _blocks.Count) _blocks.Add(new MaterialPropertyBlock());
            MaterialPropertyBlock block = _blocks[index++];
            block.SetColor(TintId, colour);
            block.SetFloat(WedgeId, wedge);
            return block;
        }

        private static RenderParams Params(Material material, MaterialPropertyBlock block) => new RenderParams(material)
        { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, matProps = block };

        /// <summary>One icon's material, loaded on first use. Icons share the ring's texel density.
        /// 0x00596850 loads Imprison and the Objectives at 0x2000 against 0x1000, but doubling their
        /// width on top drew the Lord's star twice the size of DK2's (SHOT0068), so their own
        /// larger texture seems to carry that already.</summary>
        private (Material, float) Art(string name, bool ring)
        {
            if (_art.TryGetValue(name, out var art)) return art;
            RgbaImage image = _assets.Image(Folder + name);
            Texture2D texture = MinimapFrameVisual.Decode(image);
            texture.name = name;
            // Alpha-blended (SrcAlpha, OneMinusSrcAlpha), unlit, ZWrite Off, depth-tested. The
            // native sprite blend is open (status-flowers.md §4).
            var material = new Material(_shader) { name = "status flower " + name, mainTexture = texture };
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            // After actor hover passes and the outline at +100. Terrain tag passes use negative
            // sorting orders (TransparentTerrain), which take precedence over material queues.
            // Flowers still depth-test against solid geometry.
            material.renderQueue = (int)RenderQueue.Transparent + 200;
            // H-xx is painted pale pink-grey; DK2 shows the petals in the flat keeper colour
            // (owner's screenshot: about 0.96 of the palette entry), so colour by luminance.
            if (ring) material.SetFloat("_TintByLuminance", 1);
            _owned.Add(texture); _owned.Add(material);
            float scale = image.Width / (float)_ringWidth;
            art = (material, scale);
            _art.Add(name, art);
            return art;
        }

        private Mesh Quad()
        {
            var mesh = new Mesh { name = "status flower quad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0),
            });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            var white = new Color32(255, 255, 255, 255);
            mesh.SetColors(new[] { white, white, white, white });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            return mesh;
        }
    }
}

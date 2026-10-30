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
    /// or status icon at full colour; then the Imprison tag when the viewer marked the creature
    /// (0x005961A0) and, over a hero that trigger action 42 marked, the Objective ring (0x00596770).</summary>
    public sealed class ClassicStatusFlowers : IDisposable
    {
        private const string Folder = "GUI\\moods\\";

        // Draws each later layer this much nearer the camera, along the eye ray, so sorting and depth
        // put it on top while it stays concentric on screen.
        private const float CentreLift = 0.01f;

        private static readonly string[] Rings = { "H-00", "H-01", "H-02", "H-03", "H-04", "H-05" };

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int WedgeId = Shader.PropertyToID("_Wedge");

        private readonly IClassicResourceAssets _assets;
        private readonly Shader _shader;
        private readonly List<Object> _owned = new List<Object>();
        private readonly Dictionary<string, Material> _art = new Dictionary<string, Material>();
        private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        private readonly Mesh _quad;
        private readonly ClassicFlowerDollar _dollar;

        public ClassicStatusFlowers(IClassicResourceAssets assets, DK2.Art.InstallArtProvider art = null, ClassicMeshes meshes = null)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _shader = Shader.Find("DK2/ClassicEffect");
            if (_shader == null) throw new InvalidOperationException("The classic effect shader is unavailable.");
            _quad = Quad();
            _dollar = new ClassicFlowerDollar(art, meshes);
        }

        /// <summary>Draws this frame's flowers. Creatures the scene does not show (fog, removed,
        /// inactive) get none. Call after the creatures are posed, with the world settled.</summary>
        public void Draw(Camera camera, DungeonScene scene, IReadOnlyList<StatusFlower> flowers, bool hudVisible, bool possessing)
        {
            if (camera == null || scene == null || !hudVisible) return;
            Quaternion facing = camera.transform.rotation;
            Vector3 eye = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            int block = 0;
            for (int i = 0; i < flowers.Count; i++)
            {
                StatusFlower flower = flowers[i];
                if (!scene.TryGetCreaturePosition(new CreatureQueryTarget(flower.CreatureId), out Vector3 feet)) continue;
                Vector3 centre = feet + Vector3.up * (flower.Height.Raw / (float)Fp32.One);
                if (flower.Dollar)
                {
                    _dollar.Draw(flower.CreatureId, centre, (float)(Time.unscaledTimeAsDouble * 4 % 1));
                    continue;
                }
                _dollar.Reset();
                var colour = new Color32(flower.Colour.Red, flower.Colour.Green, flower.Colour.Blue, 255);
                if (flower.DrawRing)
                    Sprite(Rings[Mathf.Clamp(flower.Ring, 0, 5)], centre, 0, colour, -1, ring: true);
                Sprite(flower.Centre, centre, 1, colour, flower.ExperienceArcs ? flower.Experience : -1, ring: false);
                if (flower.Imprison) Sprite("Imprison", centre, 2, colour, -1, ring: false);
                if (flower.Objective != null) Sprite(flower.Objective, centre, 3, colour, -1, ring: false);
            }

            // DK2 draws every layer at one point (no per-icon offset; 0x0059C360, 0x0057F3D0) and
            // layers them by draw order. Ours lifts each layer toward the camera for sorting, along
            // the eye ray so it stays concentric on screen (StatusFlowerRules.LayerPlacement). Valid for
            // a perspective camera only, which the runner always sets.
            void Sprite(string name, Vector3 anchor, int layer, Color colour, float wedge, bool ring)
            {
                var (at, scale) = StatusFlowerRules.LayerPlacement(Numerics(anchor), Numerics(eye), Numerics(forward), layer, CentreLift);
                Graphics.RenderMesh(Params(Art(name, ring), Block(ref block, colour, wedge)), _quad, 0,
                    Matrix4x4.TRS(new Vector3(at.X, at.Y, at.Z), facing,
                        Vector3.one * (StatusFlowerRules.WorldSize(name, possessing) * scale)));
            }
        }

        private static System.Numerics.Vector3 Numerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        public void Dispose()
        {
            _dollar.Dispose();
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

        /// <summary>One icon's material, loaded on first use. Its drawn size is the descriptor's
        /// (<see cref="StatusFlowerRules.WorldSize"/>), whatever the texture's pixel size.</summary>
        private Material Art(string name, bool ring)
        {
            if (_art.TryGetValue(name, out Material art)) return art;
            RgbaImage image = _assets.Image(Folder + name);
            Texture2D texture = MinimapFrameVisual.Decode(image);
            texture.name = name;
            // An "A_" material (0x00596850 loads kind 2; 0x0057C420, 0x00589250): SRCALPHA/INVSRCALPHA,
            // depth-tested, no depth write, texture times the vertex colour.
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
            _art.Add(name, material);
            return material;
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

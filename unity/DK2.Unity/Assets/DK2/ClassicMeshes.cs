// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using UnityEngine;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.Unity
{
    /// <summary>The classic look's meshes (ADR-0408): an install entry's KMF made into a Unity
    /// mesh and materials, once per entry and once per texture.</summary>
    /// <remarks>
    /// <para>The frame and the triangle order are <see cref="RenderMesh"/>'s, tested there; this
    /// class copies arrays and makes no geometric choice of its own. Image rows go in top first,
    /// so Unity's V, counted up from row 0, lands on the KMF's V, counted down from the top.</para>
    /// <para>The shader is found by name so that no URP type is compiled into the adapter
    /// (<c>docs/plans/2026-10-09-classic-look.md</c>, step 0). Every material is opaque for now;
    /// DK2's transparent and additive materials (the KMF flags, UNKNOWN in
    /// <c>src/DK2.Convert/README.md</c>) come later.</para>
    /// </remarks>
    public sealed class ClassicMeshes
    {
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string FallbackShader = "Standard";

        private readonly InstallArtProvider _art;
        private readonly Shader _shader;
        private readonly Dictionary<string, Built> _built = new Dictionary<string, Built>();
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private readonly Dictionary<(AssetRef, bool), PieceSource> _pieces = new Dictionary<(AssetRef, bool), PieceSource>();

        public ClassicMeshes(InstallArtProvider art)
        {
            _art = art;
            _shader = Shader.Find(LitShader);
            if (_shader == null)
            {
                Debug.LogWarning($"DK2: shader '{LitShader}' not found; is the project on URP? Using '{FallbackShader}'.");
                _shader = Shader.Find(FallbackShader);
            }
        }

        /// <summary>The name of the shader the materials use, for the on-screen diagnostics.</summary>
        public string ShaderName => _shader != null ? _shader.name : "none";

        /// <summary>A new object drawing the asset's model, parented and unplaced; null when the
        /// asset is not an install entry this provider answered or its KMF is a group.</summary>
        public GameObject TryCreate(in ResolvedAsset asset, string name, Transform parent)
        {
            if (asset.Source != AssetSource.InstallEntry || asset.ProviderName != _art.Name) return null;
            if (!TryBuild(asset, out Built built)) return null;

            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = built.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = built.Materials;
            return go;
        }

        /// <summary>A piece's geometry and materials for a chunk to copy, resolved straight from the
        /// install (the classic look's own tier); null when the install has no model for the key.
        /// <paramref name="lastTexture"/> takes each material's last alternative texture instead of
        /// its first: CLAIMED WALL lists Brick_DECAY3 to Brick, and the undamaged one is taken to
        /// be the plain name (STRONGLY INFERRED from the names; the damage frame is unread).</summary>
        public PieceSource Piece(AssetRef key, bool lastTexture = false)
        {
            if (_pieces.TryGetValue((key, lastTexture), out PieceSource cached)) return cached;

            PieceSource piece = null;
            if (_art.TryResolve(key, out ResolvedAsset asset) && _art.TryLoadModel(asset, out KmfModel model))
            {
                RenderMesh source = RenderMesh.FromModel(model);
                var materials = new Material[source.Triangles.Count];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = MaterialFor(i < model.Materials.Count ? model.Materials[i] : null, lastTexture);
                piece = new PieceSource(
                    ToUnity(source.Positions).ToArray(), ToUnity(source.Normals).ToArray(),
                    ToUnity(source.Uvs).ToArray(), source.Triangles, materials);
            }
            _pieces[(key, lastTexture)] = piece;
            return piece;
        }

        private bool TryBuild(in ResolvedAsset asset, out Built built)
        {
            if (_built.TryGetValue(asset.InstallEntry, out built)) return built != null;

            built = null;
            if (_art.TryLoadModel(asset, out KmfModel model))
            {
                RenderMesh source = RenderMesh.FromModel(model);
                var mesh = new Mesh { name = asset.InstallEntry };
                mesh.SetVertices(ToUnity(source.Positions));
                mesh.SetNormals(ToUnity(source.Normals));
                mesh.SetUVs(0, ToUnity(source.Uvs));
                mesh.subMeshCount = source.Triangles.Count;
                for (int i = 0; i < source.Triangles.Count; i++)
                    mesh.SetTriangles(source.Triangles[i], i, calculateBounds: false);
                mesh.RecalculateBounds();

                var materials = new Material[source.Triangles.Count];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = MaterialFor(i < model.Materials.Count ? model.Materials[i] : null);
                built = new Built(mesh, materials);
            }
            _built[asset.InstallEntry] = built;
            return built != null;
        }

        private Material MaterialFor(KmfMaterial kmf, bool lastTexture = false)
        {
            // The converter's order: the first named texture, else the environment texture.
            string texture = kmf == null ? ""
                : kmf.TextureNames.Count > 0 ? kmf.TextureNames[lastTexture ? kmf.TextureNames.Count - 1 : 0]
                : kmf.EnvironmentTextureName ?? "";
            if (_materials.TryGetValue(texture, out Material material)) return material;

            material = new Material(_shader) { name = texture.Length > 0 ? texture : "untextured" };
            if (texture.Length > 0)
            {
                try
                {
                    material.mainTexture = ToTexture(_art.LoadTexture(texture), texture);
                }
                catch (System.IO.FileNotFoundException)
                {
                    Debug.LogWarning($"DK2: engine texture '{texture}' not found; drawing it untextured.");
                }
            }
            _materials[texture] = material;
            return material;
        }

        private static Texture2D ToTexture(RgbaImage image, string name)
        {
            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, mipChain: true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[image.Width * image.Height];
            byte[] bytes = image.Pixels;
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(bytes[4 * i], bytes[4 * i + 1], bytes[4 * i + 2], bytes[4 * i + 3]);
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true);
            return texture;
        }

        private static List<Vector3> ToUnity(IReadOnlyList<NumericsVector3> values)
        {
            var result = new List<Vector3>(values.Count);
            foreach (NumericsVector3 value in values) result.Add(new Vector3(value.X, value.Y, value.Z));
            return result;
        }

        private static List<Vector2> ToUnity(IReadOnlyList<NumericsVector2> values)
        {
            var result = new List<Vector2>(values.Count);
            foreach (NumericsVector2 value in values) result.Add(new Vector2(value.X, value.Y));
            return result;
        }

        /// <summary>One install model as arrays in the renderer frame, triangles per material.</summary>
        public sealed class PieceSource
        {
            public PieceSource(Vector3[] positions, Vector3[] normals, Vector2[] uvs,
                IReadOnlyList<int[]> triangles, Material[] materials)
            {
                Positions = positions;
                Normals = normals;
                Uvs = uvs;
                Triangles = triangles;
                Materials = materials;
            }

            public Vector3[] Positions { get; }
            public Vector3[] Normals { get; }
            public Vector2[] Uvs { get; }
            public IReadOnlyList<int[]> Triangles { get; }
            public Material[] Materials { get; }
        }

        private sealed class Built
        {
            public Built(Mesh mesh, Material[] materials)
            {
                Mesh = mesh;
                Materials = materials;
            }

            public Mesh Mesh { get; }
            public Material[] Materials { get; }
        }
    }
}

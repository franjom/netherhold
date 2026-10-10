// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using UnityEngine;
using UnityEngine.Rendering;
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
    /// <para>Shaders are found by name, so no URP type is compiled into the adapter. A material is
    /// made per (texture, blend, self-illumination) as the KMF material says: opaque, alpha-blended
    /// or additive, none clipped, none shining, every face one-sided
    /// (<c>docs/research/kmf-materials.md</c>).</para>
    /// </remarks>
    public sealed class ClassicMeshes
    {
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string FallbackShader = "Standard";

        private readonly InstallArtProvider _art;
        private readonly Dictionary<string, RenderAnimation> _animations = new Dictionary<string, RenderAnimation>();
        private readonly Shader _shader;
        private readonly Dictionary<(string, bool), Built> _built = new Dictionary<(string, bool), Built>();
        private readonly Dictionary<(string, KmfBlend, float), Material> _materials =
            new Dictionary<(string, KmfBlend, float), Material>();
        private readonly Dictionary<Material, Material> _tagged = new Dictionary<Material, Material>();
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

        public void RestoreModel(GameObject go, in ResolvedAsset asset)
        {
            if (!TryBuild(asset, false, out Built built)) return;
            go.GetComponent<MeshFilter>().sharedMesh = built.Mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = built.Materials;
        }

        public bool ConfigureAnimation(GameObject go, in ResolvedAsset asset, bool loop, double seconds)
        {
            if (!_art.TryGetResource(asset, out Dk2ArtResource resource) || resource.Type != 6
                || !_art.TryLoadModel(asset, out KmfModel model) || model.Animation is not { IsComplete: true }
                || !TryBuild(asset, false, out Built built)) return false;
            if (!_animations.TryGetValue(asset.InstallEntry, out RenderAnimation clip))
            {
                clip = new RenderAnimation(model);
                _animations[asset.InstallEntry] = clip;
            }
            ClassicCreatureAnimation player = go.GetComponent<ClassicCreatureAnimation>();
            if (player == null) player = go.AddComponent<ClassicCreatureAnimation>();
            player.Configure(clip, built.Mesh, built.Materials,
                resource.FramesPerSecond > 0 ? resource.FrameCount / (double)resource.FramesPerSecond : 0d,
                loop, seconds);
            return true;
        }

        public GameObject TryCreate(AssetRef key, string name, Transform parent, bool lastFrame = false)
        {
            return _art.TryResolve(key, out ResolvedAsset asset) ? TryCreate(asset, name, parent, lastFrame) : null;
        }

        /// <summary>A new object drawing the asset's model, parented and unplaced; null when the
        /// asset is not an install entry this provider answered or its KMF is a group.</summary>
        public GameObject TryCreate(in ResolvedAsset asset, string name, Transform parent, bool lastFrame = false)
        {
            if (asset.Source != AssetSource.InstallEntry || asset.ProviderName != _art.Name) return null;
            if (!TryBuild(asset, lastFrame, out Built built)) return null;

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

        private bool TryBuild(in ResolvedAsset asset, bool lastFrame, out Built built)
        {
            if (_built.TryGetValue((asset.InstallEntry, lastFrame), out built)) return built != null;

            built = null;
            if (_art.TryLoadModel(asset, out KmfModel model))
            {
                RenderMesh source = RenderMesh.FromModel(model, lastFrame && model.Animation is { IsComplete: true }
                    ? model.Animation.Frames.Count - 1 : 0);
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
            _built[(asset.InstallEntry, lastFrame)] = built;
            return built != null;
        }

        private Material MaterialFor(KmfMaterial kmf, bool lastTexture = false)
        {
            // The converter's order: the first named texture, else the environment texture.
            string texture = kmf == null ? ""
                : kmf.TextureNames.Count > 0 ? kmf.TextureNames[lastTexture ? kmf.TextureNames.Count - 1 : 0]
                : kmf.EnvironmentTextureName ?? "";
            return kmf == null ? ForTexture(texture) : ForTexture(texture, kmf.Blend, kmf.SelfIllumination);
        }

        /// <summary>The dig tag's colour, which DK2 adds to a tagged piece's vertex light
        /// (<c>0x0059A050</c>, <c>0x0059A200</c>, summed by <c>0x0057B6D0</c>; tag-highlight.md).</summary>
        public static readonly Color TagTint = new Color(128f / 255f, 128f / 255f, 200f / 255f);

        /// <summary>BlueTexture's one colour, drawn over a tagged piece as AA_BlueTexture: ONE, ONE,
        /// no depth write (<c>0x0057C420</c>, <c>0x00589250</c>).</summary>
        public static readonly Color TagOverlayColour = new Color(63f / 255f, 64f / 255f, 1f);

        private Material _tagOverlay;

        /// <summary>A material's tagged twin: its texture lit by <see cref="TagTint"/> more, which
        /// URP's emission (texture × colour) adds as DK2's sum of light does. Shared.</summary>
        public Material Tagged(Material material)
        {
            if (_tagged.TryGetValue(material, out Material tagged)) return tagged;
            tagged = new Material(material) { name = material.name + " (tagged)" };
            // The tag's light adds to the material's own (its self-illumination, if any).
            Color own = material.IsKeywordEnabled("_EMISSION") ? material.GetColor("_EmissionColor") : Color.black;
            tagged.EnableKeyword("_EMISSION");
            tagged.SetTexture("_EmissionMap", material.mainTexture);
            tagged.SetColor("_EmissionColor", TagTint + own);
            tagged.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _tagged[material] = tagged;
            return tagged;
        }

        /// <summary>The additive pass over every tagged piece: flat <see cref="TagOverlayColour"/>.</summary>
        public Material TagOverlay()
        {
            if (_tagOverlay != null) return _tagOverlay;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var overlay = new Material(unlit) { name = "AA_BlueTexture", color = TagOverlayColour };
            overlay.SetFloat("_Surface", 1f);
            overlay.SetFloat("_Blend", 2f);
            overlay.SetFloat("_SrcBlend", (float)BlendMode.One);
            overlay.SetFloat("_DstBlend", (float)BlendMode.One);
            overlay.SetFloat("_ZWrite", 0f);
            overlay.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            overlay.SetOverrideTag("RenderType", "Transparent");
            overlay.renderQueue = (int)RenderQueue.Transparent;
            _tagOverlay = overlay;
            return overlay;
        }

        /// <summary>An opaque material of one engine texture by name, shared by every user of it.</summary>
        public Material ForTexture(string texture) => ForTexture(texture, KmfBlend.Opaque, 0f);

        /// <summary>A material of one engine texture drawn with a KMF blend, shared by every user of
        /// the same (texture, blend, self-illumination). <paramref name="selfIllumination"/> is the
        /// light the material adds to its vertices, 0 to 1 (<see cref="KmfMaterial.SelfIllumination"/>):
        /// URP's emission adds texture × colour, as DK2 adds the material colour before modulating.</summary>
        public Material ForTexture(string texture, KmfBlend blend, float selfIllumination)
        {
            var key = (texture, blend, selfIllumination);
            if (_materials.TryGetValue(key, out Material material)) return material;

            string name = texture.Length > 0 ? texture : "untextured";
            if (blend == KmfBlend.Additive)
            {
                // Additive: the constructor forces the colour to white, so the texture is drawn at
                // full light whatever lights the scene; Unlit is that.
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? _shader;
                material = new Material(unlit) { name = "AA_" + name, color = Color.white };
                MakeTransparent(material, BlendMode.One, BlendMode.One, 2f);
            }
            else
            {
                material = new Material(_shader) { name = blend == KmfBlend.AlphaBlend ? "A_" + name : name };
                // DK2 never enables specular light (SPECULARENABLE 0, 0x005899F0).
                material.SetFloat("_Smoothness", 0f);
                material.SetFloat("_SpecularHighlights", 0f);
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                material.SetFloat("_EnvironmentReflections", 0f);
                material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                if (blend == KmfBlend.AlphaBlend)
                    MakeTransparent(material, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 0f);
            }
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
            if (blend != KmfBlend.Additive && selfIllumination > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetTexture("_EmissionMap", material.mainTexture);
                material.SetColor("_EmissionColor", new Color(selfIllumination, selfIllumination, selfIllumination));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            _materials[key] = material;
            return material;
        }

        /// <summary>URP's transparent surface with an explicit blend and no depth write, as DK2's
        /// blended states clear z-write (<c>0x0057C420</c>). <paramref name="urpBlend"/> is URP's
        /// own _Blend enum (0 alpha, 2 additive), kept for its inspector.</summary>
        private static void MakeTransparent(Material material, BlendMode source, BlendMode destination, float urpBlend)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", urpBlend);
            material.SetFloat("_SrcBlend", (float)source);
            material.SetFloat("_DstBlend", (float)destination);
            material.SetFloat("_SrcBlendAlpha", (float)source);
            material.SetFloat("_DstBlendAlpha", (float)destination);
            material.SetFloat("_ZWrite", 0f);
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
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

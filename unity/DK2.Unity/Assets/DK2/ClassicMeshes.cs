// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
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
        public enum LightingMode { Instance, TerrainPiece }

        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string FallbackShader = "Standard";

        private readonly InstallArtProvider _art;
        private readonly Dictionary<string, RenderAnimation> _animations = new Dictionary<string, RenderAnimation>();
        private readonly Shader _shader;
        private readonly Dictionary<(string, bool, LightingMode), Built> _built =
            new Dictionary<(string, bool, LightingMode), Built>();
        private readonly Dictionary<(string, KmfBlend, float), Material> _materials =
            new Dictionary<(string, KmfBlend, float), Material>();
        private readonly Dictionary<Material, Material> _tagged = new Dictionary<Material, Material>();
        private readonly Dictionary<(Material, DK2.Data.Model.TerrainLightColour), Material> _tinted =
            new Dictionary<(Material, DK2.Data.Model.TerrainLightColour), Material>();
        private readonly Dictionary<(AssetRef, bool), PieceSource> _pieces = new Dictionary<(AssetRef, bool), PieceSource>();
        private readonly Dictionary<(AssetRef, int, DK2.Data.Model.TerrainLightColour), Material[]> _heartMaterials = new Dictionary<(AssetRef, int, DK2.Data.Model.TerrainLightColour), Material[]>();
        private readonly Dictionary<(Material[], DK2.Data.Model.TerrainLightColour), Material[]> _tintedSets = new Dictionary<(Material[], DK2.Data.Model.TerrainLightColour), Material[]>();

        public ClassicMeshes(InstallArtProvider art, bool classicVertexLighting = false, bool nativeInitialAmbient = false)
        {
            _art = art;
            Breakdown = new LoadBreakdown(art);
            NativeInitialAmbient = nativeInitialAmbient;
            _shader = classicVertexLighting ? Resources.Load<Shader>("Shaders/ClassicVertexLit") : null;
            UsesClassicVertexLighting = _shader != null && _shader.isSupported;
            if (!UsesClassicVertexLighting) _shader = Shader.Find(LitShader);
            if (_shader == null)
            {
                Debug.LogWarning($"DK2: shader '{LitShader}' not found; is the project on URP? Using '{FallbackShader}'.");
                _shader = Shader.Find(FallbackShader);
            }
        }

        public bool UsesClassicVertexLighting { get; }
        public bool NativeInitialAmbient { get; }

        /// <summary>Where art work's time goes, for the startup timings.</summary>
        public LoadBreakdown Breakdown { get; }

        /// <summary>The name of the shader the materials use, for the on-screen diagnostics.</summary>
        public string ShaderName => _shader != null ? _shader.name : "none";

        public void RestoreModel(GameObject go, in ResolvedAsset asset, DK2.Data.Model.TerrainLightColour addedLight = default)
        {
            if (!TryBuild(asset, false, out Built built)) return;
            go.GetComponent<MeshFilter>().sharedMesh = built.Mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = TintedMaterials(built.Materials, addedLight);
            _opaqueOf.Remove(go);
        }

        public bool ConfigureAnimation(GameObject go, in ResolvedAsset asset, bool loop, double seconds,
            Fp32? movementSpeed = null, AnimationReplacement replacement = AnimationReplacement.Immediate,
            bool hold = false, Fp32? initialMovementSpeed = null, Fp32? phaseStepOverride = null,
            DK2.Data.Model.TerrainLightColour addedLight = default)
        {
            using LoadBreakdown.Scope _ = LoadBreakdown.Span(Breakdown, LoadBreakdown.Work.Animation);
            if (!_art.TryGetResource(asset, out Dk2ArtResource resource) || resource.Type != 6
                || !_art.TryLoadModel(asset, out KmfModel model) || model.Animation is not { IsComplete: true }
                || !TryBuild(asset, false, out Built built)) return false;
            var clips = new RenderAnimation[3];
            var templates = new Mesh[3];
            var materials = new Material[3][];
            foreach (AnimationSegment segment in new[] { AnimationSegment.Start, AnimationSegment.Main, AnimationSegment.End })
            {
                if (segment == AnimationSegment.Start
                    && (resource.AnimationFlags & CreatureAnimationFlags.StartSegment) == 0) continue;
                if (segment == AnimationSegment.End
                    && (resource.AnimationFlags & CreatureAnimationFlags.EndSegment) == 0) continue;
                if (!_art.TryResolveSegment(asset, segment, out ResolvedAsset part)
                    || !_art.TryLoadModel(part, out KmfModel partModel)
                    || partModel.Animation is not { IsComplete: true } || !TryBuild(part, false, out Built partBuilt)) return false;
                if (!_animations.TryGetValue(part.InstallEntry, out RenderAnimation clip))
                    _animations[part.InstallEntry] = clip = new RenderAnimation(partModel);
                clips[(int)segment] = clip;
                templates[(int)segment] = partBuilt.Mesh;
                materials[(int)segment] = TintedMaterials(partBuilt.Materials, addedLight);
            }
            ClassicCreatureAnimation player = go.GetComponent<ClassicCreatureAnimation>();
            if (player == null) player = go.AddComponent<ClassicCreatureAnimation>();
            player.Configure(clips, templates, materials, new CreatureAnimationTiming
            {
                ResourceType = resource.Type, FrameCount = resource.FrameCount,
                FramesPerSecond = resource.FramesPerSecond, Flags = resource.AnimationFlags,
                StartSegmentFrames = resource.StartSegmentFrames, EndSegmentFrames = resource.EndSegmentFrames,
            }, loop, seconds, movementSpeed.HasValue ? new Fp32(resource.MovementScale) : null, movementSpeed,
                replacement, hold, initialMovementSpeed, phaseStepOverride);
            return true;
        }

        public void ConfigureHeartDecay(GameObject go, in ResolvedAsset asset, Fp32 phase,
            DK2.Data.Model.TerrainLightColour addedLight = default)
        {
            var key = (asset.Reference, phase.Raw, addedLight);
            if (!_heartMaterials.TryGetValue(key, out Material[] materials))
            {
                if (!_art.TryLoadModel(asset, out KmfModel model)) return;
                materials = new Material[model.Materials.Count];
                for (int i = 0; i < materials.Length; i++)
                {
                    KmfMaterial source = model.Materials[i];
                    materials[i] = source.TextureNames.Count == 0 ? MaterialFor(source)
                        : ForTexture(source.TextureNames[DungeonHeartVisual.TextureIndex(phase, source.TextureNames.Count)],
                            source.Blend, source.SelfIllumination);
                }
                for (int i = 0; i < materials.Length; i++) materials[i] = TerrainTinted(materials[i], addedLight);
                _heartMaterials[key] = materials;
            }
            go.GetComponent<ClassicCreatureAnimation>()?.SetMainMaterials(materials);
        }

        public GameObject TryCreate(AssetRef key, string name, Transform parent, bool lastFrame = false,
            LightingMode lightingMode = LightingMode.Instance)
        {
            return _art.TryResolve(key, out ResolvedAsset asset) ? TryCreate(asset, name, parent, lastFrame, lightingMode) : null;
        }

        /// <summary>A new object drawing the asset's model, parented and unplaced; null when the
        /// asset is not an install entry this provider answered or its KMF is a group.</summary>
        public GameObject TryCreate(in ResolvedAsset asset, string name, Transform parent, bool lastFrame = false,
            LightingMode lightingMode = LightingMode.Instance)
        {
            if (asset.Source != AssetSource.InstallEntry || asset.ProviderName != _art.Name) return null;
            if (!TryBuild(asset, lastFrame, out Built built, lightingMode)) return null;

            using LoadBreakdown.Scope _ = LoadBreakdown.Span(Breakdown, LoadBreakdown.Work.Objects);
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

            using LoadBreakdown.Scope _ = LoadBreakdown.Span(Breakdown, LoadBreakdown.Work.Mesh);
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

        private readonly Dictionary<(string, int), Built> _builtFrames = new Dictionary<(string, int), Built>();

        /// <summary>One frame of an animated model (an effect element of art type 6), built like a
        /// static one; false for a key with no complete animation. The frame count is
        /// <paramref name="frameCount"/> after a successful call.</summary>
        public bool TryGetMeshFrame(AssetRef key, int frame, out Mesh mesh, out Material[] materials, out int frameCount)
        {
            mesh = null; materials = null; frameCount = 0;
            if (!_art.TryResolve(key, out ResolvedAsset asset)) return false;
            if (!_art.TryLoadModel(asset, out KmfModel model) || model.Animation is not { IsComplete: true } animation
                || animation.Frames.Count == 0) return false;
            frameCount = animation.Frames.Count;
            frame = Mathf.Clamp(frame, 0, frameCount - 1);
            if (!_builtFrames.TryGetValue((asset.InstallEntry, frame), out Built built))
            {
                using LoadBreakdown.Scope _ = LoadBreakdown.Span(Breakdown, LoadBreakdown.Work.Mesh);
                RenderMesh source = RenderMesh.FromModel(model, frame);
                var frameMesh = new Mesh { name = asset.InstallEntry + " " + frame };
                frameMesh.SetVertices(ToUnity(source.Positions));
                frameMesh.SetNormals(ToUnity(source.Normals));
                frameMesh.SetUVs(0, ToUnity(source.Uvs));
                SetLightBounds(frameMesh, model);
                frameMesh.subMeshCount = source.Triangles.Count;
                for (int i = 0; i < source.Triangles.Count; i++)
                    frameMesh.SetTriangles(source.Triangles[i], i, calculateBounds: false);
                frameMesh.RecalculateBounds();
                var frameMaterials = new Material[source.Triangles.Count];
                for (int i = 0; i < frameMaterials.Length; i++)
                    frameMaterials[i] = MaterialFor(i < model.Materials.Count ? model.Materials[i] : null);
                _builtFrames[(asset.InstallEntry, frame)] = built = new Built(frameMesh, frameMaterials);
            }
            mesh = built.Mesh;
            materials = built.Materials;
            return true;
        }

        /// <summary>A Meshes.WAD entry by its own name (a group part's), built like any model.</summary>
        public bool TryGetMeshEntry(string entryName, out Mesh mesh, out Material[] materials)
        {
            if (_art.TryResolveEntry(entryName, out ResolvedAsset asset) && TryBuild(asset, false, out Built built))
            {
                mesh = built.Mesh;
                materials = built.Materials;
                return true;
            }
            mesh = null;
            materials = null;
            return false;
        }

        private readonly Dictionary<string, IReadOnlyList<EffectGroupPart>> _groupParts =
            new Dictionary<string, IReadOnlyList<EffectGroupPart>>();

        /// <summary>The members of a KMF group an effect flings (art type 8), each with its offset in
        /// position units. INFERRED from the mesh convention (RenderMesh.ToRenderFrame): the KMF's x and
        /// y are the world's, its z points down, a unit is a tile; the loader that fills the part array
        /// (heart-intro.md §3) is unread. Null when the install has no such group.</summary>
        public IReadOnlyList<EffectGroupPart> GroupParts(string groupName)
        {
            if (_groupParts.TryGetValue(groupName, out IReadOnlyList<EffectGroupPart> parts)) return parts;
            parts = null;
            if (_art.TryLoadGroup(groupName, out KmfGroup group))
            {
                var list = new List<EffectGroupPart>(group.Elements.Count);
                foreach (KmfGroupElement element in group.Elements)
                    list.Add(new EffectGroupPart(element.Name,
                        (int)(element.Position.X * Fp32.One), (int)(element.Position.Y * Fp32.One),
                        (int)(-element.Position.Z * Fp32.One)));
                parts = list;
            }
            _groupParts[groupName] = parts;
            return parts;
        }

        public bool TryGetMesh(AssetRef key, out Mesh mesh, out Material[] materials)
        {
            if (_art.TryResolve(key, out ResolvedAsset asset) && TryBuild(asset, false, out Built built))
            {
                mesh = built.Mesh;
                materials = built.Materials;
                return true;
            }
            mesh = null;
            materials = null;
            return false;
        }

        private bool TryBuild(in ResolvedAsset asset, bool lastFrame, out Built built,
            LightingMode lightingMode = LightingMode.Instance)
        {
            if (_built.TryGetValue((asset.InstallEntry, lastFrame, lightingMode), out built)) return built != null;

            using LoadBreakdown.Scope _ = LoadBreakdown.Span(Breakdown, LoadBreakdown.Work.Mesh);
            built = null;
            if (_art.TryLoadModel(asset, out KmfModel model))
            {
                RenderMesh source = RenderMesh.FromModel(model, lastFrame && model.Animation is { IsComplete: true }
                    ? model.Animation.Frames.Count - 1 : 0);
                var mesh = new Mesh { name = asset.InstallEntry };
                mesh.SetVertices(ToUnity(source.Positions));
                mesh.SetNormals(ToUnity(source.Normals));
                mesh.SetUVs(0, ToUnity(source.Uvs));
                SetLightBounds(mesh, model, lightingMode);
                mesh.subMeshCount = source.Triangles.Count;
                for (int i = 0; i < source.Triangles.Count; i++)
                    mesh.SetTriangles(source.Triangles[i], i, calculateBounds: false);
                mesh.RecalculateBounds();

                var materials = new Material[source.Triangles.Count];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = MaterialFor(i < model.Materials.Count ? model.Materials[i] : null);
                built = new Built(mesh, materials);
            }
            _built[(asset.InstallEntry, lastFrame, lightingMode)] = built;
            return built != null;
        }

        private static void SetLightBounds(Mesh mesh, KmfModel model,
            LightingMode lightingMode = LightingMode.Instance)
        {
            bool animated = model.Animation is { IsComplete: true };
            KmfMesh bounds = animated ? model.Animation.Frames[0] : model.Mesh;
            if (bounds == null) return;
            NumericsVector3 centre = RenderMesh.ToRenderFrame(bounds.Origin);
            var values = new List<Vector4>(mesh.vertexCount);
            var markers = new List<Vector2>(mesh.vertexCount);
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                values.Add(new Vector4(centre.X, centre.Y, centre.Z, bounds.BoundsScale));
                markers.Add(new Vector2(lightingMode == LightingMode.Instance ? 1 : 0, 1));
            }
            // UV2 remains the terrain table anchor; clones retain these authored draw bounds.
            mesh.SetUVs(2, values);
            mesh.SetUVs(3, markers);
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
            // Additive materials already supply white diffuse before native RGB saturation.
            if (!material.HasProperty("_EmissionColor")) return material;
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

        private Material[] TintedMaterials(Material[] source, DK2.Data.Model.TerrainLightColour addedLight)
        {
            if (addedLight == default) return source;
            var key = (source, addedLight);
            if (_tintedSets.TryGetValue(key, out Material[] materials)) return materials;
            materials = new Material[source.Length];
            for (int i = 0; i < source.Length; i++) materials[i] = TerrainTinted(source[i], addedLight);
            _tintedSets.Add(key, materials);
            return materials;
        }

        /// <summary>Keyed by the tint packed as 0xRRGGBB, so a lookup neither boxes nor hashes by reflection.</summary>
        private readonly Dictionary<(Material, int), Material> _elementTinted =
            new Dictionary<(Material, int), Material>();

        /// <summary>An effect element's mesh material lit by the element's tint more: DK2 starts a
        /// vertex's light at the material colour plus the RenderInfo tint (the element's RGB, ×255 in
        /// <c>0x00580480</c>) before adding the lights (<c>0x00581BE0</c>). An additive material is
        /// already white and returned as it is. Shared per (material, tint).</summary>
        public Material ElementTinted(Material material, Color32 tint)
        {
            if (material == null || (tint.r | tint.g | tint.b) == 0 || !material.HasProperty("_EmissionColor")
                || material.name.StartsWith("AA_", System.StringComparison.Ordinal)) return material;
            int packed = tint.r << 16 | tint.g << 8 | tint.b;
            if (_elementTinted.TryGetValue((material, packed), out Material tinted)) return tinted;
            tinted = new Material(material) { name = material.name + " (element tint)" };
            Color own = material.IsKeywordEnabled("_EMISSION") ? material.GetColor("_EmissionColor") : Color.black;
            tinted.EnableKeyword("_EMISSION");
            tinted.SetTexture("_EmissionMap", material.mainTexture);
            tinted.SetColor("_EmissionColor", own + new Color(tint.r / 255f, tint.g / 255f, tint.b / 255f, 0f));
            tinted.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _elementTinted[(material, packed)] = tinted;
            return tinted;
        }

        public Material TerrainTinted(Material material, DK2.Data.Model.TerrainLightColour tint)
        {
            tint = ClassicTerrainTint.PieceColour(tint);
            if (tint == default || !material.HasProperty("_EmissionColor")) return material;
            if (_tinted.TryGetValue((material, tint), out Material tinted)) return tinted;
            tinted = new Material(material) { name = material.name + " (terrain light)" };
            Color own = material.IsKeywordEnabled("_EMISSION") ? material.GetColor("_EmissionColor") : Color.black;
            tinted.EnableKeyword("_EMISSION");
            tinted.SetTexture("_EmissionMap", material.mainTexture);
            tinted.SetColor("_EmissionColor", own + new Color(tint.Red / 255f, tint.Green / 255f, tint.Blue / 255f, 0f));
            tinted.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _tinted[(material, tint)] = tinted;
            return tinted;
        }

        /// <summary>RedTexture's one colour, drawn over the Hand's hover target as AA_RedTexture
        /// (global 0x006FCD73, set by Presentation_Update at 0x0043BE40; status-flowers.md §1).</summary>
        public static readonly Color HoverOverlayColour = new Color(1f, 64f / 255f, 65f / 255f);

        private Material _hoverOverlay;

        /// <summary>The additive pass over the hovered creature: flat <see cref="HoverOverlayColour"/>,
        /// every frame, no pulse.</summary>
        public Material HoverOverlay() => _hoverOverlay ??= Additive("AA_RedTexture", HoverOverlayColour);

        /// <summary>The additive pass over every tagged piece: flat <see cref="TagOverlayColour"/>.</summary>
        public Material TagOverlay() => _tagOverlay ??= Additive("AA_BlueTexture", TagOverlayColour);

        private static Material Additive(string name, Color colour)
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var overlay = new Material(unlit) { name = name, color = colour };
            overlay.SetFloat("_Surface", 1f);
            overlay.SetFloat("_Blend", 2f);
            overlay.SetFloat("_SrcBlend", (float)BlendMode.One);
            overlay.SetFloat("_DstBlend", (float)BlendMode.One);
            overlay.SetFloat("_ZWrite", 0f);
            overlay.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            overlay.SetOverrideTag("RenderType", "Transparent");
            overlay.renderQueue = (int)RenderQueue.Transparent;
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

            using LoadBreakdown.Scope _ = LoadBreakdown.Span(Breakdown, LoadBreakdown.Work.Material);
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
                    RgbaImage image = _art.LoadTexture(texture);
                    using (LoadBreakdown.Span(Breakdown, LoadBreakdown.Work.Texture))
                        material.mainTexture = ToTexture(image, texture);
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
        private readonly Dictionary<(Material, byte, bool), Material> _faded = new Dictionary<(Material, byte, bool), Material>();
        private readonly Dictionary<GameObject, Material[]> _opaqueOf = new Dictionary<GameObject, Material[]>();

        /// <summary>Draws a static mesh object at <paramref name="alpha"/> (object <c>+0x8F</c>): opaque
        /// at 255 with its own materials, hidden at 0, and between with alpha-blended twins of its
        /// materials (one per material and alpha, shared). The Heart's Arches and Big Steps fade in so
        /// during the opening sequence (heart-intro.md).</summary>
        public void SetAlpha(GameObject go, byte alpha)
        {
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            if (alpha == 255)
            {
                if (_opaqueOf.TryGetValue(go, out Material[] opaque))
                {
                    renderer.sharedMaterials = opaque;
                    _opaqueOf.Remove(go);
                }
                renderer.enabled = true;
                return;
            }
            if (!_opaqueOf.TryGetValue(go, out Material[] source)) _opaqueOf[go] = source = renderer.sharedMaterials;
            renderer.enabled = alpha > 0;
            if (alpha == 0) return;
            var faded = new Material[source.Length];
            for (int i = 0; i < source.Length; i++) faded[i] = Faded(source[i], alpha, writeDepth: true);
            renderer.sharedMaterials = faded;
        }

        public Material SightFog(Material source, byte packedAlpha) => Faded(source, packedAlpha, writeDepth: false);

        /// <summary>An alpha-blended twin. A fading model (<paramref name="writeDepth"/>) keeps writing
        /// depth: the Heart's Arches and Big Steps fade in as several objects, and without depth Unity
        /// orders them by centre, so a statue behind an arch drew over it until the fade ended (owner,
        /// 2026-10-20). DK2 sorts its blended triangles; depth is the visual equivalent here.</summary>
        private Material Faded(Material source, byte alpha, bool writeDepth)
        {
            if (source == null) return null;
            var cacheKey = (source, (byte)alpha, writeDepth);
            if (_faded.TryGetValue(cacheKey, out Material faded)) return faded;
            faded = new Material(source) { name = source.name + " (alpha " + alpha + ")" };
            MakeTransparent(faded, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 0f);
            if (writeDepth) faded.SetFloat("_ZWrite", 1f);
            if (faded.HasProperty("_BaseColor"))
            {
                Color colour = faded.GetColor("_BaseColor");
                colour.a = alpha / 255f;
                faded.SetColor("_BaseColor", colour);
            }
            _faded[cacheKey] = faded;
            return faded;
        }

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
            // RGBA32's bytes are the image's, in the same order SetPixels32 took them.
            texture.SetPixelData(image.Pixels, mipLevel: 0);
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

        /// <summary>Exclusive wall time per kind of art work: a span charges only its own time, not
        /// that of spans inside it, and decoding done inside any span is charged to Decode.</summary>
        public sealed class LoadBreakdown
        {
            public enum Work { None, Terrain, Mesh, Material, Texture, Animation, Objects, Decode, Count }

            private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
            private readonly double[] _milliseconds = new double[(int)Work.Count];
            private readonly InstallArtProvider _art;
            private Work _current;
            private double _since;
            private double _decodedSince;

            public LoadBreakdown(InstallArtProvider art) => _art = art;

            /// <summary>A span of <paramref name="work"/>; does nothing when there is no breakdown.</summary>
            public static Scope Span(LoadBreakdown breakdown, Work work)
            {
                if (breakdown == null) return default;
                Work outer = breakdown._current;
                breakdown.Switch(work);
                return new Scope(breakdown, outer);
            }

            /// <summary>The totals so far, in milliseconds, indexed by <see cref="Work"/>.</summary>
            public double[] Snapshot()
            {
                Switch(_current);
                return (double[])_milliseconds.Clone();
            }

            private void Switch(Work next)
            {
                double now = _clock.Elapsed.TotalMilliseconds;
                double decoded = _art?.DecodeTime.TotalMilliseconds ?? 0;
                _milliseconds[(int)Work.Decode] += decoded - _decodedSince;
                _milliseconds[(int)_current] += now - _since - (decoded - _decodedSince);
                _since = now;
                _decodedSince = decoded;
                _current = next;
            }

            public readonly struct Scope : System.IDisposable
            {
                private readonly LoadBreakdown _breakdown;
                private readonly Work _outer;

                public Scope(LoadBreakdown breakdown, Work outer)
                {
                    _breakdown = breakdown;
                    _outer = outer;
                }

                public void Dispose() => _breakdown?.Switch(_outer);
            }
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

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Art;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace DK2.Unity
{
    public sealed class ClassicConstructionEffects : IDisposable
    {
        private static readonly ProfilerMarker DrawMarker = new ProfilerMarker("DK2.Effects.Draw");
        private static readonly ProfilerMarker ResourceMarker = new ProfilerMarker("DK2.Effects.PrepareResourceCold");
        private sealed class Group
        {
            public ParticleSystem System;
            public ParticleSystem.Particle[] Buffer = Array.Empty<ParticleSystem.Particle>();
            public int Count, SpriteFrames;
            public Material Material;
            public bool IsMesh;
            /// <summary>A mesh element's model and its materials, one per sub-mesh: drawn directly
            /// each frame, since a particle system draws a mesh with one material only and the
            /// Heart's plug carries five.</summary>
            public Mesh Mesh;
            public Material[] Materials;
            /// <summary>An animated model's frames (art type 6) and its authored rate.</summary>
            public Mesh[] Frames;
            public Material[][] FrameMaterials;
            public int FramesPerSecond;
            public readonly List<Texture2D> Textures = new List<Texture2D>();
            public readonly List<Sprite> Sprites = new List<Sprite>();
            /// <summary>A sprite's shape (EffectSpriteShape): flat sprites, and every billboard when the blended
            /// terrain draws them, are rebuilt into one mesh each frame from <see cref="Quads"/>.</summary>
            public EffectSpriteShape Shape = new EffectSpriteShape(false, false, 1f, 1f);
            public bool IsFlat => Shape.IsFlat;
            public readonly EffectQuadBuffer Quads = new EffectQuadBuffer();
            public Mesh FlatMesh;
            public readonly List<Vector3> FlatVertices = new List<Vector3>();
            public readonly List<Vector2> FlatUvs = new List<Vector2>();
            public readonly List<Color32> FlatColours = new List<Color32>();
            public readonly List<int> FlatTriangles = new List<int>();
        }

        private readonly Transform _root;
        private readonly IEffectSpriteProvider _sprites;
        private readonly ClassicMeshes _meshes;
        private readonly TransparentTerrain _transparent;
        /// <summary>Cached render resources per sprite asset or group-part mesh entry.</summary>
        private readonly Dictionary<(AssetRef, string), Group> _groups = new Dictionary<(AssetRef, string), Group>();
        private readonly EffectDrawList _list;
        public RoomConstructionParticles Model => _list.Model;
        public ShotBodyParticles ShotBodies => _list.ShotBodies;

        public ClassicConstructionEffects(Transform root, GameDatabase database, IEffectSpriteProvider sprites, ulong seed,
            Func<RoomConstructionEffect, bool> visible = null,
            Func<ViewEffectRequest, bool> effectVisible = null,
            Func<FpPosition3, Fp32> floorHeight = null,
            Func<EffectTime> clock = null,
            ClassicMeshes meshes = null,
            Func<ViewEffectRequest, CaptureEdges> captureEdges = null,
            Func<int, uint, EffectAttachment> attachment = null,
            Func<ShotId, ShotEffectAttachment> shotAttachment = null,
            Func<ViewEffectRequest, bool> cameraVisible = null,
            Func<TilePos, TerrainFlags> terrainFlags = null,
            Func<TilePos, bool> solidTile = null,
            Func<ShotState, bool> shotVisible = null,
            Func<CreatureId, Fp32?> creatureGroundHeight = null,
            PlayerId? viewer = null)
        {
            _root = root;
            _sprites = sprites;
            _meshes = meshes;
            _transparent = root.GetComponentInChildren<TransparentTerrain>();
            // The styles, the seeds and the default stopwatch clock (ADR-0458) are the draw list's.
            _list = new EffectDrawList(database, seed, clock, visible, effectVisible, floorHeight,
                captureEdges: captureEdges,
                groupParts: meshes != null ? meshes.GroupParts : null, attachment: attachment,
                shotAttachment: shotAttachment, cameraVisible: cameraVisible,
                terrainFlags: terrainFlags, solidTile: solidTile, shotVisible: shotVisible,
                creatureGroundHeight: creatureGroundHeight, viewer: viewer);
        }

        /// <summary>The frame's light table of a tile, for sprites that take light (<see cref="ConstructionParticle.TakesLight"/>).</summary>
        public TileLightLookup TileLights { get => _list.TileLights; set => _list.TileLights = value; }

        /// <summary>Runs the effect updates due by now and returns the instances' lights, which
        /// the frame's lighting takes before it is prepared; <see cref="Draw"/> then draws this state.</summary>
        public IReadOnlyList<EffectLightView> Advance() => _list.Advance();

        public void CaptureShots(IReadOnlyList<ShotState> shots, float fraction) => _list.CaptureShots(shots, fraction);

        private static Color32 ToUnity(EffectColour colour) => new Color32(colour.Red, colour.Green, colour.Blue, 255);
        private static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        private static System.Numerics.Vector3 Numerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        public void Draw()
        {
            using var drawScope = DrawMarker.Auto();
            _transparent?.BeginEffectFrame();
            float fraction = _list.Fraction;
            Camera camera = Camera.main;
            Vector3 cameraUp = camera != null ? camera.transform.up : Vector3.up;
            Vector3 cameraRight = camera != null ? camera.transform.right : Vector3.right;
            System.Numerics.Vector3 up = Numerics(cameraUp), right = Numerics(cameraRight);
            foreach (Group group in _groups.Values)
            {
                if (group == null) continue;
                group.Count = 0;
                if (!group.IsFlat && _transparent == null) continue;
                group.Quads.Clear();
            }
            EffectParticleStream particles = _list.Particles;
            for (int i = 0; i < particles.Count; i++)
            {
                ConstructionParticle source = particles[i];
                Group group = For(source.Definition, source.MeshEntry, _list.GroupKey(source));
                if (group == null) continue;
                System.Numerics.Vector3 at = EffectDrawList.Position(source, fraction);
                Vector3 position = ToUnity(at);
                if (group.IsMesh)
                {
                    float size = EffectDrawList.Size(source);
                    var matrix = Matrix4x4.TRS(position, RenderEulerRotation.Of(MeshOrientation.Particle(source, fraction)), Vector3.one * size);
                    Mesh mesh = group.Mesh;
                    Material[] materials = group.Materials;
                    if (group.Frames != null)
                    {
                        int frame = EffectDrawList.MeshFrame(source, group.Frames.Length, group.FramesPerSecond);
                        mesh = group.Frames[frame];
                        materials = group.FrameMaterials[frame];
                    }
                    Color32 tint = ToUnity(EffectDrawList.Tint(source));
                    for (int sub = 0; sub < materials.Length; sub++)
                    {
                        if (materials[sub] == null) continue;
                        var parameters = new RenderParams(_meshes.ElementTinted(materials[sub], tint))
                        {
                            shadowCastingMode = ShadowCastingMode.Off,
                            receiveShadows = false,
                            layer = _root.gameObject.layer,
                            // Culling bounds about the particle: the default is an empty box at the origin.
                            worldBounds = new Bounds(position, Vector3.one * (4f * size + 1f)),
                        };
                        if (_transparent != null && _meshes.IsAdditiveNamed(materials[sub]))
                            _transparent.AddEffectMesh(mesh, sub, materials[sub], matrix);
                        else Graphics.RenderMesh(parameters, mesh, sub, matrix);
                    }
                    continue;
                }
                if (group.IsFlat)
                {
                    _list.AddFlatQuad(group.Quads, source, fraction, group.SpriteFrames);
                    continue;
                }
                if (group.Count == group.Buffer.Length) Array.Resize(ref group.Buffer, group.Buffer.Length * 2);
                EffectBillboard billboard = _list.Billboard(source, at, group.Shape, right, up);
                if (_transparent != null)
                {
                    EffectDrawList.AddBillboardQuad(group.Quads, source, billboard, right, up, group.SpriteFrames);
                    continue;
                }
                group.Buffer[group.Count++] = new ParticleSystem.Particle
                {
                    position = ToUnity(billboard.Centre),
                    startColor = ToUnity(billboard.Colour),
                    rotation3D = Vector3.zero,
                    startSize3D = new Vector3(billboard.Width, billboard.Height, 1f),
                    startLifetime = 1f,
                    remainingLifetime = source.RemainingSpriteLifetimeFraction,
                    randomSeed = (uint)(i + 1),
                };
            }
            foreach (Group group in _groups.Values)
            {
                if (group == null || group.IsMesh) continue;
                if (group.IsFlat || _transparent != null)
                {
                    DrawFlat(group);
                    continue;
                }
                var main = group.System.main;
                if (main.maxParticles < group.Count) main.maxParticles = group.Buffer.Length;
                group.System.SetParticles(group.Buffer, group.Count);
            }
            _transparent?.EndEffectFrame();
        }

        // Uploads the draw list's quads (EffectQuadBuffer) through the group's reused lists.
        private void DrawFlat(Group group)
        {
            EffectQuadBuffer quads = group.Quads;
            if (quads.VertexCount == 0) return;
            if (group.FlatMesh == null)
            {
                group.FlatMesh = new Mesh { name = group.Material.name + " (flat)" };
                group.FlatMesh.MarkDynamic();
            }
            group.FlatVertices.Clear(); group.FlatUvs.Clear(); group.FlatColours.Clear(); group.FlatTriangles.Clear();
            ReadOnlySpan<System.Numerics.Vector3> vertices = quads.Vertices;
            ReadOnlySpan<System.Numerics.Vector2> uvs = quads.Uvs;
            ReadOnlySpan<EffectColour> colours = quads.Colours;
            for (int v = 0; v < vertices.Length; v++)
            {
                group.FlatVertices.Add(ToUnity(vertices[v]));
                group.FlatUvs.Add(new Vector2(uvs[v].X, uvs[v].Y));
                group.FlatColours.Add(ToUnity(colours[v]));
            }
            ReadOnlySpan<int> indices = quads.Indices;
            for (int n = 0; n < indices.Length; n++) group.FlatTriangles.Add(indices[n]);
            Mesh mesh = group.FlatMesh;
            mesh.Clear();
            mesh.SetVertices(group.FlatVertices);
            mesh.SetUVs(0, group.FlatUvs);
            mesh.SetColors(group.FlatColours);
            mesh.SetTriangles(group.FlatTriangles, 0, calculateBounds: true);
            if (_transparent != null)
            {
                _transparent.AddEffectMesh(mesh, 0, group.Material, Matrix4x4.identity);
                return;
            }
            var parameters = new RenderParams(group.Material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = _root.gameObject.layer,
                worldBounds = mesh.bounds,
            };
            Graphics.RenderMesh(parameters, mesh, 0, Matrix4x4.identity);
        }

        private Group MeshGroup(EffectElementDef definition, string meshEntry, AssetRef key, EffectGroupKind kind)
        {
            Mesh mesh = null; Material[] materials = null;
            Mesh[] frames = null; Material[][] frameMaterials = null;
            bool found = false;
            if (_meshes != null && kind == EffectGroupKind.AnimatedMesh)
            {
                // An animated model: every frame built once, drawn by the particle's age.
                if (_meshes.TryGetMeshFrame(key, 0, out mesh, out materials, out int frameCount))
                {
                    frames = new Mesh[frameCount];
                    frameMaterials = new Material[frameCount][];
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        _meshes.TryGetMeshFrame(key, frame, out frames[frame], out frameMaterials[frame], out _);
                    }
                    found = true;
                }
            }
            else if (_meshes != null)
            {
                found = meshEntry != null
                    ? _meshes.TryGetMeshEntry(meshEntry, out mesh, out materials)
                    : _meshes.TryGetMesh(key, out mesh, out materials);
            }
            if (!found)
            {
                _groups.Add((key, meshEntry ?? ""), null);
                return null;
            }
            // An additive effect mesh draws at the texture (STRONGLY INFERRED, heart-intro.md §6):
            // the material colour is forced white (0x0057C420), the vertex colour is that plus the
            // element tint and the lights (0x00581BE0), stage 0 modulates (0x00589250, op 4, never
            // 2X or 4X), and the blend is ONE, ONE (ADR-0492, superseding ADR-0476's doubling).
            var group = new Group
            {
                IsMesh = true, Mesh = mesh, Materials = materials, Frames = frames, FrameMaterials = frameMaterials,
                FramesPerSecond = EffectDrawList.AuthoredFramesPerSecond(definition),
            };
            _groups.Add((key, meshEntry ?? ""), group);
            return group;
        }

        /// <param name="meshEntry">A group part's mesh entry, drawn in place of the definition's art.</param>
        /// <param name="key">The draw list's <see cref="EffectDrawList.GroupKey"/>.</param>
        private Group For(EffectElementDef definition, string meshEntry, AssetRef key)
        {
            if (_groups.TryGetValue((key, meshEntry ?? ""), out Group existing)) return existing;
            using var resourceScope = ResourceMarker.Auto();
            EffectGroupKind kind = EffectDrawList.Classify(definition, meshEntry);
            if (kind != EffectGroupKind.Sprite) return MeshGroup(definition, meshEntry, key, kind);
            if (_sprites == null || !_sprites.TryResolve(key, out ResolvedAsset asset)
                || !_sprites.TryGetSprite(asset, out EffectSpriteResource resource))
            {
                _groups.Add((key, ""), null);
                return null;
            }
            Shader shader = Shader.Find("DK2/ClassicEffect");
            if (shader == null) throw new InvalidOperationException("The classic effect shader is unavailable.");
            var group = new Group { Material = new Material(shader) { name = key.Key } };
            EffectBlend blend = EffectDrawList.Blend(resource.Blend);
            group.Material.SetInt("_SrcBlend", (int)ToUnity(blend.Source));
            group.Material.SetInt("_DstBlend", (int)ToUnity(blend.Destination));
            var images = new List<RgbaImage>();
            for (int frame = 0; frame < resource.StoredFrameCount; frame++)
            {
                if (_sprites.TryLoadFrame(asset, frame, out RgbaImage image)) images.Add(image);
            }
            int width = images[0].Width, height = images[0].Height, atlasWidth = width * images.Count;
            var pixels = new byte[atlasWidth * height * 4];
            for (int frame = 0; frame < images.Count; frame++)
            {
                if (images[frame].Width != width || images[frame].Height != height)
                    throw new InvalidOperationException("Sprite frames must have equal dimensions.");
                for (int row = 0; row < height; row++)
                    Buffer.BlockCopy(images[frame].Pixels, row * width * 4, pixels, (row * atlasWidth + frame * width) * 4, width * 4);
            }
            var texture = new Texture2D(atlasWidth, height, TextureFormat.RGBA32, false)
            { name = key.Key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.LoadRawTextureData(pixels); texture.Apply(false, true);
            group.Textures.Add(texture); group.Material.SetTexture("_MainTex", texture);
            group.SpriteFrames = images.Count;
            if (_transparent == null) CreateParticleSystem(group, definition, texture);
            group.Shape = EffectSpriteShape.Of(definition, resource);
            _groups.Add((key, ""), group);
            return group;
        }

        private static BlendMode ToUnity(EffectBlendFactor factor) => factor switch
        {
            EffectBlendFactor.Zero => BlendMode.Zero,
            EffectBlendFactor.SourceAlpha => BlendMode.SrcAlpha,
            EffectBlendFactor.OneMinusSourceAlpha => BlendMode.OneMinusSrcAlpha,
            _ => BlendMode.One,
        };

        private void CreateParticleSystem(Group group, EffectElementDef definition, Texture2D texture)
        {
            var go = new GameObject("effect " + definition.Name);
            go.transform.SetParent(_root, false);
            group.System = go.AddComponent<ParticleSystem>();
            group.Buffer = new ParticleSystem.Particle[64];
            var main = group.System.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            var emission = group.System.emission; emission.enabled = false;
            var shape = group.System.shape; shape.enabled = false;
            var sheets = group.System.textureSheetAnimation;
            sheets.enabled = true;
            sheets.mode = ParticleSystemAnimationMode.Sprites;
            sheets.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            int width = texture.width / group.SpriteFrames;
            for (int frame = 0; frame < group.SpriteFrames; frame++)
            {
                var sprite = Sprite.Create(texture, new Rect(frame * width, 0, width, texture.height), new Vector2(0.5f, 0.5f));
                group.Sprites.Add(sprite); sheets.AddSprite(sprite);
            }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = group.Material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // The atlas rows are top first; Unity's sprites count v up, so the image is turned upright.
            renderer.flip = new Vector3(0f, 1f, 0f);
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            group.System.Play(); group.System.Pause();
            main.startSize3D = true;
        }

        public void Dispose()
        {
            _transparent?.BeginEffectFrame();
            _transparent?.EndEffectFrame();
            foreach (Group group in _groups.Values)
            {
                if (group == null) continue;
                if (group.System != null) UnityEngine.Object.Destroy(group.System.gameObject);
                UnityEngine.Object.Destroy(group.Material);
                if (group.FlatMesh != null) UnityEngine.Object.Destroy(group.FlatMesh);
                foreach (Sprite sprite in group.Sprites) UnityEngine.Object.Destroy(sprite);
                foreach (Texture2D texture in group.Textures) UnityEngine.Object.Destroy(texture);
            }
            _groups.Clear();
        }
    }
}

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

namespace DK2.Unity
{
    public sealed class ClassicConstructionEffects : IDisposable
    {
        private sealed class Group
        {
            public ParticleSystem System;
            public ParticleSystem.Particle[] Buffer = new ParticleSystem.Particle[64];
            public int Count;
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
            /// <summary>A sprite with art flag 0x100: DK2 draws it as a square lying flat at the
            /// element (0x0059B880 → 0x0059C720), not facing the camera; its quads are rebuilt into
            /// one mesh each frame.</summary>
            public bool IsFlat;
            public Mesh FlatMesh;
            public readonly List<Vector3> FlatVertices = new List<Vector3>();
            public readonly List<Vector2> FlatUvs = new List<Vector2>();
            public readonly List<Color32> FlatColours = new List<Color32>();
            public readonly List<int> FlatTriangles = new List<int>();
        }

        private readonly Transform _root;
        private readonly IEffectSpriteProvider _sprites;
        private readonly ClassicMeshes _meshes;
        /// <summary>One particle system per element definition, and per mesh entry for a group part.</summary>
        private readonly Dictionary<(ushort, string), Group> _groups = new Dictionary<(ushort, string), Group>();
        public RoomConstructionParticles Model { get; }

        public ClassicConstructionEffects(Transform root, GameDatabase database, IEffectSpriteProvider sprites, ulong seed,
            Func<RoomConstructionEffect, bool> visible = null,
            Func<ViewEffectRequest, bool> effectVisible = null,
            Func<FpPosition3, Fp32> floorHeight = null,
            Func<EffectTime> clock = null,
            ClassicMeshes meshes = null,
            Func<ViewEffectRequest, CaptureEdges> captureEdges = null,
            Func<int, uint, EffectAttachment> attachment = null)
        {
            _root = root;
            _sprites = sprites;
            _meshes = meshes;
            Model = new RoomConstructionParticles(database, seed, clock ?? ReadClock, visible, effectVisible, floorHeight,
                terrainDebrisStyle: TerrainDebrisStyle.Crumbling,
                captureEffectStyle: CaptureEffectStyle.Edges, captureEdges: captureEdges,
                heartSparkleStyle: HeartSparkleStyle.PillarHeight,
                groupParts: meshes != null ? meshes.GroupParts : null, attachment: attachment);
        }

        // A stopwatch rather than Unity's clock: the model's Emit runs on the runner's worker thread
        // (ADR-0458), and Unity's time is for the main thread.
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        private static EffectTime ReadClock() => new EffectTime(Clock.ElapsedMilliseconds);

        private float _fraction;
        private readonly List<EffectLightView> _lights = new List<EffectLightView>();

        /// <summary>Runs the effect updates due by now and returns the instances' lights, which
        /// the frame's lighting takes before it is prepared; <see cref="Draw"/> then draws this state.</summary>
        public IReadOnlyList<EffectLightView> Advance()
        {
            _fraction = Model.Advance();
            Model.CollectLights(_lights);
            return _lights;
        }

        public void Draw()
        {
            float fraction = _fraction;
            foreach (Group group in _groups.Values)
            {
                if (group == null) continue;
                group.Count = 0;
                if (!group.IsFlat) continue;
                group.FlatVertices.Clear(); group.FlatUvs.Clear(); group.FlatColours.Clear(); group.FlatTriangles.Clear();
            }
            for (int i = 0; i < Model.Particles.Count; i++)
            {
                ConstructionParticle source = Model.Particles[i];
                Group group = For(source.Definition, source.MeshEntry);
                if (group == null) continue;
                Vector3 position = DungeonScene.Ground(
                    Mathf.Lerp(source.PreviousXRaw, source.XRaw, fraction) / 4096f,
                    Mathf.Lerp(source.PreviousYRaw, source.YRaw, fraction) / 4096f,
                    Mathf.Lerp(source.PreviousHeightRaw, source.HeightRaw, fraction) / 4096f);
                float size = source.ScaleRaw / 4096f;
                if (group.IsMesh)
                {
                    var matrix = Matrix4x4.TRS(position, MeshRotation(source, fraction), Vector3.one * size);
                    Mesh mesh = group.Mesh;
                    Material[] materials = group.Materials;
                    if (group.Frames != null)
                    {
                        // The animation plays at its authored rate from the element's birth, one
                        // update being 50 ms, and loops.
                        int frame = (source.VisualUpdates * group.FramesPerSecond / 20) % group.Frames.Length;
                        mesh = group.Frames[frame];
                        materials = group.FrameMaterials[frame];
                    }
                    var tint = new Color32(source.Red, source.Green, source.Blue, 255);
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
                        Graphics.RenderMesh(parameters, mesh, sub, matrix);
                    }
                    continue;
                }
                if (group.IsFlat)
                {
                    AddFlatQuad(group, source, fraction);
                    continue;
                }
                if (group.Count == group.Buffer.Length) Array.Resize(ref group.Buffer, group.Buffer.Length * 2);
                group.Buffer[group.Count++] = new ParticleSystem.Particle
                {
                    position = position,
                    startColor = new Color32(source.Red, source.Green, source.Blue, 255),
                    rotation3D = Vector3.zero,
                    startSize = size,
                    startLifetime = 1f,
                    remainingLifetime = source.RemainingSpriteLifetimeFraction,
                    randomSeed = (uint)(i + 1),
                };
            }
            foreach (Group group in _groups.Values)
            {
                if (group == null || group.IsMesh) continue;
                if (group.IsFlat)
                {
                    DrawFlat(group);
                    continue;
                }
                var main = group.System.main;
                if (main.maxParticles < group.Count) main.maxParticles = group.Buffer.Length;
                group.System.SetParticles(group.Buffer, group.Count);
            }
        }

        /// <summary>A mesh particle's turn: DK2 turns a model about its X, then its Y, then the heading
        /// (0x00580480), in the object's DK2 frame mapping (pitch minus, roll plus).</summary>
        private static Quaternion MeshRotation(ConstructionParticle source, float fraction)
        {
            const float degreesPerUnit = 360f / 2048f;
            float heading = Mathf.LerpAngle(source.PreviousHeadingRaw * degreesPerUnit,
                source.HeadingRaw * degreesPerUnit, fraction);
            float pitch = Mathf.LerpAngle(source.PreviousPitchRaw * degreesPerUnit,
                source.PitchRaw * degreesPerUnit, fraction);
            float roll = Mathf.LerpAngle(source.PreviousRollRaw * degreesPerUnit,
                source.RollRaw * degreesPerUnit, fraction);
            return Quaternion.Euler(0f, heading + 180f, 0f)
                * Quaternion.AngleAxis(roll, Vector3.forward) * Quaternion.Euler(-pitch, 0f, 0f);
        }

        /// <summary>One flat sprite (0x0059C720 → 0x0057FB80): a square of half-size scale / 4096 in the
        /// DK2 x-y plane at the element, its corners (-s,-s), (s,-s), (s,s), (-s,s) taking UVs (0,0),
        /// (1,0), (1,1), (0,1), coloured by the tint. DK2 passes the angle -heading·π/1024 to a
        /// clockwise turn (0x0041C580 with the row transform 0x00594DB0), so the corners turn
        /// counter-clockwise by heading·π/1024 in the DK2 x-y plane. The frame is the particle's age
        /// through the stored frames, as the billboards' sheet takes it (DK2's frame pick for a flat
        /// sprite, 0x0057FF30, is unread).</summary>
        private static void AddFlatQuad(Group group, ConstructionParticle source, float fraction)
        {
            float x = Mathf.Lerp(source.PreviousXRaw, source.XRaw, fraction);
            float y = Mathf.Lerp(source.PreviousYRaw, source.YRaw, fraction);
            float height = Mathf.Lerp(source.PreviousHeightRaw, source.HeightRaw, fraction) / 4096f;
            float half = source.ScaleRaw;
            float angle = Mathf.LerpAngle(source.PreviousHeadingRaw * (360f / 2048f),
                source.HeadingRaw * (360f / 2048f), fraction) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            int frames = Math.Max(1, group.Sprites.Count);
            float age = 1f - source.RemainingSpriteLifetimeFraction;
            int frame = Math.Min(frames - 1, (int)(age * frames));
            float u0 = frame / (float)frames, u1 = (frame + 1) / (float)frames;
            int first = group.FlatVertices.Count;
            var colour = new Color32(source.Red, source.Green, source.Blue, 255);
            for (int corner = 0; corner < 4; corner++)
            {
                float cx = corner == 0 || corner == 3 ? -half : half;
                float cy = corner < 2 ? -half : half;
                group.FlatVertices.Add(DungeonScene.Ground((x + cx * cos - cy * sin) / 4096f,
                    (y + cx * sin + cy * cos) / 4096f, height));
                group.FlatUvs.Add(new Vector2(corner == 0 || corner == 3 ? u0 : u1, corner < 2 ? 0f : 1f));
                group.FlatColours.Add(colour);
            }
            group.FlatTriangles.Add(first); group.FlatTriangles.Add(first + 1); group.FlatTriangles.Add(first + 2);
            group.FlatTriangles.Add(first); group.FlatTriangles.Add(first + 2); group.FlatTriangles.Add(first + 3);
        }

        private void DrawFlat(Group group)
        {
            if (group.FlatVertices.Count == 0) return;
            if (group.FlatMesh == null)
            {
                group.FlatMesh = new Mesh { name = group.Material.name + " (flat)" };
                group.FlatMesh.MarkDynamic();
            }
            Mesh mesh = group.FlatMesh;
            mesh.Clear();
            mesh.SetVertices(group.FlatVertices);
            mesh.SetUVs(0, group.FlatUvs);
            mesh.SetColors(group.FlatColours);
            mesh.SetTriangles(group.FlatTriangles, 0, calculateBounds: true);
            var parameters = new RenderParams(group.Material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = _root.gameObject.layer,
                worldBounds = mesh.bounds,
            };
            Graphics.RenderMesh(parameters, mesh, 0, Matrix4x4.identity);
        }

        private Group MeshGroup(EffectElementDef definition, string meshEntry, AssetRef key)
        {
            Mesh mesh = null; Material[] materials = null;
            Mesh[] frames = null; Material[][] frameMaterials = null;
            bool found = false;
            if (_meshes != null && meshEntry == null && definition.ArtResource.Type == 6)
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
                _groups.Add((definition.EffectElementId, meshEntry ?? ""), null);
                return null;
            }
            // An additive effect mesh draws at the texture (STRONGLY INFERRED, heart-intro.md §6):
            // the material colour is forced white (0x0057C420), the vertex colour is that plus the
            // element tint and the lights (0x00581BE0), stage 0 modulates (0x00589250, op 4, never
            // 2X or 4X), and the blend is ONE, ONE (ADR-0492, superseding ADR-0476's doubling).
            var group = new Group
            {
                IsMesh = true, Mesh = mesh, Materials = materials, Frames = frames, FrameMaterials = frameMaterials,
                FramesPerSecond = AuthoredFramesPerSecond(definition),
            };
            _groups.Add((definition.EffectElementId, meshEntry ?? ""), group);
            return group;
        }

        /// <summary>An animated element's frame rate: the second dword of its descriptor read as
        /// frames a second is a HYPOTHESIS fitted to the Portal Beams (60 frames, 10, a six-second
        /// life; no reader of type-6 elements is read); 10 when the dword is out of range.</summary>
        private static int AuthoredFramesPerSecond(EffectElementDef definition)
        {
            ReadOnlySpan<byte> block = definition.ArtResource.Unknown_0044.Span;
            int rate = block.Length >= 8 ? BitConverter.ToInt32(block.Slice(4, 4)) : 0;
            return rate > 0 && rate <= 60 ? rate : 10;
        }

        /// <param name="meshEntry">A group part's mesh entry, drawn in place of the definition's art.</param>
        private Group For(EffectElementDef definition, string meshEntry)
        {
            if (_groups.TryGetValue((definition.EffectElementId, meshEntry ?? ""), out Group existing)) return existing;
            AssetRef key = AssetRef.ForEffectElement(definition.Name);
            if (meshEntry != null || definition.ArtResource.Type == 5) return MeshGroup(definition, meshEntry, key);
            if (_sprites == null || !_sprites.TryResolve(key, out ResolvedAsset asset)
                || !_sprites.TryGetSprite(asset, out EffectSpriteResource resource))
            {
                _groups.Add((definition.EffectElementId, ""), null);
                return null;
            }
            Shader shader = Shader.Find("DK2/ClassicEffect");
            if (shader == null) throw new InvalidOperationException("The classic effect shader is unavailable.");
            var group = new Group { Material = new Material(shader) { name = key.Key } };
            group.Material.SetInt("_DstBlend", (int)(resource.Blend == EffectSpriteBlend.Additive
                ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            var go = new GameObject("effect " + definition.Name);
            go.transform.SetParent(_root, false);
            group.System = go.AddComponent<ParticleSystem>();
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
            for (int frame = 0; frame < images.Count; frame++)
            {
                var sprite = Sprite.Create(texture, new Rect(frame * width, 0, width, height), new Vector2(0.5f, 0.5f));
                group.Sprites.Add(sprite); sheets.AddSprite(sprite);
            }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = group.Material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            group.System.Play(); group.System.Pause();
            group.IsFlat = (definition.ArtResource.Flags & 0x100) != 0;
            _groups.Add((definition.EffectElementId, ""), group);
            return group;
        }

        public void Dispose()
        {
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

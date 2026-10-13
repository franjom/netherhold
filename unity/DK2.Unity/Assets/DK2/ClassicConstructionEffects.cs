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
            public readonly List<Texture2D> Textures = new List<Texture2D>();
            public readonly List<Sprite> Sprites = new List<Sprite>();
        }

        private readonly Transform _root;
        private readonly IEffectSpriteProvider _sprites;
        private readonly ClassicMeshes _meshes;
        private readonly Dictionary<ushort, Group> _groups = new Dictionary<ushort, Group>();
        public RoomConstructionParticles Model { get; }

        public ClassicConstructionEffects(Transform root, GameDatabase database, IEffectSpriteProvider sprites, ulong seed,
            Func<RoomConstructionEffect, bool> visible = null,
            Func<ViewEffectRequest, bool> effectVisible = null,
            Func<FpPosition3, Fp32> floorHeight = null,
            Func<EffectTime> clock = null,
            ClassicMeshes meshes = null)
        {
            _root = root;
            _sprites = sprites;
            _meshes = meshes;
            Model = new RoomConstructionParticles(database, seed, clock ?? ReadClock, visible, effectVisible, floorHeight);
        }

        // A stopwatch rather than Unity's clock: the model's Emit runs on the runner's worker thread
        // (ADR-0458), and Unity's time is for the main thread.
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        private static EffectTime ReadClock() => new EffectTime(Clock.ElapsedMilliseconds);

        public void Draw()
        {
            float fraction = Model.Advance();
            foreach (Group group in _groups.Values) if (group != null) group.Count = 0;
            for (int i = 0; i < Model.Particles.Count; i++)
            {
                ConstructionParticle source = Model.Particles[i];
                Group group = For(source.Definition);
                if (group == null) continue;
                if (group.Count == group.Buffer.Length) Array.Resize(ref group.Buffer, group.Buffer.Length * 2);
                float phase = source.PhaseRaw / 4096f;
                group.Buffer[group.Count++] = new ParticleSystem.Particle
                {
                    position = DungeonScene.Ground(
                        Mathf.Lerp(source.PreviousXRaw, source.XRaw, fraction) / 4096f,
                        Mathf.Lerp(source.PreviousYRaw, source.YRaw, fraction) / 4096f,
                        Mathf.Lerp(source.PreviousHeightRaw, source.HeightRaw, fraction) / 4096f),
                    startColor = group.IsMesh ? new Color32(255, 255, 255, 255)
                        : new Color32(source.Red, source.Green, source.Blue, 255),
                    rotation3D = group.IsMesh ? MeshRotation(source, fraction) : Vector3.zero,
                    startSize = source.ScaleRaw / 4096f,
                    startLifetime = 1f,
                    remainingLifetime = 1f - phase,
                    randomSeed = (uint)(i + 1),
                };
            }
            foreach (Group group in _groups.Values)
            {
                if (group == null) continue;
                var main = group.System.main;
                if (main.maxParticles < group.Count) main.maxParticles = group.Buffer.Length;
                group.System.SetParticles(group.Buffer, group.Count);
            }
        }

        private static Vector3 MeshRotation(ConstructionParticle source, float fraction)
        {
            const float degreesPerUnit = 360f / 2048f;
            float heading = Mathf.LerpAngle(source.PreviousHeadingRaw * degreesPerUnit,
                source.HeadingRaw * degreesPerUnit, fraction);
            float pitch = Mathf.LerpAngle(source.PreviousPitchRaw * degreesPerUnit,
                source.PitchRaw * degreesPerUnit, fraction);
            float roll = Mathf.LerpAngle(source.PreviousRollRaw * degreesPerUnit,
                source.RollRaw * degreesPerUnit, fraction);
            Quaternion rotation = Quaternion.Euler(0f, heading + 180f, 0f)
                * Quaternion.AngleAxis(roll, Vector3.forward) * Quaternion.Euler(-pitch, 0f, 0f);
            // Particle.rotation3D stores inverse Euler angles; meshes use the object's DK2 frame mapping.
            return -rotation.eulerAngles;
        }

        private Group MeshGroup(EffectElementDef definition, AssetRef key)
        {
            if (_meshes == null || !_meshes.TryGetMesh(key, out Mesh mesh, out Material[] materials))
            {
                _groups.Add(definition.EffectElementId, null);
                return null;
            }
            var go = new GameObject("effect " + definition.Name);
            go.transform.SetParent(_root, false);
            var group = new Group { IsMesh = true, System = go.AddComponent<ParticleSystem>() };
            var main = group.System.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation3D = true;
            main.maxParticles = 64;
            var emission = group.System.emission; emission.enabled = false;
            var shape = group.System.shape; shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.mesh = mesh;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            group.System.Play(); group.System.Pause();
            _groups.Add(definition.EffectElementId, group);
            return group;
        }

        private Group For(EffectElementDef definition)
        {
            if (_groups.TryGetValue(definition.EffectElementId, out Group existing)) return existing;
            AssetRef key = AssetRef.ForEffectElement(definition.Name);
            if (definition.ArtResource.Type == 5) return MeshGroup(definition, key);
            if (_sprites == null || !_sprites.TryResolve(key, out ResolvedAsset asset)
                || !_sprites.TryGetSprite(asset, out EffectSpriteResource resource))
            {
                _groups.Add(definition.EffectElementId, null);
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
            _groups.Add(definition.EffectElementId, group);
            return group;
        }

        public void Dispose()
        {
            foreach (Group group in _groups.Values)
            {
                if (group == null) continue;
                if (group.System != null) UnityEngine.Object.Destroy(group.System.gameObject);
                UnityEngine.Object.Destroy(group.Material);
                foreach (Sprite sprite in group.Sprites) UnityEngine.Object.Destroy(sprite);
                foreach (Texture2D texture in group.Textures) UnityEngine.Object.Destroy(texture);
            }
            _groups.Clear();
        }
    }
}

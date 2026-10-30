// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Art.Textures;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using NumericsVector = System.Numerics.Vector3;

namespace DK2.Unity
{
    public sealed class ClassicLightning : IDisposable
    {
        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly Material _material;
        private readonly Texture2D _texture;
        private readonly LightningBolts _bolts;
        private readonly LightningCracklePairs _pairs;
        private readonly RoomConstructionParticles _effects;
        private readonly Dictionary<ShotId, LightningLink> _links = new Dictionary<ShotId, LightningLink>();
        private readonly HashSet<ShotId> _liveShots = new HashSet<ShotId>();
        private readonly List<ShotId> _removed = new List<ShotId>();
        private readonly Dictionary<CreatureId, int> _animationRevisions = new Dictionary<CreatureId, int>();
        private readonly List<ProceduralEffectView> _procedural = new List<ProceduralEffectView>();
        private readonly List<LightningControlPoint> _controls = new List<LightningControlPoint>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private Camera _camera;
        private bool _hasCamera;

        public ClassicLightning(Transform parent, Func<string, RgbaImage> loadTexture,
            RoomConstructionParticles effects, uint seed)
        {
            _effects = effects;
            _bolts = new LightningBolts(seed);
            _pairs = new LightningCracklePairs(new Dk2Lcg(seed));
            _root = new GameObject("Procedural lightning"); _root.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "Lightning ribbons", indexFormat = IndexFormat.UInt32 }; _mesh.MarkDynamic();
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _root.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
            Shader shader = Resources.Load<Shader>("Shaders/ClassicLightning");
            if (shader == null) throw new InvalidOperationException("Lightning shader unavailable.");
            _material = new Material(shader);
            RgbaImage image = loadTexture("Lightning");
            _texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
            { name = "Lightning", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _texture.LoadRawTextureData(image.Pixels); _texture.Apply(false, true);
            _material.SetTexture("_MainTex", _texture); _renderer.sharedMaterial = _material;
            _renderer.enabled = false;
        }

        public void BeginFrame(Camera camera)
        {
            _vertices.Clear(); _uvs.Clear(); _triangles.Clear();
            _hasCamera = camera != null;
            _camera = camera;
        }

        public void DrawShots(WorldState world, GameDatabase database, WorldView view, float fraction)
        {
            _liveShots.Clear();
            foreach (ShotState shot in world.Shots)
            {
                if (!database.Shots.TryGetValue(shot.ShotTypeId, out ShotDef definition)
                    || definition.MeshResource.Type != 7) continue;
                var id = new ShotId(shot.Id); _liveShots.Add(id);
                if (!view.CanShowTile(world, new TilePos(shot.PosX >> 12, shot.PosY >> 12))) continue;
                if (!_links.TryGetValue(id, out LightningLink link))
                    _links.Add(id, link = new LightningLink(definition.MeshResource.ProceduralKind));
                FpPosition3 current = shot.ViewPosition, previous = shot.PreviousViewPosition ?? current;
                var at = new FpPosition3(new Fp32((int)Mathf.Lerp(previous.X.Raw, current.X.Raw, fraction)),
                    new Fp32((int)Mathf.Lerp(previous.Y.Raw, current.Y.Raw, fraction)),
                    new Fp32((int)Mathf.Lerp(previous.Z.Raw, current.Z.Raw, fraction)));
                FpPosition3? casterPoint = null;
                CreatureState caster = world.CreatureById(shot.CasterId);
                if (caster != null && !CreatureDeathRules.IsCorpse(caster))
                    casterPoint = new FpPosition3(new Fp32((int)Mathf.Lerp(caster.LastPosX, caster.PosX, fraction)),
                        new Fp32((int)Mathf.Lerp(caster.LastPosY, caster.PosY, fraction)), default);
                LightningEndpoints? endpoints = link.Resolve(at, Target(world, database, view, shot),
                    new LightningCaster(shot.CasterId != 0, casterPoint));
                if (endpoints.HasValue) Add(endpoints.Value);
            }
            _removed.Clear();
            foreach (ShotId id in _links.Keys) if (!_liveShots.Contains(id)) _removed.Add(id);
            foreach (ShotId id in _removed) _links.Remove(id);
        }

        private static LightningTarget Target(WorldState world, GameDatabase database, WorldView view, ShotState shot)
        {
            FpPosition3 point;
            switch (shot.TargetKind)
            {
                case FightTargetKind.Creature:
                case FightTargetKind.Body:
                    CreatureState creature = world.CreatureById(shot.TargetId);
                    if (creature == null || shot.TargetKind == FightTargetKind.Creature && CreatureDeathRules.IsCorpse(creature)) return default;
                    point = new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ));
                    return new LightningTarget(true, false, new FpPosition3(point.X, point.Y,
                        new Fp32(view.GroundHeightAt(world, point).Raw + creature.PosZ)));
                case FightTargetKind.Door:
                    DoorState door = world.DoorById(shot.TargetId);
                    if (door == null) return default;
                    point = new FpPosition3(new Fp32(CreatureState.CentreOf(door.X)), new Fp32(CreatureState.CentreOf(door.Y)), default);
                    break;
                case FightTargetKind.Trap:
                    TrapState trap = world.TrapById(shot.TargetId);
                    if (trap == null) return default;
                    point = new FpPosition3(new Fp32(trap.PosX), new Fp32(trap.PosY), default);
                    break;
                case FightTargetKind.Heart:
                    if (!HeartPillarRules.TryResolve(world, database, new PlayerId(shot.TargetPlayerId),
                        new HeartPillarId(shot.TargetId), out HeartPillarTarget pillar)) return default;
                    point = new FpPosition3(pillar.X, pillar.Y, default);
                    break;
                case FightTargetKind.Shot:
                    ShotState other = world.ShotById(shot.TargetId);
                    return other != null ? new LightningTarget(true, false, other.ViewPosition) : default;
                default:
                    return default;
            }
            return new LightningTarget(true, false, new FpPosition3(point.X, point.Y, view.GroundHeightAt(world, point)));
        }

        public void DrawEffects()
        {
            _effects.CollectProceduralEffects(_procedural);
            foreach (ProceduralEffectView effect in _procedural)
                if (effect.Kind == ProceduralArtKind.Lightning || effect.Kind == ProceduralArtKind.TripleLightning
                    || effect.Kind == ProceduralArtKind.FixedLightning)
                    Add(new LightningEndpoints(effect.Position,
                        new FpPosition3(effect.Position.X, effect.Position.Y, new Fp32(Fp32.One)),
                        effect.Kind == ProceduralArtKind.TripleLightning ? 3 : 1));
        }

        public void DrawCreature(CreatureId id, GameTick tick, ClassicCreatureAnimation animation)
        {
            if (animation == null) return;
            bool installed = !_animationRevisions.TryGetValue(id, out int previous) || previous != animation.InstallationRevision;
            _animationRevisions[id] = animation.InstallationRevision;
            // The Unity clip install is our chooser's observation; its existing activity-to-art
            // mapping is not a complete port of the native chooser (Q660).
            if (!_effects.ChooseCreatureCrackle(id, tick, installed)) return;
            animation.CollectLightningControlPoints(_controls);
            foreach (LightningSegment segment in _pairs.Choose(_controls))
                AddBolt(segment.Start, segment.End, LightningAmplitude.Creature);
        }

        private void Add(LightningEndpoints endpoints)
        {
            for (int i = 0; i < endpoints.BoltCount; i++)
                AddBolt(Tiles(endpoints.Start), Tiles(endpoints.End), LightningAmplitude.Shot);
        }

        private void AddBolt(NumericsVector start, NumericsVector end, LightningAmplitude amplitude)
        {
            if (!_hasCamera) return;
            Vector3 viewStart = _camera.transform.InverseTransformPoint(new Vector3(start.X, start.Z, -start.Y));
            Vector3 viewEnd = _camera.transform.InverseTransformPoint(new Vector3(end.X, end.Z, -end.Y));
            foreach (LightningPath path in _bolts.Build(new NumericsVector(viewStart.x, viewStart.y, viewStart.z),
                new NumericsVector(viewEnd.x, viewEnd.y, viewEnd.z), amplitude))
            {
                LightningRibbon ribbon = LightningRibbon.Build(path.Nodes);
                int offset = _vertices.Count;
                foreach (NumericsVector vertex in ribbon.Vertices)
                    _vertices.Add(_camera.transform.TransformPoint(new Vector3(vertex.X, vertex.Y, vertex.Z)));
                foreach (System.Numerics.Vector2 uv in ribbon.Uvs) _uvs.Add(new Vector2(uv.X, uv.Y));
                foreach (int index in ribbon.Triangles) _triangles.Add(offset + index);
            }
        }

        public void EndFrame()
        {
            _renderer.enabled = _hasCamera && _vertices.Count != 0;
            _mesh.Clear();
            if (!_renderer.enabled) return;
            _mesh.SetVertices(_vertices); _mesh.SetUVs(0, _uvs); _mesh.SetTriangles(_triangles, 0); _mesh.RecalculateBounds();
        }

        private static NumericsVector Tiles(FpPosition3 point) =>
            new NumericsVector(point.X.Raw / 4096f, point.Y.Raw / 4096f, point.Z.Raw / 4096f);

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_material); UnityEngine.Object.Destroy(_texture);
        }
    }
}

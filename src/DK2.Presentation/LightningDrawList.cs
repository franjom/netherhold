// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
namespace DK2.Presentation;
/// <summary>Optional native point conversions when an engine's Transform rounds differently from its matrix.</summary>
public interface ILightningCameraTransform
{
    Vector3 ToCamera(Vector3 renderWorld);
    Vector3 ToWorld(Vector3 cameraPoint);
}
/// <summary>Lightning selection and camera-space ribbons, with reused frame storage.</summary>
public sealed class LightningDrawList
{
    private readonly ILightningCameraTransform? _nativeCamera;
    private readonly RoomConstructionParticles _effects;
    private readonly LightningBolts _bolts;
    private readonly LightningCracklePairs _pairs;
    private readonly Dictionary<ShotId, LightningLink> _links = new();
    private readonly List<LightningLink> _freeLinks = new();
    private readonly HashSet<ShotId> _liveShots = new();
    private readonly List<ShotId> _removed = new();
    private readonly Dictionary<CreatureId, int> _animationRevisions = new();
    private readonly List<ProceduralEffectView> _procedural = new();
    private readonly List<LightningSegment> _segments = new();
    private readonly List<Vector3> _vertices = new();
    private readonly List<Vector2> _uvs = new();
    private readonly List<int> _triangles = new();
    private Matrix4x4 _view, _camera;
    private bool _hasCamera;
    public IReadOnlyList<Vector3> Vertices
    {
        get
        {
            return _vertices;
        }
    }

    public IReadOnlyList<Vector2> Uvs
    {
        get
        {
            return _uvs;
        }
    }

    public IReadOnlyList<int> Triangles
    {
        get
        {
            return _triangles;
        }
    }

    public LightningDrawList(RoomConstructionParticles effects, uint seed, ILightningCameraTransform? nativeCamera = null)
    {
        _nativeCamera = nativeCamera;
        _effects = effects;
        _bolts = new(seed);
        _pairs = new(new Dk2Lcg(seed));
    }
    /// <summary>Matrices use render-world (x, z, -y), row vectors, and camera +Z forward.</summary>
    public void BeginFrame(Matrix4x4 worldToCamera, Matrix4x4 cameraToWorld, bool hasCamera = true)
    {
        _vertices.Clear(); _uvs.Clear(); _triangles.Clear();
        UpdateCameraMatrices(worldToCamera, cameraToWorld, hasCamera);
    }
    /// <summary>Refreshes the camera after query follow; already drawn crackle and random streams stay intact.</summary>
    public void UpdateCameraMatrices(Matrix4x4 worldToCamera, Matrix4x4 cameraToWorld, bool hasCamera = true)
    {
        _view = worldToCamera; _camera = cameraToWorld; _hasCamera = hasCamera;
    }
    public void DrawShots(WorldState world, GameDatabase database, WorldView view, float fraction)
    {
        _liveShots.Clear();
        for (int shotIndex = 0; shotIndex < world.Shots.Count; shotIndex++)
        {
            ShotState shot = world.Shots[shotIndex];
            if (!database.Shots.TryGetValue(shot.ShotTypeId, out ShotDef? definition)
                || definition.MeshResource.Type != 7)
            {
                continue;
            }

            var id = new ShotId(shot.Id); _liveShots.Add(id);
            if (!view.CanShowTile(world, new TilePos(shot.PosX >> 12, shot.PosY >> 12)))
            {
                continue;
            }

            if (!_links.TryGetValue(id, out LightningLink? link))
            {
                _links.Add(id, link = RentLink(definition.MeshResource.ProceduralKind));
            }

            FpPosition3 current = shot.ViewPosition, previous = shot.PreviousViewPosition ?? current;
            var at = new FpPosition3(new Fp32((int)Lerp(previous.X.Raw, current.X.Raw, fraction)),
                new Fp32((int)Lerp(previous.Y.Raw, current.Y.Raw, fraction)),
                new Fp32((int)Lerp(previous.Z.Raw, current.Z.Raw, fraction)));
            FpPosition3? casterPoint = null;
            CreatureState? caster = world.CreatureById(shot.CasterId);
            if (caster != null && !CreatureDeathRules.IsCorpse(caster))
            {
                casterPoint = new FpPosition3(new Fp32((int)Lerp(caster.LastPosX, caster.PosX, fraction)),
                    new Fp32((int)Lerp(caster.LastPosY, caster.PosY, fraction)), default);
            }

            LightningEndpoints? endpoints = link.Resolve(at, Target(world, database, view, shot),
                new LightningCaster(shot.CasterId != 0, casterPoint));
            if (endpoints.HasValue)
            {
                Add(endpoints.Value);
            }
        }
        _removed.Clear();
        foreach (ShotId id in _links.Keys)
        {
            if (!_liveShots.Contains(id))
            {
                _removed.Add(id);
            }
        }

        foreach (ShotId id in _removed)
        {
            _freeLinks.Add(_links[id]);
            _links.Remove(id);
        }
    }

    private static LightningTarget Target(WorldState world, GameDatabase database, WorldView view, ShotState shot)
    {
        FpPosition3 point;
        switch (shot.TargetKind)
        {
            case FightTargetKind.Creature:
            case FightTargetKind.Body:
                CreatureState? creature = world.CreatureById(shot.TargetId);
                if (creature == null || shot.TargetKind == FightTargetKind.Creature && CreatureDeathRules.IsCorpse(creature))
                {
                    return default;
                }

                point = new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ));
                return new LightningTarget(true, false, new FpPosition3(point.X, point.Y,
                    new Fp32(view.GroundHeightAt(world, point).Raw + creature.PosZ)));
            case FightTargetKind.Door:
                DoorState? door = world.DoorById(shot.TargetId);
                if (door == null)
                {
                    return default;
                }

                point = new FpPosition3(new Fp32(CreatureState.CentreOf(door.X)), new Fp32(CreatureState.CentreOf(door.Y)), default);
                break;
            case FightTargetKind.Trap:
                TrapState? trap = world.TrapById(shot.TargetId);
                if (trap == null)
                {
                    return default;
                }

                point = new FpPosition3(new Fp32(trap.PosX), new Fp32(trap.PosY), default);
                break;
            case FightTargetKind.Heart:
                if (!TryResolveHeartPillar(world, database, new PlayerId(shot.TargetPlayerId),
                    new HeartPillarId(shot.TargetId), out HeartPillarTarget pillar))
                {
                    return default;
                }

                point = new FpPosition3(pillar.X, pillar.Y, default);
                break;
            case FightTargetKind.Shot:
                ShotState? other = world.ShotById(shot.TargetId);
                return other != null ? new LightningTarget(true, false, other.ViewPosition) : default;
            default:
                return default;
        }
        return new LightningTarget(true, false, new FpPosition3(point.X, point.Y, view.GroundHeightAt(world, point)));
    }

    private static bool TryResolveHeartPillar(WorldState world, GameDatabase database, PlayerId owner,
        HeartPillarId id, out HeartPillarTarget target)
    {
        target = default;
        if (id.Value < 0 || id.Value / 3 >= world.TileCount)
        {
            return false;
        }

        bool standing = false;
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            if (player.PlayerId == owner)
            {
                standing = !player.HeartRuined && player.HeartHealth > 0;
            }
        }
        if (!standing)
        {
            return false;
        }

        int tile = id.Value / 3;
        if (world.TileAtIndex(tile).OwnerPlayerId != owner
            || database.RoomByTerrain(world.TileAtIndex(tile).TerrainId)?.TypeId != RoomTypeId.DungeonHeart
            || RoomIndex.RoomAt(world, database, tile) is not { } room)
        {
            return false;
        }

        int ordinal = id.Value % 3;
        int position = ordinal == 0 ? 7 : ordinal == 1 ? 16 : 18;
        // HeartPillarRules.TryResolve sorts a fresh slab array. Count this tile's rank in
        // the same (SlabOrder, tile index) ordering to avoid allocating it for every draw.
        int rank = 0, order = world.TileAtIndex(tile).SlabOrder;
        for (int i = 0; i < room.TileIndices.Count; i++)
        {
            int other = room.TileIndices[i], otherOrder = world.TileAtIndex(other).SlabOrder;
            if (otherOrder < order || otherOrder == order && other < tile)
            {
                rank++;
            }
        }
        if (position >= room.TileIndices.Count || rank != position)
        {
            return false;
        }

        target = HeartPillarRules.At(new TilePos(tile % world.Width, tile / world.Width), (HeartPillar)ordinal);
        return true;
    }

    public void DrawEffects()
    {
        _effects.CollectProceduralEffects(_procedural);
        foreach (ProceduralEffectView effect in _procedural)
        {
            if (effect.Kind == ProceduralArtKind.Lightning || effect.Kind == ProceduralArtKind.TripleLightning
                || effect.Kind == ProceduralArtKind.FixedLightning)
            {
                Add(new LightningEndpoints(effect.Position,
                    new FpPosition3(effect.Position.X, effect.Position.Y, new Fp32(Fp32.One)),
                    effect.Kind == ProceduralArtKind.TripleLightning ? 3 : 1));
            }
        }
    }


    public bool NeedsCreatureCrackle(CreatureId id, GameTick tick, int installationRevision)
    {
        bool installed = !_animationRevisions.TryGetValue(id, out int previous) || previous != installationRevision;
        _animationRevisions[id] = installationRevision;
        return _effects.ChooseCreatureCrackle(id, tick, installed);
    }
    public void DrawCrackle(IReadOnlyList<LightningControlPoint> controls)
    {
        _pairs.ChooseInto(controls, _segments);
        foreach (LightningSegment segment in _segments)
        {
            AddBolt(segment.Start, segment.End, LightningAmplitude.Creature);
        }
    }
    private LightningLink RentLink(ProceduralArtKind kind)
    {
        if (_freeLinks.Count == 0)
        {
            return new(kind);
        }

        int last = _freeLinks.Count - 1;
        LightningLink link = _freeLinks[last];
        _freeLinks.RemoveAt(last);
        link.Reset(kind);
        return link;
    }
    private void Add(LightningEndpoints endpoints)
    {
        for (int i = 0; i < endpoints.BoltCount; i++)
        {
            AddBolt(Tiles(endpoints.Start), Tiles(endpoints.End), LightningAmplitude.Shot);
        }
    }
    private void AddBolt(Vector3 start, Vector3 end, LightningAmplitude amplitude)
    {
        if (!_hasCamera)
        {
            return;
        }

        Vector3 renderStart = new(start.X, start.Z, -start.Y), renderEnd = new(end.X, end.Z, -end.Y);
        Vector3 a = _nativeCamera is null ? Transform(renderStart, _view) : _nativeCamera.ToCamera(renderStart);
        Vector3 b = _nativeCamera is null ? Transform(renderEnd, _view) : _nativeCamera.ToCamera(renderEnd);
        IReadOnlyList<LightningPathBuffer> paths = _bolts.BuildInto(a, b, amplitude);
        for (int p = 0; p < paths.Count; p++)
        {
            int offset = _vertices.Count;
            LightningRibbon.Append(paths[p].Nodes, _vertices, _uvs, _triangles);
            for (int i = offset; i < _vertices.Count; i++)
            {
                _vertices[i] = _nativeCamera is null ? Transform(_vertices[i], _camera) : _nativeCamera.ToWorld(_vertices[i]);
            }
        }
    }
    // Keep Unity MultiplyPoint3x4's scalar operation order; SIMD Transform can round differently.
    private static Vector3 Transform(Vector3 p, Matrix4x4 m)
    {
        return new(
        m.M11 * p.X + m.M21 * p.Y + m.M31 * p.Z + m.M41,
        m.M12 * p.X + m.M22 * p.Y + m.M32 * p.Z + m.M42,
        m.M13 * p.X + m.M23 * p.Y + m.M33 * p.Z + m.M43);
    }

    private static float Lerp(float a, float b, float fraction)
    {
        return a + (b - a) * Math.Clamp(fraction, 0, 1);
    }

    private static Vector3 Tiles(FpPosition3 p)
    {
        return new(p.X.Raw / 4096f, p.Y.Raw / 4096f, p.Z.Raw / 4096f);
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Assets;
using DK2.Art;
using DK2.Core;
using DK2.Presentation;
using DK2.Data.Model;
using DK2.Simulation;
using UnityEngine;
using Unity.Profiling;

namespace DK2.Unity
{
    /// <summary>Turns the presenter's pushes into GameObjects: the Unity-specific half of the renderer.</summary>
    /// <remarks>
    /// <para>Almost no judgement lives here: <see cref="ScenePresenter"/> decided
    /// what changed, in what order, which asset each thing needs and create-vs-
    /// update, and the asset layer decided file or placeholder (ADR-0036). This
    /// file is the one part of the project the test suite cannot cover.</para>
    /// <para>Coordinates: the grid lies on Unity's XZ plane, Y up, X east and Z north (ADR-0411);
    /// tile <c>(x, y)</c> centres at <c>(x + 0.5, 0, -(y + 0.5))</c> (<see cref="Ground"/>) and a
    /// creature's <see cref="Fp32"/> position divides by <see cref="Fp32.One"/> — here and
    /// nowhere else (ADR-0001).</para>
    /// </remarks>
    public sealed class DungeonScene : IRenderTarget
    {
        /// <summary>DK2's ordinary floor height, 0x1000 (<c>0x004545C0</c>; pits, pools and
        /// water lie lower). Heights here are DK2's own, so a torch's 1.5 is mid-wall.</summary>
        public const float FloorHeight = 1f;

        /// <summary>The top of a tile whose terrain fills its cell: every solid terrain KMF in
        /// the install spans heights 1 to 2, a wall one tile above the floor. Public because
        /// <see cref="TilePicker"/> needs the same number to work out what the pointer is over.</summary>
        public const float SolidHeight = 2f;

        /// <summary>The top of a tile you could stand on, the floor; for the picker too.</summary>
        public const float OpenHeight = FloorHeight;

        /// <summary>A floor slab's thickness: thin, so a creature on it reads as standing on it.</summary>
        private const float SlabThickness = 0.06f;

        /// <summary>What a tile marked for digging is tinted towards. DK2 plants flags;
        /// this is a placeholder renderer, so it tints. An interface choice, not a claim
        /// about the original (ADR-0104).</summary>
        private static readonly Color TagTint = new Color(1f, 0.85f, 0.2f);

        private readonly Transform _root;
        private readonly TerrainLattice _lattice;
        private ClassicTorchFlames _torchFlames;
        public void AttachTorchSprites(IEffectSpriteProvider sprites, System.Func<EffectTime> clock = null)
        {
            _torchFlames?.Dispose();
            _torchFlames = new ClassicTorchFlames(_root, _lattice, sprites, clock ?? (() => HeartEffectClock()));
        }
        private bool _torchGeometryDirty;
        private readonly Dictionary<int, GameObject> _tiles = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _furniture = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _creatures = new Dictionary<int, GameObject>();
        // Each drawn creature's head (KMF point 5000) in native units, null when its clip has none;
        // read by the effects on the runner's worker thread (ADR-0458), so it is concurrent.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, FpPosition3?> _creatureHeads =
            new System.Collections.Concurrent.ConcurrentDictionary<int, FpPosition3?>();
        private readonly Dictionary<int, AssetRef> _creatureArt = new Dictionary<int, AssetRef>();
        private readonly Dictionary<int, CreatureView> _creaturePoses = new Dictionary<int, CreatureView>();
        private readonly Dictionary<int, (AssetRef Clip, GameTick? StartedAt, bool Loops, bool Deferred)> _creatureClips =
            new Dictionary<int, (AssetRef Clip, GameTick? StartedAt, bool Loops, bool Deferred)>();
        private double _renderSeconds;
        private readonly HeartPulseGate _heartPulseGate = new HeartPulseGate();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(PlayerId Owner, Fp32 Step)> _heartAdvances =
            new System.Collections.Concurrent.ConcurrentQueue<(PlayerId Owner, Fp32 Step)>();
        public void QueueHeartHealthChange(PlayerId owner, DungeonHeartVisual visual) =>
            _heartAdvances.Enqueue((owner, visual.PhaseStep));
        public System.Action<ViewEffectRequest> HeartEffectRequested { get; set; }
        /// <summary>A drawn gem carrier's mesh point 10001, once per draw (CreatureView.GemDustSparkle).</summary>
        public System.Action<FpPosition3> GemSparkleRequested { get; set; }
        private readonly Dictionary<int, long> _gemSparkleQuanta = new Dictionary<int, long>();
        public System.Func<EffectTime> HeartEffectClock { get; set; } =
            () => new EffectTime((long)(Time.realtimeSinceStartupAsDouble * 1000));
        private readonly Dictionary<int, GameObject> _objects = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, ObjectView> _objectPoses = new Dictionary<int, ObjectView>();
        private readonly Dictionary<int, (AssetRef Clip, GameTick? StartedAt, bool Loops, DK2.Data.Model.TerrainLightColour AddedLight, Fp32 Phase)> _objectClips =
            new Dictionary<int, (AssetRef Clip, GameTick? StartedAt, bool Loops, DK2.Data.Model.TerrainLightColour AddedLight, Fp32 Phase)>();
        private readonly Dictionary<StructureId, GameObject> _structures = new Dictionary<StructureId, GameObject>();
        private readonly Dictionary<StructureId, TilePos> _structureTiles = new Dictionary<StructureId, TilePos>();
        private readonly List<Material> _hoverMaterials = new List<Material>();
        private readonly Dictionary<int, GameObject> _torches = new Dictionary<int, GameObject>();
        private readonly Dictionary<uint, Material> _materials = new Dictionary<uint, Material>();
        private bool _disposed;
        private readonly ClassicTorchLightBuffer _torchLights;
        private readonly ClassicTileFillBuffer _tileFill;
        private ClassicTileLightBuffer _tileLights;
        private readonly ClassicWorldLightCache _worldLights;
        private readonly PortalCentreLights _portalCentreLights;
        private int _portalLightRequestsPublished;
        private readonly ClassicLightFlags _torchFlags;
        private TilePos[] _visibleLightTiles = System.Array.Empty<TilePos>();
        private readonly HashSet<TilePos> _publishedLightTiles = new HashSet<TilePos>();
        private ClassicCameraPose? _lightCameraPose;
        private CameraViewport _lightViewport;
        private float _lightViewDistance;
        private static readonly ProfilerMarker LightMarker = new ProfilerMarker("DK2.Scene.PrepareLighting");
        private readonly ClassicCreatureShadows _creatureShadows;
        private readonly int _worldWidth;
        private readonly int _worldHeight;
        private readonly ClassicMeshes _classic;
        private readonly TerrainChunks _terrain;
        private readonly LiquidSurfaces _liquids;
        private readonly HashSet<int> _meshObjects = new HashSet<int>();
        // Objects the install has no art for, keyed with the key they asked for, so they are not tried again.
        private readonly Dictionary<int, AssetRef> _artless = new Dictionary<int, AssetRef>();
        private readonly HashSet<int> _meshCreatures = new HashSet<int>();

        private bool _warnedAboutFiles;

        /// <summary>
        /// How many tiles and creatures exist as objects. For the on-screen
        /// diagnostics, and for noticing a leak.
        /// </summary>
        public int TileObjects => _tiles.Count;
        public int CreatureObjects => _creatures.Count;
        public bool TryGetCreaturePosition(CreatureQueryTarget target, out Vector3 position)
        {
            if (_creatures.TryGetValue(target.Value, out GameObject creature) && creature != null && creature.activeInHierarchy)
            { position = creature.transform.position; return true; }
            position = default; return false;
        }
        public int ObjectObjects => _objects.Count;

        /// <summary>DK2 draws the Hand's hover id a second time with AA_RedTexture each frame
        /// (Presentation_Update, 0x0043BE40): the caller decides which picks are tinted
        /// (<see cref="HoverTint"/>). A gold object's art (flag 0x10000) first swaps each material to
        /// its last texture, the spill cards' _HIGHLIGHT twin, drawn here over the original one queue
        /// later. The red pass skips additive and alpha-blended materials unless
        /// <paramref name="coversBlended"/> (draw flag 0x400; <see cref="HoverTint.DrawsOverlay"/>;
        /// gold-hover-and-sparkle.md §2). A mesh holds its sampled pose, so both follow the animation.</summary>
        public void DrawHoverOverlay(WorldHoverHit hit, bool coversBlended = false)
        {
            if (_classic == null) return;
            GameObject go;
            Material[] swapped = null;
            if (hit.Kind == WorldHoverKind.Creature)
            {
                if (!_creatures.TryGetValue(hit.EntityId, out go)) return;
            }
            else if (hit.Kind == WorldHoverKind.Object && _meshObjects.Contains(hit.EntityId)
                && _objects.TryGetValue(hit.EntityId, out go) && _objectPoses.TryGetValue(hit.EntityId, out ObjectView pose))
            {
                if (pose.HoverTexture) swapped = _classic.HoverTextures(pose.Mesh);
            }
            else if (hit.Kind == WorldHoverKind.Door || hit.Kind == WorldHoverKind.Trap)
            {
                var id = new StructureId(hit.Kind == WorldHoverKind.Door ? StructureKind.Door : StructureKind.Trap, hit.EntityId);
                if (!_structures.TryGetValue(id, out go)) return;
            }
            else return;
            if (go == null || !go.activeInHierarchy || !go.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return;
            var overlay = new RenderParams(_classic.HoverOverlay())
            { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            Mesh mesh = filter.sharedMesh;
            Matrix4x4 matrix = go.transform.localToWorldMatrix;
            _hoverMaterials.Clear();
            if (go.TryGetComponent(out Renderer renderer)) renderer.GetSharedMaterials(_hoverMaterials);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (swapped != null && sub < swapped.Length && swapped[sub] != null)
                    Graphics.RenderMesh(new RenderParams(swapped[sub])
                    { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, mesh, sub, matrix);
                bool blended = sub < _hoverMaterials.Count && _classic.IsKmfBlended(_hoverMaterials[sub]);
                if (HoverTint.DrawsOverlay(blended, coversBlended)) Graphics.RenderMesh(overlay, mesh, sub, matrix);
            }
        }

        /// <summary>What the Hand points at (Hand_ResolveScreenTarget 0x005992B0): first the rendered
        /// hit, every mesh <paramref name="rendered"/> admits whose screen circle (KMF bounding sphere
        /// from UV2, else renderer bounds; <see cref="HoverPickRules.CircleHolds"/>) holds the pointer
        /// and which the terrain does not hide, the nearest triangle under the pointer winning over the
        /// nearest centre in view depth along <paramref name="viewForward"/> (<see cref="RenderedPick"/>);
        /// then the 3×3 sweep (0x0044DEC0) over what <paramref name="fallback"/> admits. A rendered winner
        /// <paramref name="resolves"/> refuses leaves the tile hit without the sweep, as 0x005992B0 does; a
        /// refused sweep object leaves it too.
        /// Null filters admit everything. The ray distance stands in for DK2's mean vertex depth; the
        /// terrain test uses the ray's entry into the renderer bounds, or, when only the circle holds,
        /// the ray distance at the sphere centre's depth. The triangle test reads the MeshFilter's mesh
        /// (a creature's posed vertices); a placeholder gets the centre test only.</summary>
        public WorldHoverHit PickHoverEntity(Ray ray, Vector3 viewForward, float terrainDistance, TilePos tile,
            Vector3 pointerGround, System.Func<WorldHoverKind, int, bool> rendered = null,
            System.Func<WorldHoverKind, int, bool> fallback = null, System.Func<WorldHoverHit, bool> resolves = null,
            Plane[] frustum = null, bool objectsOnly = false)
        {
            WorldHoverHit hit = new WorldHoverHit(WorldHoverKind.Tile, tile);
            _pickCandidates.Clear();
            var pick = new RenderedPick();
            foreach (var entry in _creatures)
                Offer(entry.Value, new WorldHoverHit(WorldHoverKind.Creature, _creaturePoses[entry.Key].Tile, entry.Key));
            foreach (var entry in _objects)
                if (_objectPoses.TryGetValue(entry.Key, out ObjectView pose)
                    && ObjectHoverHighlight.IsHoverTarget(entry.Key, pose.ObjectTypeId))
                    Offer(entry.Value, new WorldHoverHit(WorldHoverKind.Object, pose.Tile, entry.Key));
            foreach (var entry in _structures)
                if (_structureTiles.TryGetValue(entry.Key, out TilePos at))
                    Offer(entry.Value, new WorldHoverHit(entry.Key.Kind == StructureKind.Door ? WorldHoverKind.Door : WorldHoverKind.Trap,
                        at, entry.Key.Value));
            if (pick.TryResult(out int picked, out _))
            {
                WorldHoverHit winner = _pickCandidates[picked];
                return resolves == null || resolves(winner) ? winner : hit;
            }

            var pointer = new FpPosition3(new Fp32((int)(pointerGround.x * Fp32.One)),
                new Fp32((int)(-pointerGround.z * Fp32.One)), new Fp32(0));
            int closestObject = HoverPickRules.FallbackObjectRadius(objectsOnly);
            for (int y = tile.Y - 1; y <= tile.Y + 1; y++)
                for (int x = tile.X - 1; x <= tile.X + 1; x++)
                {
                    foreach (var entry in _creaturePoses)
                        if (!objectsOnly && entry.Value.Tile == new TilePos(x, y) && _creatures.TryGetValue(entry.Key, out GameObject go)
                            && go != null && go.activeSelf && Distance(go) < Fp32.One / 2
                            && (fallback == null || fallback(WorldHoverKind.Creature, entry.Key)))
                            return new WorldHoverHit(WorldHoverKind.Creature, entry.Value.Tile, entry.Key);
                    foreach (var entry in _objectPoses)
                        if (ObjectHoverHighlight.IsHoverTarget(entry.Key, entry.Value.ObjectTypeId) && entry.Value.Tile == new TilePos(x, y)
                            && _objects.TryGetValue(entry.Key, out GameObject go) && go != null && go.activeSelf
                            && (fallback == null || fallback(WorldHoverKind.Object, entry.Key)))
                        {
                            int distance = Distance(go);
                            if (distance < closestObject)
                            {
                                closestObject = distance;
                                hit = new WorldHoverHit(WorldHoverKind.Object, entry.Value.Tile, entry.Key);
                            }
                        }
                }
            // The sweep's winner reaches the same class dispatch (0x00599710): an object the
            // ownership arm refuses leaves the tile hit; the next-nearest object is not tried.
            return hit.Kind == WorldHoverKind.Object && resolves != null && !resolves(hit)
                ? new WorldHoverHit(WorldHoverKind.Tile, tile) : hit;

            int Distance(GameObject go)
            {
                Vector3 point = go.transform.position;
                return HandHoverDistance.Between(pointer, new FpPosition3(new Fp32((int)(point.x * Fp32.One)),
                    new Fp32((int)(-point.z * Fp32.One)), new Fp32(0)));
            }
            void Offer(GameObject go, WorldHoverHit candidate)
            {
                if (go == null || !go.activeSelf || !go.TryGetComponent<Renderer>(out Renderer renderer)) return;
                Mesh mesh = go.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                Bounds bounds = renderer.bounds;
                // DK2 culls a draw outside the view (0x00575D70) before it can enter the pick list; without
                // this, a sphere reaching behind the eye "holds" every pointer (CircleHolds' unbounded case)
                // and an off-screen thing behind the camera wins the nearest-depth test.
                if (frustum != null && !GeometryUtility.TestPlanesAABB(frustum, bounds)) return;
                Vector3 centre;
                float radius;
                if (mesh != null && TryPickSphere(mesh, out Vector4 sphere))
                {
                    Transform t = go.transform;
                    centre = t.TransformPoint(sphere.x, sphere.y, sphere.z);
                    Vector3 scale = t.lossyScale;
                    radius = sphere.w * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                }
                else
                {
                    centre = bounds.center;
                    radius = bounds.extents.magnitude;
                }
                float depth = Vector3.Dot(centre - ray.origin, viewForward);
                float along = Vector3.Dot(ray.direction, viewForward);
                if (along <= 0f) return;
                float rayDistance = depth / along;
                if (!HoverPickRules.CircleHolds(depth, radius, (centre - ray.GetPoint(rayDistance)).sqrMagnitude)) return;
                float entryDistance = bounds.IntersectRay(ray, out float boundsEntry) ? boundsEntry : rayDistance;
                if (entryDistance >= terrainDistance) return;
                if (rendered != null && !rendered(candidate.Kind, candidate.EntityId)) return;
                float? triangle = mesh != null ? NearestTriangle(mesh, go.transform, ray, terrainDistance) : null;
                pick.Offer(_pickCandidates.Count, triangle, depth);
                _pickCandidates.Add(candidate);
            }
        }

        public CampaignSlot PickFrontEndMap(Ray ray, IReadOnlyDictionary<int, CampaignSlot> mapSlots)
        {
            float nearest = float.PositiveInfinity;
            CampaignSlot result = CampaignSlot.None;
            foreach (var pair in _objects)
            {
                GameObject go = pair.Value;
                if (go == null || !go.activeSelf || !go.TryGetComponent(out MeshFilter filter)
                    || !go.TryGetComponent(out Renderer renderer) || !renderer.bounds.IntersectRay(ray)) continue;
                float? triangle = NearestTriangle(filter.sharedMesh, go.transform, ray, nearest);
                if (triangle is not float distance || distance >= nearest) continue;
                nearest = distance;
                result = mapSlots.TryGetValue(pair.Key, out CampaignSlot slot) ? slot : CampaignSlot.None;
            }
            return result;
        }

        private readonly List<WorldHoverHit> _pickCandidates = new List<WorldHoverHit>();
        private readonly Dictionary<Mesh, Vector4> _pickSpheres = new Dictionary<Mesh, Vector4>();
        private readonly List<Vector4> _pickSphereScratch = new List<Vector4>();

        /// <summary>The mesh's KMF bounding sphere in its own space (xyz the Origin, w BoundsScale), which
        /// ClassicMeshes writes into every vertex's UV2 and an instanced creature mesh copies; read once
        /// per mesh. False for a mesh without it (a placeholder) or one the CPU cannot read.</summary>
        private bool TryPickSphere(Mesh mesh, out Vector4 sphere)
        {
            if (!_pickSpheres.TryGetValue(mesh, out sphere))
            {
                sphere = new Vector4(0f, 0f, 0f, -1f);
                if (mesh.isReadable && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2))
                {
                    mesh.GetUVs(2, _pickSphereScratch);
                    if (_pickSphereScratch.Count > 0) sphere = _pickSphereScratch[0];
                    _pickSphereScratch.Clear();
                }
                // Destroyed creature meshes stay as keys; a bound keeps the table from growing for ever.
                if (_pickSpheres.Count >= 4096) _pickSpheres.Clear();
                _pickSpheres[mesh] = sphere;
            }
            return sphere.w > 0f;
        }
        private readonly List<Vector3> _pickVertices = new List<Vector3>();
        private readonly List<int> _pickTriangles = new List<int>();

        /// <summary>The ray distance to the nearest triangle of <paramref name="mesh"/> it crosses before
        /// <paramref name="limit"/>, either winding, or null (Möller-Trumbore in the mesh's own space; an
        /// affine transform keeps the ray parameter). A mesh the CPU cannot read counts as missed.
        /// DK2 (0x00585AD0) accepts one screen winding only (all three edge products &lt;= 0); which of
        /// our windings that is has not been mapped through the KMF import and DK2's projection, so
        /// both are taken. A ray through a closed mesh meets its near side first either way, so this
        /// differs only where the pointer reaches a triangle's back alone.</summary>
        private float? NearestTriangle(Mesh mesh, Transform transform, Ray ray, float limit)
        {
            if (!mesh.isReadable) return null;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            Vector3 origin = toLocal.MultiplyPoint3x4(ray.origin), direction = toLocal.MultiplyVector(ray.direction);
            mesh.GetVertices(_pickVertices);
            float best = limit;
            bool found = false;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                mesh.GetTriangles(_pickTriangles, sub);
                for (int i = 0; i + 2 < _pickTriangles.Count; i += 3)
                {
                    Vector3 a = _pickVertices[_pickTriangles[i]];
                    Vector3 ab = _pickVertices[_pickTriangles[i + 1]] - a, ac = _pickVertices[_pickTriangles[i + 2]] - a;
                    Vector3 p = Vector3.Cross(direction, ac);
                    float det = Vector3.Dot(ab, p);
                    if (det > -1e-9f && det < 1e-9f) continue;
                    float inv = 1f / det;
                    Vector3 s = origin - a;
                    float u = Vector3.Dot(s, p) * inv;
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(s, ab);
                    float v = Vector3.Dot(direction, q) * inv;
                    if (v < 0f || u + v > 1f) continue;
                    float t = Vector3.Dot(ac, q) * inv;
                    if (t > 0f && t < best) { best = t; found = true; }
                }
            }
            return found ? best : (float?)null;
        }

        /// <summary>
        /// Set when a provider handed back a real file. Nothing loads one yet
        /// (see <see cref="Instantiate"/>), so this is how the runner can say so
        /// on screen rather than silently drawing a box.
        /// </summary>
        public bool SawFileBackedAssets { get; private set; }

        /// <summary>A point on the ground plane, from DK2's plane coordinates in tiles: X east,
        /// Y south. The one place the grid's axes meet Unity's.</summary>
        public static Vector3 Ground(float x, float y, float height = 0f) => new Vector3(x, height, -y);

        /// <param name="classic">Draws install-backed objects, creatures, torches and plain terrain
        /// with their real meshes; null keeps every one a placeholder.</param>
        /// <param name="lattice">Bends the classic terrain as DK2 does; null draws it straight.</param>
        public DungeonScene(Transform root, int worldWidth, int worldHeight, ClassicMeshes classic = null,
            TerrainLattice lattice = null, bool creatureShadows = true, bool cheapLighting = false)
        {
            _root = root;
            _lattice = lattice;
            _worldWidth = worldWidth;
            _worldHeight = worldHeight;
            _classic = classic;
            if (classic != null && creatureShadows) _creatureShadows = new ClassicCreatureShadows(root);
            if (classic != null && classic.UsesClassicVertexLighting)
            {
                _torchLights = new ClassicTorchLightBuffer(worldWidth, worldHeight);
                _tileFill = new ClassicTileFillBuffer(worldWidth, worldHeight);
                _worldLights = new ClassicWorldLightCache(worldWidth, worldHeight);
                _portalCentreLights = new PortalCentreLights(new EffectTime((long)(Time.unscaledTimeAsDouble * 1000d)));
                _torchFlags = cheapLighting ? ClassicLightFlags.CheapTorch : ClassicLightFlags.NormalTorch;
                Shader.SetGlobalFloat("_DK2InitialAmbientPreview", classic.NativeInitialAmbient ? 1 : 0);
            }
            if (classic != null)
            {
                _terrain = new TerrainChunks(root, classic, worldWidth, worldHeight, lattice);
                _liquids = new LiquidSurfaces(root, classic, worldWidth, worldHeight);
            }
        }

        public void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
        {
            _worldLights?.SetTerrain(tile.Position, tile.PointLight);
            // Revealed ground (Sight, the claim scan) is as bright as explored ground (owner 2026-10-21).
            _tileFill?.Set(tileIndex, tile.Explored || tile.Scanned || tile.PointLight.HasValue);
            _torchGeometryDirty = true;
            // Steps 1 and 2 of the classic look: plain terrain and rooms from their pieces. A room
            // drawn so needs no furniture box; its objects are step 3.
            _liquids?.Set(tileIndex, tile);
            if (_terrain != null && _terrain.TrySet(tileIndex, tile))
            {
                ApplyFurniture(tileIndex, tile, default);
                return;
            }

            GameObject go = Instantiate(ground, $"tile {tile.Position.X},{tile.Position.Y}");
            if (go != null)
            {
                Place(go, tile.Position.X, tile.Position.Y, tile.Solid);
                if (tile.Tagged)
                    SetColour(go, Color.Lerp(ColourOf(LookOf(ground)), TagTint, 0.65f));
                _tiles[tileIndex] = go;
            }

            ApplyFurniture(tileIndex, tile, furniture);
        }

        public void UpdateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
        {
            // The terrain may have become something else entirely — a dug wall
            // becomes floor — so the ground object is rebuilt rather than
            // recoloured. Tiles change rarely (the presenter guarantees only
            // changed ones arrive), so this costs nothing in practice and avoids
            // a second, subtly different code path.
            if (_tiles.TryGetValue(tileIndex, out GameObject old) && old != null)
            {
                Object.Destroy(old);
                _tiles.Remove(tileIndex);
            }

            CreateTile(tileIndex, tile, ground, furniture);
        }

        public void CreateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
        {
            using var createScope = CreateCreatureMarker.Auto();
            // The idle model remains the fallback for a missing or unimplemented clip choice.
            GameObject go = _classic?.TryCreate(mesh, $"creature {creatureId}", _root);
            if (go != null) _meshCreatures.Add(creatureId);
            else go = Instantiate(mesh, $"creature {creatureId}");
            if (go == null) return;

            _creatures[creatureId] = go;
            _creatureHeads.TryAdd(creatureId, null);
            _creatureArt[creatureId] = mesh.Reference;
            _creaturePoses[creatureId] = creature;
            ConfigureCreatureClip(go, creatureId, creature, mesh, animation);
            if (_meshCreatures.Contains(creatureId)) _creatureShadows?.Register(go);
            PositionCreature(go, creatureId, creature, mesh.Look);
        }

        private static readonly ProfilerMarker CreateCreatureMarker = new ProfilerMarker("DK2.Scene.CreateCreature");

        public void UpdateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
        {
            if (_creatureArt.TryGetValue(creatureId, out AssetRef previous) && previous != mesh.Reference)
                RemoveCreature(creatureId, keepHead: true);
            if (!_creatures.TryGetValue(creatureId, out GameObject go) || go == null)
            {
                CreateCreature(creatureId, creature, mesh, animation);
                return;
            }

            PositionCreature(go, creatureId, creature, mesh.Look);
            _creaturePoses[creatureId] = creature;
            ConfigureCreatureClip(go, creatureId, creature, mesh, animation);
            if (_meshCreatures.Contains(creatureId)) return;

            // A creature carrying gold reads as carrying it. Standing in for the
            // real thing, which is an object an imp holds — and objects are M6.
            SetColour(go, creature.GoldHeld > 0
                ? new Color(0.95f, 0.82f, 0.25f)
                : ColourOf(mesh.Look));
        }

        /// <summary>An effect's tie to a creature (0x00434E80): gone with its object, else its head
        /// as last drawn (Ground's inverse), or not found for a clip without one or another point.
        /// Safe from the effects' worker thread.</summary>
        public EffectAttachment CreatureAttachment(int creatureId, uint point)
        {
            if (!_creatureHeads.TryGetValue(creatureId, out FpPosition3? head)) return EffectAttachment.Gone;
            return point == DK2.Art.Kmf.KmfControlPoint.Head && head.HasValue
                ? EffectAttachment.At(head.Value) : EffectAttachment.NotFound;
        }

        public void RemoveCreature(int creatureId) => RemoveCreature(creatureId, keepHead: false);

        // A new look for the same creature keeps its head entry, so a tied effect is not let go.
        private void RemoveCreature(int creatureId, bool keepHead)
        {
            if (_creatures.TryGetValue(creatureId, out GameObject go) && go != null)
            {
                _creatureShadows?.Remove(go);
                go.SetActive(false);
                Object.Destroy(go);
            }
            _meshCreatures.Remove(creatureId);

            _creatures.Remove(creatureId);
            if (!keepHead) _creatureHeads.TryRemove(creatureId, out _);
            _gemSparkleQuanta.Remove(creatureId);
            _creatureArt.Remove(creatureId);
            _creaturePoses.Remove(creatureId);
            _creatureClips.Remove(creatureId);
        }

        private void ConfigureCreatureClip(GameObject go, int id, in CreatureView creature,
            in ResolvedAsset mesh, in ResolvedAsset animation)
        {
            if (_classic == null || !_meshCreatures.Contains(id)) return;
            AssetRef clip = creature.PlaysAnimation ? animation.Reference : default;
            var identity = (clip, creature.AnimationStartedAt, creature.AnimationLoops, creature.AnimationDeferred);
            if (_creatureClips.TryGetValue(id, out var previous) && previous == identity) return;
            _creatureClips[id] = identity;
            ClassicCreatureAnimation player = go.GetComponent<ClassicCreatureAnimation>();
            double startSeconds = creature.AnimationStartedAt.HasValue
                ? (creature.AnimationStartedAt.Value.Value + 1d) / DK2.Simulation.SimulationTiming.OriginalTicksPerGameSecond
                : _renderSeconds;
            if (creature.PlaysAnimation && _classic.ConfigureAnimation(go, animation, creature.AnimationLoops,
                startSeconds, creature.AnimationMovementSpeed,
                creature.AnimationDeferred ? AnimationReplacement.Deferred : AnimationReplacement.Immediate,
                creature.AnimationPaused, creature.AnimationInitialMovementSpeed)) return;
            if (player != null) player.Stop();
            _classic.RestoreModel(go, mesh);
        }

        public void AdvanceTorchAnimation() => _torchFlames?.Advance();

        // Called before the worker tick starts; no render callback reads the mutable world.
        public void DrawCreatureShadows(Camera camera, WorldView view, WorldState world)
        {
            _torchFlames?.Draw(camera);
            if (_creatureShadows == null || camera == null) return;
            foreach (GameObject creature in _creatures.Values)
            {
                if (creature == null) continue;
                Vector3 position = creature.transform.position;
                var point = new FpPosition3(new Fp32((int)(position.x * Fp32.One)),
                    new Fp32((int)(-position.z * Fp32.One)), new Fp32((int)(position.y * Fp32.One)));
                float ground = view.GroundHeightAt(world, point).Raw / (float)Fp32.One;
                _creatureShadows.SetGroundHeight(creature, ground + .01f);
            }
            _creatureShadows.Render(camera);
        }

        public void SetCreatureFrameTime(GameTick tick, float fraction)
        {
            _renderSeconds = (tick.Value + (double)Mathf.Clamp01(fraction)) / DK2.Simulation.SimulationTiming.OriginalTicksPerGameSecond;
        }

        private static readonly ProfilerMarker FrameMarker = new ProfilerMarker("DK2.Scene.AnimatedFrame");

        public void DrawCreatureFrame(GameTick tick, float fraction)
        {
            using var frameScope = FrameMarker.Auto();
            fraction = Mathf.Clamp01(fraction);
            SetCreatureFrameTime(tick, fraction);
            while (_heartAdvances.TryDequeue(out var advance))
                foreach (KeyValuePair<int, ObjectView> heart in _objectPoses)
                    if (heart.Value.HeartVisual.HasValue && heart.Value.OwnerPlayerId == advance.Owner
                        && _objects.TryGetValue(heart.Key, out GameObject heartObject) && heartObject != null)
                        heartObject.GetComponent<ClassicCreatureAnimation>()?.AdvanceHealthPhase(advance.Step);
            foreach (KeyValuePair<int, CreatureView> entry in _creaturePoses)
            {
                if (!_creatures.TryGetValue(entry.Key, out GameObject go) || go == null) continue;
                CreatureView pose = entry.Value;
                Vector3 position = go.transform.position;
                Vector3 ground = Ground(
                    Mathf.Lerp(pose.PreviousPosX, pose.PosX, fraction) / Fp32.One,
                    Mathf.Lerp(pose.PreviousPosY, pose.PosY, fraction) / Fp32.One,
                    Mathf.Lerp(pose.PreviousHeight, pose.Height, fraction) / Fp32.One);
                go.transform.position = new Vector3(ground.x,
                    _meshCreatures.Contains(entry.Key) ? ground.y : position.y, ground.z);
                ClassicCreatureAnimation player = go.GetComponent<ClassicCreatureAnimation>();
                if (player != null) player.Draw(_renderSeconds, pose.AnimationMovementSpeed, pose.AnimationPaused);
                _creatureHeads[entry.Key] = player != null
                    && player.TryGetControlPoint(DK2.Art.Kmf.KmfControlPoint.Head, out Vector3 head)
                        ? new FpPosition3(new Fp32((int)(head.x * Fp32.One)), new Fp32((int)(-head.z * Fp32.One)),
                            new Fp32((int)(head.y * Fp32.One)))
                        : (FpPosition3?)null;
                // Render_MeshEffectPoints (0x005A3AA0) runs after each draw of a creature's mesh: a
                // gem carrier gives off effect 257 at its point 10001, every draw. Per-draw work is
                // paced by the 50 ms presentation quantum, as the wall flames are (ADR-0494): at most
                // one per draw and one per quantum, since retail DK2's frame rate is open (Q604).
                if (pose.GemCarrier && player != null && GemSparkleRequested != null
                    && player.TryGetControlPoint(CreatureView.GemDustSparkle.ControlPoint, out Vector3 sparkle))
                {
                    long quantum = HeartEffectClock().Milliseconds / 50;
                    if (!_gemSparkleQuanta.TryGetValue(entry.Key, out long last) || quantum > last)
                    {
                        _gemSparkleQuanta[entry.Key] = quantum;
                        GemSparkleRequested(new FpPosition3(new Fp32((int)(sparkle.x * Fp32.One)),
                            new Fp32((int)(-sparkle.z * Fp32.One)), new Fp32((int)(sparkle.y * Fp32.One))));
                    }
                }
            }
            foreach (KeyValuePair<int, ObjectView> entry in _objectPoses)
            {
                if (!_objects.TryGetValue(entry.Key, out GameObject go) || go == null) continue;
                ObjectView pose = entry.Value;
                Vector3 ground = Ground(Mathf.Lerp(pose.PreviousPosX, pose.PosX, fraction) / Fp32.One,
                    Mathf.Lerp(pose.PreviousPosY, pose.PosY, fraction) / Fp32.One,
                    Mathf.Lerp(pose.PreviousHeight, pose.Height, fraction) / Fp32.One);
                go.transform.position = new Vector3(ground.x,
                    _meshObjects.Contains(entry.Key) ? ground.y : go.transform.position.y, ground.z);
                int turn = ((pose.Heading - pose.PreviousHeading + 1024) & 2047) - 1024;
                float yaw = (pose.PreviousHeading + 1024 + turn * fraction) * 360f / 2048f;
                float pitch = pose.PreviousPitch + (((pose.Pitch - pose.PreviousPitch + 1024) & 2047) - 1024) * fraction;
                float roll = pose.PreviousRoll + (((pose.Roll - pose.PreviousRoll + 1024) & 2047) - 1024) * fraction;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f)
                    * Quaternion.AngleAxis(roll * 360f / 2048f, Vector3.forward)
                    * Quaternion.Euler(-pitch * 360f / 2048f, 0f, 0f);
                ClassicCreatureAnimation player = go.GetComponent<ClassicCreatureAnimation>();
                player?.Draw(_renderSeconds, phaseStepOverride: pose.HeartVisual?.PhaseStep);
                if (pose.HeartVisual.HasValue && player != null && player.BeatPending
                    && go.GetComponent<Renderer>().isVisible && _heartPulseGate.ConsumePendingBeat(HeartEffectClock()))
                {
                    player.ConsumeBeat();
                    if (HeartPulseGate.CanEmit(tick) && pose.HeartVisual.Value.ManaEffect != EffectId.None)
                        HeartEffectRequested?.Invoke(new ViewEffectRequest(pose.HeartVisual.Value.ManaEffect,
                            new PlayerId(pose.OwnerPlayerId), new FpPosition3(new Fp32(pose.PosX),
                                new Fp32(pose.PosY), new Fp32(pose.Height))));
                }
            }
            if (_torchGeometryDirty)
            {
                foreach (GameObject torch in _torches.Values)
                    if (torch != null && torch.transform.childCount > 0)
                        torch.transform.GetChild(0).GetComponent<ClassicTorchGeometry>()?.Refresh();
                _torchGeometryDirty = false;
            }
        }

        public void SetPortalLights(IReadOnlyList<TilePos> requests)
        {
            if (_worldLights == null) return;
            while (_portalLightRequestsPublished < requests.Count)
                _portalCentreLights.Create(requests[_portalLightRequestsPublished++]);
            _worldLights.UpdatePortalLights(_portalCentreLights.Advance(
                new EffectTime((long)(Time.unscaledTimeAsDouble * 1000d)), _torchFlags == ClassicLightFlags.CheapTorch));
        }

        public void SetObjectLights(IReadOnlyList<ObjectView> objects, float fraction)
        {
            _worldLights?.UpdateObjects(objects, fraction, _torchFlags == ClassicLightFlags.CheapTorch);
        }

        public void SetHandLight(ClassicPointLight? light) => _worldLights?.UpdateHand(light);

        public void SetEffectLights(IReadOnlyList<EffectLightView> lights)
        {
            _worldLights?.UpdateEffects(lights, _torchFlags == ClassicLightFlags.CheapTorch);
        }

        public void CreateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
        {
            // With the install, an object it has no art for draws nothing, as DK2's draws nothing
            // for an art not loaded (0x0059B880): the Guard Post, the Heart's Obstacle.
            GameObject go = _classic?.TryCreate(mesh, $"object {objectId}", _root);
            if (go != null) _meshObjects.Add(objectId);
            else if (_classic == null) go = Instantiate(mesh, $"object {objectId}");
            if (go == null)
            {
                _artless[objectId] = mesh.Reference;
                return;
            }
            _artless.Remove(objectId);

            _objects[objectId] = go;
            PositionObject(go, objectId, instance, mesh.Look);
            ConfigureObjectClip(go, objectId, instance, mesh);
            if (instance.HeartVisual.HasValue) _classic?.ConfigureHeartDecay(go, mesh, instance.HeartVisual.Value.DecayPhase, instance.AddedLight);
            else if (instance.TexturePhase is Fp32 phase) _classic?.ConfigureTexturePhase(go, mesh, phase, instance.AddedLight);
            if (_meshObjects.Contains(objectId)) _classic?.SetAlpha(go, instance.Alpha);
        }

        public void UpdateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
        {
            if (!_objects.TryGetValue(objectId, out GameObject go) || go == null)
            {
                if (!(_artless.TryGetValue(objectId, out AssetRef asked) && asked == mesh.Reference))
                    CreateObject(objectId, instance, mesh);
                return;
            }

            PositionObject(go, objectId, instance, mesh.Look);
            ConfigureObjectClip(go, objectId, instance, mesh);
            if (instance.HeartVisual.HasValue) _classic?.ConfigureHeartDecay(go, mesh, instance.HeartVisual.Value.DecayPhase, instance.AddedLight);
            else if (instance.TexturePhase is Fp32 phase) _classic?.ConfigureTexturePhase(go, mesh, phase, instance.AddedLight);
            if (_meshObjects.Contains(objectId)) _classic?.SetAlpha(go, instance.Alpha);
        }

        private void ConfigureObjectClip(GameObject go, int id, in ObjectView pose, in ResolvedAsset mesh)
        {
            if (!pose.PlaysAnimation)
            {
                _objectPoses[id] = pose;
                if (_objectClips.Remove(id)) go.GetComponent<ClassicCreatureAnimation>()?.Stop();
                if (_classic != null && _meshObjects.Contains(id)) _classic.RestoreModel(go, mesh, pose.AddedLight);
                return;
            }
            _objectPoses[id] = pose;
            var identity = (mesh.Reference, pose.AnimationStartedAt, pose.AnimationLoops, pose.AddedLight, pose.AnimationInitialPhase);
            if (_objectClips.TryGetValue(id, out var previous) && previous == identity) return;
            _objectClips[id] = identity;
            double startSeconds = pose.AnimationStartedAt.HasValue
                ? (pose.AnimationStartedAt.Value.Value + 1d) / DK2.Simulation.SimulationTiming.OriginalTicksPerGameSecond
                : _renderSeconds;
            if (_classic != null && _meshObjects.Contains(id)
                && _classic.ConfigureAnimation(go, mesh, pose.AnimationLoops, startSeconds,
                    phaseStepOverride: pose.HeartVisual?.PhaseStep, addedLight: pose.AddedLight, initialPhase: pose.AnimationInitialPhase)) return;
            go.GetComponent<ClassicCreatureAnimation>()?.Stop();
            if (_classic != null && _meshObjects.Contains(id)) _classic.RestoreModel(go, mesh, pose.AddedLight);
        }

        public void RemoveObject(int objectId)
        {
            if (_objects.TryGetValue(objectId, out GameObject go) && go != null)
                Object.Destroy(go);

            _objects.Remove(objectId);
            _objectPoses.Remove(objectId);
            _objectClips.Remove(objectId);
            _meshObjects.Remove(objectId);
            _artless.Remove(objectId);
        }

        public void CreateStructure(StructureId id, in StructureView structure, in ResolvedAsset mesh)
        {
            GameObject go = _classic?.TryCreate(mesh, $"{id.Kind} {id.Value}", _root, structure.LastFrame);
            bool realMesh = go != null;
            if (go == null && _classic == null) go = Instantiate(mesh, $"{id.Kind} {id.Value}");
            if (go == null) return;
            _structures[id] = go;
            _structureTiles[id] = structure.Tile;
            float x = structure.PosX / (float)Fp32.One, y = structure.PosY / (float)Fp32.One;
            go.transform.position = Ground(x, y, structure.Height / (float)Fp32.One);
            go.transform.localRotation = Quaternion.Euler(0f, structure.MeshYaw * 360f / 2048f, 0f);
            if (realMesh) go.transform.localScale = Vector3.one;
            if (!structure.LockMesh.IsEmpty && _classic != null)
                _classic.TryCreate(structure.LockMesh, "door lock", go.transform);
        }

        public void UpdateStructure(StructureId id, in StructureView structure, in ResolvedAsset mesh)
        {
            RemoveStructure(id);
            CreateStructure(id, structure, mesh);
        }

        public void RemoveStructure(StructureId id)
        {
            if (_structures.TryGetValue(id, out GameObject go) && go != null) Object.Destroy(go);
            _structures.Remove(id);
            _structureTiles.Remove(id);
        }

        /// <summary>A wall torch: a point light, and its mesh when it has one. How DK2's light
        /// numbers map onto Unity's (colour over 255, intensity over 128) is this renderer's
        /// choice, not a claim about the original (ADR-0391).</summary>
        public void CreateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh)
        {
            // Unity's fake null makes ?? unsafe on its objects, hence the explicit tests.
            GameObject go = new GameObject($"torch {floorTileIndex}");
            go.transform.SetParent(_root, false);
            GameObject model = _classic?.TryCreate(mesh, "torch mesh", go.transform,
                lightingMode: ClassicMeshes.LightingMode.TerrainPiece);
            bool deform = model != null && _lattice != null;
            if (model == null)
            {
                model = Instantiate(mesh, "torch mesh");
                if (model != null) model.transform.localScale = new Vector3(0.1f, 0.25f, 0.1f); // a placeholder cube is a whole tile
            }
            if (model != null)
            {
                model.transform.SetParent(go.transform, false);
                if (deform) model.AddComponent<ClassicTorchGeometry>();
            }
            ApplyTorchPose(go, torch);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            ApplyTorchLight(light, torch);
            _torches[floorTileIndex] = go;
            _torchLights?.Set(floorTileIndex, torch);
            _worldLights?.SetTorch(new TilePos(floorTileIndex % _worldWidth, floorTileIndex / _worldWidth),
                ClassicPointLight.FromTorch(torch, _torchFlags));
            _creatureShadows?.SetTorch(torch);
            _torchFlames?.Set(torch);
        }

        public void UpdateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh)
        {
            if (!_torches.TryGetValue(floorTileIndex, out GameObject go) || go == null)
            {
                CreateTorch(floorTileIndex, torch, mesh);
                return;
            }

            ApplyTorchPose(go, torch);
            Light light = go.GetComponent<Light>();
            if (light != null) ApplyTorchLight(light, torch);
            _torchLights?.Set(floorTileIndex, torch);
            _worldLights?.SetTorch(new TilePos(floorTileIndex % _worldWidth, floorTileIndex / _worldWidth),
                ClassicPointLight.FromTorch(torch, _torchFlags));
            _creatureShadows?.SetTorch(torch);
            _torchFlames?.Set(torch);
        }

        public void RemoveTorch(int floorTileIndex)
        {
            if (_torches.TryGetValue(floorTileIndex, out GameObject go) && go != null)
                Object.Destroy(go);

            _torches.Remove(floorTileIndex);
            _torchLights?.Remove(floorTileIndex);
            _worldLights?.SetTorch(new TilePos(floorTileIndex % _worldWidth, floorTileIndex / _worldWidth), null);
            _creatureShadows?.RemoveTorch(floorTileIndex);
            _torchFlames?.Remove(floorTileIndex);
        }

        private void ApplyTorchPose(GameObject go, in TorchView torch)
        {
            go.transform.localPosition = Ground(
                torch.PosX / (float)Fp32.One, torch.PosY / (float)Fp32.One, torch.Height / (float)Fp32.One);
            if (go.transform.childCount == 0) return;
            Transform model = go.transform.GetChild(0);
            ClassicTorchGeometry geometry = model.GetComponent<ClassicTorchGeometry>();
            if (geometry != null)
            {
                geometry.Configure(torch, _lattice);
                return;
            }
            model.localPosition = Ground(
                (torch.MeshPosX - torch.PosX) / (float)Fp32.One,
                (torch.MeshPosY - torch.PosY) / (float)Fp32.One);
            // Type-5 light meshes use yaw without the ordinary object's half-turn (wall-torches.md §4).
            model.localRotation = Quaternion.Euler(0f, torch.Yaw * 360f / 2048f, 0f);
        }

        private static void ApplyTorchLight(Light light, in TorchView torch)
        {
            ClassicPointLight inputs = ClassicPointLight.FromTorch(torch);
            light.color = new Color(inputs.Colour.X, inputs.Colour.Y, inputs.Colour.Z);
            light.intensity = inputs.Intensity;
            light.range = inputs.Radius;
        }

        /// <summary>Stages selected per-vertex point lights for Flush and replaces punctual lighting across the grid.
        /// Empty/unpublished tiles receive none; caller owns native visibility and classification.</summary>
        public void SetPointLightTable(ClassicLightTable table)
        {
            if (table == null) throw new System.ArgumentNullException(nameof(table));
            if ((uint)table.Tile.X >= (uint)_worldWidth || (uint)table.Tile.Y >= (uint)_worldHeight)
                throw new System.ArgumentOutOfRangeException(nameof(table));
            if (_classic == null || !_classic.UsesClassicVertexLighting)
                throw new System.InvalidOperationException("Tile point-light tables require classic vertex lighting.");
            _tileLights ??= new ClassicTileLightBuffer(_worldWidth, _worldHeight);
            _tileLights.Set(table);
        }

        public void PrepareClassicLighting(Camera camera, float nativeViewDistance, Camera additionalCamera = null)
        {
            if (_worldLights == null || camera == null) return;
            using (LightMarker.Auto())
            {
                Vector3 position = camera.transform.position;
                Vector3 forward = camera.transform.forward;
                Vector3 up = camera.transform.up;
                var pose = new ClassicCameraPose(
                    new System.Numerics.Vector3(position.x, position.y, position.z),
                    new System.Numerics.Vector3(forward.x, forward.y, forward.z),
                    new System.Numerics.Vector3(up.x, up.y, up.z), camera.fieldOfView,
                    camera.farClipPlane, 0, 0);
                var viewport = new CameraViewport(Mathf.Max(camera.pixelWidth, Screen.width),
                    camera.pixelWidth, camera.pixelHeight);
                if (viewport.Width <= 0 || viewport.Height <= 0) return;
                if (!_lightCameraPose.HasValue || !_lightCameraPose.Value.Equals(pose)
                    || !_lightViewport.Equals(viewport) || _lightViewDistance != nativeViewDistance)
                {
                    TilePos[] polygon = ClassicLightVisibility.BuildPolygon(pose, viewport, nativeViewDistance);
                    _visibleLightTiles = ClassicLightVisibility.EnumerateTiles(polygon);
                    _lightCameraPose = pose; _lightViewport = viewport; _lightViewDistance = nativeViewDistance;
                }
                TilePos[] visible = _visibleLightTiles;
                if (additionalCamera != null)
                {
                    Vector3 at = additionalCamera.transform.position, ahead = additionalCamera.transform.forward,
                        upward = additionalCamera.transform.up;
                    var additionalPose = new ClassicCameraPose(new System.Numerics.Vector3(at.x, at.y, at.z),
                        new System.Numerics.Vector3(ahead.x, ahead.y, ahead.z), new System.Numerics.Vector3(upward.x, upward.y, upward.z),
                        additionalCamera.fieldOfView, additionalCamera.farClipPlane, 0, 0);
                    var additionalViewport = new CameraViewport(Screen.width, additionalCamera.pixelWidth, additionalCamera.pixelHeight);
                    var tiles = new HashSet<TilePos>(visible);
                    foreach (TilePos tile in ClassicLightVisibility.EnumerateTiles(
                        ClassicLightVisibility.BuildPolygon(additionalPose, additionalViewport, 10))) tiles.Add(tile);
                    visible = new TilePos[tiles.Count]; tiles.CopyTo(visible);
                }
                _worldLights.PrepareFrame(visible);
                _tileLights ??= new ClassicTileLightBuffer(_worldWidth, _worldHeight);
                foreach (TilePos previous in _publishedLightTiles)
                    if (!_worldLights.IsVisible(previous)) _tileLights.Remove(previous);
                _publishedLightTiles.Clear();
                foreach (ClassicLightTable table in _worldLights.VisibleTables)
                {
                    if ((uint)table.Tile.X >= (uint)_worldWidth || (uint)table.Tile.Y >= (uint)_worldHeight) continue;
                    _tileLights.Set(table);
                    _publishedLightTiles.Add(table.Tile);
                }
                _tileLights.Flush();
            }
        }

        /// <summary>Uploads changed lighting and terrain batches.</summary>
        public void Flush()
        {
            _torchLights?.Flush();
            _tileFill?.Flush();
            _tileLights?.Flush();
            _terrain?.Flush();
            _liquids?.Animate(Time.time);
        }

        public void DisposeLightResources()
        {
            _torchFlames?.Dispose();
            _tileLights?.Dispose();
            _creatureShadows?.Dispose();
            _torchLights?.Dispose();
            _tileFill?.Dispose();
        }

        /// <summary>The scene's final teardown (L3 of performance-audit-2026-10.md), idempotent: its
        /// objects, chunk and liquid meshes (<see cref="Clear"/>), its light resources, then the
        /// placeholder materials. The shared art those objects drew (ClassicMeshes) is the caller's,
        /// released after this.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
            DisposeLightResources();
            foreach (Material material in _materials.Values) if (material != null) Object.Destroy(material);
            _materials.Clear();
        }

        /// <summary>Destroys everything. Pair with <see cref="ScenePresenter.Resync"/>.</summary>
        public void Clear()
        {
            _torchFlames?.Clear();
            _worldLights?.Clear();
            _publishedLightTiles.Clear();
            _visibleLightTiles = System.Array.Empty<TilePos>();
            _lightCameraPose = null;
            _tileLights?.Clear();
            _torchLights?.Clear();
            _tileFill?.Clear();
            _creatureShadows?.Clear();
            foreach (GameObject go in _tiles.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _furniture.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _creatures.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _objects.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _structures.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _torches.Values) if (go != null) Object.Destroy(go);
            _terrain?.Clear();
            _liquids?.Clear();

            _tiles.Clear();
            _furniture.Clear();
            _creatures.Clear();
            _creatureHeads.Clear();
            _creatureArt.Clear();
            _creaturePoses.Clear();
            _creatureClips.Clear();
            _renderSeconds = 0d;
            _objects.Clear();
            _objectPoses.Clear();
            _objectClips.Clear();
            while (_heartAdvances.TryDequeue(out _)) { }
            _structures.Clear();
            _structureTiles.Clear();
            _meshObjects.Clear();
            _artless.Clear();
            _meshCreatures.Clear();
            _torches.Clear();
        }

        private void ApplyFurniture(int tileIndex, in TileView tile, in ResolvedAsset furniture)
        {
            if (_furniture.TryGetValue(tileIndex, out GameObject existing) && existing != null)
            {
                Object.Destroy(existing);
                _furniture.Remove(tileIndex);
            }

            if (furniture.Reference.IsEmpty) return;

            GameObject go = Instantiate(furniture, $"room {tile.Position.X},{tile.Position.Y}");
            if (go == null) return;

            // Room furniture stands on the floor rather than replacing it, so it
            // is deliberately smaller than a tile and sits above the ground.
            go.transform.localScale = new Vector3(0.6f, 0.35f, 0.6f);
            go.transform.position = Ground(tile.Position.X + 0.5f, tile.Position.Y + 0.5f, FloorHeight + 0.2f);
            _furniture[tileIndex] = go;
        }

        /// <summary>Makes a placeholder object for a resolved asset. A converter file
        /// (<see cref="ResolvedAsset.FilePath"/>) is never loaded: ADR-0408 reads the install
        /// instead, through <see cref="ClassicMeshes"/>, which draws objects so far. Anything else
        /// file- or install-backed resolves to the same primitive a placeholder does, and
        /// <see cref="SawFileBackedAssets"/> records that it happened, so the runner can say so on
        /// screen.</summary>
        private GameObject Instantiate(in ResolvedAsset asset, string name)
        {
            if (asset.Reference.IsEmpty) return null;

            if (!asset.IsPlaceholder)
            {
                SawFileBackedAssets = true;
                if (!_warnedAboutFiles)
                {
                    _warnedAboutFiles = true;
                    Debug.LogWarning(
                        $"DK2: '{asset.Reference}' resolved to {asset} but nothing draws that kind " +
                        "yet — drawing a placeholder. See DungeonScene.Instantiate.");
                }
            }

            PlaceholderShape shape = asset.IsPlaceholder
                ? asset.Look.Shape
                : PlaceholderProvider.ShapeFor(asset.Reference);

            if (shape == PlaceholderShape.None) return null;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_root, worldPositionStays: false);

            // Colliders on thousands of tiles cost a great deal and buy nothing
            // until something needs picking. Removed rather than never added,
            // because CreatePrimitive is the cheapest way to a cube with a mesh.
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            SetColour(go, ColourOf(LookOf(asset)));

            return go;
        }

        /// <summary>
        /// A tile fills its cell if the terrain does and is a thin slab if it does not.
        ///
        /// <para><b>This changed nothing you can see, and the commit that made it said
        /// otherwise.</b> Height used to come from the resolved asset's
        /// <c>HeightPermille</c> — which is a flat 1000 for a block and 40 for a slab, and
        /// hashed only for a creature, so a solid tile was already full height. Reading
        /// <see cref="TileView.Solid"/> says the same thing on purpose rather than by
        /// coincidence, and covers the one case the old code got wrong: a file-backed
        /// asset carries no placeholder look, so a real wall mesh would have drawn as a
        /// slab. Nothing loads files yet (Q71). Colour still comes from the hash.</para>
        /// </summary>
        private void Place(GameObject go, int x, int y, bool solid)
        {
            // A block stands from 0 to its top; a slab's top is the floor.
            float top = solid ? SolidHeight : OpenHeight;
            float thickness = solid ? SolidHeight : SlabThickness;

            go.transform.localScale = new Vector3(1f, thickness, 1f);
            go.transform.position = Ground(x + 0.5f, y + 0.5f, top - thickness / 2f);
        }

        private static PlaceholderLook LookOf(in ResolvedAsset asset) => asset.IsPlaceholder
            ? asset.Look
            : PlaceholderProvider.LookFor(asset.Reference);

        private void PositionCreature(GameObject go, int creatureId, in CreatureView creature, in PlaceholderLook look)
        {
            if (_meshCreatures.Contains(creatureId))
            {
                // A real mesh is modelled in tiles about its feet.
                go.transform.position = Ground(creature.PosX / (float)Fp32.One, creature.PosY / (float)Fp32.One,
                    creature.Height / (float)Fp32.One);
                go.transform.localRotation = Quaternion.Euler(0f, creature.MeshYaw * 360f / 2048f, 0f)
                    * Quaternion.AngleAxis(creature.Roll * 360f / 2048f, Vector3.forward)
                    * Quaternion.Euler(-creature.Pitch * 360f / 2048f, 0f, 0f);
                return;
            }

            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.5f;

            go.transform.localScale = new Vector3(0.4f, height, 0.4f);
            go.transform.position = Ground(
                creature.PosX / (float)Fp32.One,
                creature.PosY / (float)Fp32.One,
                FloorHeight + height / 2f);
        }

        /// <summary>
        /// A mesh stands where the view places it, turned by its renderer yaw (2048 to a turn) and
        /// scaled uniformly. A placeholder box sits at that point, small and low, so a bed reads as
        /// furniture rather than as another creature standing on it.
        /// <para>The renderer turns a mesh by −(h + 0x400)·π/1024 in DK2's frame (object-drawing.md
        /// §4). Row-vector matrices and the frame conversion give Unity +(h + 0x400), which
        /// aligns the Prison door and bar with the cell entrance.</para>
        /// </summary>
        private void PositionObject(GameObject go, int objectId, in ObjectView instance, in PlaceholderLook look)
        {
            float x = instance.PosX / (float)Fp32.One, y = instance.PosY / (float)Fp32.One;
            if (_meshObjects.Contains(objectId))
            {
                // A real mesh is modelled in tiles about its own origin, at the object's height.
                go.transform.position = Ground(x, y, instance.Height / (float)Fp32.One);
                // DK2 turns about the model's X, then its Y, then the heading (0x00580480); its X is
                // ours, its Y our -Z (RenderMesh.ToRenderFrame), hence pitch minus and roll plus.
                go.transform.localRotation = Quaternion.Euler(0f, instance.MeshYaw * 360f / 2048f, 0f)
                    * Quaternion.AngleAxis(instance.Roll * 360f / 2048f, Vector3.forward)
                    * Quaternion.Euler(-instance.Pitch * 360f / 2048f, 0f, 0f);
                float scale = instance.Scale / (float)Fp32.One;
                go.transform.localScale = new Vector3(scale, scale * (instance.HeartVisual?.VerticalScale.Raw ?? Fp32.One) / Fp32.One, scale);
                return;
            }

            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.25f;
            height *= 0.5f;

            go.transform.localScale = new Vector3(0.5f, height, 0.5f);
            go.transform.position = Ground(x, y, FloorHeight + height / 2f);
        }

        private static Color ColourOf(in PlaceholderLook look) =>
            new Color(look.Red / 255f, look.Green / 255f, look.Blue / 255f);

        /// <summary>
        /// Sets a renderer's colour, sharing one material per colour.
        ///
        /// <para>Sharing matters: a level is thousands of objects and a material
        /// per object would mean thousands of draw calls and thousands of
        /// allocations. Colours come from a hash of the asset key, so the number
        /// of distinct ones is small and bounded by the number of asset
        /// types.</para>
        /// </summary>
        private void SetColour(GameObject go, Color colour)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            uint key = ((uint)(colour.r * 255) << 16) | ((uint)(colour.g * 255) << 8) | (uint)(colour.b * 255);

            if (!_materials.TryGetValue(key, out Material material) || material == null)
            {
                material = new Material(renderer.sharedMaterial) { color = colour };
                _materials[key] = material;
            }

            renderer.sharedMaterial = material;
        }
    }
}

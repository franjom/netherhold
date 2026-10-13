// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DK2.Unity
{
    public sealed class RoomLifeProbe : MonoBehaviour
    {
        private const byte TrainingDummyObjectTypeId = 14;
        [Serializable] private sealed class Metrics
        {
            public string unity, gpu, shader, ratAsset, goblinAsset, warlockAsset;
            public int particleGroups, particleCount, modelParticles, effectChangedPixels;
            public int lairTilesInput, lairTilesNext, lairTilesFinal, constructionRequests, expiredNativeParticles, expiredModelParticles;
            public double effectLifetimeObservedSeconds;
            public bool lairWaveCorrect, effectsExpired;
            public bool shaderSupported, ratMeshChanged, ratPositionChanged, goblinMeshChanged, warlockMeshChanged;
            public bool goblinFinalHeld, warlockFinalHeld, passed;
            public bool dwarfNativeInterpolation, warlockNativeInterpolation, guardAtDummyCentre;
            public bool guardFailedHuntSeen, guardWanderSeen, guardLeftDummyCentre;
            public int guardStartX, guardStartY, guardEndX, guardEndY;
            public int guardFirstMoveTick, guardRowsMoved, guardMaxDisplacementRaw;
            public int[] guardStates;
            public string guardCreature, dwarfMesh;
            public Vector3[] dwarfNativeSamples, warlockNativeSamples;
            public int[] authoredEffectIds, authoredRequestCounts, authoredBirths, authoredModelCounts, authoredNativeCounts, authoredChangedPixels;
            public bool authoredEffectsPassed;
            public int[] authoredMeshCounts, authoredMeshChangedPixels;
            public bool[] authoredMeshRotationChanged;
            public string[] arrivalGeometryNames;
            public float[] arrivalEndFootHeights, arrivalIdleFootHeights;
        }
        private readonly Metrics _metrics = new Metrics();
        private InstallArtProvider _art;
        private ClassicConstructionEffects _effects;
        private DungeonScene _scene;
        private GameObject _actors, _effectRoot, _rat, _goblin, _warlock;
        private ResolvedAsset _ratAsset;
        private Camera _camera;
        private RenderTexture _target;
        private UniversalRenderPipeline.SingleCameraRequest _request;
        private Vector3[] _ratFirst, _goblinFirst, _warlockFirst, _goblinLast, _warlockLast;
        private Vector3 _ratPosition;
        private string _output;
        private int _frame;
        private bool _ready;
        private double _effectStarted;
        private double _expiredAt = -1;
        private SimEvent[] _constructionEvents;
        private GameDatabase _probeDatabase;
        private IEffectSpriteProvider _probeSprites;
        private ClassicMeshes _probeMeshes;
        private sealed class WaveSink : ISimulationEventSink
        {
            public readonly ListEventSink Events = new ListEventSink();
            public void Emit(in SimEvent value) => Events.Emit(value);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("Room life visual probe").AddComponent<RoomLifeProbe>();
        private static string Arg(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        private void Start()
        {
            try
            {
                _output = Arg("-probeOutput", Application.persistentDataPath);
                Directory.CreateDirectory(_output);
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics required");
                string install = Arg("-dk2Install", "E:/GogGames/Dungeon Keeper 2");
                var db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
                _art = InstallArtProvider.Open(install, db);
                var meshes = new ClassicMeshes(_art);
                CheckArrivalGeometry();
                CheckNativeCreatureInterpolation(meshes);
                CheckLevelEightGuard(db, install);
                _actors = new GameObject("Actors");
                _scene = new DungeonScene(_actors.transform, 10, 10, meshes);
                if (!_art.TryResolve(AssetRef.ForObject("Rat", AssetKind.Mesh), out _ratAsset)) throw new InvalidOperationException("Rat missing");
                _scene.CreateObject(1, RatPose(4096), _ratAsset);
                _rat = _actors.transform.Find("object 1").gameObject;
                _goblin = Entrance(meshes, "Goblin", 3, out _metrics.goblinAsset);
                _warlock = Entrance(meshes, "Warlock", 5, out _metrics.warlockAsset);
                _ratFirst = Vertices(_rat); _goblinFirst = Vertices(_goblin); _warlockFirst = Vertices(_warlock);
                _ratPosition = _rat.transform.position;
                _effectRoot = new GameObject("Construction effects");
                var sprites = new InstallEffectSpriteProvider(db, _art.LoadTexture);
                _effects = new ClassicConstructionEffects(_effectRoot.transform, db, sprites, 42, meshes: meshes);
                CheckWave(db);
                _effects.Draw();
                Shader shader = Shader.Find("DK2/ClassicEffect");
                _metrics.shader = shader == null ? "missing" : shader.name;
                _metrics.shaderSupported = shader != null && shader.isSupported;
                _metrics.unity = Application.unityVersion; _metrics.gpu = SystemInfo.graphicsDeviceName;
                _metrics.ratAsset = _ratAsset.InstallEntry;
                _camera = new GameObject("Probe camera").AddComponent<Camera>();
                _camera.enabled = false; _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(.035f, .035f, .035f, 1);
                _camera.transform.position = new Vector3(3, 6, -9);
                _camera.transform.LookAt(new Vector3(3, 1.3f, -3));
                _camera.nearClipPlane = .1f; _camera.farClipPlane = 50;
                _target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); _target.Create();
                _request = new UniversalRenderPipeline.SingleCameraRequest { destination = _target };
                var sun = new GameObject("Probe sun").AddComponent<Light>(); sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                RenderSettings.ambientLight = Color.gray;
                _probeDatabase = db; _probeSprites = sprites; _probeMeshes = meshes;
                _ready = true;
            }
            catch (Exception ex) { Debug.LogException(ex); Application.Quit(1); }
        }
        private ObjectView RatPose(int x) => new ObjectView(1, 0, new PlayerId(1), new TilePos(1, 2),
            _ratAsset.Reference, 0, x, 8192, 4096, 0, 4096, playsAnimation: true, animationStartedAt: GameTick.Zero);
        private void CheckNativeCreatureInterpolation(ClassicMeshes meshes)
        {
            var fixture = new GameObject("Native creature interpolation fixture");
            var scene = new DungeonScene(fixture.transform, 8, 8, meshes);
            string ignored;
            _metrics.dwarfNativeInterpolation = NativeCreatureSamples(scene, fixture.transform, "Dwarf", 101,
                16384, 4096, 4096, 8192, 12288, 2048,
                new[] { new Vector3(2, .5f, -3), new Vector3(3, .75f, -2), new Vector3(4, 1, -1) }, out _metrics.dwarfMesh,
                out _metrics.dwarfNativeSamples);
            _metrics.warlockNativeInterpolation = NativeCreatureSamples(scene, fixture.transform, "Warlock", 102,
                12288, 16384, 2048, 4096, 8192, 4096,
                new[] { new Vector3(1, 1, -2), new Vector3(2, .75f, -3), new Vector3(3, .5f, -4) }, out ignored,
                out _metrics.warlockNativeSamples);
            fixture.SetActive(false); Destroy(fixture);
        }
        private bool NativeCreatureSamples(DungeonScene scene, Transform root, string name, int id,
            int x, int y, int height, int previousX, int previousY, int previousHeight,
            Vector3[] expected, out string entry, out Vector3[] measured)
        {
            if (!_art.TryResolve(AssetRef.ForCreature(name, AssetKind.Mesh), out var asset))
                throw new InvalidOperationException(name + " native mesh missing");
            entry = asset.InstallEntry;
            var pose = new CreatureView(id, PlayerIds.FirstKeeper, x, y, asset.Reference, default, 0,
                height: height, previousPosX: previousX, previousPosY: previousY, previousHeight: previousHeight);
            scene.CreateCreature(id, pose, asset, default);
            var go = root.Find("creature " + id).gameObject;
            if (go.GetComponent<MeshFilter>() == null) throw new InvalidOperationException(name + " native mesh not created");
            bool correct = true;
            measured = new Vector3[expected.Length];
            for (int sample = 0; sample < expected.Length; sample++)
            {
                scene.DrawCreatureFrame(GameTick.Zero, sample * .5f);
                measured[sample] = go.transform.position;
                correct &= Vector3.Distance(measured[sample], expected[sample]) < .00001f;
            }
            return correct;
        }
        private void CheckLevelEightGuard(GameDatabase db, string install)
        {
            var sim = CampaignSimulationFactory.Create(db, GameDatabaseLoader.LoadCampaignMap(install, "Level8"),
                GameDatabaseLoader.LoadCampaignLevel(install, "Level8"), GameDatabaseLoader.LoadCampaignPlayers(install, "Level8"),
                GameDatabaseLoader.LoadCampaignThings(install, "Level8"), GameDatabaseLoader.LoadCampaignTriggers(install, "Level8"), 1);
            var guard = sim.World.CreatureById(22) ?? throw new InvalidOperationException("Level8 Guard22 missing");
            _metrics.guardCreature = db.CreatureById(guard.CreatureTypeId).Name;
            _metrics.guardStartX = guard.PosX; _metrics.guardStartY = guard.PosY;
            var view = new WorldView(db, PlayerIds.Good);
            _metrics.guardStates = new int[1500];
            for (int tick = 0; tick < _metrics.guardStates.Length; tick++)
            {
                sim.Tick(); _metrics.guardStates[tick] = guard.Dk2StateId;
                bool atCentre = guard.PosX == 79872 && guard.PosY == 108544;
                if (guard.Dk2StateId == HeroJobRules.WaitFailedState)
                {
                    _metrics.guardFailedHuntSeen = true;
                    if (atCentre && !_metrics.guardAtDummyCentre)
                        _metrics.guardAtDummyCentre = view.Objects(sim.World).Any(thing => thing.ObjectTypeId == TrainingDummyObjectTypeId
                            && thing.PosX == 79872 && thing.PosY == 108544);
                }
                _metrics.guardWanderSeen |= guard.Dk2StateId == 87;
                long dx = (long)guard.PosX - 79872, dy = (long)guard.PosY - 108544;
                int displacement = (int)Math.Sqrt(dx * dx + dy * dy);
                _metrics.guardMaxDisplacementRaw = Math.Max(_metrics.guardMaxDisplacementRaw, displacement);
                if (!atCentre)
                {
                    _metrics.guardRowsMoved++;
                    if (_metrics.guardFirstMoveTick == 0) _metrics.guardFirstMoveTick = tick + 1;
                }
                _metrics.guardLeftDummyCentre |= _metrics.guardFailedHuntSeen && displacement > 1024;
            }
            _metrics.guardEndX = guard.PosX; _metrics.guardEndY = guard.PosY;
        }
        private void CheckWave(GameDatabase db)
        {
            var floor = db.Terrain.Values.First(t => t.IsOwnable && !t.IsSolid && !t.IsRoom
                && !t.IsWater && !t.IsLava && (t.Flags & TerrainFlags.PlayerColouredPath) != 0);
            var tiles = Enumerable.Repeat(new MapTile(floor.TerrainId, PlayerIds.FirstKeeper, 0, 0), 64).ToArray();
            var world = WorldState.FromMap(db, new LevelMap(8, 8, tiles), new byte[] { 0, PlayerIds.Neutral, PlayerIds.FirstKeeper });
            RoomRules.Place(db, ref world.Tile(new TilePos(5, 5)), db.Room(RoomTypeId.DungeonHeart), PlayerIds.FirstKeeper);
            RoomRules.Place(db, ref world.Tile(new TilePos(5, 3)), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
            GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 10000);
            var sim = new GameSimulation(db, world, 7, new ISimulationSystem[] { new RoomBuildSystem(), new RoomBuildWaveSystem() });
            var sink = new WaveSink(); sim.ObserveWith(sink);
            byte terrain = db.Room(RoomTypeId.Lair).TerrainId;
            Func<int> count = () => Enumerable.Range(0, world.TileCount).Count(i => world.TileAtIndex(i).TerrainId == terrain);
            sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
            _metrics.lairTilesInput = count(); sim.Tick(); _metrics.lairTilesNext = count();
            sim.Tick(); _metrics.lairTilesFinal = count();
            _constructionEvents = sink.Events.Events.Where(e => e.ConstructionEffect.HasValue).ToArray();
            var requests = _constructionEvents.Select(e => e.ConstructionEffect.Value.Tile).ToArray();
            _metrics.constructionRequests = requests.Length;
            _metrics.lairWaveCorrect = _metrics.lairTilesInput == 1 && _metrics.lairTilesNext == 3 && _metrics.lairTilesFinal == 4
                && requests.SequenceEqual(new[] { new TilePos(2, 2), new TilePos(2, 1), new TilePos(1, 2), new TilePos(1, 1) });
        }

        private static WorldState EffectWorld(GameDatabase db)
        {
            var tiles = Enumerable.Repeat(new MapTile(1, PlayerIds.Unowned, 0, 0), 49).ToArray();
            return WorldState.FromMap(db, new LevelMap(7, 7, tiles),
                new byte[] { PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper });
        }

        private static SimEvent[] AuthoredRequests(GameDatabase db, EffectId effectId, out WorldState world)
        {
            world = EffectWorld(db);
            TilePos target = new TilePos(3, 3);
            ISimulationSystem[] systems;
            if (effectId.Value == 6 || effectId.Value == 7)
            {
                TerrainDef terrain = db.Terrain.Values.Single(t => t.Name == (effectId.Value == 6 ? "Rock" : "Gold"));
                world.Tile(target).TerrainId = terrain.TerrainId;
                world.Tile(target).OwnerPlayerId = PlayerIds.Neutral;
                world.Tile(target).Health = terrain.StartingHealth;
                systems = new ISimulationSystem[] { new TileWorkSystem() };
            }
            else if (effectId.Value == 5)
            {
                TerrainDef path = db.Terrain.Values.Single(t => t.Name == "Dirt Path");
                world.Tile(target).TerrainId = path.TerrainId;
                world.Tile(target).Health = path.StartingHealth;
                systems = new ISimulationSystem[] { new TileWorkSystem() };
            }
            else
            {
                RoomTypeId type = effectId.Value == 8 ? RoomTypeId.Portal : RoomTypeId.Lair;
                byte owner = effectId.Value == 8 ? PlayerIds.Neutral : PlayerIds.FirstKeeper;
                for (int y = 2; y <= 4; y++)
                    for (int x = 2; x <= 4; x++) RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), owner);
                systems = effectId.Value == 8 ? new ISimulationSystem[] { new TileWorkSystem() }
                    : new ISimulationSystem[] { new CreatureMovementSystem(), new CreatureSleepSystem() };
                if (effectId.Value == 17)
                    world.SpawnCreature(db, db.Creatures.Values.Single(c => c.Name == "Goblin").CreatureId,
                        PlayerIds.FirstKeeper, target);
            }
            var sim = new GameSimulation(db, world, 1, systems);
            var sink = new ListEventSink(); sim.ObserveWith(sink);
            for (int tick = 0; tick < 500 && !sink.Events.Any(e => e.EffectRequest.HasValue); tick++)
            {
                if (effectId.Value == 6 || effectId.Value == 7)
                    world.QueueWork(target.X, target.Y, effectId.Value == 6 ? TileWorkKind.Dig : TileWorkKind.MineGold,
                        PlayerIds.FirstKeeper);
                if (effectId.Value == 5) world.QueueWork(target.X, target.Y, TileWorkKind.Claim, PlayerIds.FirstKeeper);
                if (effectId.Value == 8) world.QueueWork(target.X, target.Y, TileWorkKind.ConvertRoom, PlayerIds.FirstKeeper);
                sim.Tick();
            }
            return sink.Events.Where(e => e.EffectRequest.HasValue && e.EffectRequest.Value.EffectId == effectId).ToArray();
        }

        private void CheckAuthoredEffects(GameDatabase db, IEffectSpriteProvider sprites)
        {
            _metrics.authoredEffectIds = new[] { 5, 8, 17, 6, 7 };
            _metrics.authoredRequestCounts = new int[5]; _metrics.authoredBirths = new int[5];
            _metrics.authoredModelCounts = new int[5]; _metrics.authoredNativeCounts = new int[5];
            _metrics.authoredChangedPixels = new int[5]; _metrics.authoredEffectsPassed = true;
            _metrics.authoredMeshCounts = new int[5]; _metrics.authoredMeshChangedPixels = new int[5];
            _metrics.authoredMeshRotationChanged = new bool[5];
            int[] expectedRequests = { 1, 9, 1, 1, 1 }, expectedParticles = { 32, 256, 256, 72,
                db.Effects[7].ElementsPerTurn * db.Effects[7].GenerateIds.Count(element => element != 0) };
            _actors.SetActive(false); _effectRoot.SetActive(false);
            for (int index = 0; index < _metrics.authoredEffectIds.Length; index++)
            {
                var id = new EffectId((ushort)_metrics.authoredEffectIds[index]);
                SimEvent[] requests = AuthoredRequests(db, id, out WorldState world);
                var root = new GameObject("Authored effect " + id.Value);
                long milliseconds = 0;
                using (var effects = new ClassicConstructionEffects(root.transform, db, sprites, 42,
                    clock: () => new EffectTime(milliseconds), meshes: _probeMeshes,
                    captureEdges: request => CaptureEffectLayout.Boundary(world, db, request)))
                {
                    Color32[] baseline = Capture("authored-" + id.Value + "-off");
                    _metrics.authoredRequestCounts[index] = requests.Length;
                    foreach (SimEvent request in requests) effects.Model.Emit(request);
                    _metrics.authoredEffectsPassed &= effects.Model.Particles.Count == 0;
                    var births = new HashSet<ConstructionParticle>();
                    for (int update = 1; update <= 4; update++)
                    {
                        milliseconds = update * 50 + 1;
                        effects.Draw();
                        foreach (ConstructionParticle particle in effects.Model.Particles) births.Add(particle);
                    }
                    _metrics.authoredBirths[index] = births.Count;
                    _metrics.authoredModelCounts[index] = effects.Model.Particles.Count;
                    _metrics.authoredNativeCounts[index] = root.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount);
                    Color32[] pixels = Capture("authored-" + id.Value + "-on");
                    _metrics.authoredChangedPixels[index] = pixels.Where((p, i) => !p.Equals(baseline[i])).Count();
                    if (id.Value == 6 || id.Value == 7)
                    {
                        ParticleSystemRenderer[] renderers = root.GetComponentsInChildren<ParticleSystemRenderer>();
                        foreach (ParticleSystemRenderer renderer in renderers)
                            renderer.enabled = renderer.renderMode == ParticleSystemRenderMode.Mesh;
                        _metrics.authoredMeshCounts[index] = renderers.Where(renderer => renderer.enabled)
                            .Sum(renderer => renderer.GetComponent<ParticleSystem>().particleCount);
                        Vector3[] rotations = NativeMeshRotations(renderers);
                        Color32[] meshPixels = Capture("authored-" + id.Value + "-mesh-first");
                        _metrics.authoredMeshChangedPixels[index] = meshPixels.Where((p, i) => !p.Equals(baseline[i])).Count();
                        milliseconds += 50; effects.Draw();
                        Vector3[] nextRotations = NativeMeshRotations(renderers);
                        Capture("authored-" + id.Value + "-mesh-next");
                        _metrics.authoredMeshRotationChanged[index] = rotations.Length == nextRotations.Length
                            && rotations.Where((rotation, i) => rotation != nextRotations[i]).Any();
                        int expectedMeshes = db.Effects[id.Value].ElementsPerTurn * db.Effects[id.Value].GenerateIds
                            .Count(element => element != 0 && db.EffectElements[element].ArtResource.Type == 5);
                        _metrics.authoredEffectsPassed &= _metrics.authoredMeshCounts[index] == expectedMeshes
                            && _metrics.authoredMeshChangedPixels[index] > 0 && _metrics.authoredMeshRotationChanged[index];
                        foreach (ParticleSystemRenderer renderer in renderers) renderer.enabled = true;
                    }
                    milliseconds = 5001; effects.Draw(); Capture("authored-" + id.Value + "-expired");
                    _metrics.authoredEffectsPassed &= requests.Length == expectedRequests[index]
                        && births.Count == expectedParticles[index] && _metrics.authoredChangedPixels[index] > 0
                        && _metrics.authoredNativeCounts[index] == _metrics.authoredModelCounts[index]
                        && effects.Model.Particles.Count == 0
                        && root.GetComponentsInChildren<ParticleSystem>().All(p => p.particleCount == 0);
                }
                root.SetActive(false); Destroy(root);
            }
            _actors.SetActive(true); _effectRoot.SetActive(true);
        }
        private static Vector3[] NativeMeshRotations(ParticleSystemRenderer[] renderers)
        {
            var rotations = new List<Vector3>();
            foreach (ParticleSystemRenderer renderer in renderers)
            {
                if (renderer.renderMode != ParticleSystemRenderMode.Mesh) continue;
                var system = renderer.GetComponent<ParticleSystem>();
                var particles = new ParticleSystem.Particle[system.particleCount];
                int count = system.GetParticles(particles);
                for (int i = 0; i < count; i++) rotations.Add(particles[i].rotation3D);
            }
            return rotations.ToArray();
        }

        private void CaptureRecoveryClips(GameDatabase database, ClassicMeshes meshes)
        {
            var keys = new[]
            {
                AssetRef.ForCreature("Goblin", AssetKind.Animation, "stunned"),
                AssetRef.ForCreature("Goblin", AssetKind.Animation, "getting_up"),
                AssetRef.ForCreature("Mistress", AssetKind.Animation, "sleeping"),
                AssetRef.ForCreature("Salamander", AssetKind.Animation, "sleeping"),
                AssetCatalogue.ObjectArt(database.ObjectById(LairRules.MistressBedObjectId).Name, 7),
            };
            string[] names = { "goblin-stunned", "goblin-get-up", "mistress-sleep", "salamander-sleep", "mistress-bed-entry" };
            _actors.SetActive(false); _effectRoot.SetActive(false);
            for (int i = 0; i < keys.Length; i++)
            {
                if (!_art.TryResolve(keys[i], out var asset)) throw new InvalidOperationException(keys[i] + " missing");
                GameObject go = meshes.TryCreate(asset, "recovery fixture", transform);
                if (go == null) throw new InvalidOperationException(keys[i] + " mesh missing");
                bool animated = meshes.ConfigureAnimation(go, asset, false, 0);
                CloseUp(go, "recovery-" + names[i] + "-start");
                if (animated) go.GetComponent<ClassicCreatureAnimation>().Draw(.25);
                CloseUp(go, "recovery-" + names[i] + "-next");
                go.SetActive(false); Destroy(go);
            }
            _actors.SetActive(true); _effectRoot.SetActive(true);
        }

        private void CheckArrivalGeometry()
        {
            _metrics.arrivalGeometryNames = new[] { "Goblin", "Warlock", "Troll", "Mistress", "Salamander" };
            _metrics.arrivalEndFootHeights = new float[_metrics.arrivalGeometryNames.Length];
            _metrics.arrivalIdleFootHeights = new float[_metrics.arrivalGeometryNames.Length];
            for (int i = 0; i < _metrics.arrivalGeometryNames.Length; i++)
            {
                string name = _metrics.arrivalGeometryNames[i];
                _metrics.arrivalEndFootHeights[i] = Foot(name, "entrance", 1f);
                _metrics.arrivalIdleFootHeights[i] = Foot(name, "idle", 0f);
            }

            float Foot(string name, string clip, float phase)
            {
                if (!_art.TryResolve(AssetRef.ForCreature(name, AssetKind.Animation, clip), out var asset)
                    || !_art.TryLoadModel(asset, out KmfModel model) || model.Animation == null) return float.NaN;
                var animation = new RenderAnimation(model);
                var vertices = new System.Numerics.Vector3[animation.Mesh.Positions.Count];
                animation.Sample(phase, false, vertices);
                return vertices.Min(vertex => vertex.Y);
            }
        }

        private GameObject Entrance(ClassicMeshes meshes, string name, float x, out string entry)
        {
            if (!_art.TryResolve(AssetRef.ForCreature(name, AssetKind.Animation, "entrance"), out var asset)) throw new InvalidOperationException(name + " entrance missing");
            entry = asset.InstallEntry;
            var go = meshes.TryCreate(asset, name, _actors.transform);
            if (go == null || !meshes.ConfigureAnimation(go, asset, false, 0)) throw new InvalidOperationException(name + " entrance not animated");
            go.transform.position = new Vector3(x, 1, -2); return go;
        }
        private static Vector3[] Vertices(GameObject go) => go.GetComponent<MeshFilter>().sharedMesh.vertices;
        private static bool Same(Vector3[] a, Vector3[] b) => a.Length == b.Length && a.SequenceEqual(b);
        private Color32[] Capture(string name)
        {
            if (!RenderPipeline.SupportsRenderRequest(_camera, _request)) throw new InvalidOperationException("URP request unsupported");
            RenderPipeline.SubmitRenderRequest(_camera, _request);
            var previous = RenderTexture.active; RenderTexture.active = _target;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            var pixels = texture.GetPixels32(); File.WriteAllBytes(Path.Combine(_output, name + ".png"), texture.EncodeToPNG());
            RenderTexture.active = previous; Destroy(texture); return pixels;
        }
        private void CloseUp(GameObject go, string name)
        {
            Vector3 position = _camera.transform.position;
            Quaternion rotation = _camera.transform.rotation;
            float near = _camera.nearClipPlane;
            Bounds bounds = go.GetComponent<MeshRenderer>().bounds;
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .05f);
            _camera.nearClipPlane = .001f;
            _camera.transform.position = bounds.center + new Vector3(size, size, -size * 2);
            _camera.transform.LookAt(bounds.center);
            Capture(name);
            _camera.transform.SetPositionAndRotation(position, rotation);
            _camera.nearClipPlane = near;
        }
        private void Update()
        {
            if (!_ready) return;
            try
            {
                _frame++;
                _effects.Draw();
                if (_frame > 5 && _expiredAt < 0 && _effects.Model.Particles.Count == 0)
                    _expiredAt = Time.realtimeSinceStartupAsDouble;
                if (_frame == 5)
                {
                    CheckAuthoredEffects(_probeDatabase, _probeSprites);
                    CaptureRecoveryClips(_probeDatabase, _probeMeshes);
                    _actors.SetActive(false);
                    var baseline = Capture("effects-off");
                    _effectStarted = Time.realtimeSinceStartupAsDouble;
                    foreach (var request in _constructionEvents) _effects.Model.Emit(request);
                    _effects.Draw();
                    var effects = Capture("effects-on");
                    _metrics.effectChangedPixels = effects.Where((p, i) => !p.Equals(baseline[i])).Count();
                    var groups = _effectRoot.GetComponentsInChildren<ParticleSystem>();
                    _metrics.particleGroups = groups.Length; _metrics.particleCount = groups.Sum(p => p.particleCount);
                    _metrics.modelParticles = _effects.Model.Particles.Count;
                    _actors.SetActive(true); Capture("entrance-start");
                    CloseUp(_rat, "rat-start-close");
                }
                if (_frame == 8)
                {
                    _scene.UpdateObject(1, RatPose(6144), _ratAsset); _scene.DrawCreatureFrame(new GameTick(5), .5f);
                    _goblin.GetComponent<ClassicCreatureAnimation>().Draw(.75);
                    _warlock.GetComponent<ClassicCreatureAnimation>().Draw(.75);
                    _metrics.ratMeshChanged = !Same(_ratFirst, Vertices(_rat));
                    _metrics.ratPositionChanged = _ratPosition != _rat.transform.position;
                    _metrics.goblinMeshChanged = !Same(_goblinFirst, Vertices(_goblin));
                    _metrics.warlockMeshChanged = !Same(_warlockFirst, Vertices(_warlock));
                    Capture("entrance-moving-rat-moved");
                    CloseUp(_rat, "rat-moving-close");
                    CloseUp(_goblin, "goblin-moving-close");
                    CloseUp(_warlock, "warlock-moving-close");
                }
                if (_frame == 11)
                {
                    _goblin.GetComponent<ClassicCreatureAnimation>().Draw(20); _warlock.GetComponent<ClassicCreatureAnimation>().Draw(20);
                    _goblinLast = Vertices(_goblin); _warlockLast = Vertices(_warlock); Capture("entrance-final");
                    CloseUp(_goblin, "goblin-final-close");
                    CloseUp(_warlock, "warlock-final-close");
                }
                if (_frame == 14)
                {
                    _goblin.GetComponent<ClassicCreatureAnimation>().Draw(30); _warlock.GetComponent<ClassicCreatureAnimation>().Draw(30);
                    _metrics.goblinFinalHeld = Same(_goblinLast, Vertices(_goblin)); _metrics.warlockFinalHeld = Same(_warlockLast, Vertices(_warlock));
                    Capture("entrance-held");
                }
                if (_frame >= 14 && Time.realtimeSinceStartupAsDouble - _effectStarted >= 2.2)
                {
                    _metrics.effectLifetimeObservedSeconds = _expiredAt - _effectStarted;
                    _metrics.expiredModelParticles = _effects.Model.Particles.Count;
                    _metrics.expiredNativeParticles = _effectRoot.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount);
                    _metrics.effectsExpired = _metrics.expiredModelParticles == 0 && _metrics.expiredNativeParticles == 0
                        && _metrics.effectLifetimeObservedSeconds >= 0 && _metrics.effectLifetimeObservedSeconds <= 2.2;
                    Capture("effects-expired");
                    _metrics.passed = _metrics.shaderSupported && _metrics.effectChangedPixels > 0 && _metrics.particleGroups > 0
                        && _metrics.particleCount == _metrics.modelParticles && _metrics.ratMeshChanged && _metrics.ratPositionChanged
                        && _metrics.goblinMeshChanged && _metrics.warlockMeshChanged && _metrics.goblinFinalHeld && _metrics.warlockFinalHeld
                        && _metrics.lairWaveCorrect && _metrics.effectsExpired && _metrics.authoredEffectsPassed
                        && _metrics.dwarfNativeInterpolation && _metrics.warlockNativeInterpolation
                        && _metrics.guardCreature == "Guard" && _metrics.guardAtDummyCentre && _metrics.guardFailedHuntSeen
                        && _metrics.guardWanderSeen && _metrics.guardLeftDummyCentre;
                    File.WriteAllText(Path.Combine(_output, "metrics.json"), JsonUtility.ToJson(_metrics, true));
                    _ready = false; Application.Quit(_metrics.passed ? 0 : 1);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); _ready = false; Application.Quit(1); }
        }
        private void OnDestroy() { _effects?.Dispose(); _art?.Dispose(); }
    }
}

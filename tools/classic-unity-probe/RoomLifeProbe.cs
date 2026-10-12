// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using DK2.Art;
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
        [Serializable] private sealed class Metrics
        {
            public string unity, gpu, shader, ratAsset, goblinAsset, warlockAsset;
            public int particleGroups, particleCount, modelParticles, effectChangedPixels;
            public int lairTilesInput, lairTilesNext, lairTilesFinal, constructionRequests, expiredNativeParticles, expiredModelParticles;
            public double effectLifetimeObservedSeconds;
            public bool lairWaveCorrect, effectsExpired;
            public bool shaderSupported, ratMeshChanged, ratPositionChanged, goblinMeshChanged, warlockMeshChanged;
            public bool goblinFinalHeld, warlockFinalHeld, passed;
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
                _effects = new ClassicConstructionEffects(_effectRoot.transform, db, sprites, 42);
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
                _ready = true;
            }
            catch (Exception ex) { Debug.LogException(ex); Application.Quit(1); }
        }
        private ObjectView RatPose(int x) => new ObjectView(1, 0, new PlayerId(1), new TilePos(1, 2),
            _ratAsset.Reference, 0, x, 8192, 4096, 0, 4096, playsAnimation: true, animationStartedAt: GameTick.Zero);
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
                        && _metrics.lairWaveCorrect && _metrics.effectsExpired;
                    File.WriteAllText(Path.Combine(_output, "metrics.json"), JsonUtility.ToJson(_metrics, true));
                    _ready = false; Application.Quit(_metrics.passed ? 0 : 1);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); _ready = false; Application.Quit(1); }
        }
        private void OnDestroy() { _effects?.Dispose(); _art?.Dispose(); }
    }
}

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Save;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Systems;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>Owns a simulation, ticks it, and hands the result to a
    /// <see cref="DungeonScene"/> — the whole of Unity's involvement in the game.</summary>
    /// <remarks>
    /// <para>Holds no game state, sends the simulation nothing but
    /// <see cref="InputFrame"/>s, reads no asset path, and decides nothing about
    /// what changed: deleting this component leaves the simulation running
    /// identically (plan §4.7).</para>
    /// <para>The on-screen tick rate comes from
    /// <see cref="SimulationTiming.TicksPerSecond"/> — one constant, and it lives
    /// in the simulation (ADR-0011). Do not tune it here.</para>
    /// </remarks>
    public sealed class DungeonRunner : MonoBehaviour
    {
        [Header("Where Dungeon Keeper 2 is installed")]
        [Tooltip("Leave blank to use the DK2_DIR environment variable, or the usual install locations.")]
        [SerializeField] private string installDirectory = "";

        [Header("What to load")]
        [SerializeField] private string levelName = "Level1";
        [Tooltip("A .SAV or .QSAV to load instead of the campaign level. " +
                 "Bare filename (looked up in the install's Data/Save) or a full path. " +
                 "Loads terrain and ownership only — a save's creatures and objects are undecoded.")]
        [SerializeField] private string saveFile = "";
        [SerializeField] private ulong seed = 1;
        [SerializeField] private bool fogOfWar;
        [Range(0, TileState.MaxTaggingPlayerId)]
        [SerializeField] private int viewingPlayerId = PlayerIds.FirstKeeper;

        [Header("Input")]
        [SerializeField] private PlayerToolMode tool = PlayerToolMode.TagTiles;
        [Range(1, byte.MaxValue)]
        [SerializeField] private int selectedRoomId = (int)RoomTypeId.Treasury;

        [Header("Running")]
        [Tooltip("Multiplies the simulation's own tick rate. 0 pauses; 1 is the placeholder rate (Q1).")]
        [SerializeField] private float speed = 1f;
        [SerializeField] private bool showDiagnostics = true;

        [Header("Camera")]
        [Tooltip("Degrees below horizontal. 90 is straight down; 55 gives enough tilt to read wall heights.")]
        [Range(20f, 90f)]
        [SerializeField] private float cameraPitch = 55f;
        [SerializeField] private float cameraPanSpeed = 12f;
        [SerializeField] private float cameraZoomSpeed = 25f;

        private GameSimulation _simulation;
        private ScenePresenter _presenter;
        private WorldView _worldView;
        private DungeonScene _scene;
        private AssetProviderChain _assets;
        private readonly PlayerInputTranslator _input = new PlayerInputTranslator();
        private readonly List<PlayerCommand> _pendingCommands = new List<PlayerCommand>();
        private RoomTypeId[] _roomIds = Array.Empty<RoomTypeId>();
        private double _tickDebt;
        private string _failure;
        private string _loadedFrom;

        /// <summary>The running simulation, or null if it could not start. Read-only by intent.</summary>
        public GameSimulation Simulation => _simulation;

        private void Start()
        {
            try
            {
                StartGame();
            }
            catch (Exception ex)
            {
                // A missing install is the normal reason this fails and it is
                // not exceptional, so it is reported on screen rather than
                // thrown into the console and forgotten.
                _failure = ex.Message;
                Debug.LogError($"DK2: could not start — {ex.Message}");
            }
        }

        private void StartGame()
        {
            string install = string.IsNullOrWhiteSpace(installDirectory)
                ? Dk2Install.Find()
                : installDirectory;

            if (install == null)
                throw new InvalidOperationException(
                    "no Dungeon Keeper 2 installation found. Set the Install Directory field, " +
                    "or the DK2_DIR environment variable.");

            bool campaignLevel = string.IsNullOrWhiteSpace(saveFile);
            GameDatabase database = campaignLevel
                ? GameDatabaseLoader.LoadForCampaignLevel(install, levelName)
                : GameDatabaseLoader.Load(install);

            LevelMap map = campaignLevel
                ? GameDatabaseLoader.LoadCampaignMap(install, levelName)
                    ?? throw new InvalidOperationException($"the install has no '{levelName}' map.")
                : LoadSave(install);

            WorldState world = WorldState.FromMap(
                database, map, PlayersOf(map), fogOfWarEnabled: fogOfWar);

            if (campaignLevel)
            {
                var things = GameDatabaseLoader.LoadCampaignThings(install, levelName);
                if (things != null) ThingProjection.Place(world, database, things);
            }

            _simulation = new GameSimulation(database, world, seed, SimulationSystems.Default());
            _roomIds = database.Rooms.Keys.OrderBy(id => (byte)id).ToArray();
            SelectKnownRoom();

            // The chain the plan specifies (§7.2). Both roots are relative to
            // the project directory and both normally missing, which is the
            // supported "no assets" case: everything draws as boxes.
            _assets = AssetProviderChain.Standard(
                replacementRoot: "art-pack",
                convertedRoot: "build/converted");

            _scene = new DungeonScene(transform, world.Width);
            _worldView = new WorldView(database, (byte)viewingPlayerId);
            _presenter = new ScenePresenter(
                _worldView, _assets, _scene);

            _presenter.Present(world);
            LookAt(world);
        }

        private void Update()
        {
            if (_simulation == null) return;

            ControlCamera();
            ReadToolKeys();
            QueueInput();

            if (speed > 0f)
            {
                _tickDebt += Time.deltaTime * SimulationTiming.TicksPerSecond * speed;

                // Whole ticks only, and a cap so a long editor stall does not
                // fast-forward the game by a thousand ticks in one frame.
                int ticks = (int)Math.Min(_tickDebt, 20);
                _tickDebt -= ticks;

                for (int i = 0; i < ticks; i++)
                {
                    InputFrame input = i == 0 && _pendingCommands.Count > 0
                        ? new InputFrame(_pendingCommands.ToArray())
                        : InputFrame.Empty;
                    _simulation.Tick(input);
                    if (i == 0) _pendingCommands.Clear();
                }
            }

            _presenter.Present(_simulation.World);
        }

        private void QueueInput()
        {
            PointerButtonTransition button = PointerButtonTransition.None;
            if (Input.GetMouseButtonDown(0)) button |= PointerButtonTransition.Pressed;
            if (Input.GetMouseButtonUp(0)) button |= PointerButtonTransition.Released;

            if (button == PointerButtonTransition.None) return;

            TilePos? tile = ScreenToTile(Input.mousePosition);
            var intent = new PlayerIntent(
                (byte)viewingPlayerId, tool, tile, button, (RoomTypeId)selectedRoomId);
            PlayerInputSnapshot snapshot = PlayerInputSnapshot.Capture(
                _worldView, _simulation.World, (byte)viewingPlayerId, _simulation.Database);
            InputFrame frame = _input.Translate(intent, snapshot);

            foreach (PlayerCommand command in frame.All)
                _pendingCommands.Add(command);
        }

        /// <summary>
        /// Which tile the pointer is over. The rule is <see cref="TilePicker"/>'s, because
        /// it is testable and casting at the ground plane — what this did first — reads
        /// the tile behind whatever wall the pointer is on.
        /// </summary>
        private TilePos? ScreenToTile(Vector3 screenPosition)
        {
            Camera camera = Camera.main;
            if (camera == null || _worldView == null) return null;

            Ray ray = camera.ScreenPointToRay(screenPosition);
            return TilePicker.Pick(
                new PickRay(
                    ray.origin.x, ray.origin.y, ray.origin.z,
                    ray.direction.x, ray.direction.y, ray.direction.z),
                _worldView,
                _simulation.World,
                DungeonScene.SolidHeight,
                DungeonScene.OpenHeight);
        }

        private void ReadToolKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) tool = PlayerToolMode.TagTiles;
            if (Input.GetKeyDown(KeyCode.Alpha2)) tool = PlayerToolMode.UntagTiles;
            if (Input.GetKeyDown(KeyCode.Alpha3)) tool = PlayerToolMode.BuildRoom;
            if (Input.GetKeyDown(KeyCode.Alpha4)) tool = PlayerToolMode.SellRoom;
            if (Input.GetKeyDown(KeyCode.Alpha5)) tool = PlayerToolMode.Hand;
            if (Input.GetKeyDown(KeyCode.Alpha6)) tool = PlayerToolMode.Slap;
            if (Input.GetKeyDown(KeyCode.Alpha7)) tool = PlayerToolMode.CreateImp;

            if (Input.GetKeyDown(KeyCode.LeftBracket)) SelectAdjacentRoom(-1);
            if (Input.GetKeyDown(KeyCode.RightBracket)) SelectAdjacentRoom(1);
        }

        private void SelectKnownRoom()
        {
            if (_roomIds.Length == 0) return;
            if (Array.IndexOf(_roomIds, (RoomTypeId)selectedRoomId) >= 0) return;
            selectedRoomId = (byte)_roomIds[0];
        }

        private void SelectAdjacentRoom(int direction)
        {
            if (_roomIds.Length == 0) return;

            int index = Array.IndexOf(_roomIds, (RoomTypeId)selectedRoomId);
            if (index < 0) index = 0;
            index = (index + direction + _roomIds.Length) % _roomIds.Length;
            selectedRoomId = (byte)_roomIds[index];
        }

        private void ControlCamera()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            var pan = new Vector3(horizontal, 0f, vertical);
            if (pan.sqrMagnitude > 1f) pan.Normalize();
            camera.transform.position += pan * cameraPanSpeed * Time.unscaledDeltaTime;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) return;

            Vector3 next = camera.transform.position +
                (camera.transform.forward * scroll * cameraZoomSpeed);
            if (next.y > 2f) camera.transform.position = next;
        }

        /// <summary>Frames the whole level, whatever shape it is: fits the level's
        /// bounding sphere inside the vertical field of view. Uses the
        /// half-diagonal so the framing cannot depend on which dimension is the
        /// larger — a camera height derived from the width alone dropped the near
        /// end of campaign level 1 (30 wide, 38 deep) off the screen, and no test
        /// can catch that; only a screenshot can.</summary>
        private void LookAt(WorldState world)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            var centre = new Vector3(world.Width / 2f, 0f, world.Height / 2f);

            float radius = 0.5f * Mathf.Sqrt(
                (world.Width * (float)world.Width) + (world.Height * (float)world.Height));

            // Unity's fieldOfView is the vertical one. A wide game view has a
            // wider horizontal field, so fitting the sphere vertically always
            // fits it horizontally too.
            float halfFov = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = radius / Mathf.Max(0.01f, Mathf.Sin(halfFov));

            Quaternion rotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            camera.transform.rotation = rotation;
            camera.transform.position = centre - (rotation * Vector3.forward * distance);

            // A level far enough away to fit needs the far plane to match, or it
            // is clipped away rather than drawn small.
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance * 3f);
        }

        /// <summary>Loads the grid out of a DK2 saved game: terrain and ownership
        /// only. The 189-byte entity records after the grid are undecoded (Q182),
        /// so a save arrives with the dungeon the player left and nobody in it —
        /// unlike a campaign level, whose placed entities do arrive, through
        /// <c>ThingProjection</c>. The save names its own level and the reader
        /// takes the grid dimensions from that level's design map, so
        /// <c>levelName</c> is ignored here.</summary>
        private LevelMap LoadSave(string install)
        {
            string path = Path.IsPathRooted(saveFile)
                ? saveFile
                : Path.Combine(Dk2SaveReader.SaveDirectory(install), saveFile);

            if (!File.Exists(path))
                throw new InvalidOperationException($"no save at '{path}'.");

            SavedGame save = Dk2SaveReader.Load(install, path)
                ?? throw new InvalidOperationException(
                    $"'{Path.GetFileName(path)}' names a level this install has no map for.");

            // The read's own check, surfaced rather than swallowed. A correct
            // read of a played save agrees with the design map on most tiles; a
            // grid read at the wrong offset agrees on almost none. Worth seeing
            // in the log on every load, because the failure it catches produces
            // a plausible-looking dungeon of noise rather than an exception.
            _loadedFrom =
                $"{Path.GetFileName(path)} ({save.Header.LevelName}, " +
                $"{save.TerrainAgreement:P0} of terrain unchanged from the design map)";

            Debug.Log($"DK2: loaded {_loadedFrom}");

            return save.Map;
        }

        private static byte[] PlayersOf(LevelMap map)
        {
            // Whoever the map says owns something. The same rule the harness
            // uses when a scenario does not name players.
            var seen = new System.Collections.Generic.SortedSet<byte>();
            foreach (MapTile tile in map.Tiles) seen.Add(tile.PlayerId);

            var players = new byte[seen.Count];
            seen.CopyTo(players);
            return players;
        }

        private void OnGUI()
        {
            if (!showDiagnostics) return;

            if (_failure != null)
            {
                GUI.Label(new Rect(10, 10, 900, 60), $"DK2 could not start: {_failure}");
                return;
            }

            if (_simulation == null) return;

            string art = _assets == null ? "" : _assets.Summarise();

            GUI.Label(
                new Rect(10, 10, 1200, 20),
                $"tick {_simulation.World.CurrentTick.Value}   " +
                $"state {_simulation.StateHash():x16}   " +
                $"{_scene.TileObjects} tile objects   {_scene.CreatureObjects} creatures   " +
                $"pushed {_presenter.TilesPushed} tiles / {_presenter.CreaturesPushed} creatures this frame");

            GUI.Label(new Rect(10, 30, 1200, 20), $"art: {art}");

            GUI.Label(
                new Rect(10, 50, 1200, 20),
                $"tool: {tool}" +
                (tool == PlayerToolMode.BuildRoom ? $" (room {selectedRoomId})" : "") +
                "   1 tag  2 untag  3 build  4 sell  5 hand  6 slap  7 create imp  [/] room");

            if (_loadedFrom != null)
                GUI.Label(new Rect(10, 90, 1200, 20), $"loaded from save: {_loadedFrom} — terrain and ownership only, no creatures or objects (Q182)");

            GUI.Label(
                new Rect(10, 70, 1200, 20),
                "tick rate is SimulationTiming.TicksPerSecond, the unmeasured Q1 placeholder — " +
                "tick counts are faithful, wall-clock speed is not a claim");
        }
    }
}

// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Systems;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>
    /// Owns a simulation, ticks it, and hands the result to a
    /// <see cref="DungeonScene"/>. The whole of Unity's involvement in the game.
    ///
    /// <para><b>Note what it does not do.</b> It holds no game state — the
    /// simulation does. It sends nothing to the simulation but
    /// <see cref="InputFrame"/>s. It reads no asset path. It decides nothing
    /// about what changed. Deleting this component and its objects would leave
    /// the simulation running identically, which is plan §4.7's requirement and
    /// is true here by construction rather than by discipline.</para>
    ///
    /// <para><b>The tick rate on screen is not a claim about DK2.</b> It comes
    /// from <see cref="SimulationTiming.TicksPerSecond"/>, which is the
    /// unmeasured Q1 placeholder. The number of ticks a job takes is faithful;
    /// how fast that looks in seconds is not, and will not be until Q1 is
    /// measured against the original. Do not tune it here — there is one
    /// constant and it lives in the simulation (ADR-0011).</para>
    /// </summary>
    public sealed class DungeonRunner : MonoBehaviour
    {
        [Header("Where Dungeon Keeper 2 is installed")]
        [Tooltip("Leave blank to use the DK2_DIR environment variable, or the usual install locations.")]
        [SerializeField] private string installDirectory = "";

        [Header("What to load")]
        [SerializeField] private string levelName = "Level1";
        [SerializeField] private ulong seed = 1;

        [Header("Running")]
        [Tooltip("Multiplies the simulation's own tick rate. 0 pauses; 1 is the placeholder rate (Q1).")]
        [SerializeField] private float speed = 1f;
        [SerializeField] private bool showDiagnostics = true;

        [Header("Camera")]
        [Tooltip("Degrees below horizontal. 90 is straight down; 55 gives enough tilt to read wall heights.")]
        [Range(20f, 90f)]
        [SerializeField] private float cameraPitch = 55f;

        private GameSimulation _simulation;
        private ScenePresenter _presenter;
        private DungeonScene _scene;
        private AssetProviderChain _assets;
        private double _tickDebt;
        private string _failure;

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

            GameDatabase database = GameDatabaseLoader.Load(install);

            LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, levelName)
                ?? throw new InvalidOperationException($"the install has no '{levelName}' map.");

            WorldState world = WorldState.FromMap(database, map, PlayersOf(map));

            _simulation = new GameSimulation(database, world, seed, SimulationSystems.Default());

            // The chain the plan specifies (§7.2). Both roots are relative to
            // the project directory and both normally missing, which is the
            // supported "no assets" case: everything draws as boxes.
            _assets = AssetProviderChain.Standard(
                replacementRoot: "art-pack",
                convertedRoot: "build/converted");

            _scene = new DungeonScene(transform, world.Width);
            _presenter = new ScenePresenter(new WorldView(database), _assets, _scene);

            _presenter.Present(world);
            LookAt(world);
        }

        private void Update()
        {
            if (_simulation == null) return;

            if (speed > 0f)
            {
                _tickDebt += Time.deltaTime * SimulationTiming.TicksPerSecond * speed;

                // Whole ticks only, and a cap so a long editor stall does not
                // fast-forward the game by a thousand ticks in one frame.
                int ticks = (int)Math.Min(_tickDebt, 20);
                _tickDebt -= ticks;

                for (int i = 0; i < ticks; i++)
                    _simulation.Tick();
            }

            _presenter.Present(_simulation.World);
        }

        /// <summary>
        /// Frames the whole level, whatever shape it is.
        ///
        /// <para><b>The first version of this was wrong, and a screenshot is the
        /// only thing that could have found it.</b> It put the camera at a
        /// height of <c>world.Width * 0.75</c> — derived from the width alone —
        /// so on campaign level 1, which is 30 wide and <b>38 deep</b>, the near
        /// end of the dungeon fell off the bottom of the screen. No test could
        /// have caught it: the smoke test proves 1140 objects exist, not that a
        /// camera points at them.</para>
        ///
        /// <para>Derived now rather than guessed: fit the level's bounding
        /// sphere inside the vertical field of view. Using the half-diagonal
        /// means the framing cannot depend on which dimension happens to be
        /// larger, which was the whole of the original bug.</para>
        /// </summary>
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
                "tick rate is SimulationTiming.TicksPerSecond, the unmeasured Q1 placeholder — " +
                "tick counts are faithful, wall-clock speed is not a claim");
        }
    }
}

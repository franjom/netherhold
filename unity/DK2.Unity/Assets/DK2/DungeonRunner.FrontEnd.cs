// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner
    {
        private FrontEndSession _frontEnd;
        private ClassicFrontEndMenu _frontEndMenu;
        private IReadOnlyList<FrontEndProp> _frontEndScenery;
        private double _frontEndStarted;
        [SerializeField] private string campaignProfileFile = "";
        [SerializeField] private string hiScoresFile = "";
        private CampaignProfile _campaignProfile;
        private string _campaignProfilePath;
        private bool _secretFiveAvailable;
        private CampaignMapController _campaignMap;
        private IReadOnlyList<FrontEndProp> _frontEndBaseScenery;
        private readonly Dictionary<int, CampaignSlot> _campaignPickSlots = new Dictionary<int, CampaignSlot>();
        private int _frontEndMapRevision = -1;
        private GameSessionRequest _pendingSession;
        public CampaignMapController CampaignMap => _campaignMap;

        private void LoadFrontEndProfile()
        {
            _campaignProfilePath = string.IsNullOrWhiteSpace(campaignProfileFile)
                ? Path.Combine(Application.persistentDataPath, "campaign-profile.dk2p") : campaignProfileFile;
            CampaignProfile loaded = CampaignProfileFile.LoadOrCreate(_campaignProfilePath, InstalledCampaignRegistry.ReadCurrentUser);
            _campaignProfile = CampaignProgression.EnsureOpenMap(loaded);
            if (sessionMode == GameSessionMode.FrontEnd && frontEndReturnPage == FrontEndPage.CampaignResults)
                _campaignProfile = CampaignProgression.RepairAfterLevel(_campaignProfile);
            if (!ReferenceEquals(loaded, _campaignProfile)) CampaignProfileFile.Save(_campaignProfilePath, _campaignProfile);
            _secretFiveAvailable = CampaignMoon.IsSecretFiveAvailable(CampaignMoon.PhaseAt(DateTime.Now));
        }

        public GameSessionMode SessionMode => sessionMode;
        public FrontEndSession FrontEnd => _frontEnd;
        public ClassicFrontEndMenu FrontEndMenu => _frontEndMenu;
        public string LoadedLevelName => levelName;
        public string StartupFailure => _failure;

        /// <summary>Starts a named session on a fresh runtime hierarchy, preserving the runner's settings.</summary>
        public DungeonRunner LoadLevel(GameSessionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!Application.isPlaying) throw new InvalidOperationException("Session switching requires Play mode.");
            WaitForTick();
            var next = new GameObject(request.Mode == GameSessionMode.FrontEnd ? "DK2 Front End" : "DK2 " + request.LevelName);
            next.SetActive(false);
            next.transform.SetParent(transform.parent, false);
            next.transform.localPosition = transform.localPosition;
            next.transform.localRotation = transform.localRotation;
            next.transform.localScale = transform.localScale;
            var runner = next.AddComponent<DungeonRunner>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), runner);
            runner.sessionMode = request.Mode;
            runner.levelName = request.LevelName;
            runner.frontEndReturnPage = request.ReturnPage;
            runner.saveFile = "";
            runner.enabled = true;
            gameObject.SetActive(false);
            // Shader globals belong to one scene; release them before the replacement publishes its buffers.
            ReleaseRuntimeResources();
            Destroy(gameObject);
            next.SetActive(true);
            return runner;
        }

        private void StartFrontEnd(string install, WorldState world)
        {
            var paths = CameraPathLoader(install);
            _frontEnd = new FrontEndSession(path => paths((int)path), frontEndReturnPage);
            _frontEndStarted = Time.realtimeSinceStartupAsDouble;
            var menuAssets = new InstallClassicFrontEndMenuAssets(install);
            string hiScoresPath = string.IsNullOrWhiteSpace(hiScoresFile)
                ? Path.Combine(Application.persistentDataPath, "HiScores.dat") : hiScoresFile;
            _campaignMap = new CampaignMapController(_frontEnd, _campaignProfile, _secretFiveAvailable, menuAssets.Briefing,
                LoadHiScores(hiScoresPath, install));
            _campaignMap.HiScoresChanged += rows => HiScoresFile.Save(hiScoresPath, rows);
            _campaignMap.ProfileChanged += profile =>
            {
                _campaignProfile = profile;
                CampaignProfileFile.Save(_campaignProfilePath, profile);
            };
            // Dispatcher 0xC: the session ends and the campaign level runs with return page 0x1A.
            _campaignMap.LaunchRequested += slot => _pendingSession = GameSessionRequest.Campaign(slot.LevelName);
            _campaignPickSlots.Clear();
            foreach (FrontEndProp prop in _frontEndBaseScenery)
            {
                if (prop.MapPiece is DK2.Assets.FrontEndMapPiece piece) _campaignPickSlots[prop.View.Id] = new CampaignSlot((byte)piece);
                else if (prop.MarkerSlot is CampaignSlot slot) _campaignPickSlots[prop.View.Id] = slot;
            }
            _frontEndMenu = new ClassicFrontEndMenu(transform, _frontEnd, menuAssets, _campaignMap, _optionsPages);
            _frontEndMenu.Model.QuitRequested += QuitFrontEnd;
            ApplyGameSettings(_gameSettings);
            _frontEnd.Update(TimeSpan.Zero);
            _scene.SetCreatureFrameTime(GameTick.Zero, 0);
            _presenter.Present(world);
            _startup.Step("front-end.present");
            Cursor.visible = true;
            // 0x00534F70 plays a won map's title card and cutscene before the front end runs.
            if (frontEndReturnPage == FrontEndPage.CampaignResults) BeginCutscene(install);
            if (!UpdateCutscene()) UpdateFrontEnd();
        }

        private void UpdateFrontEnd()
        {
            double elapsed = Math.Max(0, Time.realtimeSinceStartupAsDouble - _frontEndStarted);
            _frontEnd.Update(TimeSpan.FromSeconds(elapsed));
            if (_campaignMap.EntryRow >= 0) TypeHiScoreName();
            _frontEndMenu?.Refresh();
            // A click replaces this runner; switch only after the menu has finished with it.
            if (_pendingSession != null) { LoadLevel(_pendingSession); return; }
            if (_frontEnd.Pose is CameraPathPose pose) ApplyFrontEndCamera(pose);
            CampaignSlot picked = _campaignMap.IsAtMap && Camera.main != null
                ? _scene.PickFrontEndMap(Camera.main.ScreenPointToRay(Input.mousePosition), _campaignPickSlots) : CampaignSlot.None;
            _campaignMap.UpdatePointer(picked, Input.GetMouseButton(0));
            RefreshCampaignMap();
            double turns = elapsed * SimulationTiming.OriginalTicksPerGameSecond;
            var tick = new GameTick((long)Math.Floor(turns));
            float fraction = (float)(turns - Math.Floor(turns));
            _scene.DrawCreatureFrame(tick, fraction);
            _scene.SetObjectLights(_worldView.LightObjects(_simulation.World), 1);
            ApplyClassicLook();
            if (_frontEnd.Pose is CameraPathPose lightingPose) _scene.PrepareClassicLighting(Camera.main, lightingPose.ViewDistance);
            _scene.AdvanceTorchAnimation();
            _constructionEffects?.Draw();
            string settled = _startup?.Frame(Time.unscaledDeltaTime * 1000d, _installArt);
            if (settled != null) Debug.Log(settled);
        }

        /// <summary>Our own table; on first use the install's runtime table is read (never written), else
        /// ten empty rows (ADR-0538's import pattern).</summary>
        private static IReadOnlyList<HiScoreEntry> LoadHiScores(string path, string install)
        {
            string original = Path.Combine(install, "Data", "Settings", "HiScores.dat");
            foreach (string candidate in new[] { path, original })
            {
                try { if (File.Exists(candidate)) return HiScoresFile.Read(File.ReadAllBytes(candidate)); }
                catch (InvalidDataException) { Debug.LogWarning("DK2: unreadable hi-score table " + candidate); }
            }
            return HiScoreTable.Empty;
        }

        private void TypeHiScoreName()
        {
            foreach (char typed in Input.inputString)
            {
                if (typed == '\b') _campaignMap.EraseEntry();
                else if (typed == '\n' || typed == '\r') { _campaignMap.CommitEntry(); return; }
                else _campaignMap.TypeEntry(typed);
            }
        }

        private void RefreshCampaignMap()
        {
            if (_frontEndMapRevision != _campaignMap.Revision)
            {
                _frontEndMapRevision = _campaignMap.Revision;
                _frontEndScenery = FrontEndScenery.ApplyCampaignMap(_frontEndBaseScenery, _campaignMap);
                _worldView.FrontEndScenery = _frontEndScenery;
                _presenter.Present(_simulation.World);
            }
        }

        private static void QuitFrontEnd()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void ApplyFrontEndCamera(CameraPathPose pose)
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            camera.rect = new Rect(0, 0, 1, 1);
            int width = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            int height = camera.targetTexture != null ? camera.targetTexture.height : Screen.height;
            if (width <= 0 || height <= 0) return;
            camera.orthographic = false;
            camera.ResetProjectionMatrix();
            // DK2 drew the front end on a 4:3 screen and its path field of view is horizontal. On a wider
            // screen keep the 4:3 vertical extent (the map's castle sits at its top edge) and show more at
            // the sides; on a narrower one keep the horizontal extent as before.
            camera.fieldOfView = pose.VerticalFieldOfView(Math.Min(width, height * 4 / 3), height);
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + .01f, pose.ViewDistance);
            camera.transform.SetPositionAndRotation(new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z),
                Quaternion.LookRotation(new Vector3(pose.Forward.X, pose.Forward.Y, pose.Forward.Z),
                    new Vector3(pose.Up.X, pose.Up.Y, pose.Up.Z)));
        }

        private void DrawFrontEndDiagnostics()
        {
            _frameOverlay.Draw();
            GUI.Label(new Rect(10, 10, 1200, 22), $"Front end (mode 5) · path {_frontEnd.Path} · page {_frontEnd.Page}");
        }
    }
}

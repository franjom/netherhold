// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Core;
using DK2.Data.Campaign;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DK2.Unity.Editor
{
    [InitializeOnLoad]
    public static class CompletionFlowProbe
    {
        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private const string Active = "DK2.CompletionFlowProbe";
        private static DungeonRunner _runner, _bound;
        private static IEnumerator<Gate> _flow;
        private static Gate _gate;
        private static EditorWindow _gameView;
        private static readonly Report Result = new Report();
        private static string _output, _state, _profile, _save;
        private static int _sequence;
        private static bool _finished;
        private static bool _resolutionRequested;
        private static double _deadline;
        private static string _unexpectedLog;
        private static int _ignoredEditorSearch;
        public static int GuiRepaints, MovieRepaints, RewardRepaints, TitleRepaints;
        public static int GuiWidth, GuiHeight;

        static CompletionFlowProbe()
        {
            if (SessionState.GetBool(Active, false))
            {
                _deadline = EditorApplication.timeSinceStartup + 1200;
                Application.logMessageReceived += Logged;
                EditorApplication.update += Poll;
            }
        }

        public static void Run()
        {
            if (!File.Exists(".completion-flow-probe")) throw new Exception("A marked isolated completion project is required.");
            string state = Environment.GetEnvironmentVariable("DK2_COMPLETION_STATE");
            string output = Environment.GetEnvironmentVariable("DK2_COMPLETION_OUTPUT");
            Require(!string.IsNullOrEmpty(state) && Directory.Exists(state), "Private state directory missing.");
            Require(!string.IsNullOrEmpty(output) && Directory.Exists(output) && !Directory.EnumerateFiles(output).Any(), "Fresh output directory missing.");
            SessionState.SetString(Active + ".state", state); SessionState.SetString(Active + ".output", output);
            string profile = Path.Combine(state, "campaign.dk2p");
            Require(!File.Exists(profile), "Initial profile must be fresh; reusing a project still requires a new state directory.");
            CampaignProfileFile.Save(profile, new CampaignProfile());
            HiScoresFile.Save(Path.Combine(state, "HiScores.dat"), HiScoreTable.Empty);
            GameSettingsFile.Save(Path.Combine(state, "game-settings.ini"), new GameSettings {
                MasterVolume = 80, SpeechVolume = 75, MusicVolume = 60, SoundEffectVolume = 50, UiScale = 100 });
            EditorSceneManager.OpenScene("Assets/DK2/Scenes/Dungeon.unity");
            OpenGameView();
            var runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            JsonUtility.FromJsonOverwrite("{\"sessionMode\":5,\"frontEndReturnPage\":0,\"skipStartup\":false,\"speed\":0,\"seed\":1,\"tickOnWorkerThread\":false,\"showDiagnostics\":false,\"installDirectory\":\"E:/GogGames/Dungeon Keeper 2\"}", runner);
            typeof(DungeonRunner).GetField("campaignProfileFile", Private).SetValue(runner, profile);
            typeof(DungeonRunner).GetField("hiScoresFile", Private).SetValue(runner, Path.Combine(state, "HiScores.dat"));
            typeof(DungeonRunner).GetField("savedSessionDirectory", Private).SetValue(runner, Path.Combine(state, "Saves"));
            PlayModeWindow.SetCustomRenderingResolution(1024, 768, "Completion flow");
            SessionState.SetBool(Active, true); EditorApplication.isPlaying = true;
        }

        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private | BindingFlags.Public).GetValue(target);
        private static T Field<T>(string name) => Field<T>(_runner, name);
        private static void Set(string name, object value) => typeof(DungeonRunner).GetField(name, Private).SetValue(_runner, value);
        private static object CallOn(object target, string name, params object[] values) => target.GetType().GetMethod(name, Private).Invoke(target, values);
        private static object Call(string name, params object[] values) => CallOn(_runner, name, values);
        private static double Now => Time.realtimeSinceStartupAsDouble;
        private static void Require(bool value, string why) { if (!value) throw new Exception(why); }
        private static void Logged(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            if (message.StartsWith("ArgumentOutOfRangeException: Index was out of range.", StringComparison.Ordinal)
                && stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")) { _ignoredEditorSearch++; return; }
            if (_unexpectedLog == null) _unexpectedLog = message + "\n" + stack;
        }
        private static void OpenGameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            _gameView = Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault()
                ?? EditorWindow.GetWindow(type, false, "Completion flow", false);
            _gameView.autoRepaintOnSceneChange = true; _gameView.Show(); _gameView.Repaint();
        }

        private static bool Ready()
        {
            if (_runner == null) _runner = UnityEngine.Object.FindAnyObjectByType<DungeonRunner>();
            if (_runner == null) return false;
            if (_runner.StartupFailure != null) throw new Exception(_runner.StartupFailure);
            if (_runner.Simulation == null) return false;
            if (UnityEngine.Object.FindAnyObjectByType<DK2.Unity.CompletionGuiWitness>() == null)
                new GameObject("Completion GUI repaint witness").AddComponent<DK2.Unity.CompletionGuiWitness>();
            DK2.Unity.CompletionGuiWitness.Repainted = WitnessGui;
            if (!_resolutionRequested) { Screen.SetResolution(1024, 768, FullScreenMode.Windowed); _resolutionRequested = true; }
            if (_runner != _bound)
            {
                _bound = _runner;
                var witness = UnityEngine.Object.FindAnyObjectByType<DK2.Unity.CompletionGuiWitness>();
                Require(witness != null && witness.isActiveAndEnabled, "Runtime GUI witness was not attached and active.");
                Observe("observed", "runtime-gui-witness", "Attached active runtime component from assembly " + witness.GetType().Assembly.GetName().Name);
                Require(Path.GetFullPath(Field<SaveSlotsView>("_saves").Directory) == Path.GetFullPath(Path.Combine(_state, "Saves")), "Host did not initialize the private saved-session directory.");
                Observe("observed", "private-state-binding", "Host initialized its real SaveSlotsView from the serialized per-run directory override.");
            }
            return true;
        }

        private static void Poll()
        {
            if (_finished || !Application.isPlaying) return;
            try
            {
                if (Now == 0) return;
                if (_gameView == null) OpenGameView(); else _gameView.Repaint();
                if (_output == null)
                {
                    _output = SessionState.GetString(Active + ".output", ""); _state = SessionState.GetString(Active + ".state", "");
                    _profile = Path.Combine(_state, "campaign.dk2p");
                    Result.unityVersion = Application.unityVersion; Result.graphics = SystemInfo.graphicsDeviceType.ToString();
                    Result.audioSampleRate = AudioSettings.outputSampleRate;
                }
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Completion probe total deadline reached.");
                if (_unexpectedLog != null) throw new Exception("Unexpected runtime/editor product error: " + _unexpectedLog);
                if (!Ready()) return;
                if (_flow == null) _flow = Flow().GetEnumerator();
                if (_gate == null)
                {
                    if (!_flow.MoveNext()) { Finish(true, "All scheduled runtime checks completed."); return; }
                    _gate = _flow.Current;
                }
                if (Now - _gate.started > _gate.timeout) throw new Exception("Timed out: " + _gate.name);
                if (Time.frameCount < _gate.frame + 3 || !_gate.condition()) return;
                Observe("pass", _gate.name, "Condition passed after actual play frames."); _gate = null;
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(false, ex.ToString()); }
        }

        private static Gate Wait(string name, Func<bool> condition, double timeout = 90)
            => new Gate { name = name, condition = condition, started = Now, frame = Time.frameCount, timeout = timeout };

        private static IEnumerable<Gate> Flow()
        {
            Observe("fixture", "initial-storage", "Blank private campaign profile and empty private high-score table; original install read-only. Controller actions below are API equivalents, not physical inputs.");
            yield return Wait("title-texture-and-repaint", () => Field<Texture2D>("_startupTitle") != null && TitleRepaints > 0 && GuiWidth == 1024 && GuiHeight == 768, 20);
            var title = Field<Texture2D>("_startupTitle"); Require(title.width > 100 && title.height > 100, "Title texture is missing.");
            WriteNew("startup-title-texture.png", title.EncodeToPNG());
            double titleStarted = Field<double>("_startupStarted");
            yield return Wait("real-ten-second-title-wait", () => Field<FrontEndStartup>("_frontEndStartup").Stage != FrontEndStartupStage.Title, 20);
            Require(Now - titleStarted >= 10, "Title advanced before its real ten-second hold.");
            foreach (var gate in Movie("startup-bullfrog", () => Field<ClassicMoviePlayer>("_frontEndMovie"), ClassicMoviePlayer.BullfrogIntro)) yield return gate;
            foreach (var gate in Movie("startup-intro", () => Field<ClassicMoviePlayer>("_frontEndMovie"), "Data/Movies/Intro.tgq")) yield return gate;
            yield return Wait("startup-done", () => Field<FrontEndStartup>("_frontEndStartup") == null);
            FinishPath(); yield return Wait("main-menu", () => _runner.FrontEnd.Page == FrontEndPage.Main && !_runner.FrontEnd.PathPlaying);
            Require(Math.Abs(Field<float>(_runner.SoundPlayer, "_master") - .8f) < .001f, "Persisted master volume did not reach a newly created sound player.");
            var bounds = _runner.FrontEndMenu.LabelInkBounds(new FrontEndWidgetId(1));
            ResetAttract(); _runner.FrontEndMenu.Model.UpdatePointer(new FrontEndPointer(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, false));
            yield return Wait("menu-hover-pcm-source", () => Effects().Any(source => source.clip != null));
            CheckEffect("menu-hover", .4f);
            Activate(5); yield return Wait("quit-dialog", () => _runner.FrontEnd.Page == FrontEndPage.Dialog);
            Activate(668); yield return Wait("quit-cancel-keeps-menu", () => _runner.FrontEnd.Page == FrontEndPage.Main);
            Activate(4); FinishPath(); yield return Wait("extras", () => _runner.FrontEnd.Page == FrontEndPage.Extras);
            Activate(22); yield return Wait("movie-gallery", () => _runner.FrontEnd.Page == FrontEndPage.Movies);
            Require(_runner.FrontEndMenu.Model.Movies.Entries[0].Available && !_runner.FrontEndMenu.Model.Movies.Entries[1].Available, "Fresh gallery unlock state is wrong.");
            Activate(53);
            foreach (var gate in Movie("gallery-intro", () => Field<ClassicMoviePlayer>("_frontEndMovie"), "Data/Movies/Intro.tgq")) yield return gate;
            yield return Wait("gallery-return", () => _runner.FrontEndMenu.Model.Page == FrontEndPage.Movies);
            Activate(65); Activate(24); FinishPath(); yield return Wait("main-for-attract", () => _runner.FrontEnd.Page == FrontEndPage.Main);
            StartAttract(FrontEndMovie.BullfrogIntro);
            foreach (var gate in Movie("attract-bullfrog", () => Field<ClassicMoviePlayer>("_frontEndMovie"), ClassicMoviePlayer.BullfrogIntro)) yield return gate;
            StartAttract(FrontEndMovie.Intro);
            foreach (var gate in Movie("attract-intro", () => Field<ClassicMoviePlayer>("_frontEndMovie"), "Data/Movies/Intro.tgq")) yield return gate;
            Activate(1); yield return Wait("single-player", () => _runner.FrontEnd.Page == FrontEndPage.SinglePlayer);
            Activate(6); if (_runner.FrontEnd.Page == FrontEndPage.Dialog) Activate(667);
            FinishPath(); yield return Wait("new-campaign-map", () => _runner.CampaignMap.IsAtMap);
            Select(new CampaignSlot(1)); FinishPath(); yield return Wait("level-one-briefing", () => _runner.CampaignMap.CanLaunch);
            Activate(170); yield return Wait("level-one-launch", () => _runner.SessionMode == GameSessionMode.Campaign && _runner.LoadedLevelName == new CampaignSlot(1).LevelName);
            yield return Wait("opening-speech-with-subtitle-flag", () => _runner.SoundPlayer.CurrentSpeech is SoundRequest request && request.Subtitle);
            Require(Field<ScriptedViewDirector>("_scriptedView").Subtitle != null, "Playing flagged opening speech has no path subtitle.");
            CheckClip(Field<AudioSource>(_runner.SoundPlayer, "_speech"), "opening speech");
            Require(Math.Abs(Field<AudioSource>(_runner.SoundPlayer, "_speech").volume - .6f) < .001f, "Persisted speech volume is wrong.");
            _runner.OptionsPages.Enter(FrontEndPage.Sound, OptionsApplicationMode.InGame);
            _runner.OptionsPages.Edit(settings => settings with { MasterVolume = 40 }); _runner.OptionsPages.Accept();
            Require(Math.Abs(Field<AudioSource>(_runner.SoundPlayer, "_speech").volume - .3f) < .001f, "Live speech volume callback failed.");
            Observe("pass", "live-volume-callback", "Source volume changed0.6->0.3; decoded PCM remains nonzero. This is not a hearing verdict.");
            var director = Field<MusicDirector>("_musicDirector"); director.Raise(MusicLevel.Battle);
            _runner.SoundPlayer.UpdateMusic(director.AdvanceFrame(true, _runner.SoundPlayer.SoundQuality));
            var music = Field<object>(_runner.SoundPlayer, "_music");
            Require(music != null && Field<AudioSource>(music, "Source") != null, "Music stream was not created.");
            var ring = Field<PcmAudioQueue>(music, "Samples");
            int musicPosition = Field<AudioSource>(music, "Source").timeSamples;
            yield return Wait("battle-music-running-nonzero-pcm-ring", () => Field<AudioSource>(music, "Source").isPlaying
                && Field<AudioSource>(music, "Source").timeSamples != musicPosition && HasQueuedPcm(ring));
            Observe("observed", "music-stream-progress", "AudioSource playhead advanced; read-only locked queue scan found nonzero remaining PCM, frames=" + ring.RemainingFrames);
            Observe("fixture", "battle-music", "Raised MusicDirector.Battle and consumed it through the real linked music player; no combat was played.");
            Observe("pending", "first-fight", "Native/gameplay fight production and human music hearing remain outside this synthetic request.");
            OrdinaryHud();
            var slots = Field<SaveSlotsView>("_saves"); slots.SetName("completion-checkpoint");
            ulong savedHash = _runner.Simulation.StateHash(); Call("SaveSession"); slots.Refresh();
            Require(slots.Entries.Count == 1, "Private save was not written."); _save = slots.Entries[0].Path;
            Call("LoadSession", _save);
            var old = _runner; yield return Wait("save-load-session-replacement", () => _runner != old && _runner.SessionMode == GameSessionMode.Campaign);
            Require(_runner.Simulation.StateHash() == savedHash, "Save/load changed the frozen simulation hash.");
            Observe("pass", "save-load-hash", savedHash.ToString("X16") + "; playmode simulation speed0.");
            _runner.OptionsPages.Enter(FrontEndPage.Controls, OptionsApplicationMode.InGame);
            _runner.OptionsPages.Edit(settings => settings with { GameSpeed = 1 }); _runner.OptionsPages.Accept(); Set("speed", 1f);
            long slowStart = _runner.Simulation.CurrentTick.Value; double sampleStarted = Now;
            yield return Wait("game-speed-one-live-ticks", () => Now - sampleStarted >= 2);
            long slowTicks = _runner.Simulation.CurrentTick.Value - slowStart;
            _runner.OptionsPages.Enter(FrontEndPage.Controls, OptionsApplicationMode.InGame);
            _runner.OptionsPages.Edit(settings => settings with { GameSpeed = 16 }); _runner.OptionsPages.Accept();
            long fastStart = _runner.Simulation.CurrentTick.Value; sampleStarted = Now;
            yield return Wait("game-speed-sixteen-live-ticks", () => Now - sampleStarted >= 2);
            long fastTicks = _runner.Simulation.CurrentTick.Value - fastStart; Set("speed", 0f);
            Require(fastTicks > slowTicks, "Live GameSpeed16 did not advance more ticks than GameSpeed1.");
            Observe("observed", "game-speed-tick-counts", "Two real-time2s windows: setting1=" + slowTicks + ", setting16=" + fastTicks + "; comparative callback check, not a performance measurement.");
            _runner.OptionsPages.Enter(FrontEndPage.Controls, OptionsApplicationMode.InGame);
            _runner.OptionsPages.Edit(settings => settings with { GameSpeed = 4 }); _runner.OptionsPages.Accept();
            OrdinaryHud();
            Require(VerdictRules.End(_runner.Simulation.World, _runner.Simulation.Database, new PlayerId(3), PlayerStatus.Lost), "Loss fixture refused.");
            Observe("fixture", "loss-verdict", "VerdictRules.End(Lost), not a played defeat.");
            yield return Wait("loss-profile-recorded", () => Field<bool>("_resultRecorded"));
            _runner = _runner.LoadLevel(GameSessionRequest.FrontEnd(FrontEndPage.CampaignResults));
            Observe("fixture", "loss-space-equivalent", "Host LoadLevel return request equivalent to Space; no OS key injection.");
            yield return Wait("loss-front-end", () => _runner.FrontEnd != null); FinishPath();
            yield return Wait("loss-statistics", () => _runner.FrontEnd.Page == FrontEndPage.CampaignResults && !_runner.FrontEnd.PathPlaying);
            Activate(181); FinishPath(); yield return Wait("retry-map", () => _runner.CampaignMap.IsAtMap);
            Select(new CampaignSlot(1)); FinishPath(); yield return Wait("retry-briefing", () => _runner.CampaignMap.CanLaunch);
            Activate(170); yield return Wait("retry-level-one", () => _runner.SessionMode == GameSessionMode.Campaign);
            OrdinaryHud(); _runner.SoundPlayer.StopSpeech((long)(Time.unscaledTimeAsDouble * 1000));
            Require(VerdictRules.End(_runner.Simulation.World, _runner.Simulation.Database, new PlayerId(3), PlayerStatus.Won), "Win fixture refused.");
            Observe("fixture", "win-verdict", "VerdictRules.End(Won), not a played victory or native finale.");
            yield return Wait("win-profile-recorded", () => Field<bool>("_resultRecorded"));
            var rewards = Field<WinScreen>("_winScreen"); Require(rewards != null, "Reward view is missing.");
            rewards.Start(Time.unscaledTimeAsDouble * 1000);
            Observe("fixture", "reward-view-start", "WinScreen.Start view fixture replaces unplayed finale trigger; actual level reward bytes and speech/tablet consumers retained.");
            yield return Wait("reward-tablet-and-audio", () => rewards.Current.HasValue && rewards.Tablet.Frame > 1 && _runner.SoundPlayer.CurrentSpeech.HasValue && RewardRepaints > 0);
            CheckClip(Field<AudioSource>(_runner.SoundPlayer, "_speech"), "reward announcement");
            yield return Wait("reward-audio-tablet-completion", () => rewards.Done, 180);
            _runner = _runner.LoadLevel(GameSessionRequest.FrontEnd(FrontEndPage.CampaignResults));
            Observe("fixture", "win-space-equivalent", "Host return request equivalent to Space after actual reward audio/tablet completion.");
            yield return Wait("postwin-title-and-speech", () => _runner.FrontEnd != null && Field<string>("_cutsceneTitle") != null && _runner.SoundPlayer.SpeechBusy);
            foreach (var gate in Movie("postwin-cutscene", () => Field<ClassicMoviePlayer>("_cutscenePlayer"), "Data/Movies/CutSceneLevel1.tgq")) yield return gate;
            yield return Wait("postwin-cutscene-finished", () => Field<CampaignCutscene>("_cutscene") == null);
            FinishPath(); yield return Wait("win-statistics", () => _runner.FrontEnd.Page == FrontEndPage.CampaignResults && !_runner.FrontEnd.PathPlaying);
            Require(_runner.CampaignMap.Profile.PlayerLevelStatus[0] == CampaignLevelStatus.Won, "Actual win did not reach the profile.");
            Activate(181); yield return Wait("highscore-name-entry", () => _runner.CampaignMap.EntryRow >= 0);
            foreach (char character in "AUTOPROBE") Require(_runner.CampaignMap.TypeEntry(character), "Hi-score entry refused.");
            Require(_runner.CampaignMap.CommitEntry(), "Hi-score commit refused."); ResetAttract(); FinishPath();
            yield return Wait("level-two-unlocked-map", () => _runner.CampaignMap.IsAtMap);
            Require(_runner.CampaignMap.Profile.PlayerLevelStatus[1] == CampaignLevelStatus.Open, "Level2 was not opened through result acceptance.");
            Select(new CampaignSlot(2)); FinishPath(); yield return Wait("level-two-briefing", () => _runner.CampaignMap.CanLaunch);
            Activate(170); yield return Wait("level-two-launch", () => _runner.SessionMode == GameSessionMode.Campaign && _runner.LoadedLevelName == new CampaignSlot(2).LevelName);
            OrdinaryHud(); Hud(ClassicHudAction.ToggleOptions); Hud(ClassicHudAction.EndGame); Hud(ClassicHudAction.QuitGame); Hud(ClassicHudAction.CancelConfirmation);
            Require(Field<ClassicHudControls>("_hudControls").Confirmation == ClassicSessionAction.None, "Quit cancellation retained its request.");
            Hud(ClassicHudAction.BackToOptions); Hud(ClassicHudAction.RestartLevel); Hud(ClassicHudAction.Confirm);
            old = _runner; yield return Wait("restart-host-consumer", () => _runner != old && _runner.LoadedLevelName == new CampaignSlot(2).LevelName);
            OrdinaryHud(); Hud(ClassicHudAction.ToggleOptions); Hud(ClassicHudAction.EndGame); Hud(ClassicHudAction.ReturnToMenu); Hud(ClassicHudAction.Confirm);
            yield return Wait("abandon-front-end", () => _runner.FrontEnd != null); FinishPath();
            yield return Wait("abandon-returns-map37", () => _runner.CampaignMap.IsAtMap);
            Require(_runner.FrontEnd.Page != FrontEndPage.CampaignResults, "Abandon incorrectly entered statistics.");
            Require(_runner.CampaignMap.BackToMenu(), "Map return to SinglePlayer refused."); ResetAttract(); FinishPath();
            yield return Wait("single-player-after-abandon", () => _runner.FrontEnd.Page == FrontEndPage.SinglePlayer);
            Activate(9); Activate(4); FinishPath(); yield return Wait("extras-after-win", () => _runner.FrontEnd.Page == FrontEndPage.Extras);
            Activate(22); yield return Wait("gallery-unlock-after-win", () => _runner.FrontEnd.Page == FrontEndPage.Movies);
            Require(_runner.FrontEndMenu.Model.Movies.Entries[1].Available, "Completed Level1 cutscene was not unlocked.");
            Activate(65); Activate(24); FinishPath(); yield return Wait("main-before-reset", () => _runner.FrontEnd.Page == FrontEndPage.Main);
            Activate(1); Activate(6); Require(_runner.FrontEnd.Page == FrontEndPage.Dialog, "Progressed campaign omitted reset confirmation."); Activate(667);
            FinishPath(); yield return Wait("real-reset-map", () => _runner.CampaignMap.IsAtMap);
            Require(_runner.CampaignMap.Profile.PlayerLevelStatus[0] == CampaignLevelStatus.Open && _runner.CampaignMap.Profile.PlayerLevelStatus[1] == CampaignLevelStatus.Locked, "Actual new-campaign reset retained progress.");
            Require(!_runner.FrontEndMenu.Model.Movies.Entries[1].Available, "Gallery did not refresh after actual campaign reset.");
            Observe("pending", "physical-input-and-hearing", "Controller callbacks, model clocks and labelled outcomes were automated; owner keyboard/mouse/hearing/fullscreen acceptance remains pending.");
            var output = new float[1024]; AudioListener.GetOutputData(output, 0);
            Observe(output.Any(value => value != 0) ? "observed" : "pending", "device-output", "AudioListener RMS=" + Rms(output) + "; no human hearing claim.");
        }

        private static IEnumerable<Gate> Movie(string name, Func<ClassicMoviePlayer> current, string expectedPath)
        {
            ClassicMoviePlayer movie = null; int repaints = MovieRepaints;
            yield return Wait(name + "-two-decoded-frames-and-audio-clip", () => {
                movie = current(); return movie != null && Field<int>(movie, "_framesShown") >= 2
                    && Field<AudioClip>(movie, "_clip") != null && MovieRepaints > repaints; });
            Require(Field<string>(movie, "moviePath") == expectedPath, "Wrong movie consumer: " + name);
            var texture = Field<Texture2D>(movie, "_texture"); Require(texture != null, "Decoded movie texture missing.");
            WriteNew(name + "-uploaded-frame.png", texture.EncodeToPNG());
            Observe("observed", name, "Active movie uploaded " + Field<int>(movie, "_framesShown") + "frames; GameView repaint witnessed; clip=" + Field<AudioClip>(movie, "_clip").frequency + "Hz.");
            CallOn(movie, "Finish"); Observe("fixture", name + "-skip", "Private Finish invoked after real decoding/repaint/audio-source setup; not a physical skip key.");
            yield return Wait(name + "-finished-callback", () => !ReferenceEquals(current(), movie));
        }

        private static void Activate(int id)
        {
            ResetAttract(); Require(_runner.FrontEndMenu.Model.Activate(new FrontEndWidgetId((ushort)id)), "Menu refused widget" + id);
            int sounds = _runner.FrontEndMenu.Model.Sounds.Pending.Count;
            _runner.SoundPlayer.Play(_runner.FrontEndMenu.Model.Sounds);
            if (sounds > 0) CheckClip(Effects().Last(source => source.clip != null), "menu-click-" + id);
            Observe("fixture", "menu-action-" + id, "Real model Activate and SoundPlayer request consumer; sound requests=" + sounds + "; no OS input.");
        }
        private static void Select(CampaignSlot slot) { ResetAttract(); Require(_runner.CampaignMap.Select(slot), "Map selection refused " + slot); }
        private static void ResetAttract()
        { if (_runner.FrontEnd != null) Field<FrontEndAttract>("_attract").Reset(TimeSpan.FromSeconds(Math.Max(0, Now - Field<double>("_frontEndStarted")))); }
        private static void FinishPath()
        {
            Set("_frontEndStarted", Field<double>("_frontEndStarted") - 120);
            ResetAttract(); Observe("fixture", "monotonic-front-end-clock", "Advance host elapsed clock120s and reset attract at the new time; never rewind either clock.");
        }
        private static void StartAttract(FrontEndMovie expected)
        {
            var attract = Field<FrontEndAttract>("_attract");
            var start = TimeSpan.FromSeconds(Math.Max(_runner.FrontEnd.Now.TotalSeconds, Now - Field<double>("_frontEndStarted")));
            attract.Reset(start); Require(attract.Update(start + TimeSpan.FromSeconds(180), _runner.FrontEnd.Page, false) == null, "Attract fired at the inclusive boundary.");
            Require(attract.Update(start + TimeSpan.FromSeconds(180) + TimeSpan.FromTicks(1), _runner.FrontEnd.Page, false) == expected, "Attract strict boundary/alternation failed.");
            Set("_frontEndStarted", Now - start.TotalSeconds - 181); ResetAttract();
            Call("PlayFrontEndMovie", expected == FrontEndMovie.BullfrogIntro ? ClassicMoviePlayer.BullfrogIntro : "Data/Movies/Intro.tgq");
            Observe("fixture", "strict-attract-clock", "Exact180s then+1tick model clock, alternating result sent to real movie consumer; host clock advanced monotonically.");
        }
        private static void OrdinaryHud()
        {
            var view = Field<ScriptedViewDirector>("_scriptedView"); view.Skip();
            view.Emit(new SimEvent(_runner.Simulation.CurrentTick, "completion_probe", "scripted_view", "ordinary HUD fixture") {
                ScriptedView = new ScriptedViewRequest(ScriptedViewKind.Letterbox, new PlayerId(3), 2, default, false) });
            view.Update(Time.unscaledTimeAsDouble * 1000, _runner.Simulation.CurrentTick.Value);
            Field<ClassicHudControls>("_hudControls").SetGuiVisible(true);
            Observe("fixture", "ordinary-hud", "Skip bootstrap view and force off letterbox through existing presentation request; simulation speed remains0.");
        }
        private static void Hud(ClassicHudAction action)
        {
            Call("ApplyHudAction", action); Observe("fixture", "hud-" + action, "Existing host action API equivalent; no OS input.");
            if (action != ClassicHudAction.Confirm) return;
            var request = Field<ClassicHudControls>("_hudControls").TakeSessionAction();
            if (request == ClassicSessionAction.Restart)
                Set("_pendingSession", GameSessionRequest.Restart(GameSessionRequest.Campaign(_runner.LoadedLevelName, Field<FrontEndPage>("frontEndReturnPage"))));
            else if (request == ClassicSessionAction.ReturnToMenu)
                Set("_pendingSession", GameSessionRequest.FrontEnd(Field<FrontEndPage>("frontEndReturnPage") == FrontEndPage.CampaignResults ? FrontEndPage.CampaignMap : FrontEndPage.None));
            else Require(request != ClassicSessionAction.Quit, "Probe never confirms application Quit.");
            Observe("fixture", "queued-session-consumer", "Consumed " + request + " and selected the same pending request as the host mouse-click arm; not physical UI input.");
        }
        private static List<AudioSource> Effects() => Field<List<AudioSource>>(_runner.SoundPlayer, "_effects");
        private static void CheckEffect(string name, float volume)
        { var source = Effects().Last(item => item.clip != null); CheckClip(source, name); Require(Math.Abs(source.volume - volume) < .001f, "Effect source volume mismatch."); }
        private static void CheckClip(AudioSource source, string name)
        {
            Require(source != null && source.clip != null, name + " clip missing.");
            var samples = new float[Math.Min(source.clip.samples * source.clip.channels, 65536)];
            Require(source.clip.GetData(samples, 0) && samples.Any(value => Math.Abs(value) > .00001f), name + " decoded PCM is zero.");
            Observe("observed", name + "-pcm", "Nonstreaming clipRMS=" + Rms(samples) + " sourceVolume=" + source.volume + " playing=" + source.isPlaying);
        }
        private static double Rms(float[] samples) => Math.Sqrt(samples.Sum(value => (double)value * value) / samples.Length);
        private static bool HasQueuedPcm(PcmAudioQueue ring)
        {
            lock (Field<object>(ring, "_gate"))
            {
                var current = Field<PcmClip>(ring, "_current");
                int frame = Field<int>(ring, "_frame");
                if (current != null && Field<float>(ring, "_gain") != 0
                    && current.Samples.Skip(frame * current.Channels).Any(sample => sample != 0)) return true;
                var queued = (System.Collections.IEnumerable)typeof(PcmAudioQueue).GetField("_queued", Private).GetValue(ring);
                foreach (object item in queued)
                {
                    var clip = (PcmClip)item.GetType().GetField("Item1").GetValue(item);
                    float gain = (float)item.GetType().GetField("Item2").GetValue(item);
                    if (gain != 0 && clip.Samples.Any(sample => sample != 0)) return true;
                }
                return false;
            }
        }

        public static void WitnessGui()
        {
            if (Event.current.type != EventType.Repaint || _runner == null || _runner.Simulation == null) return;
            GuiRepaints++;
            GuiWidth = Screen.width; GuiHeight = Screen.height;
            if (_runner.SessionMode == GameSessionMode.FrontEnd && Field<FrontEndStartup>("_frontEndStartup")?.Stage == FrontEndStartupStage.Title) TitleRepaints++;
            if (UnityEngine.Object.FindAnyObjectByType<ClassicMoviePlayer>() != null) MovieRepaints++;
            if (_runner.SessionMode == GameSessionMode.Campaign && Field<WinScreen>("_winScreen")?.Current != null) RewardRepaints++;
        }
        private static void Observe(string kind, string name, string detail)
        {
            var row = new Observation { kind = kind, name = name, detail = detail, seconds = Now, dspTime = AudioSettings.dspTime,
                frame = Time.frameCount, width = GuiWidth, height = GuiHeight, tick = _runner?.Simulation?.CurrentTick.Value ?? -1,
                level = _runner?.LoadedLevelName ?? "", page = _runner?.FrontEnd?.Page.ToString() ?? "gameplay" };
            Result.observations.Add(row);
            WriteNew((++_sequence).ToString("D3") + "-" + name + ".json", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(row, true)));
            Debug.Log("Completion flow " + kind + ": " + name + " " + detail);
        }
        private static void Finish(bool success, string message)
        {
            _finished = true; Result.success = success; Result.message = message;
            Result.unexpectedLog = _unexpectedLog; Result.ignoredKnownEditorSearch = _ignoredEditorSearch;
            Result.guiRepaints = GuiRepaints; Result.titleRepaints = TitleRepaints; Result.movieRepaints = MovieRepaints; Result.rewardRepaints = RewardRepaints;
            Result.finalFrame = Time.frameCount; Result.guiWidth = GuiWidth; Result.guiHeight = GuiHeight;
            Result.activeGate = _gate?.name; Result.level = _runner?.LoadedLevelName;
            if (_runner != null) Result.startupState = Field<FrontEndStartup>("_frontEndStartup")?.Stage.ToString() ?? "none";
            if (_runner != null) Call("CloseInputJournals", "completion flow probe finished");
            WriteNew(success ? "completed.json" : "failed.json", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(Result, true)));
            SessionState.SetBool(Active, false); Application.logMessageReceived -= Logged;
            EditorApplication.update -= Poll; EditorApplication.Exit(success ? 0 : 1);
        }
        private static void WriteNew(string name, byte[] bytes)
        { using (var stream = new FileStream(Path.Combine(_output, name), FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes, 0, bytes.Length); }
        private sealed class Gate { public string name; public Func<bool> condition; public double started, timeout; public int frame; }
        [Serializable] private sealed class Report
        {
            public string role = "Automated runtime callbacks and labelled fixtures; not native parity, physical input or owner hearing acceptance.";
            public bool success; public string message, unityVersion, graphics; public int audioSampleRate;
            public int guiRepaints, titleRepaints, movieRepaints, rewardRepaints;
            public int finalFrame, guiWidth, guiHeight; public string startupState;
            public string activeGate, level;
            public string unexpectedLog; public int ignoredKnownEditorSearch;
            public List<Observation> observations = new List<Observation>();
        }
        [Serializable] private sealed class Observation
        { public string kind, name, detail, level, page; public double seconds, dspTime; public int frame, width, height; public long tick; }
    }
}

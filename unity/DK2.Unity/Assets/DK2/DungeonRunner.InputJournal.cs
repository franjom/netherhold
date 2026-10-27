// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>
    /// The Unity input journal (ADR-0553 Phase 1), off unless <see cref="inputJournalDirectory"/> or
    /// <c>DK2_INPUT_JOURNAL</c> names a folder. Each runner (the front end, each level) gets a new
    /// session folder with <c>frames.journal</c> (one <see cref="FrameInputRecord"/> per rendered
    /// frame) and, once its simulation ticks, <c>input.journal</c> (ADR-0552: frame per tick, hash
    /// after it). Recording only reads, and a journal that fails stops itself, never the game.
    /// </summary>
    public sealed partial class DungeonRunner
    {
        [SerializeField] private string inputJournalDirectory = "";

        // KeyCode values below Mouse0: the keyboard. Mouse buttons are recorded as buttons, and
        // joystick codes are out of scope (no gamepad, ADR-0553).
        private static readonly KeyCode[] JournalKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Where(k => k > KeyCode.None && k < KeyCode.Mouse0).Distinct().ToArray();

        private bool _journalResolved;
        private string _journalSession;
        private Dictionary<string, string> _journalHeader;
        private FileStream _frameJournalFile;
        private FrameInputJournalWriter _frameJournal;
        private long _journalFrames;
        private readonly FrameInputRecorder _journalRecorder = new FrameInputRecorder();
        private readonly List<HostKey> _journalDown = new List<HostKey>(), _journalUp = new List<HostKey>(), _journalHeld = new List<HostKey>();
        private bool _journalWasHeld;

        // Opened and written on the tick thread; closed on the main thread only after WaitForTick.
        private FileStream _tickJournalFile;
        private InputJournalWriter _tickJournal;
        private volatile bool _tickJournalFailed;

        private void JournalBeginFrame()
        {
            if (!_journalResolved) OpenInputJournal();
            if (_frameJournal == null) return;

            _journalDown.Clear(); _journalUp.Clear(); _journalHeld.Clear();
            if (Input.anyKey || Input.anyKeyDown || _journalWasHeld)
                foreach (KeyCode key in JournalKeys)
                {
                    if (Input.GetKeyDown(key)) _journalDown.Add(new HostKey((int)key));
                    if (Input.GetKeyUp(key)) _journalUp.Add(new HostKey((int)key));
                    if (Input.GetKey(key)) _journalHeld.Add(new HostKey((int)key));
                }
            // A key released this frame is no longer anyKey; one more scan catches its up edge.
            _journalWasHeld = _journalHeld.Count > 0;

            bool inGame = _frontEnd == null && _simulation != null && _scriptedView != null;
            Vector3 pointer = Input.mousePosition;
            Vector2 wheel = Input.mouseScrollDelta;
            _journalRecorder.BeginFrame(new FrameInputRecord
            {
                Frame = Time.frameCount,
                UnscaledDeltaSeconds = Time.unscaledDeltaTime,
                Mode = _frontEnd != null ? FrameInputMode.FrontEnd : FrameInputMode.Game,
                PointerX = pointer.x,
                PointerY = pointer.y,
                ButtonsHeld = Buttons(Input.GetMouseButton),
                ButtonsDown = Buttons(Input.GetMouseButtonDown),
                ButtonsUp = Buttons(Input.GetMouseButtonUp),
                WheelX = wheel.x,
                WheelY = wheel.y,
                PanHorizontal = Input.GetAxisRaw("Horizontal"),
                PanVertical = Input.GetAxisRaw("Vertical"),
                TypedText = Input.inputString,
                KeysDown = _journalDown.ToArray(),
                KeysUp = _journalUp.ToArray(),
                KeysHeld = _journalHeld.ToArray(),
                Hud = inGame ? JournalHud() : FrameHudFlags.None,
                ActiveTool = ActiveTool,
            }, _pendingCommands);
        }

        private static FrameMouseButtons Buttons(Func<int, bool> read) =>
            (read(0) ? FrameMouseButtons.Primary : 0) | (read(1) ? FrameMouseButtons.Secondary : 0)
            | (read(2) ? FrameMouseButtons.Middle : 0);

        private FrameHudFlags JournalHud()
        {
            HudVisibility hud = CurrentHud;
            return (hud.AllowOrdinaryInput ? FrameHudFlags.OrdinaryInput : 0) | (HudModal ? FrameHudFlags.Modal : 0)
                | (_hudControls.OptionsVisible ? FrameHudFlags.Options : 0)
                | (_scriptedView.PathPlaying ? FrameHudFlags.CameraPath : 0);
        }

        private void JournalIntent(in PlayerIntent intent)
        {
            if (_frameJournal != null) _journalRecorder.Intent(intent);
        }

        /// <summary>Called just before the pending queue is handed to a tick and cleared.</summary>
        private void JournalHandOff()
        {
            if (_frameJournal != null) _journalRecorder.HandOff(_pendingCommands);
        }

        /// <summary>The frame's tick count, read after its ticks are scheduled. On the main-thread
        /// path they have already run; on the worker path they have not started.</summary>
        private void JournalTicks(int ticks)
        {
            if (_frameJournal == null) return;
            _journalRecorder.Ticks(new GameTick(_simulation.World.CurrentTick.Value - (tickOnWorkerThread ? 0 : ticks)), ticks);
        }

        private void LateUpdate()
        {
            if (_frameJournal == null) return;
            WriteJournalFrame(_journalRecorder.EndFrame(_pendingCommands, _tickDebt));
        }

        private void WriteJournalFrame(FrameInputRecord frame)
        {
            if (frame == null || _frameJournal == null) return;
            try
            {
                _frameJournal.Write(frame);
                _journalFrames++;
            }
            catch (Exception ex)
            {
                StopFrameJournal(ex);
            }
        }

        /// <summary>On the tick thread: the frame given to the tick, then the hash after it.</summary>
        private void JournalTick(long tick, in InputFrame input, bool before)
        {
            if (_frameJournal == null || _tickJournalFailed) return;
            try
            {
                if (before)
                {
                    if (_tickJournal == null) OpenTickJournal();
                    _tickJournal.WriteFrame(tick, input);
                }
                else _tickJournal.WriteHash(tick, _simulation.StateHash());
            }
            catch (Exception ex)
            {
                _tickJournalFailed = true;
                Debug.LogWarning($"Input journal (ticks) stopped: {ex.Message}");
            }
        }

        private void JournalTickFailed(Exception failure)
        {
            if (_tickJournal == null || _tickJournalFailed) return;
            try
            {
                long tick = _simulation.World.CurrentTick.Value;
                _tickJournal.WriteEnd(tick, $"exception at tick {tick}: {failure.GetType().FullName}: {failure.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Input journal (ticks) could not record the failure: {ex.Message}");
            }
            _tickJournalFailed = true;
        }

        private void OpenInputJournal()
        {
            _journalResolved = true;
            string root = string.IsNullOrWhiteSpace(inputJournalDirectory)
                ? Environment.GetEnvironmentVariable("DK2_INPUT_JOURNAL") : inputJournalDirectory;
            if (string.IsNullOrWhiteSpace(root)) return;
            try
            {
                string name = sessionMode == GameSessionMode.FrontEnd ? "front-end" : levelName;
                _journalSession = Path.Combine(root,
                    $"session-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{name}");
                Directory.CreateDirectory(_journalSession);
                _journalHeader = JournalHeader();
                _frameJournalFile = new FileStream(Path.Combine(_journalSession, "frames.journal"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                _frameJournal = new FrameInputJournalWriter(_frameJournalFile, _journalHeader);
                Debug.Log($"Input journal: {_journalSession}");
            }
            catch (Exception ex)
            {
                StopFrameJournal(ex);
            }
        }

        private void OpenTickJournal()
        {
            var header = new Dictionary<string, string>(_journalHeader)
            {
                ["world.start.tick"] = _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture),
            };
            _tickJournalFile = new FileStream(Path.Combine(_journalSession, "input.journal"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _tickJournal = new InputJournalWriter(_tickJournalFile, header);
            _tickJournal.WriteHash(_simulation.World.CurrentTick.Value, _simulation.StateHash());
        }

        /// <summary>Main thread, after WaitForTick: writes the frame still open (a session switch
        /// leaves from inside Update, before LateUpdate), ends both journals and closes the files.</summary>
        private void CloseInputJournals(string reason)
        {
            if (_frameJournal != null) WriteJournalFrame(_journalRecorder.EndFrame(_pendingCommands, _tickDebt));
            Close(ref _tickJournal, ref _tickJournalFile, w =>
            {
                if (!_tickJournalFailed && _simulation != null) w.WriteEnd(_simulation.World.CurrentTick.Value, reason);
            });
            Close(ref _frameJournal, ref _frameJournalFile, w => w.WriteEnd(_journalFrames, reason));
        }

        /// <summary>Main thread: a frame-journal failure closes the frame journal now; the tick
        /// journal may be in use on the tick thread, so it only stops writing and is closed later.</summary>
        private void StopFrameJournal(Exception ex)
        {
            Debug.LogWarning($"Input journal stopped: {ex.Message}");
            _tickJournalFailed = true;
            Close(ref _frameJournal, ref _frameJournalFile, w => w.WriteEnd(_journalFrames, "journal failed: " + ex.Message));
        }

        private static void Close<T>(ref T writer, ref FileStream file, Action<T> end) where T : class, IDisposable
        {
            try
            {
                if (writer != null)
                {
                    end(writer);
                    writer.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Input journal did not close cleanly: {ex.Message}");
            }
            try { file?.Dispose(); }
            catch (IOException) { }
            writer = null;
            file = null;
        }

        private Dictionary<string, string> JournalHeader()
        {
            var header = new Dictionary<string, string>
            {
                ["source"] = "unity",
                ["unity.version"] = Application.unityVersion,
                ["keys"] = "UnityEngine.KeyCode",
                ["session.mode"] = sessionMode.ToString(),
                ["level"] = string.IsNullOrEmpty(saveFile) ? levelName : saveFile,
                ["seed"] = seed.ToString(CultureInfo.InvariantCulture),
                ["viewing.player"] = viewingPlayerId.ToString(CultureInfo.InvariantCulture),
                ["fog.of.war"] = fogOfWar ? "on" : "off",
                ["tick.on.worker"] = tickOnWorkerThread ? "on" : "off",
                ["screen"] = $"{Screen.width}x{Screen.height}",
                ["commit"] = "unknown",
                ["src.dirty"] = "unknown",
            };
            if (!Application.isEditor) return header;
            // The editor runs from a checkout; a player does not know its commit.
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            if (Git(repo, "rev-parse HEAD") is string commit) header["commit"] = commit;
            if (Git(repo, "status --porcelain -- src unity/DK2.Unity/Assets") is string dirty)
                header["src.dirty"] = dirty.Length == 0 ? "no" : "yes";
            return header;
        }

        /// <summary>A git query that takes no index lock (the owner may be using git) and gives up after five seconds.</summary>
        private static string Git(string repo, string args)
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("git", $"--no-optional-locks -C \"{repo}\" {args}")
                {
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                };
                using var process = System.Diagnostics.Process.Start(start);
                if (process == null) return null;
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    process.Kill();
                    return null;
                }
                return process.ExitCode == 0 && output.Wait(1000) ? output.Result.Trim() : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                return null;
            }
        }
    }
}

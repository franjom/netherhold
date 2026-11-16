// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>
    /// The Unity side of the input journal (<see cref="FrameJournalSession"/>, which holds the policy: the opt-in, the
    /// session folder, the files, the call order and the failure handling). Unity reads its devices into each frame's raw
    /// sample (keys numbered by <c>UnityEngine.KeyCode</c>), names the folder by <see cref="inputJournalDirectory"/> or
    /// <c>DK2_INPUT_JOURNAL</c>, and adds its version and, in the editor, the commit to the header.
    /// </summary>
    public sealed partial class DungeonRunner
    {
        [SerializeField] private string inputJournalDirectory = "";

        // KeyCode values below Mouse0: the keyboard. Mouse buttons are recorded as buttons, and
        // joystick codes are out of scope (no gamepad, ADR-0553).
        private static readonly KeyCode[] JournalKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Where(k => k > KeyCode.None && k < KeyCode.Mouse0).Distinct().ToArray();

        private readonly FrameJournalSession _journal = new FrameJournalSession(Debug.Log, Debug.LogWarning);
        private readonly List<HostKey> _journalDown = new List<HostKey>(), _journalUp = new List<HostKey>(), _journalHeld = new List<HostKey>();
        private bool _journalWasHeld;

        private void JournalBeginFrame()
        {
            if (!_journal.Resolved) OpenInputJournal();
            if (!_journal.Recording) return;

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
            _journal.BeginFrame(new FrameInputRecord
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

        /// <summary>The frame's tick count, read after its ticks are scheduled. On the main-thread
        /// path they have already run; on the worker path they have not started.</summary>
        private void JournalTicks(int ticks)
        {
            if (!_journal.Recording) return;
            _journal.Ticks(new GameTick(World.CurrentTick.Value + _ticksPending - (tickOnWorkerThread ? 0 : ticks)), ticks);
        }

        private void LateUpdate() => _journal.EndFrame(_pendingCommands, _tickDebt);

        private void OpenInputJournal() =>
            _journal.Open(FrameJournalSession.Root(inputJournalDirectory, Environment.GetEnvironmentVariable),
                FrameJournalSession.FolderName(DateTime.UtcNow, sessionMode, levelName), JournalHeader);

        /// <summary>Main thread, after WaitForTick: writes the frame still open (a session switch
        /// leaves from inside Update, before LateUpdate), ends both journals and closes the files.</summary>
        private void CloseInputJournals(string reason) => _journal.Close(reason, _pendingCommands, _tickDebt, _simulation);

        private IReadOnlyDictionary<string, string> JournalHeader()
        {
            Dictionary<string, string> header = FrameJournalSession.Header("unity", FrameJournalSession.UnityKeyTable, sessionMode,
                string.IsNullOrEmpty(saveFile) ? levelName : saveFile, seed, new PlayerId((byte)viewingPlayerId), fogOfWar,
                tickOnWorkerThread, Screen.width, Screen.height);
            header["unity.version"] = Application.unityVersion;
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

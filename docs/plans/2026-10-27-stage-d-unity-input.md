# Stage D — Unity input journal, input extraction, Input System, HUD automation

**Status:** accepted as ADR-0553 (2026-10-27). Phase 1 journals built and reviewed, not yet run in the editor; Phase 1b, the baseline sessions and Phases 2–5 not started.
**Parent:** `plans/2026-10-27-autonomous-playtesting.md` (ADR-0552, Stages A–C, headless).
**Mandate:** `AGENTS.md` Goal 10 (AI/Bot Interface) and Goal 11 (presentation modes stay thin).

## 1. Objective

Test the real Unity human-input path automatically:

input → Unity adapter → `PlayerIntent` → `PlayerInputTranslator` → `PlayerCommand` →
`GameSimulation` → HUD and world.

No gameplay rule moves into Unity, and nothing changes simulation behaviour for testing. No
phase is expected to move `HashVersion`; a change that moves the pinned hash is a defect.

**Order:** record today's behaviour; move the input mapping into `DK2.Presentation` and test it
headlessly; decide on the Input System; migrate; automate the rendered HUD last.

## 2. What the code shows (2026-10-27, master 3910ee47)

- Unity 6000.6.0f1, `activeInputHandler: 0` (legacy only). Neither `com.unity.inputsystem`
  nor `com.unity.test-framework` is installed; the Unity project has no automated tests.
  Script compilation is checked by `unity/DK2.Unity.Compile` (`dotnet build`).
- About 120 legacy `Input.*` reads (117 occurrences; by line, `DungeonRunner.cs` 38 and
  `DungeonRunner.Hud.cs` 36), the rest in `DungeonRunner.FrontEnd.cs`, `ClassicFrontEndMenu.cs`, `ClassicMoviePlayer.cs`,
  `ClassicMessageTabs.cs`, `DungeonRunner.LevelEnd.cs` and `DungeonRunner.Finale.cs`. Kinds:
  `mousePosition`, `GetKeyDown`, `GetKey`, `GetMouseButton(Down/Up)`, `GetAxisRaw`
  (Horizontal/Vertical camera pan), `mouseScrollDelta` (zoom, front-end wheel), `inputString`
  (front-end text entry).
- **Two input routes, not three.** (a) Polled `Input.*` in `Update`. (b) UI Toolkit pointer
  callbacks on the bottom panel (`ClassicBottomPanel.cs`: `PointerDown/Up/Enter/Leave/Cancel`,
  pointer capture) and on the message tabs (`ClassicMessageTabs.cs`: `PointerDown/Enter/Leave`;
  their dismiss, close and go-to change view state, not commands). The other `UIDocument`s turn
  picking off and are hit-tested by our own geometry from `Input.mousePosition`
  (`ClassicHudControlLayout.ActionAt`, `PointerOverHud`, the minimap). IMGUI and the finale's
  `GL` calls only draw: there is no `GUI.Button` or `GUILayout` call.
- One `PlayerIntent` is built per frame at most (`DungeonRunner.QueueInput`), and only on a
  primary or secondary button edge; the translator's commands are queued there. Ten more sites
  queue commands into `_pendingCommands` directly (HUD 7, finale 2, path skip 1).
- **Frames do not map to ticks one to one.** `Update` adds real elapsed time to `_tickDebt`
  (`DungeonRunner` feeds `FrameElapsedClock` `Time.realtimeSinceStartupAsDouble`), runs 0..`maxTicksPerFrame`
  whole ticks, and gives all pending commands to the first tick of a frame that runs one;
  commands from a frame with no tick wait. `tickOnWorkerThread` runs those ticks after
  `Update` returns. Unity runs no computer keeper, so the frame given to a tick is exactly
  what the player queued.
- Each session switch builds a new `DungeonRunner` (`LoadLevel`); a runner's `_simulation` is
  set once.
- Losing focus cancels the pointer gesture and the panel's gesture
  (`DungeonRunner.OnApplicationFocus`).

## 3. Phase 1 — Unity input journal (built)

Off by default; on when `DK2_INPUT_JOURNAL` (or the runner's `inputJournalDirectory` field)
names a folder. Each runner writes a new `session-<utc>-<level or front-end>` folder
(`DungeonRunner.InputJournal.cs`), so a front-end walk into a level leaves two folders:

1. `input.journal`, once the runner's simulation ticks (the front end does not) — the Stage B
   format (`InputJournalWriter`): the frame given to each tick
   and the hash after it, so `GameSimulation` behaviour is checkable and, once Phase 1b holds,
   replayable by the harness. Header: `source=unity`, level, seed, `unity.version`, commit and
   `src.dirty` (from git, editor only; `unknown` in a player).
2. `frames.journal` — `FrameInputJournal` (`DK2.Presentation`), one record per rendered frame:
   frame number, unscaled delta, mode (game, front end), tick at start, ticks run, tick debt
   after, the raw legacy input (pointer, three buttons held/down/up, wheel, the two pan axes,
   typed text, every key down/up/held), HUD flags (ordinary input allowed, modal, options,
   camera path) and the active tool at the frame's start, every `PlayerIntent` built, every
   command queued in queue order, tagged before `Update`, during the frame (before its tick
   hand-off, or in a frame with none) or after the hand-off, and how many commands the frame
   handed to its first tick. The bookkeeping is `FrameInputRecorder` (Presentation, tested).
   The frame open when a session switch leaves `Update` is written on release.

Both are written record-whole and flushed in bounded steps; the reader keeps every whole record
of a killed session. A journal that fails stops itself and logs a warning, never the game.
Journaling only reads, so it is hash-neutral, but not timing-neutral: the tick thread hashes
after every tick (a few ms), which can change how many ticks land in a frame. A journaled
session is faithful to what ran; it is not the same session as an unjournaled one.

**Phase 1b.** (1) The harness replays a Unity `input.journal` when Unity's world construction
for a campaign level matches `ScenarioRunner`'s `campaign:levelN` setup; if it does not, the
difference is listed and either fixed in one place or recorded here. (2) The frame record gains
what a raw-to-intent mapping needs and does not have: the adapter's pointer tile and position
and pointer-over-HUD every frame (they need the camera), the raw tool, `selectedRoomId`, the
bottom-panel choice, the query tool and the message-tab state. The owner uses the message tabs
in play (2026-10-27), so their state and clicks are required, not optional. A door, trap or
spell chosen in the panel stays armed after a placement in DK2 (owner, 2026-10-27), as ours does
(`DungeonRunner.Hud.cs`: only a right-click clears it), so the choice is per-frame state.

**Baseline sessions.** Legacy `Input` is read-only, so nothing can drive it before Phase 4.
The Phase 2 baseline is three to five short sessions the owner plays with journaling on
(digging, a room, pickup/drop/slap, a spell, a door or trap, camera, a front-end walk).
Evidence goes to `D:\DK2Evidence\runs\unity-input-<date>\` (AGENTS.md, add-only).

## 4. Phase 2 — move the input mapping into Presentation

Unity adapter → device-neutral per-frame input snapshot → pure mapping to intents and HUD
actions → existing `PlayerInputTranslator`. The snapshot type is the one `frames.journal`
records, so a recorded session is directly a test input. The mapping covers mouse buttons and
pointer, clicks, releases, drags, keyboard shortcuts and modifiers, camera pan and zoom, HUD
selection and targeting, and the front-end keys. Bottom-panel choices enter as inputs (the
recorded tool and panel state), never re-derived. Rendering, `UIDocument` ownership and scene
work stay in Unity.

**Acceptance:** headless xUnit tests on the mapping (edges, ordering, modifiers), and every
baseline session recorded after Phase 1b replayed through it yields the recorded intents. The
commands from an intent need the world (`PlayerInputSnapshot`), so they are compared by
replaying `input.journal`, not by the mapping.

## 5. Phase 3 — Input System decision (its own ADR)

Preferred: the Input System, if a package version supports 6000.6.0f1. ADR-0552 is at its
line budget, so this is a new ADR. It must settle, with evidence:

1. **Wheel units.** Legacy reports about ±1 per notch; the Input System on Windows has
   reported ±120. Check whether the installed version's scroll-normalisation setting
   (`InputSettings.scrollDeltaBehavior`, unverified for this version) gives legacy units;
   otherwise normalise in the adapter. Zoom and the front-end wheel must not change speed.
2. **Pan axes.** `GetAxisRaw` becomes explicit key bindings; Input Manager axis settings
   (dead zone, gravity, snap, alternative keys) are read from `InputManager.asset` and kept or
   their loss recorded. The bindings are DK2's default key list (`research/controls.md`; the
   camera keys also read in the default table 0x005604C0): arrows scroll, SHIFT + arrow
   speed-scrolls. W/A/S/D, Q/E and joystick axes are ours, not DK2's (A is the Ally Window; the
   owner confirms WASD does not pan, 2026-10-27), and are dropped. The owner pans mostly at the
   screen edge (5 pixels, `classic-camera.md` section 7).
3. **Text.** `Input.inputString` becomes `Keyboard.onTextInput`, buffered per frame.
4. **Timing.** Press/release edges are read once per rendered frame (dynamic update), as
   today; the frame→tick relation of §2 is unchanged.
5. **UI Toolkit.** Confirm the bottom panel's pointer callbacks receive Input System events
   (and injected ones) at runtime without a uGUI `EventSystem`, or say what it needs.

## 6. Phase 4 — migration

One adapter collects input for every route; no duplicate event processing; both backends
(`activeInputHandler: 2`) only while the baseline sessions are compared through the new
adapter; legacy reads removed after that comparison passes, editor probes included. No
gamepad. Device state stays outside `GameSimulation`.

## 7. Phase 5 — rendered HUD automation

- **Where it runs.** A windowed Mono player built from a separate checkout (Unity's project lock
  refuses a batch build of the project the owner has open; IL2CPP lacks its module). The
  copy's Library is imported once and reused; DLLs come from `sync-assemblies.ps1`. Never
  `-nographics` for layout, hit tests or screenshots. One instance, one scenario, a wall-clock
  limit, launched by hand.
- **Fixed step.** `Time.captureDeltaTime` alone does not fix the tick count, because
  `FrameElapsedClock` reads real time. A fixed-step mode feeds the clock the captured delta;
  the worker-thread tick is pinned off or shown not to change the per-tick frames.
- **Isolation.** `runInBackground`; physical mouse and keyboard disabled, input from a virtual
  device only; focus loss is part of a test, not an accident.
- **Edge pan.** The owner's main camera control; the driver moves the virtual pointer to each
  edge and corner, and the window must not lose the pointer to a second monitor.
- **Driver.** Injects input through the production backend, by frame number; aims at
  coordinates from real UI geometry; records `frames.journal`, `input.journal` and
  screenshots at checkpoints. Verdicts: input (intent as expected), gameplay (command and
  outcome), presentation (HUD/world state); an expected refusal is not an input failure.

## 8. Coverage inventory

Generated from `HumanCommandPolicy`, the intent paths and the front-end pages, with one status
each: implemented, automatically tested, manually tested, unavailable. A test keeps it in step
with the policy. Possession, door locking and the other `NoHumanPathYet` commands stay
unavailable until a production input path exists.

## 9. Evidence

Every failure bundle: commit, Unity and Input System versions, scene and display settings,
`frames.journal`, `input.journal`, screenshots, the Unity log, how to reproduce. All of it under
`D:\DK2Evidence` (owner rule 2026-10-24: nothing outside the repo lives only in scratch).

## 10. Out of scope

Gamepad, continuous or parallel Unity runs, live LLM screenshot reading, computer-vision
agents, new gameplay commands for testing, replacing the headless harness.

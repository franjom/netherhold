# Playable end to end: finishing the Step 6 HUD and the M10 front end

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.
> **For Codex:** this plan is written for you. Follow it task by task, in order inside a phase; phases A–E are independent of one another except where a task names a dependency.

**Goal:** a player starts the game, sees DK2's title and intro, walks the menus, picks a campaign level, plays it under the finished classic HUD with its Options menu (objective, save, load, restart, end game, options), hears the interface and the Mentor, sees the result and the statistics, and returns to the map, so the owner can give gameplay feedback.

**Architecture:** unchanged from ADR-0002, ADR-0408, ADR-0497 and `docs/plans/2026-10-23-front-end.md`. Rules and decoders in `src/` (netstandard2.1, Unity-loadable); what the HUD and the front end *show* in `DK2.Presentation`, tested headlessly; Unity (`unity/DK2.Unity/Assets/DK2`) draws and forwards input. Every DK2 behaviour is read from DKII.EXE before it is built; what cannot be read stays UNKNOWN and disabled, never a successful-looking placeholder.

**Tech stack:** .NET 10 SDK, xUnit, Unity 6000.6.0f1 with UI Toolkit and URP, `DK2.Unity.Compile` for adapter builds without the editor, Ghidra 12.1.4 through `pyghidra-mcp` and the `ghidra-queries` skill, the `dk2kb` function records.

---

## 0. Where things stand (2026-10-29)

Read these before anything else; this plan does not repeat them.

| What | Where |
|---|---|
| Step 6 HUD scope, built items and open gates | `docs/plans/2026-10-17-classic-look-step6.md` |
| Front-end phases F1–F9, built items, play checks | `docs/plans/2026-10-23-front-end.md` |
| HUD design rules | `docs/ART_DIRECTION.md` §13, ADR-0497 (still *proposed*) |
| Project state and verification numbers | `docs/HANDOFF.md` ("Classic look", "Next work" item 6) |
| How a Codex brief is run here | `docs/CODEX_TASKS.md` ("Setup", "Boundaries that apply to every brief") |
| Traps that have cost time | `docs/TRAPS.md` (read whole, once) |

Built and merged: steps 0–5 of the classic look; the minimap, top bar, cursor, hover text, bottom panel, creature panels, message tabs, status flowers, HUD hide/restore; front-end F1–F5 (menus, campaign map, briefing, level loop, statistics, hi-scores, transfer creature, cutscenes, finale), F7's movie decoder and playback proof of concept, Today's Top Keepers.

**Implementation status (2026-10-29):** A–E are built in their phase branches
and integrated on `codex/hud-completion-final`, merged to master 2026-10-30.
Options objective, confirmations, versioned remake saves, startup/gallery/attract,
runtime audio, settings/key consumers, scalable HUD and the Step6 leftovers are
implemented. Prepared HUD captures and original-game requests live in the
[comparison checklist](../research/hud-comparison-checklist.md).

Owner play, hearing and matched-original acceptance remain open. The PhaseF
table below records automated runtime evidence separately. Unresolved policies
and disabled consumers remain explicit in Q683/Q685/Q686 and the research;
this implementation adds no simulation hash change.

## 1. Rules for the executor

These are the repo's rules restated as a checklist; the sources are AGENTS.md and `docs/CODEX_TASKS.md`.

- **One worktree per phase**, on a `codex/<phase>` branch, hooks installed: `git config core.hooksPath .githooks`. Do not merge into master; the owner merges. Stage named paths only. Commit trailer `Assisted-by: GPT-6` (or your model), never `Co-Authored-By`, never `--no-verify`.
- **Read before you build.** Every task that says *Research* names function addresses. Call `kb_get` on each first; a later session may already have read or disputed it. Decompile only what the record leaves open. Write what you learn back with `kb_upsert` (in a worktree: edit `docs/research/functions/<ADDRESS>.md` by hand, then `kb check` and `kb index`), with a dated History line, in the same commit as the code. Decompiler output never crosses into the repo (ADR-0046).
- **Confidence is part of the fact.** CONFIRMED / STRONGLY INFERRED / HYPOTHESIS / UNKNOWN in prose; `[Dk2Behavior(Confidence.X, "docs/research/<note>.md#<section>")]` on the class that mirrors it. A thing you could not pin stays disabled in the UI and is written down as UNKNOWN with the address that would close it.
- **Tests first.** Each task's step 1 is a failing test in the `tests/DK2.<Assembly>.Tests` project; run it and see it fail before writing the implementation. Tests that need the install use `[InstallFact]`/`[InstallTheory]` and skip without `DK2_DIR` (ADR-0100). Never commit decoded game text, images or audio.
- **Numbers are taken last.** Before taking an ADR number, a Q number or a `HashVersion`, check master, `git worktree list`, and each worktree's `docs/DECISIONS.md`, `docs/research/open-questions.md` and `docs/research/hash-versions.md`. Nothing in this plan should move the hash; if a task finds it must, stop and report (that is a correct outcome).
- **The gate**, before every commit that touches code (from `docs/CODEX_TASKS.md`):

  ```
  dotnet build DK2.slnx -c Release
  dotnet test  DK2.slnx -c Release --no-build
  dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj -c Release
  dotnet run --project src/DK2.Tools -c Release --no-build -- kb check
  dotnet run --project src/DK2.Tools -c Release --no-build -- docs --check
  dotnet run --project src/DK2.Tools -c Release --no-build -- coverage --check
  dotnet run --project src/DK2.Tools -c Release --no-build -- verification-ledger --check
  ```

  Zero warnings on the builds; skips expected in the tests; a skip count of zero means the runner found game data it should not have. Run `powershell -File unity/sync-assemblies.ps1` before any Unity check. Unity's compiler is C# 9: no struct `with`, no file-scoped namespaces in `unity/` (see TRAPS).
- **Every task ends with a review and a documentation pass.** Review: apply `.claude/agents/reviewer.md`'s method to the whole diff with your own Ghidra copy; fix what it confirms. Docs: by `.claude/agents/documenter.md`'s file, rewrite every document the change made stale (the two plans above, HANDOFF, GOAL_ALIGNMENT rows 2 and 11 and gap 5, open-question rows, research notes, `unity/README.md`), *replacing* text rather than appending. AGENTS.md budgets apply to what you touch.
- **Owner questions are recorded, not guessed.** When a task needs an observable DK2 fact nobody has read, write one row in `docs/research/open-questions.md` (≤ 800 characters) and build the part that does not depend on it.
- **Shared memory.** At the start, `search_nodes` the Docker MCP memory graph for `front-end-plan` and `hud`; at the end of each phase, `add_observations` with the date, the commit, what is built and what is not.

## 2. Definition of done

A recorded Unity session (`DK2_INPUT_JOURNAL` set, folder under `D:\DK2Evidence\runs\unity-input-<date>\`) in which the owner, from Play with **Session Mode = Front End**:

1. sees the title screen, the two intro movies (skippable), path 250 and the main menu, with the hover and click sounds;
2. starts a new campaign, picks Level 1 on the map, reads the briefing, presses Go;
3. plays with the full HUD and hears the Mentor's first lines and the music start on the first fight;
4. opens Options: reads the objective, saves, restarts the level, loads the save, changes a slider on Game Options and sees it applied, and returns with Continue;
5. abandons through End Game and returns directly to campaign map37 without recording a result; separately loses and reaches statistics, and wins Level1 to see rewards, cutscene, statistics and hi-score entry (the original statistics-on-abandon criterion was corrected by A1's binary reading);
6. does all of the above at 1024×768 and at 2560×1440 with nothing smaller on screen than at 1024×768 (ART_DIRECTION §13).

Each of the six is a checkbox in Phase F's acceptance table with the journal folder beside it.

---

## Phase A — The in-game Options menu

Order: A1 (research), then A2–A6 in any order; A5 needs A4.

### Task A1: Read the Options menu's row handlers

**Research only; no code.** `ClassicOptionsLayout` (`src/DK2.Presentation/ClassicOptionsLayout.cs`) carries the seven rows' Text.str ids and rectangles but cites no function, and only Continue is wired. `docs/research/minimap-controls.md` "Options toggle" has the open/close (action 92, pause). The row destinations, settings/camera pages and quit confirmations are established in [options-menu.md](../research/options-menu.md). Remaining host effects and save/load callbacks are Q683.

**Files:**
- Modify: `docs/research/minimap-controls.md` (new section "Options rows") or a new `docs/research/options-menu.md` if the section passes 60 lines.
- Create/modify: `docs/research/functions/<ADDRESS>.md` for each handler read.

**Steps:**

1. Continue from the confirmed menu22 table `0x00696BE8`, widgets `0x006945C8` ([options-menu.md](../research/options-menu.md)); cross-check against the row ids below. The original discovery route was: start from the menu that `0x00416FD0` relays to (bottom-panel-controls.md), or search strings for Text.str ids 537, 1265, 143, 201, 1266, 1269, 142 in widget records. Record the menu id and table address.
2. Use [options-menu.md](../research/options-menu.md) for established row destinations, objective headers, confirmation actions and the settings/camera menu family. Finish Q683: trace actions1/102/125 to host/profile effects, and read menu23/24 list, load, name-entry and overwrite callbacks for slot count, file naming and pause/return behavior. Preserve the distinction between the native input-layer menu and the eventual host action.
3. Record each handler in the KB, write the section, and file an open-question row for anything observable but unread (for example whether End Game asks for confirmation).
4. Commit: `research: the in-game Options rows and where each leads`.

### Task A2: Level Objective from Options

**Files:**
- Modify: `src/DK2.Presentation/ClassicHudControls.cs` (add `ClassicHudAction.ShowObjective`; `ObjectiveVisible` state; closing rule from A1)
- Modify: `unity/DK2.Unity/Assets/DK2/DungeonRunner.Hud.cs:284-288` (route `ClassicOptionsAction.LevelObjective`), `unity/DK2.Unity/Assets/DK2/ClassicOptionsPanel.cs` (`SetSupported(LevelObjective, true)`)
- Modify: `unity/DK2.Unity/Assets/DK2/ClassicMessageTabs.cs:462` (`DrawObjective` becomes callable from the Options path)
- Test: `tests/DK2.Presentation.Tests/ClassicHudControlsTests.cs`

**Step 1 — failing test** (shape; adjust names to what A1 read):

```csharp
[Fact]
public void ShowObjectiveFromOptionsHidesOptionsUntilTheObjectiveCloses()
{
    var controls = new ClassicHudControls();
    controls.Apply(ClassicHudAction.ToggleOptions);
    controls.Apply(ClassicHudAction.ShowObjective);
    Assert.True(controls.ObjectiveVisible);
    Assert.False(controls.OptionsVisible);          // or True, if A1 read that menu33 overlays Options
    controls.Apply(ClassicHudAction.CloseObjective);
    Assert.False(controls.ObjectiveVisible);
}
```

**Step 2** — run: `dotnet test tests/DK2.Presentation.Tests -c Release --filter ShowObjectiveFromOptions` → FAIL (no `ShowObjective`).

**Step 3** — implement the state in `ClassicHudControls.Apply` exactly as A1 read it (which menus close, whether the pause holds). In `DungeonRunner.Hud.cs` route the click: `if (action == ClassicOptionsAction.LevelObjective) ApplyHudAction(ClassicHudAction.ShowObjective);`. Draw with the existing `DrawObjective`.

**Step 4** — run the test → PASS; run the gate; `sync-assemblies`; open Unity, Escape → Level Objective → the menu33 text.

**Step 5** — commit: `hud: Level Objective opens from the Options menu`.

### Task A3: Restart Level and End Game

**Files:**
- Modify: `src/DK2.Presentation/GameSessionRequest.cs` — add the factory the A1 reading needs, for example:

```csharp
/// <summary>Restart Level (A1's reading): the same campaign map again, same return page.</summary>
public static GameSessionRequest Restart(GameSessionRequest current)
    => new(current.Mode, current.LevelName, current.ReturnPage);
```

- Modify: `src/DK2.Presentation/ClassicHudControls.cs` (actions `RestartLevel`, `EndGame`, and a confirmation state if A1 read one)
- Modify: `unity/DK2.Unity/Assets/DK2/DungeonRunner.Hud.cs` (route both rows), `unity/DK2.Unity/Assets/DK2/DungeonRunner.LevelEnd.cs` (End Game's profile write: only what A1 read — if DK2 counts an abandoned level as an attempt, call `CampaignProgression.RecordResult(..., won:false)`; otherwise write nothing), `ClassicOptionsPanel.cs` (`SetSupported` for both)
- Test: `tests/DK2.Presentation.Tests/GameSessionRequestTests.cs` (new), `ClassicHudControlsTests.cs`

**Steps:** failing test for `Restart` preserving mode, level and return page → FAIL → implement → PASS. Failing test that `EndGame` while `InputSuppressed` is ignored (result already landed) → implement → PASS. In the runner, Restart calls `LoadLevel(GameSessionRequest.Restart(...))`; End Game calls `LoadLevel(GameSessionRequest.FrontEnd(frontEndReturnPage))` after the profile write A1 justifies. A level started from the Inspector (not the front end) restarts to itself and End Game returns to the front end with return page `None`. Gate, Unity check (Escape → Restart; Escape → End Game → statistics page or main menu as read), commit: `hud: Restart Level and End Game from the Options menu`.

### Task A4: Our own save file (F6, first half)

The simulation already serialises itself: `WorldSnapshot.Write/Read/ToBytes/FromBytes/LoadGame` (`src/DK2.Simulation/WorldSnapshot.cs`). What is missing is a *game* file that also carries what `0x0055B980` restores (campaign-map-and-progression.md §7): the campaign profile's per-player score blocks, Level Number, statuses 1–25, secret-level fields. Our profile is `CampaignProfile` (`src/DK2.Data/Campaign/CampaignProfileFile.cs`).

**Files:**
- Create: `src/DK2.Data/Save/SavedSession.cs` — a header (magic, version, level name, `GameSessionMode`, return page, UTC time, display name) + the snapshot bytes + the profile bytes, written through `CampaignProfileFile`'s existing serialiser. No new encoding: reuse `WorldSnapshot.ToBytes` and the profile writer.
- Create: `src/DK2.Data/Save/SavedSessionFile.cs` — `Save(path, SavedSession)`, `Load(path)`, `List(directory)` (headers only, no snapshot read), `SlotPath(directory, slot)` with the slot count A1 read.
- Test: `tests/DK2.Data.Tests/SavedSessionFileTests.cs`

**Step 1 — failing test:**

```csharp
[Fact]
public void RoundTripsHeaderSnapshotAndProfile()
{
    var session = new SavedSession(new SavedSessionHeader("Level1", GameSessionMode.Campaign, FrontEndPage.CampaignResults,
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), "First save"), snapshot: new byte[] { 1, 2, 3 }, profile: SampleProfile());
    string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    SavedSessionFile.Save(path, session);
    SavedSession back = SavedSessionFile.Load(path);
    Assert.Equal(session.Header, back.Header);
    Assert.Equal(session.Snapshot, back.Snapshot);
    Assert.Equal(session.Profile, back.Profile);
}
```

Note `GameSessionMode`/`FrontEndPage` live in `DK2.Presentation`, which `DK2.Data` must not reference. Store them as `byte`/`ushort` in the header and convert at the Unity boundary, or move the two enums to `DK2.Data.Model` (they are DK2 facts: game mode 5, page ids). Prefer the move; `EngineNeutralityTests` and `ArchitectureTests` will tell you if a reference goes the wrong way.

**Steps 2–5:** FAIL → implement → PASS; a second test that `List` reads headers without touching snapshot bytes (truncate the file after the header and assert `List` still returns it) → implement → PASS; gate; commit: `data: a saved session carries the world snapshot and the campaign profile`.

### Task A5: Save and Load from Options and from the front end's Load page

Native in-game Load/Save are menus23/24, both prepared by `0x0042A760`; their list/name/overwrite callbacks remain Q683. Read them before inventing slot counts or equating them to front-end page8; see [options-menu.md](../research/options-menu.md).

**Files:**
- Modify: `src/DK2.Presentation/ClassicHudControls.cs` (actions `OpenSave`, `OpenLoad`; the slot page state A1 read: slots, name entry, confirm overwrite)
- Create: `src/DK2.Presentation/SaveSlotsView.cs` — the slot list the Unity panel draws (slot index, display name, time, empty), built from `SavedSessionFile.List`; typing follows `HiScoreTable`'s name entry (already built for page 18).
- Create: `unity/DK2.Unity/Assets/DK2/ClassicSaveLoadPanel.cs` — UI Toolkit, same pattern as `ClassicOptionsPanel` (reference 1024×768, `PanelScaleMode.ScaleWithScreenSize`, `.bf4` text through `ClassicOptionsPanel.TextTexture`), art from the install as A1 read (`InstallHudSources`).
- Modify: `unity/DK2.Unity/Assets/DK2/DungeonRunner.cs` — `SaveSession()` = `WaitForTick()` then `WorldSnapshot.ToBytes(_simulation)` + the current profile → `SavedSessionFile.Save`; `LoadSession(path)` = `LoadLevel` with a new `GameSessionRequest.Resume(header)` and, on the new runner's start, `WorldSnapshot.LoadGame`/`FromBytes` instead of the level build, then `CampaignProfileFile.Save` of the file's profile (that is what `0x0055B980` does: a load overwrites campaign progress).
- Modify: `src/DK2.Presentation/FrontEndMenuController.cs:120-160` — page 8 (`FrontEndPage.Load`): list the same slots; its accept raises `LoadRequested(path)`; `DungeonRunner.FrontEnd.cs` turns it into `_pendingSession`. Dispatcher case 7 is the Load transition in front-end-menus.md §5; read page 8's widgets in the catalogue (`FrontEndPages.Catalogue.cs`) for the rectangles.
- Tests: `tests/DK2.Presentation.Tests/SaveSlotsViewTests.cs`, `FrontEndMenuTests.cs` (Load page lists slots and its accept raises the request), `ClassicHudControlsTests.cs`

**Steps:** failing tests for the view (empty directory → N empty rows; a saved file → its name and time in its slot) → implement → PASS. Failing test for the menu's Load page → implement → PASS. Unity: save in Level 1, Escape → Load → the slot → the same dungeon with the same tick; from the front end, Single Player → Load → the slot → the level. Verify the hash: after loading, `_simulation.World` hash equals the saved snapshot's recorded hash (WorldSnapshot stores it; assert in a Simulation test over `ToBytes/FromBytes` on a scenario if one does not exist already). Gate; commit: `save and load our own sessions from Options and the front end's Load page`.

### Task A6: Game Options from inside a level

Depends on Phase D (the settings record). Until D lands, keep the row dimmed. Route row1265 to in-game menu25 (title97), then menus26/27/29 and user-camera31/32, using [options-menu.md](../research/options-menu.md) as the layout/behavior contract. Reuse shared settings controls where they fit; the front-end pages15–17 are not the in-game page family. Preserve paused-level input ownership. Failing test: controls state, then Unity check, commit: `hud: Game Options opens the Options pages over a paused level`.

---

## Phase B — Front end: the loop a player actually walks

### Task B1: Play F5 in Unity, fix what breaks

Nothing in F5 has run in Unity. Before adding anything, play it and fix it.

**Files:** whatever breaks; expect `DungeonRunner.LevelEnd.cs`, `DungeonRunner.Finale.cs`, `ClassicFrontEndMenu.cs`, `FrontEndScrollBar`.

**Steps:**
1. `sync-assemblies`; Unity, runner **Session Mode = Front End**, `DK2_INPUT_JOURNAL` set to `D:\DK2Evidence\runs\unity-input-2026-10-29-f5\`.
2. Walk: main → Single Player → New Campaign → map → Level 1 → Go → play to a loss (let the Heart die) → Space → statistics → accept → map. Then win Level 1 (dig to the hero gate, or use the editor preview `DK2 / Preview win screen` if a win takes too long) → reward tablet → cutscene → statistics → hi-score entry → map → Level 2 unlocked.
3. For each failure: `superpowers:systematic-debugging`; a failing test where the defect is in Presentation; fix; gate.
4. Tick off the F5 "Still to see in play" list in `docs/plans/2026-10-23-front-end.md` (tablet upright, sparkles, scroll bar thumb, held arrow, glow linger, paragraph text absent). Write what you saw in the plan's status line; the owner's questions stay owner questions.
5. Commit per fix: `front end: <what was wrong>`. The journal folder's path goes into HANDOFF's item 6.

### Task B2: Page 26's paragraph text

**Research:** `0x0052A330` and the field at `+0x3118E` (F5 stage B left it unread). Read what text it is (the played level's briefing? a rank line?) and when it is drawn.

**Files:** `src/DK2.Presentation/CampaignResults.cs`, `unity/DK2.Unity/Assets/DK2/ClassicFrontEndMenu.cs`, test `CampaignResultsTests.cs`, KB record `0052A330.md`.

**Steps:** failing test for the text chosen for a sample result → implement → PASS → Unity check on page 26 → commit: `front end: page 26's paragraph text as 0x0052A330 draws it`. If the read shows nothing is drawn, record that and close the item.

### Task B3: Start-up: title screen, intro movies, attract mode (F7, second half)

Facts are in `docs/research/front-end-menus.md` §2 (all STRONGLY INFERRED): first front-end init shows `TitleScreen\TitleScreen` for 10 s, no input; then `BullfrogIntro.tgq`, then `Intro.tgq`, skip on Esc/Enter/Space/mouse; then path 250 and the main menu; after 180 s without mouse movement, on a non-multiplayer page, the two movies replay alternately, any key resets the timer.

**Files:**
- Create: `src/DK2.Presentation/FrontEndStartup.cs` — a small state machine `TitleScreen(10 s) → Movie(Bullfrog) → Movie(Intro) → Done`, wall-clock driven like `FrontEndSession`, with `Skip()`; and `FrontEndAttract` (idle timer, page filter, alternation).
- Modify: `src/DK2.Presentation/FrontEndSession.cs` — the intro path starts only when startup is `Done`.
- Modify: `unity/DK2.Unity/Assets/DK2/DungeonRunner.FrontEnd.cs` — before `_frontEnd.Update` runs, drive the startup: draw the title image (the `.444`/BMP reader from F1; which file is `TitleScreen\TitleScreen` in the WAD — front-end-assets.md §1.2) then `ClassicMoviePlayer` for each movie; a serialized **Skip Startup** switch on the runner for development, default off; the first init of the *run* only (a return from a level skips it, as `0x0074035C` does).
- Modify: `src/DK2.Presentation/FrontEndMenuController.cs` — Extras → View Movies (page 5, `FrontEndState.MoviesReady`, `FrontEndOperation.InitializeMovies`): list the install's `Movies/*.tgq` (front-end-assets.md §6.2) and play the chosen one; read page 5's widgets in the catalogue and `0x0053BCD0` for the list's source before assuming a layout.
- Tests: `tests/DK2.Presentation.Tests/FrontEndStartupTests.cs`

**Step 1 — failing tests:**

```csharp
[Fact]
public void TitleHoldsTenSecondsAndIgnoresSkip()
{
    var startup = new FrontEndStartup();
    startup.Skip();
    Assert.Equal(FrontEndStartupStage.Title, startup.Update(TimeSpan.FromSeconds(9.9)));
    Assert.Equal(FrontEndStartupStage.BullfrogIntro, startup.Update(TimeSpan.FromSeconds(10)));
}

[Fact]
public void AttractReplaysAfterThreeMinutesIdleAndAlternates()
{
    var attract = new FrontEndAttract();
    Assert.Null(attract.Update(TimeSpan.FromSeconds(179), FrontEndPage.Main, pointerMoved: false));
    Assert.Equal(FrontEndMovie.BullfrogIntro, attract.Update(TimeSpan.FromSeconds(180), FrontEndPage.Main, pointerMoved: false));
    Assert.Equal(FrontEndMovie.Intro, attract.Update(TimeSpan.FromSeconds(360), FrontEndPage.Main, pointerMoved: false));
    Assert.Null(attract.Update(TimeSpan.FromSeconds(540), FrontEndPage.Sessions, pointerMoved: false));
}
```

**Steps 2–5:** FAIL → implement → PASS; Unity: Play shows title, movies, skip works, menu follows; leave the mouse for three minutes and see the replay. Gate; commit: `front end: title screen, intro movies and attract mode`.

### Task B4: Credits and the DK3 trailer (cheap, optional)

Page 40 (`CreditsReady`) and `PlayTrailer`. Credits' content source is unread (`0x005306F0` case for Extras → Credits); read it first. The trailer is a movie file (front-end-assets.md §6.2). Build only if the read is short; otherwise leave dimmed and note the address. Commit: `front end: Credits and the trailer from Extras`.

---

## Phase C — Sound (F8)

Owner decisions (2026-10-23): decoded in C# at load time, in memory, no cache, nothing derived written to disk. Formats: `docs/research/front-end-assets.md` §5 (SDT container is read by `DK2.Convert.SdtArchive`, `src/DK2.Convert/SdtArchive.cs`; front-end/GUI effects MPEG Layer I, speech and music MPEG-2 Layer II; `BANK.map`/`SFX.map` layouts in §5.3, STRONGLY INFERRED, no reader). Music rules: `docs/research/music.md` (§8 has our pseudocode, §10 the unknowns). Speech: `docs/research/mentor-speech-and-notifications.md`.

`DK2.Convert` is a tool assembly; the runtime readers go in `src/DK2.Data/Media` beside the movie decoder, so Unity can load them. Move `SdtArchive` there (keep the old name as a forwarding type if `dk2tools` uses it) in C1.

### Task C1: MPEG Layer I and II decoding in memory

**Decided (owner, 2026-10-29): use NLayer** (MIT). Write the ADR ("MPEG audio is decoded by NLayer, in memory, at load time"), add the `PackageReference` to `src/DK2.Data/DK2.Data.csproj` (a netstandard2.0 build of NLayer that Unity can load; check with `ArchitectureTests`), add `NLayer.dll` as the ninth assembly in `unity/sync-assemblies.ps1` (copied from the NuGet cache beside the eight built DLLs), and record the licence in `THIRD-PARTY-NOTICES.md`. Wrap it behind our own `MpegAudioDecoder` type so nothing outside `DK2.Data.Media` names NLayer. Prove it first on `FrontEndHD` entry 0 (Layer I) and one `speech_mentor` entry (Layer II); if either fails to decode, stop and report rather than writing a decoder (the binary's decoder is a licensed library, not worth reading).

**Files:**
- Move: `src/DK2.Convert/SdtArchive.cs` → `src/DK2.Data/Media/SdtArchive.cs`
- Create: `src/DK2.Data/Media/MpegAudioDecoder.cs` (or the NLayer adapter) → `PcmClip Decode(ReadOnlySpan<byte> mpeg)` with `SampleRate`, `Channels`, `short[] Samples`
- Create: `src/DK2.Data/Media/SoundBank.cs` — loads one SDT archive and decodes each entry on first use (lazy, cached in memory for the session)
- Tests: `tests/DK2.Data.Tests/MpegAudioDecoderTests.cs`

**Additional C1 regression contract:** retain the last complete MPEG frame. Before comparing decoded sample count with SDT `+32`, resolve Q677's units and trimming; padded frame totals are not that field. The TODO is in `tests/DK2.Convert.Tests/SdtArchiveTests.cs`; see [front-end-assets.md section5.1](../research/front-end-assets.md#51-sdt-container-confirmed-bytes-and-sdtarchive).

**Step 1 — failing tests** (`[InstallFact]`; the first decodes a Layer I entry and checks sample rate, channel count and a duration computed from the frame headers in §5.1; the second a Layer II speech entry at 22,050 Hz mono; a third asserts no file is written under the install or the temp directory during decode).

**Steps 2–5:** FAIL → implement → PASS; a `dk2tools sound play-info <bank> <entry>` subcommand is optional for checking durations. Gate; commit: `media: MPEG Layer I and II decoding of SDT entries in memory (ADR-05xx)`.

### Task C2: `BANK.map` and `SFX.map` readers

**Files:**
- Create: `src/DK2.Data/Media/BankMap.cs`, `src/DK2.Data/Media/SfxMap.cs` with the layouts of §5.3 and native flags in [sound-effects.md](../research/sound-effects.md), section5. Keep SFX flags as u16 and parameter id separate; BANK bit0x20 and the SFX backend meaning remain Q684. Earlier OpenKeeper provenance stays in `THIRD-PARTY-NOTICES.md`; add the two file names without adopting its inferred meanings.
- Tests: `tests/DK2.Data.Tests/SoundMapTests.cs` (`[InstallFact]`): `front_end` names `Global\Gui` and `Global\FrontEnd`; group counts 4 / 18 / 13 for front_end / options / parchment as §5.3 states.

**Steps:** FAIL → implement → PASS → gate → commit: `media: read the sound bank and effect maps`. Where a field is unknown, name it `Unknown0x14` and say so in the XML comment; do not guess a meaning.

### Task C3: Front-end and HUD sounds

**Research first:** the front end's hover sound is id `0x30F` (`0x00532670`, front-end-menus.md §4); clicks and tab slides are named in `GuiHD` (§5.2: `FE3D TAB END`, `FE3D TAB SLIDE`, `FE3D CLICK ON T…`). How a sound *id* maps to an archive entry runs through the `SFX.map` groups: read the request path once (`0x005674F0`'s callers for a GUI sound, music.md §1–3 name the sound manager) so the id → group → variant → `(archiveId, index)` chain is a fact and not a guess. For the in-game HUD, read what the bottom panel's button press and the message tab's arrival play (`0x00416FD0`'s relay, bottom-panel-controls.md).

**Files:**
- Create: `src/DK2.Presentation/SoundRequests.cs` — a per-frame list of `SoundRequest(SoundId id, Vector3? world)` that the menu controller, HUD controls, bottom panel and message tabs append to; tested headlessly (hover → one 0x30F per entry, not per frame).
- Create: `src/DK2.Presentation/SoundResolver.cs` — id → `(bank, entry)` through the maps of C2.
- Create: `unity/DK2.Unity/Assets/DK2/ClassicSoundPlayer.cs` — an `AudioSource` pool; `AudioClip.Create` from decoded PCM (the pattern is in `ClassicMoviePlayer.cs:133`); 2D for UI, positioned for world sounds; volumes from Phase D's settings when they exist, 1.0 until then.
- Modify: `FrontEndMenuController.cs` (hover and press requests), `ClassicHudControls.cs`, `BottomPanelView.cs`, `MessageTabsView.cs`.
- Tests: `tests/DK2.Presentation.Tests/SoundRequestsTests.cs`, `FrontEndMenuTests.cs`

**Steps:** failing test "hovering a label raises 0x30F once until the pointer leaves" → implement → PASS; Unity: hover the main menu and hear it. Gate; commit: `sound: the front end's hover and click sounds and the HUD's button sounds`.

**Later world-sound queue:** parse the native 48 animation-mark pairs and route creature footsteps using [sound-effects.md](../research/sound-effects.md), sections1–4. Preserve phase crossing, cumulative thresholds and the native slow-frame throttle; do not import OpenKeeper voice/fade budgets (Q678) or unresolved final conversions (Q684).

### Task C4: The Mentor speaks

`docs/research/mentor-speech-and-notifications.md` §1–3: one line is one id in `Speech.str`, the `speech_mentor` bank and the notification table; §2 is the queue (one line at a time, what pre-empts what); §4–5 the taunt pool and idle mechanism. Script action 24 (a message with speech) is built as text (ADR-0535); its speech is this task.

**Files:**
- Create: `src/DK2.Presentation/SpeechQueue.cs` — the queue of §2 over `MessageTabsView`'s arrivals and the scripted message events; decides which entry plays and when; tested headlessly.
- Modify: `unity/DK2.Unity/Assets/DK2/ClassicSoundPlayer.cs` — a speech channel.
- Modify: the title card's 2 s hold in `DungeonRunner.Cutscene.cs` and the win screen's subtitle wait in `DungeonRunner.Finale.cs` become "until the line ends" (the plan's F8 placeholders).
- Tests: `tests/DK2.Presentation.Tests/SpeechQueueTests.cs`

**Steps:** failing tests from §2's rules (the second line waits; a higher-priority line pre-empts as the note says; a line does not repeat within its repeat gate) → implement → PASS; Unity: Level 1's opening Mentor lines play; gate; commit: `sound: the Mentor's lines play from the message queue`. Idle taunts (§5) are a second commit if §5's trigger is CONFIRMED; otherwise an open-question row.

### Task C5: Music

`docs/research/music.md` §8 is the pseudocode: a music level `L` raised by raids, battles and the Reaper, intensity 20/40/60/80, segments picked by `musicSFX.map`'s links with `SoundRand`, the ambience player, the one-shot atmosphere. §10 lists the unknowns; build what §8 states and leave §10's items as they are.

**Files:**
- Create: `src/DK2.Presentation/MusicDirector.cs` — `L`, `Intensity`, the segment picker over C2's `SfxMap` for the `music` bank (§6), fed by the simulation's existing events (fight start: Q666 says a fight not involving the player also starts it; raids: the states §2 names). It reads the world; it changes nothing (hash-neutral).
- Modify: `unity/DK2.Unity/Assets/DK2/ClassicSoundPlayer.cs` — a streamed music channel that queues the next segment before the current ends (`AudioClip.Create` streaming with a read callback, as the movie player does).
- Tests: `tests/DK2.Presentation.Tests/MusicDirectorTests.cs` — `Intensity` table; `Pick` with a fixed `SoundRand` sequence chooses only links whose range covers `i`; a stopped instance stays stopped until `i` is set again.

**Steps:** FAIL → implement → PASS; Unity: Level 1, the first fight starts the music; gate; commit: `sound: the music level, segment picker and ambience as music.md §8`. Options-screen music (`options_music`, `Track_1_4`) and the front end's music are UNKNOWN per §5.4; leave silent and keep the row.

---

## Phase D — Options pages bound to settings (F9)

`docs/research/front-end-menus.md` §6 lists every setting on pages 15 (Graphics), 16 (Sound), 17 (Control), its widget, its range and the registry key it wrote (STRONGLY INFERRED). Ours: a settings file beside the campaign profile, same field meanings; Direct3D-only values are dropped and the page says so.

### Task D1: The settings record and file

**Files:**
- Create: `src/DK2.Data/Settings/GameSettings.cs` — one record: `WaterTranslucency`, `EnvironmentMapping`, `Gamma (15..60)`, `ShadowLevel (0..3)`, `SoundQuality`, `Headphones`, `MasterVolume/SpeechVolume/MusicVolume/SoundEffectVolume (0..100)`, `SpeechSwitch/MusicSwitch/SfxSwitch`, `QSound`, `EnvironmentalEffects`, `MouseSensitivity (5..20)`, `GameSpeed (1..16)`, `ScrollSpeed (5..20)`, `InvertMouse`, plus our `UiScale`. Ranges are the §6 table's; defaults are DK2's where read (`0x0055B980` restores them from a save: read the default block there) else written as `HYPOTHESIS` in the XML comment with the address to read.
- Create: `src/DK2.Data/Settings/GameSettingsFile.cs` — load/save, missing file → defaults, unknown key → ignored.
- Tests: `tests/DK2.Data.Tests/GameSettingsFileTests.cs` (round trip; clamping to range; defaults on a missing file).

**Steps:** FAIL → implement → PASS → gate → commit: `data: the game settings record and file`.

### Task D2: Bind pages 15, 16 and 17

**Files:**
- Create: `src/DK2.Presentation/OptionsPagesController.cs` — slider/toggle/button state per widget from `GameSettings`; D0x1A capture-on-entry and D0x1C revert / D0x1B keep for Sound (§6, STRONGLY INFERRED; which icon is OK is HYPOTHESIS — read `0x0054B4B0`'s callers to settle it before building, or leave both buttons labelled by their Text.str and record the question); Graphics' apply-on-OK (`0x0054BB80`); Control's restore (D0x38) and write (D0x39).
- Modify: `FrontEndMenuController.cs:150-157` — the three pages' dispatch goes through the new controller before returning to Options.
- Modify: `unity/DK2.Unity/Assets/DK2/ClassicFrontEndMenu.cs` — draw sliders and toggles with the install's widget art (read the native draw callbacks of page 15–17 widgets from the catalogue; art names are in front-end-assets.md §1.2); apply `GameSpeed` to the runner's tick speed, `ScrollSpeed`/`MouseSensitivity` to `ClassicCameraControls`, volumes to `ClassicSoundPlayer`, `Gamma` to a URP colour adjustment, `UiScale` to the HUD `PanelSettings`.
- Tests: `tests/DK2.Presentation.Tests/OptionsPagesControllerTests.cs` (volume slider live-applies; cancel reverts to the entry capture; Graphics applies only on OK).

**Steps:** FAIL → implement → PASS → Unity: change Master Volume and hear it; set Game Speed and see the tick rate; gate; commit: `front end: the Options pages read and write our settings`. Then A6.

### Task D3: Keyboard Setup

Page 17's key list (`0x0054BD70`, capture `0x0054BD90`) rebinds DK2's 62 actions (`docs/research/controls.md`). Build the list and the capture only for actions we have (`controls.md` "What this means for us" table marks them); the rest show their names dimmed. Where DK2 persists a rebound keymap is open (controls.md "What is open" 2); ours goes in `GameSettings`. Commit: `front end: Keyboard Setup rebinds the keys we have`.

---

## Phase E — Step 6 leftovers

Additional researched presentation queue: Q676 Keeper Thunderbolt ribbon ([shot-effects.md](../research/shot-effects.md), section6); Q682 acceptance-aware refused-drop cursor feedback. This audit implements neither.

### Task E1: Full-map preview, markers, legend and hover symbols

`docs/plans/2026-10-17-classic-look-step6.md` lists markers as open; `ClassicFullMapLayout` draws terrain and navigation. Add the native live preview and legend using [minimap-controls.md](../research/minimap-controls.md#preview-and-legend-confirmed-binary-2026-10-29); test viewport scaling, continuous pointer mapping and fog behavior, then verify in Unity. The preview is a camera/render task distinct from GPU map markers. **Research:** the full-map draw (start from the M toggle's consumer in minimap-controls.md "Heart navigation and full-screen map" and `0x0041CC50`'s overlay arms recorded in minimap-rendering.md) for creature squares, legend entries and the hover symbol. Build each as a `MinimapMarker` kind over the existing GPU path, tested in `ClassicFullMapTests.cs`. Commit: `hud: the full map's creature squares, legend and hover symbol`.

### Task E2: The objects-only pick (PAGE UP, binding 26)

`docs/research/hand-cursor-rendering.md:163` and controls.md "What is open" 1: binding 26 (default PAGE UP, `0x005604C0`), held, sets the render sink's objects-only flag (`0x0059C130`), keeping creatures out of the pick. **Files:** `src/DK2.Presentation/HoverPickRules.cs` (an `objectsOnly` input), `PlayerInput.cs`, `DungeonRunner.cs` (the key, read from D3's bindings), test `HoverPickRulesTests.cs` ("with objectsOnly a creature under the pointer is skipped for the gold beneath it"). Commit: `hud: PAGE UP holds the objects-only pick`.

### Task E3: Drop our debug keys, add DK2's missing keys (owner decision, 2026-10-29)

controls.md's audit table: W/A/S/D, Q/E, `1 3 4 5 7` and `[ ]` are ours and collide with DK2's meanings (A is Ally Window, 1–7 are possession abilities; DK2 has no keyboard selection of rooms or tools at all). **Decided: drop ours and add DK2's.** Room and tool selection stay available through the HUD (bottom panel, Sell and Query corners) and the DungeonRunner Inspector for debugging.

**Files:**
- Modify: `unity/DK2.Unity/Assets/DK2/DungeonRunner.cs` and `DungeonRunner.Hud.cs` — remove the `1/3/4/5/7` tool keys, `[`/`]`, WASD and Q/E; remove the InputManager `Horizontal`/`Vertical` axis pan if it is what binds WASD.
- Modify: `src/DK2.Presentation/ClassicCameraControls.cs` — add the DK2 defaults the audit table marks "absent" or "keyboard bindings built; X/mouse absent": hold X + mouse to rotate, hold Z + mouse to zoom, P (Zoom To Portal), F, I (player information table, menu38; build the key only if the panel exists, else leave it out and say so), A (Ally Window: multiplayer only, leave out). Each default comes from `0x005604C0` (controls.md "The default key list"); its motion constants are unread (controls.md "What is open" 4), so read the scroll and rotation rates there or in classic-camera.md §7 before choosing a number, and mark anything measured rather than read as HYPOTHESIS.
- Modify: `unity/README.md` "Running it" — the controls paragraph says only DK2's keys; `docs/research/controls.md` audit table — rewrite the "ours" rows.
- Test: `tests/DK2.Presentation.Tests/ClassicCameraControlsTests.cs` (X held + pointer motion rotates; Z held + pointer motion zooms; P requests the Portal glide through the same consumer as H's Heart glide).

**Steps:** failing tests → implement → PASS → gate → Unity: the number keys do nothing, X/Z with the mouse rotate and zoom, P flies to the Portal → commit: `hud: DK2's camera keys replace the adapter's debug tool keys`. Phase D3's Keyboard Setup then lists these as the rebindable set.

### Task E4: Small hover-text and flower leftovers

Each is one read and one small change; do them together.

- Lord of the Land's level string (world-hover-text.md:67, 2866..2879 else 3079): read the arm, add to `WorldHoverText`, test, done.
- The "Enemy Wall" arm (tile `+0xB` bit0) reuses the existing Secret Door tile flag (ADR-0536); built without a simulation/hash change. The original blocker in this plan was stale.
- Status flowers §4: the casino Dollar mesh, the trigger window (action 30), state 0x110. Read; build the Dollar and the window if CONFIRMED; 0x110 stays open.
- Terrain occlusion of the pick and the Options-pause `+0x2722` special slot (already noted at `DungeonRunner.Hud.cs:192`): owner comparison only.

Commit: `hud: hover-text and flower leftovers from step 6`.

### Task E5: Owner comparisons that close the slices

Every Step 6 slice ends "owner comparison at 1024×768 and 2560×1440". None is closed. Prepare, do not perform: for each slice (minimap disc/frame/rotation, top bar, cursor and cage, bottom panel sectors, message panel, flowers, full map), one Unity capture at each size at a named Level 1 location, and the matching DK2 screenshot request, in a checklist `docs/research/hud-comparison-checklist.md` (≤ 400 lines) with the capture paths under `D:\DK2Evidence\captures\hud-2026-<date>\`. The owner compares; findings become Q rows or fixes. Commit: `docs: the HUD comparison checklist`.

---

## Phase F — Close the documents

Prepared on the integration branch after A–E; the owner merges each phase:

1. `docs/plans/2026-10-17-classic-look-step6.md`: status line rewritten to what is built; every `[ ]` either `[x]` or moved to an owner-comparison row. ADR-0497 is ready for the owner's acceptance decision after comparison; retain its proposed status until then.
2. `docs/plans/2026-10-23-front-end.md`: F6–F9 checkboxes; "After F5" line replaced by what is true; deferred list unchanged.
3. `docs/GOAL_ALIGNMENT.md` rows 2 and 11 and gap 5; `docs/HANDOFF.md` "Classic look" and "Next work" 6; `unity/README.md` "Running it" (the Options rows, save/load, sound, settings, startup switch).
4. The acceptance table (Definition of done, six rows) with the journal folder per row; the owner's gameplay feedback becomes the next plan.
5. Memory graph: `add_observations` on `front-end-plan` with the merge commit, what is built, what remains.

### Acceptance record

The integration branch `codex/hud-completion-final` was merged to master on 2026-10-30; the merged
Release suite passed 7,095 tests with 30 skips and the gates were clean.
Branch-time record:
The combined Release suite passes 7,072 tests with 30 expected skips; solution and
Unity adapter builds have zero warnings. Docs, KB (2,642 records), coverage and
verification ledger (336 rules) gates pass. Hash/save remains 332.

The clean integrated walkthrough is preserved at
`D:/DK2Evidence/runs/unity-input-2026-10-29-completion/20261029-221115-1d91224b/`:
`reports/completed.json`, nine sessions under `journals/`, and
`source-and-evidence-hashes.json`. Below, **C** refers to this run. It rejects
unexpected product exceptions; the known Editor Search indexing exception is
reported separately. The earlier model-flow run exposed missing gallery image
suffixes; the shipped-WAD regression fails before the fix and passes afterward.

Automated callbacks and labelled fixtures do not mark the owner's checkboxes.
The original-game comparison requests and capture paths have one home in the
[HUD checklist](../research/hud-comparison-checklist.md).

| # | Acceptance criterion | Automated verification | Owner acceptance |
|---|---|---|---|
| 1 | Title 10 s, both intros, menu and hover/click sound | C: real 10 s hold, both movie decoders/audio clips/repaints, gallery/attract and nonzero menu PCM | [ ] Play and hear |
| 2 | New campaign, Level1 map, briefing and Go | C: actual campaign consumers launch Level1 through map and briefing; API actions are labelled | [ ] Play |
| 3 | Full HUD, opening Mentor and first-fight music | C: opening speech PCM/subtitle and advancing music playhead/nonzero queue. Battle request is synthetic; first played fight remains open | [ ] Play and hear |
| 4 | Objective, save/restart/load, live settings and Continue | `build/hud-completion-a/build/asl-203817` checks both load hashes/profile/carry; `build/hud-completion-a/build/igo-212359` checks live sliders/key capture; final `build/isc-214759` checks scaled hits, saved/live audio state and rebound navigation; C checks save hash and comparative live GameSpeed ticks | [ ] Gameplay and native comparison |
| 5 | Abandon→map37; separately loss→statistics and win→rewards/movie/statistics/hi-score | C: labelled loss/win fixtures traverse rewards, cutscene, statistics, hi-score, Level2, restart and abandon→map37; no played victory claim | [ ] Play |
| 6 | 1024×768 and 2560×1440 sizing/readability | E5 checklist has both-size full-view captures; A6 checks1440p scrollbar; final scaled-hit probe checks100/125/capped200% | [ ] Compare both sizes |

ADR-0497 remains proposed until the owner accepts the comparisons. Credits,
unresolved page26 paragraph(Q685), native overwrite/disable policies(Q683),
sensitivity backend mapping(Q686) and unsupported settings/actions retain their
documented limits. Distant full-map preview lighting is not established by the
Heart-only fixture. The owner's next gameplay feedback defines the next plan.

Commit: `docs: step 6 and the front end are playable end to end; what remains`.

---

## Appendix — addresses this plan asks to be read

| Task | Address or record | For |
|---|---|---|
| A1 | the Options menu's widget table; rows' callbacks (find via Text.str 537, 1265, 143, 201, 1266, 1269) | what each row does |
| A1, A5 | `0x0055B980`, `0x0050ADE0` | what a load restores; the save page's slots and names |
| A3 | `0x0053C3C0` cases 1–4 | restart / next after a result |
| B2 | `0x0052A330`, `+0x3118E` | page 26's paragraph |
| B3 | `0x005340F0`, `0x0052F140`, `0x0053BCD0`, `0x0053C270`, `0x0053C2A0`, `0x00533D30`, `0x0052FAF0` | title, movies, skip, attract (already read; confirm nothing was missed) |
| B4 | `0x005306F0` Extras → Credits case | Credits' content |
| C3 | the GUI sound request path from `0x005674F0`'s callers; `0x00532670` (0x30F) | id → bank entry |
| C4 | mentor-speech-and-notifications.md §2's functions | the queue |
| C5 | `0x006163C0`, music.md §6 | segment picker |
| D1 | `0x0055B980`'s option block | settings defaults |
| D2 | `0x0054B4B0`'s callers | which Sound button is OK |
| E1 | the full-map draw from `0x0041CC50`'s overlay arms | markers and legend |
| E2 | `0x005604C0` (binding 26), `0x0059C130` | objects-only pick |
| E4 | world-hover-text.md:67 arm; status-flowers.md §4 | leftovers |

Nothing here is expected to move `HashVersion`. If a task finds it must (for example the Secret Door's tile flag for "Enemy Wall"), stop, write the row, and report.

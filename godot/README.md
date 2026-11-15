# The Godot project

`godot/DK2.Godot` is the Godot adapter that is to replace `unity/DK2.Unity` (ADR-0805). It holds
no DK2 behaviour: rules live in `src/`, and its HUD renderer (`HudCanvas`, Unity's counterpart of the
same name) draws `DK2.Presentation`'s `HudFrame`. The main scene shows the classic front-end menu
(`ClassicFrontEndMenu` over `FrontEndMenuHud`) on a black screen. Not wired yet: station scenery,
movies, sound and keyboard; the model disables the campaign, save and movie buttons, and the settings
pages open but their controls are inert. Like Unity's player it starts full screen (see *Verifying* for a window of a fixed size).

`InGameHud.tscn` is the in-game HUD, ported panel by panel (migration plan step 1b): it starts a campaign level as
Unity's `DungeonRunner` does and ticks it at the default game speed, over a black screen with no 3D world. Eight panels
are there so far: the hover HUD (`ClassicHoverHud` over `HoverHud`: the wooden bar under the other panels, the status
line and the delayed tooltip over them), which shows what the shared `HudHoverText` picks from the panels under the
pointer (the resource strip, the bottom panel, the creature overview, the message tabs, the battle tab; world hover needs
the 3D world and is not wired); the battle tab right of the message tabs (`ClassicBattleTab` over `BattleTabHud` and
`BattleTabView`, fed by the battle records of `MusicWorldObserver`: the crossed blades while one of the keeper's
creatures fights, the Call to Arms cancel, the alarm; the cancel's left click posts its command, while the blades' zoom,
the cancel's and the alarm's glides and the fly-in notice need the camera and the reward tablet and are not wired;
Level1 has no early fight, so the scene's `--stage-battle` argument spawns a hero Knight on the tile of the keeper's
first creature and lets the simulation start the fight); the
resource strip at the top left (`ClassicResourceHud` over `KeeperResourceHud`, fed by
`KeeperResourceView`: mana with its income and upkeep, gold, the Heart and payday gauges), which takes no input; the
bottom panel (`ClassicBottomPanel` over `BottomPanelHud`), whose clicks post the commands Unity's do; the creature
overview in its Creatures panel (`ClassicCreatureOverview` over `CreatureOverviewHud`, fed by `CreatureOverview` and
`InstallCreatureOverviewArt`: the worker column and a column per owned type, counts by mode, the mode buttons and page
strips, which change it; a press on a column or worker row raises the pickup or focus it asks for, which nothing handles
yet, because the hand and the camera are not ported); the creature query details (`ClassicCreatureQueryPanel` over
`CreatureQueryHud`, fed by `CreatureQueryView`: the queried creature's text, with its inert scroll bar when the text is
taller than the widget), which take no input; choosing a creature needs the query tool's world click, so the scene's `--query-first-creature` argument
queries the viewer's first owned creature instead); and the message
tabs above it (`ClassicMessageTabs` over `MessageTabsHud` and `MessageTabsView`: the level's messages sliding in, a left
click opening one in the message panel, a right click dismissing it, the Review Objective button's objective panel, the
level's indicators). The level's scripted view (the shared `ScriptedViewDirector`: opening camera path, HUD hiding,
FLASH_BUTTON, the resource flash), the tool a selection sets, camera glides (a message's goto button shows but moves
nothing) and sound are not wired, because the code that drives them is still in Unity's runner (plan
step 2). The options menu (`ClassicOptionsPanel` over `OptionsHud`) opens on Escape (`ClassicKeyboard` maps Godot physical keys to DirectInput scan codes, the inverse of Unity's `ClassicKeyBindings.Key`; the two tables change together) and pauses the level; its root, objective, exit and confirmation pages work, Save, Load and Game Options are greyed, and the session action after a confirmed restart, return or quit is dropped. `--open-options [Entry]` opens it at start and refuses an unsupported entry. The level start (`CampaignLevelData`), tick pacing (`TickPacer`), the panel's opening state, the stored-special
click, the message string tables (`MessageTextTables`), player names (`PlayerNames`) and which panel's hover text wins
(`HudHoverText`), the creature overview's install art (`InstallCreatureOverviewArt`), the query target and its details
(`CreatureQueryView`), the creature names it shows (`InstallClassicCreatureNames`), and the battle tab's place, hover
text and click actions (`BattleTabView.MenuTop`, `BattleTabHoverText`, `BattleTabView.Action`) are shared with Unity in
`DK2.Presentation`. Run it by naming the scene, with
optional user arguments after `--`:
`<console exe> --path godot/DK2.Godot res://InGameHud.tscn -- --level Level2 --seed 1` (defaults Level1, seed 1; add
`--query-first-creature` to show the query details, `--stage-battle` to show the battle tab, `--open-options [Entry]` to open the options menu at an entry).
Why the toolchain looks like this: ADR-0810. Agents working here: `.claude/agents/godot-developer.md`.

| File | Role |
|---|---|
| `godot-version.json` | the one pin: editor build and SHA-512, `Godot.NET.Sdk` version, Godot AI version and SHA-256 |
| `setup.ps1` | installs the pinned editor and the Godot AI addon, verified; warns on drift from the pin |
| `editor.ps1` | starts the pinned editor on the project, from the repository root |
| `DK2.Godot/` | the Godot project: `project.godot`, `DK2.Godot.csproj` (`ProjectReference`s into `src/`), scenes, C# |
| `DK2.Godot/Main.tscn`, `Main.cs` | the main scene: the front-end menu (`ClassicFrontEndMenu`) |
| `DK2.Godot/InGameHud.tscn`, `InGameHud.cs` | the in-game HUD over a running campaign level, no 3D world (see above) |
| `DK2.Godot/ClassicHoverHud.cs` | the hover HUD: `HoverHud` drawn by two `HudCanvas`es, top-docked, the bar at z index -1 and the text at +1 around the other panels, no input |
| `DK2.Godot/ClassicResourceHud.cs` | the keeper's resource strip: `KeeperResourceHud` drawn by `HudCanvas`, top-docked, no input |
| `DK2.Godot/ClassicBottomPanel.cs` | the keeper's bottom panel: `BottomPanelHud` drawn by `HudCanvas`, mouse forwarded |
| `DK2.Godot/ClassicCreatureOverview.cs` | the creature overview in the Creatures panel: `CreatureOverviewHud` drawn by `HudCanvas`, bottom-docked, presses forwarded without taking them from the panel |
| `DK2.Godot/ClassicCreatureQueryPanel.cs` | the creature query details: `CreatureQueryHud` drawn by `HudCanvas`, bottom-docked, no input |
| `DK2.Godot/ClassicBattleTab.cs` | the battle tab: `BattleTabHud` drawn by `HudCanvas`, bottom-docked, presses on a shown button taken and raised |
| `DK2.Godot/ClassicMessageTabs.cs` | the message tabs, message and objective panels, indicators: `MessageTabsHud` drawn by `HudCanvas`, bottom-docked, mouse forwarded |
| `DK2.Godot/HudCanvas*.cs` | the one `HudFrame` renderer every panel draws through |

## Setting up a machine

Needs the .NET 10 SDK (`global.json`; Godot 4.8 requires .NET 10) and, for Godot AI,
[uv](https://docs.astral.sh/uv/getting-started/installation/).

```powershell
powershell -File godot/setup.ps1     # editor into %LOCALAPPDATA%\Programs\Godot\<version>\, addon into DK2.Godot/addons/godot_ai/
powershell -File godot/editor.ps1    # open the editor
```

The game reads the original install in place: `DK2_DIR`, else the usual install locations
(`DK2.Data.Dk2Install`). The front end's replacement TrueType face is Bullfrog's DungeonKeeperII.ttf, which is not
ours to ship (download: see `unity/README.md`). It is read from an absolute path, never imported:
`DK2_REPLACEMENT_FONT`, else `DK2.Godot/Fonts/DungeonKeeperII.ttf` (gitignored), else the Unity project's
gitignored copy. Without it the menu draws DK2's BF4 bitmap font.

`-InstallRoot` or `DK2_GODOT_ROOT` puts editors elsewhere. Each version gets its own folder, so
an upgrade never touches the version you are leaving. Export templates are not installed yet.

## Connecting Claude Code and Codex (Godot AI)

[Godot AI](https://github.com/hi-godot/godot-ai) (MIT) is an editor addon plus a Python MCP server
that lets an agent drive the open editor. There is no Claude or Codex marketplace plugin for it;
its own channel is the dock, which writes each client's MCP entry with a version-pinned
`uvx ... godot-ai==<version> godot-ai attach` command and re-pins it on every update. On
Windows that entry holds machine paths (pythonw, uvx), so it is never committed.

Once per machine (and once per worktree, for Claude Code), in the editor started by `editor.ps1`:

1. **Editor → Editor Settings**, filter for `mcp_client_scope` (turn on *Advanced Settings* if it
   is hidden), set **Plugins → Godot AI → Mcp Client Scope** to `local`. `editor.ps1` starts
   Godot in the repository root because the dock registers against the editor's working directory.
2. **Godot AI dock → Clients & Tools → Clients tab → Configure** next to *Claude Code* and
   *Codex*. Codex's entry goes to `~/.codex/config.toml`, where this repo's other Codex MCP
   servers already live. Restart both clients.

Telemetry needs no step: `editor.ps1` sets `GODOT_AI_DISABLE_TELEMETRY`, so Configure writes
`--disable-telemetry` into each entry; an editor started any other way writes entries without it.
Leave the dock's *Developer mode* off. Check an entry with `claude mcp get godot-ai` from the
repository root (the dock may show Claude Code red anyway; `docs/TRAPS.md`).

Configure deletes `godot-ai` entries from every scope first, including a checked-in `.mcp.json`;
keep Godot AI out of the repository's `.mcp.json`. Its C# support is text-only: it writes `.cs`
files but does not compile them, so build with `dotnet`.

## Verifying

```powershell
dotnet build godot/DK2.Godot/DK2.Godot.csproj                               # compile, no editor needed
powershell -File godot/editor.ps1 -Console --headless --import               # import; errors print here
powershell -File godot/editor.ps1 -Console --headless --build-solutions --quit
powershell -File godot/editor.ps1 -Console --headless --quit-after 10        # run the main scene
```

The last prints `DK2.Godot front end: page Main` once the menu is up; with `res://InGameHud.tscn` added it runs the
in-game HUD instead and prints `DK2.Godot in-game HUD: Level1 seed 1`. `--headless` has no renderer; to see a
frame, run the console build without it, e.g. `--path godot/DK2.Godot --write-movie <dir>/f.png --fixed-fps 30
--quit-after 120` (Movie Maker writes every frame as a PNG; keep the output outside the repository).
`editor.ps1` adds `--editor` to any run without `--headless`, so call the pinned console executable directly for this.
The window takes the desktop's size and `--windowed`/`--resolution` do not change it (`docs/TRAPS.md`); for a
1920x1080 capture put a temporary `DK2.Godot/override.cfg` holding `[display]` / `window/size/mode=0` beside
`project.godot`, and delete it afterwards.

## Upgrading Godot

1. Take the new build's file name and SHA-512 from the release's `SHA512-SUMS.txt`
   (`github.com/godotengine/godot-builds/releases/<version>`), and the matching `Godot.NET.Sdk`
   version from NuGet. Update `godot-version.json`.
2. Run `setup.ps1`, then `editor.ps1`. On a new minor version the editor offers its project
   upgrade tool and rewrites the `Sdk` in `DK2.Godot.csproj` and `config/features` in
   `project.godot`; accept it. `setup.ps1` warns until the csproj and the pin agree.
3. Run the verification above and commit the pin with whatever the editor rewrote, nothing else.
4. Read the release's *breaking changes* list (the `breaks compat` label on the milestone).

Godot AI: press **Update** in its dock (signature-checked, re-pins both clients), then set
`godotAi` in `godot-version.json` to the new version and archive SHA-256. Never extract a release
over an existing `addons/godot_ai/`.

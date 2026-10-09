# Blender MCP

Live control of a running Blender from Claude Code and Codex through
[mcp-for-blender](https://github.com/ahujasid/mcp-for-blender) (formerly `blender-mcp`). An agent
inspects the scene, looks at it, and runs Blender Python in the open session. It complements the
headless scripts in this folder (`README.md`); it does not replace them. Asset work follows
`docs/ART_DIRECTION.md` and `docs/ASSET_PIPELINE.md`.

## Versions (verified 2026-10-09)

| component | version | where |
|---|---|---|
| MCP server | `mcp-for-blender` **2.1.9** (PyPI, 2026-10-06), pinned | run by `uvx` on demand |
| Blender add-on | "MCP for Blender" `bl_info` 1.8, protocol **13** (bundled with 2.1.9) | `%APPDATA%\Blender Foundation\Blender\5.2\scripts\addons\blender_mcp.py`, enabled in user preferences |
| Blender | 5.2.2 LTS (the project's pinned build, `README.md`) | `F:\blender-5.2.2-windows-x64\blender.exe` |
| uv / uvx | 0.11.12 | `%USERPROFILE%\.local\bin` |
| Claude Code | 2.1.185 | |
| Codex CLI | 0.162.0-alpha.17.2, bundled in the VS Code extension `openai.chatgpt` 26.1007.21434 | `bin\windows-x86_64\codex.exe` in the newest `%USERPROFILE%\.vscode\extensions\openai.chatgpt-*` folder (not on PATH) |

The server checks the add-on's protocol on connect; `get_addon_status` reports `up_to_date`.

## Configuration

Both clients start the same pinned server against the same endpoint, `localhost:9876`, with
telemetry off:

- **Claude Code**: `blender` in the repository's `.mcp.json`, approved by `enabledMcpjsonServers`
  in `.claude/settings.json`. No tool is pre-allowed; Claude asks before each call.
- **Codex**: `[mcp_servers.blender]` in `%USERPROFILE%\.codex\config.toml` (user-level, beside
  `pyghidra-mcp` and `dk2kb`):

```toml
[mcp_servers.blender]
command = "uvx"
args = ["mcp-for-blender@2.1.9"]
startup_timeout_sec = 120

[mcp_servers.blender.env]
BLENDER_HOST = "localhost"
BLENDER_PORT = "9876"
DISABLE_TELEMETRY = "true"
```

Codex asks before each MCP call. To pre-approve a read-only tool, add
`[mcp_servers.blender.tools.get_scene_info]` with `approval_mode = "approve"` (likewise `look`,
`get_addon_status`); never pre-approve `execute_blender_code`.

**Local only.** `DISABLE_TELEMETRY=true` stops even the anonymous usage record; content telemetry
is opt-in and off. Poly Haven, Sketchfab, Poly Pizza, Hyper3D, Hunyuan3D and Tripo are off by
default (the sidebar checkboxes; `get_addon_status` lists them): leave them off. `search_assets`,
`import_asset` and `generate_3d` then answer that the integration is disabled, and nothing is
fetched or uploaded. `BLENDER_MCP_SAFE_MODE` is not set because export
scripts need file writes.

## Starting

1. Launch Blender with its GUI: `F:\blender-5.2.2-windows-x64\blender.exe`. The add-on starts its
   socket server automatically. It refuses to run under `--background`, so headless runs, such as
   the other scripts here, never open the port.
2. Check: `Test-NetConnection localhost -Port 9876`, or in the 3D viewport press **N** and open
   the **MCP for Blender** tab (**Connect to MCP server** if it is stopped).
3. Start the agent session. Each session spawns its own server, which connects when it starts.
   A session that started before Blender connects on its first tool call. A session that started
   before this configuration existed has no `blender` tools: start a new one.

## Concurrency: one driver at a time

Configure both clients, but let one agent drive Blender at a time. The add-on accepts several
connections and runs every command in order on Blender's main thread. Two servers each made ten
interleaved calls on 2026-10-09, and every reply reached the right client. Both connections still
share one scene, one selection, one undo stack and one viewport, so two agents editing at once
overwrite each other's state, and upstream advises one server. Disconnecting is not required: the
idle client simply makes no `blender` calls. To be sure, disable the server for that session
(`/mcp` in Claude Code, or remove the client's `blender` entry).

## Working with it

- **Look while you work.** During interactive modelling or review, inspect with `get_scene_info`
  and check every change visually with `look` (`viewport`, `camera`, `angles`, solid, material,
  wireframe or X-ray). Do not trust an edit you have not looked at.
- **Anything repeatable belongs in the repository.** Generation, validation and export go in
  scripts under `tools/blender/` and run headless (`README.md`), with output under `build/`. Use
  MCP to explore and to check a result, then put the steps in a script. An `execute_blender_code`
  sequence that nobody committed is lost.
- **Protect open work.** `execute_blender_code` runs arbitrary Python in the live session. Ask
  the owner to save before you edit their file. Test in a new scene or a new unsaved file, and
  never save a file you did not create.

## Verification (2026-10-09)

Against a fresh GUI Blender with its unsaved startup file:

| check | result |
|---|---|
| server over stdio (scratch MCP client) | connects; lists `get_addon_status`, `get_scene_info`, `execute_blender_code`, `look`, `generate_3d`, `search_assets`, `import_asset`, `disable_telemetry`, `record_trajectory_feedback` |
| inspect and capture the current scene | `Scene`: Cube, Light, Camera; viewport PNG returned |
| disposable scene | new scene `MCP_Smoke` with `MCP_Smoke_Cube` created, inspected, captured (`look` angles, 4 views), then removed with its mesh, material and scene; original scene restored, file still unsaved |
| Claude Code (`claude -p`, a separate headless session in this repository) | `blender` connected; `get_addon_status` and `get_scene_info` answered |
| Codex (`codex exec`, a separate headless session) | server started; calls denied until approved (see below); with per-run approval both answered |

## Troubleshooting

| symptom | cause and fix |
|---|---|
| `install-addon` prints a `UnicodeEncodeError` (`\u2192`) traceback | the file was copied, and only the cp1252 console print failed; set `PYTHONIOENCODING=utf-8` and rerun to see the message |
| `setup` finds no Blender or no Codex | the Blender here is a portable install, and Codex is the VS Code extension's CLI; install with `install-addon` and edit the client configs as above |
| `codex exec`: "MCP tool call requires approval, but approval policy is never" | headless Codex never prompts; pass `-c 'mcp_servers.blender.tools.<tool>.approval_mode="approve"'` for the read-only tools in that run |
| auto-start skipped, "port 9876 is already in use" | another Blender already serves the port; use that one, or set a different port in the sidebar and in both configs (`BLENDER_PORT`) |
| no `blender` tools in a session | the session predates the configuration or Blender; start a new session, or reconnect through `/mcp` |

**Upgrading.** Change the pin in `.mcp.json` and in the Codex config together, run
`uvx mcp-for-blender@<version> install-addon` with `PYTHONIOENCODING=utf-8`, restart Blender, start
new agent sessions, and update the versions table.

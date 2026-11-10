# Context-mode through Docker MCP

Runs upstream [context-mode](https://github.com/mksglu/context-mode) 2.1.7 in
the Docker MCP `local` profile. The image has Node.js 24 (2.x requires >= 24.15),
Python 3, Bash, Git and ripgrep. It does not include .NET, Unity or Ghidra.

## Build and register

From the repository root, with Docker Desktop's Linux engine running:

```powershell
powershell -NoProfile -File tools/context-mode/Deploy.ps1
python tools/context-mode/smoke.py
```

`Deploy.ps1` accepts `-Profile` and `-ProjectPath`. It builds the local image,
copies the server definition to a new uniquely named file under
`~/.docker/mcp/catalogs/`, adds it to the profile, and sets its project mount.
It restricts this server to the five analysis tools listed below through the
profile's tool allowlist; YAML tool metadata alone does not restrict discovery.
Upstream 2.x also ships `ctx_resume`, `ctx_doctor`, `ctx_upgrade` and `ctx_purge`;
they are deliberately not enabled (session resume is not integrated, upgrade must
not run in a container, purge deletes the shared index). Re-running the script
replaces the existing registration, so it is also the upgrade path. Existing
catalog files are preserved. The upstream package version and base
image digest are pinned; Debian packages are resolved at build time (2.x has no
npm dependencies). Nothing is pushed to a registry.

2.x renamed or merged the 1.x tools: `ctx_execute_file` is now `ctx_execute` with
a `file` argument (the path reaches the code as `CTX_FILE`, not `FILE_CONTENT`), `ctx_batch_execute` is `ctx_batch` (commands are plain strings),
and `ctx_fetch_and_index` is `ctx_index` with a `url` argument (the index label is
the URL itself, not `source`). 2.x also needs an agent id and a trusted `/workspace`;
the image sets `--agent codex` and a system `safe.directory` for that reason.

The upstream dependency retains its
[Elastic License 2.0](https://github.com/mksglu/context-mode/blob/main/LICENSE).
This directory contains packaging and verification code, not vendored upstream
source. Rebuild the image to upgrade; do not use the upstream upgrade tool inside
a running container.

## Connect an agent

The client launches `docker mcp gateway run --profile local`. Its gateway process
must have `MCP_GATEWAY_DOCKER_BIND_ALLOWED_PATHS` set to the absolute host project
folder. This authorizes read-only mounts; do not set the writable equivalent.
For example, the project Codex configuration is:

```toml
[mcp_servers.MCP_DOCKER.env]
MCP_GATEWAY_DOCKER_BIND_ALLOWED_PATHS = 'D:\DungeonKeeperRemake'
```

For Claude Code, the project `.mcp.json` provides the same environment variable
on its `MCP_DOCKER` server. Restart/reconnect the client after changing gateway
environment or adding this server. A gateway already running with an older
configuration may reject dynamic activation of a newly registered server.

Call `ctx_execute`, `ctx_batch`, `ctx_index`, `ctx_search` and `ctx_stats`
through `MCP_DOCKER`.
The smoke test supplies the read-only allowlist to its own gateway process;
its success does not establish that an already-running client has reconnected.

## Files and persistence

The selected host project is mounted read-only at `/workspace`; use container
paths in tool arguments. The named volume `dk2-context-mode-content` holds
`/data`, including the context-mode index. Other host folders, evidence,
credentials, agent transcripts and the Docker socket are not mounted.
Execution can write temporary files and index data, and fetch public URLs.
It cannot modify the mounted project. Windows-specific tools remain on the host.

The index is shared by clients using this profile. This is an analysis/index
service, not automatic conversation memory: host session hooks and session
database synchronization have not been integrated. The Docker Memory server
continues to hold deliberate cross-agent observations. Do not point host hooks
at this volume or describe session capture as working without a separate test.

Upstream performs age-based cleanup of stored content; the volume is a cache,
not an archive or a home for irreplaceable evidence. Do not remove it during
routine cleanup.

## Verification

`smoke.py` speaks MCP JSON-RPC to the actual Docker gateway. It checks tool
discovery, JavaScript/Python/shell execution, file processing, a read-only project
mount, batch execution, indexing/search, HTTPS fetching, stats, and search after
a gateway restart. It adds uniquely labelled synthetic index content and a small
example.com fetch; it does not clear the index or write into the repository.

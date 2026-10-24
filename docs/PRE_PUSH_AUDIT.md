# Pre-push audit

Snapshot: 2026-10-24, `master` at `be0d879d1f5cc7b27027e8c2281a597f107274b9`,
before the README and this report were added. No remote was configured. This is
a publication review of `master`, not a claim about every local Git reference.

## Inventory

| Item | Result at the audited commit |
|---|---|
| Tracked files | 4,158, totaling 22,439,294 bytes |
| Main directories | `docs/` 2,611; `src/` 617; `tests/` 544; `unity/` 193; `tools/` 127; `scenarios/` 43 |
| Existing worktree changes | None |
| Existing untracked, non-ignored files | None |
| Ignored files | 49,687; primarily Unity caches, build output and research material |
| Tracked files matching ignore rules | None |
| Tracked symlinks or submodules | None |
| Largest tracked file | `docs/DECISIONS.md`, 1,095,945 bytes |
| Reachable history | 1,745 commits; 20,182 distinct blobs, 1,106,495,261 uncompressed bytes |

The historical byte total sums all distinct blob versions; it is not the push
size. The largest historical blob is also 1,095,945 bytes. No tracked or
historically reachable filenames matched the audit's executable, archive,
original-game asset, font, key-store or native-capture extension checks.
Only the explanatory READMEs under `original-reference/` and
`reverse-engineering/` appear in the inspected `master` object-path inventory.

## Findings to resolve or acknowledge before publication

### Commit citations still use the pre-rewrite hashes

The assistance-credit rewrite changed commit IDs but preserved file contents.
A scan matched 217 old commit-hash references across 73 tracked files to commits
in the backup history. For example, `docs/DECISIONS.md` cites `af4038d`, whose
rewritten counterpart is `e1c6c67`. The mapping was matched using identical
tree, author time, committer time and subject, with unique keys in both histories.
These citations need migration or an intentional archival strategy before
publishing a self-contained research history. The local backup makes old IDs
resolvable here; a fresh clone of only rewritten `master` will not have that
history. This audit leaves the wider citation migration unchanged.

### Native world logs are already tracked

- `docs/research/samples/worldlog-envmapshowcase-gt231.log` (606,516 bytes) is an
  outside contributor's native world dump and includes a Wine home-directory
  path in its header.
- `docs/research/samples/worldlog-owner-4dumps.log.gz` (300,581 compressed bytes)
  contains the owner's native game dumps and installation path.

These are original-game output, rather than C# source or decompiler text. Their
provenance is documented in [debug-dumpers.md](research/debug-dumpers.md), and
research/function records link to them. Decide whether their contents and
third-party provenance are suitable for the intended remote audience. This
audit does not establish permission to redistribute the outside contribution.
No same-named copies were found among 5,314 files under `D:\DK2Evidence`;
equivalent copies under other names or in archives were not established.

The logs are present in the audited commit and its history: removing or
ignoring their current paths would not remove them from a push of that history.
At the final worktree check, both paths appeared as unstaged deletions outside
this audit's edits. Those deletions were left untouched and excluded from the
documentation commit. This audit did not move, rename, overwrite or delete evidence.

### Developer-specific configuration is tracked

[.mcp.json](../.mcp.json) points to scripts under the absolute checkout path
`D:/DungeonKeeperRemake` and selects the `claude` Ghidra agent. It also configures
a local Blender connection. There are no credential values in this file, but
it requires local adaptation on other machines. [.claude/settings.json](../.claude/settings.json)
also tracks tool permissions and plugin choices. Review whether these should
be shared project defaults or documented examples before inviting contributors.

### Ignored research files need preservation, not staging

The ignored tree contains native research CSV/log material and archived work,
including `reverse-engineering/imp-store-full-20261017/`,
`reverse-engineering/imp-merge-acceptance-20261017/` and
`reverse-engineering/worktree-archive/`. Ignored build directories also contain
research runs and converted assets. These files will not accompany a normal
push, and their external-copy coverage was not verified by this audit.
`D:\DK2Evidence` exists; follow [the evidence custody rules](../AGENTS.md)
before any cleanup. Do not force-add these directories for publication.

### Choose the reference being published explicitly

The original pre-rewrite history remains under
`refs/backup/master-before-assisted-by-20261024-b8961930`. Local stash and app
checkpoint references also exist and were outside the content scan's scope.
The rewritten `master` has no `Co-Authored-By:` trailers. Publish the intended
branch explicitly; a mirror push would also publish backup, stash and app
references. Personal author/committer email metadata is present in history.

### Some contributor documentation remains stale

[unity/README.md](../unity/README.md) tells normal play to leave Native Initial
Ambient off, while the saved scene enables it and [HANDOFF.md](HANDOFF.md)
records that as the owner's selected setting. The hook's introductory comment
also says there is no CI, although `.github/workflows/ci.yml` exists. These were
not changed as part of the root README update; the README avoids recommending
the contradictory ambient setting.

## Checks and limits

The working files and every distinct blob reachable from `master` were checked
for common private-key, GitHub, AWS, OpenAI, Slack and Google credential
signatures, credential-bearing URLs and quoted credential assignments.
Historical gzip content and commit messages were also inspected. No matches
were found. A scan for dense Ghidra-generated local-name patterns also found
no candidates. These are heuristic checks, not proof that all sensitive data
or proprietary text is absent. Gitleaks and TruffleHog were not installed.

The root README did not exist in the checkout and had no matching history under
the common README names queried. The new [README](../README.md) documents the
development status, build/test workflow, required original data, headless
runner, Unity scene and contributor references. Its commands and links are
checked against repository sources rather than assumed from older prose.

Verification used the existing Release binaries; no full build, test suite or
Unity session was run for this documentation-only change:

- `docs`: `no budget violations in what changed since HEAD.` (exit 0).
- `kb check`: `kb check: 2307 records, no findings.` (exit 0).
- Headless scenario listing succeeded without game data (42 entries).
- Running the documented synthetic scenario with an invalid `DK2_DIR` returned
  the expected missing-install error (exit 3), confirming the stated requirement.
- Every local Markdown link in both new documents resolves.
- The independent read-only reviewer found no defects in either document,
  including the old-hash citation finding.

This audit changed no game behavior or original-game evidence. No remote was
added and nothing was pushed.

---
name: ghidra-queries
description: Query DKII.EXE (the original Dungeon Keeper 2 binary) headlessly with this repo's Ghidra scripts - who reads or writes a field offset, who calls a function, where an address or constant appears, disassembly or decompile of a range, caller trees, vtable slots, RTTI, dispatch tables. Use whenever a DK2 behaviour question needs the binary checked, before filing anything as UNKNOWN, and before writing a new Ghidra script.
---

# Ghidra queries against DKII.EXE

The binary is the primary source (AGENTS.md, *Existing research*). This skill
is the repeatable part: one runner, a catalogue of parameterised scripts, and
the traps that have already cost committed wrong claims.

## The rule first (ADR-0046)

- **Every output file goes under `reverse-engineering/` (gitignored) or your
  scratchpad.** Never under `docs/`, `src/`, `tests/` or `tools/`.
- Decompiler output never crosses into the repository, reformatted or not.
  What may cross is a *fact*: a number, an offset, a formula, an order of
  operations, written in our own words in `docs/research/`, with provenance
  (function address, what was read).
- RTTI class names (`Rtti.py`, `RttiFind.py`, `NewSizes.py`) stay local too:
  only addresses, offsets and "same class or not" may leave.
- `git status --short reverse-engineering/` must stay empty.

## Before running anything

1. **Grep the repo first.** `docs/research/` and `docs/research/open-questions.md`
   often answer an offset or mask under a different name.
2. **Know whose project you open.** Claude uses
   `D:/Ghidra/ghidra projects claude/DK2_RE`, Codex uses
   `D:/Ghidra/ghidra projects/DK2_RE`. Never open the other agent's project.
3. **One run per project at a time.** Batch the steps into one call instead of
   running several calls in parallel.

## The MCP server (pyghidra-mcp)

Since 2026-09-30 the same binary is also served over MCP by
[pyghidra-mcp](https://github.com/clearbluejar/pyghidra-mcp) (headless,
PyGhidra, installed with `uv tool install pyghidra-mcp`). It answers the quick
questions - decompile one function, list its xrefs, disassemble a range, search
strings or symbols, walk a call graph - without a 15 s headless start, and the
runner above stays the tool for anything scripted or bulk.

- **Nobody starts it by hand.** The client runs
  `python tools/ghidra/mcp-serve.py --agent <you>` over stdio when a session
  opens and closes it when the session ends: Claude from the repo's
  `.mcp.json`, Codex from `[mcp_servers.pyghidra-mcp]` in
  `~/.codex/config.toml`. The first tool answer comes after about 8 s of JVM
  start-up (Claude's start-up timeout is raised to 120 s in
  `.claude/settings.json`); a lock left by a killed session does not block the
  next start. `uv run tools/ghidra/mcp-check.py <agent>` says PASS or FAIL in
  about ten seconds, on its own copy, so it never collides with a live session.
- **One server per agent, on a copy of that agent's project.** The server
  holds its project open for its whole life, so it runs on
  `D:\Ghidra\mcp\<agent>\DK2_RE` and never blocks the runner. Two sessions
  of the same agent cannot both hold the copy: the second one's MCP fails to
  connect, and the runner still works. The launcher refreshes the copy at
  start-up whenever the real project's database changed, so names given
  through `--write` runs or the GUI arrive at the next session by themselves.
- **The copy is a reading surface.** `rename_function`, `set_comment` and the
  other mutation tools change the copy only, and the next refresh replaces it.
  Real naming goes into the agent's own project. `delete_project_binary` and
  `import_binary` are denied to Claude in `.claude/settings.json`; the
  read-only tools are pre-approved there. Codex has no per-tool filter: do not
  call those two.
- **Build the semantic index once.** `search_code` runs on a chromadb index
  next to the copy. While it is incomplete every session start embeds the
  whole binary in the background, which slows every other Ghidra start on the
  machine and holds shutdown until the in-flight work is done. Run
  `python tools/ghidra/mcp-serve.py --agent <you> --index-only` once (and
  with `--reindex` after many renames); a complete index is skipped at start.
- **Big functions do not fit.** Claude truncates a tool result above its MCP
  output limit; a dispatcher of several thousand lines will be cut. For those
  use the runner with an output file and read it with a filter, as below.
- For the CodeBrowser in the same JVM, or one server shared by several
  sessions of one agent, run the HTTP variant by hand:
  `python tools/ghidra/mcp-serve.py --agent <you> --http [--gui]` (ports
  8000/8001) and point the client at `http://127.0.0.1:<port>/mcp`.
- **ADR-0046 applies unchanged.** What the MCP returns is decompiler output:
  it stays in the conversation or under `reverse-engineering/`, never in
  `docs/`, `src/`, `tests/` or `tools/`. Facts cross; text does not.
- The binary's name in every tool call is `DKII.EXE`. The install is pinned
  (`uv tool install pyghidra-mcp==0.2.7`); the project is beta, so upgrade on
  purpose, then run `mcp-check.py`.

## Running

```
python tools/ghidra/ghidra.py --agent claude|codex <Script> <args...> [:: <Script2> <args...>]...
```

- Steps separated by a standalone `::` run in order in **one** Ghidra start
  (about 15 s of start-up, paid once). Batch every read a question needs.
- The run is `-readOnly` by default. A `ForceAt`/`ForceGaps`/`DecompLoose`
  change still applies to later steps in the same run, but is not saved.
  `--write` saves it; use that only in your own project, and only when you
  mean it.
- **The output path is always the first argument, and must be absolute**
  (Ghidra's working directory is not yours). Put it in your scratchpad or
  `reverse-engineering/`.
- Addresses are hex, with or without `0x`.
- Only the scripts' own lines (`WROTE ...`) and errors are printed. The full
  log is in `reverse-engineering/.cache/ghidra-<agent>.log`, and `--verbose`
  echoes all of it. The exit code is non-zero if any step raised an error.
- The runner refuses to start while another run holds the project. If
  Ghidra's own `DK2_RE.lock` is left behind with no Java process alive,
  delete it.
- `--script-path DIR` adds your own scripts (see *Writing a new script*).
  `--dry-run` prints the command. Toolchain paths can be overridden with
  `DK2_GHIDRA_HOME` and `DK2_JAVA_HOME`.

Read the output files with a filtering tool (grep, or a short script), not by
pasting whole files into context. `InsGrep` on a busy global returns
thousands of lines.

## Catalogue (`tools/ghidra/scripts/`)

`<out>` is first everywhere. `...` means one or more.

**Where is X used?** Pick by what X is.

| Script | Args | Answers |
|---|---|---|
| `ScanAll.py` | `<out> <addr>...` | Every place the 4 little-endian bytes of an address occur, in **every** initialised block (code, `.rdata`, `.data`), with the containing instruction. Use for globals, vtables and function pointers. |
| `CallScanAll.py` | `<out> <addr>...` | Every `E8`/`E9` rel32 call or jump to the address in every executable block, including undisassembled bytes, plus absolute occurrences. Use for "who calls this". |
| `InsGrep.py` | `<out> <substr>...` | Every disassembled instruction whose text contains a substring, case-insensitive (`"0x6ee540"`, `"+ 0x9ee]"`). |
| `MaskScan.py` | `<out> <const>...` | Instructions mentioning a hex constant. |
| `ReadsAt.py` / `ReadsAtMulti.py` | `<out> <off>...` | Instructions whose **source** is `[reg + off]`: the readers of a field. |
| `StoreAt.py` | `<out> <off>...` | Instructions that **write** `[reg + off]`: the writers of a field. |
| `BitReaders.py` | `<out> <off> <mask>` | Loads of `[reg+off]` followed within 5 instructions by TEST/AND/OR on that bit. |
| `Intersect.py` | `<out> <global> <callee>` | Functions that both reference a global and call a function. |
| `Imports.py` | `<out> <needle>...` | Imported symbols matching a needle, with their call sites. |

**What does this code do?**

| Script | Args | Answers |
|---|---|---|
| `Disasm.py` | `<out> <start> <end>` | Disassembly of `[start, end)`, with the values at referenced memory. |
| `DisasmMulti.py` | `<out> <start>:<end>...` | The same over several ranges. |
| `Windows.py` | `<out> <addr>...` | `[addr-0x20, addr+0x48)` around each hit, so a folded load is seen with its use. |
| `Decomp.py` | `<out> <addr>...` | Decompile the function containing each address. |
| `DecompLoose.py` | `<out> <addr>...` | The same, creating a function first where none contains the address. |
| `ForceAt.py` | `<out> <addr>...` | Define a function at **exactly** the address and decompile it. |
| `ForceFunc.py` | `<out> <addr>...` | Find the prologue before an address reached only through a vtable, define the function there, decompile it. |
| `FnStrings.py` | `<out> <fn>...` | Every string a function references: identifies a nameless `FUN_`. |
| `Funcs.py` | `<out> <start> <end>` | Functions and symbols in a range. |

**Who calls whom, with what?**

| Script | Args | Answers |
|---|---|---|
| `Up.py` | `<out> <addr> <depth>` | Caller tree upward, breadth-first. |
| `AtAndCallers.py` | `<out> <addr>...` | Decompile the containing function and list its callers. |
| `PreCall.py` | `<out> <target> [n]` | The `n` instructions before every call to a target (its arguments). |
| `SetStateLits.py` | `<out> <target>...` | The last PUSH before each call to a target: a literal first argument, for example a state id. |
| `CallSlot.py` | `<out> <slot> <global>` | Indirect calls through a vtable slot offset, flagged where the function also names a global. |
| `SlotCallCtx.py` | `<out> <slot> [k]` | Every indirect call through a slot, with the `k` instructions before it (where the object came from). |

**Tables, classes, layout.**

| Script | Args | Answers |
|---|---|---|
| `DispatchMap.py` | `<out> <byteTable> <count> <jumpTable> [target]` | Two-level dispatcher: record type to handler arm, and which types reach an arm. |
| `VtableSlot.py` | `<out> <needle> <knownSlot> <wantSlot>` | Vtables found through one known member; prints another slot of each. |
| `Rtti.py` | `<out> <addr>...` | The class of a vtable (or of any code pointer inside one) and the base's offset. Names stay local. |
| `RttiFind.py` | `<out> <substr> <slot>...` | RTTI types matching a substring, their vtables and chosen slots. |
| `NewSizes.py` | `<out> <table>...` | Record sizes: for each function that installs a primary vtable, the size pushed to the allocation just before it. Also lists every write through each table address given. |
| `CopyPairs.py` | `<out>` | Record-loader copy pairs (file struct offset to runtime field offset). |
| `Bytes.py` | `<out> <addr> <countWords>` | Raw words and bytes at an address. |
| `DumpDwords.py` | `<out> <start> <count>` | Signed dwords from an address. |

**Coverage of the analysis itself.**

| Script | Args | Answers |
|---|---|---|
| `Blocks.py` | `<out>` | Memory blocks, ranges, permissions. |
| `InsCount.py` | `<out>` | Instructions disassembled per executable block. |
| `Gaps.py` | `<out> [minRun]` | Executable bytes auto-analysis never made into functions. Reports only. |
| `ForceGaps.py` | `<out> [minRun] [dry]` | Define a function at the head of each such run. Needs `--write` to persist. |

Two verification helpers live in `tools/verification/`: `ScanBatch.py` and
`InsBatch.py`, `<out> <file-of-hex-addresses>`. They take a large batch of
addresses from a file, which keeps the list under Windows' command-line limit.
Pass `--script-path tools/verification` to use them.

## Traps that have produced wrong conclusions

Check each one that applies before you write a conclusion down.

- **A scan proves nothing outside its range.** `DKII.EXE` has a second code
  block, `cseg` (`0x00653000`), beside `.text`. "No references" from a
  `.text`-only scan, or from a query that never matched its positive control,
  is not a negative. Run a known positive through the same query.
- **A negative grep licenses "this field is unread", never "this behaviour is
  absent".** Another path may compute the same thing.
- **The loader moves offsets.** A creature record's runtime offset is its
  file offset + `0x3F0`, and the loader may multiply a value in place. Find
  the loader store (`CopyPairs.py`, `StoreAt.py`), not just the file offset.
- **PE sections have separate file deltas.** One borrowed delta lands on
  unrelated data and scans as a convincing false negative.
- **`this` is hidden by the decompiler.** In a `void` state, per-entity or
  global is decided by each call's `MOV ECX`. Read it in `Disasm.py`, not in
  the decompile.
- **Count the callee's arguments before reading a push window.** Read
  `RET n` and its `[ESP+k]` loads first. Otherwise the second-to-last push is
  easily taken for the argument.
- **`PreCall.py` drops shared tails.** Branches that `JMP` to one `CALL` are
  missing from its window. Read the branches.
- **Read the prologue and epilogue first.** The saved argument and the
  returned local name the function's shape.
- **A type number is not an arm.** `DispatchMap.py` prints `index + 1`, and
  the walker that reached the record decides which dispatcher runs.
- **`ForceGaps` can name a function late.** An unbalanced epilogue means the
  real entry is earlier, so "nothing references it" may be about the wrong
  address.
- **No writer does not mean zero.** Read the initialised `.data` bytes
  (`Bytes.py`) before calling a global zero.
- **Look for the debug dumper.** `DKII.EXE` prints many of its own field names
  (`FnStrings.py`, `docs/research/debug-dumpers.md`).
- **Count call multiplicities before reading bodies.** Four calls to one
  helper are often a rectangle's sides, and a recursive call is often a segment
  walk.

## Writing a new script

Write a one-off in your scratchpad and run it with `--script-path <dir>`.
PyGhidra (CPython 3). Ghidra 12.1 has no Jython. Template:

```python
# One line: the question this answers. Args: <out> <addrHex>...
import insdb                                   # from tools/ghidra/scripts


def main():                                    # every loop inside a function: ~10x faster
    args = getScriptArgs()
    out = open(args[0], 'w')
    wanted = set(int(a, 16) for a in args[1:])
    db = insdb.load(currentProgram, runScript)  # cached listing: ~1 s, not 7-20 s of JPype calls
    for addr, fentry, mnem, text, op0, scalars in db.instructions():
        if wanted.intersection(scalars):
            out.write('%08x %s %s\n' % (addr, db.name(fentry), text))
    out.close()
    print('WROTE %s' % args[0])


main()
```

- For raw bytes: `JArray(JByte)(n)` from `jpype`, `memory.getBytes(...)`,
  then `bytes(buf)` at once and `bytes.find`. Never index the Java array
  element by element.
- `//` for integer division. Keep the source ASCII.
- **Promote** a script into `tools/ghidra/scripts/` when a second question
  would reuse it. Keep its header comment (the question, then `Args: <out> ...`),
  keep it parameterised with no subject-specific constants, and add a row to
  the catalogue above. A script tied to one subject (a fog bit, one table)
  stays local.
- More porting notes: `docs/RESEARCH-QUEUE.md`, *How the binary work is done here*.

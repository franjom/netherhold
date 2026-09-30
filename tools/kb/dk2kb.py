# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=1.26,<2"]
# ///
# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""dk2kb: a stdio MCP server over the DKII.EXE function knowledge base (docs/research/functions/<ADDRESS>.md).

Claude Code (.mcp.json) and Codex (~/.codex/config.toml) both spawn it with `uv run --quiet tools/kb/dk2kb.py`.
Parsing mirrors src/DK2.Tools/FunctionKb.cs; writes are validated by `dk2tools kb check --all` and undone on a
finding about the written record. `--selftest` spawns the server over stdio and exercises every tool, then runs the
check-and-restore path in-process on a scratch folder and a build() parity round-trip over every record.
"""

from __future__ import annotations

import asyncio
import datetime
import os
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FOLDER_REL = "docs/research/functions"
FOLDER = ROOT / FOLDER_REL
DLL = ROOT / "src" / "DK2.Tools" / "bin" / "Release" / "net10.0" / "DK2.Tools.dll"

HEADER_ORDER = ["address", "name", "role", "confidence", "status", "evidence", "implements"]
TOKEN_KEYS = {"address", "confidence", "status", "evidence", "implements"}
LIST_KEYS = {"evidence", "implements"}
CONFIDENCES = ("CONFIRMED", "LIKELY", "GUESS", "DISPUTED")
STATUSES = ("READ", "BUILT", "SUPERSEDED")
HISTORY_HEADING = "## History"
ADDRESS_RX = re.compile(r"^00[4-6][0-9A-F]{5}$")
AGENT_RX = re.compile(r"^[a-z][a-z0-9-]*$")

INSTRUCTIONS = (
    "The DKII.EXE function knowledge base: one record per function in docs/research/functions/<ADDRESS>.md. "
    "Read a record (kb_get, or kb_search by name/offset/topic) before deciding what a function is or does. "
    "Write every new finding or correction back with kb_upsert, with a History note saying what changed and why "
    "(AGENTS.md HARD rule): a function you read and did not record is lost."
)


# ---------------------------------------------------------------- parsing (mirrors FunctionKb.Parse)

def normalize_address(address: str) -> str:
    """'0x0050EA70', '0050ea70', 'FUN_0050EA70' -> '0050EA70'; ValueError outside 0x00400000-0x006FFFFF."""
    text = str(address).strip()
    if text.upper().startswith("FUN_"):
        text = text[4:]
    if text[:2] in ("0x", "0X"):
        text = text[2:]
    text = text.upper()
    if not ADDRESS_RX.match(text):
        raise ValueError(f"address '{address}' is not a DKII.EXE code address: expected 00[4-6]xxxxx as eight hex "
                         "digits, e.g. 0x0050EA70, 0050ea70 or FUN_0050EA70")
    return text


def record_path(addr: str) -> Path:
    return FOLDER / f"{addr}.md"


def read_lines(path: Path) -> list[str]:
    text = path.read_bytes().decode("utf-8-sig")
    return [l.rstrip("\r") for l in text.split("\n")]


def split_record(lines: list[str]):
    """-> (header [(key, value, raw)], header_end index, history_at index or len(lines))."""
    header = []
    i = 0
    while i < len(lines) and lines[i].strip():
        if lines[i].strip() == HISTORY_HEADING:
            break
        colon = lines[i].find(":")
        if colon > 0:
            key = lines[i][:colon].strip()
            value = lines[i][colon + 1:].strip()
            hash_at = value.find(" #")
            if hash_at >= 0 and key in TOKEN_KEYS:
                value = value[:hash_at].strip()
            header.append((key, value, lines[i]))
        i += 1
    history_at = next((j for j, l in enumerate(lines) if l.strip() == HISTORY_HEADING), len(lines))
    return header, i, history_at


def parse_history(lines: list[str], history_at: int) -> list[str]:
    entries: list[str] = []
    for line in lines[history_at + 1:]:
        if not line.strip():
            continue
        if line.startswith("  ") and entries:
            entries[-1] += " " + line.strip()
        else:
            entries.append(line.rstrip())
    return entries


def parse(lines: list[str]) -> dict:
    header, end, history_at = split_record(lines)
    fields: dict[str, object] = {}
    for key, value, _ in header:
        if key in LIST_KEYS:
            fields.setdefault(key, []).append(value)
        elif key in fields:
            prev = fields[key]
            fields[key] = (prev if isinstance(prev, list) else [prev]) + [value]
        else:
            fields[key] = value
    body_start = min(end, history_at)
    reasoning = "\n".join(lines[body_start:history_at]).strip()
    return {"fields": fields, "reasoning": reasoning, "history": parse_history(lines, history_at)}


# ---------------------------------------------------------------- dk2tools

def run_kb(*args: str, timeout: float = 120) -> tuple[int, str]:
    if not DLL.exists():
        return 2, f"{DLL.relative_to(ROOT).as_posix()} is missing; build DK2.Tools in Release first"
    try:
        p = subprocess.run(["dotnet", str(DLL), "kb", *args], cwd=ROOT, capture_output=True,
                           text=True, encoding="utf-8", errors="replace", timeout=timeout)
    except (OSError, subprocess.TimeoutExpired) as e:
        return 2, f"dk2tools kb {' '.join(args)}: {e}"
    return p.returncode, (p.stderr.strip() or p.stdout.strip())


# The runners kb_upsert calls; module-level so --selftest can swap them for fakes.
def run_check() -> tuple[int, str]:
    return run_kb("check", "--all")


def run_index() -> tuple[int, str]:
    return run_kb("index", timeout=30)


def tools_built() -> bool:
    return DLL.exists()


# ---------------------------------------------------------------- writing

def one_line(text: str) -> str:
    return " ".join(str(text).split())


def csv_list(text: str) -> list[str]:
    return [t.strip() for t in str(text).split(",") if t.strip()]


def build(old_lines: list[str] | None, changes: dict[str, object], reasoning: str, history_line: str | None) -> str:
    """The new file text. Unchanged header lines keep their raw text (and any ' # comment'); body and history
    are kept verbatim unless reasoning is passed; history_line, if any, is appended at the end. With no
    changes, no reasoning and no history_line an existing record comes back byte-for-byte (the selftest checks)."""
    if old_lines is None:
        raw: dict[str, list[str]] = {}
        header_lines: list[str] = []
        body = reasoning.strip()
    else:
        header, end, history_at = split_record(old_lines)
        raw = {}
        for key, _, line in header:
            raw.setdefault(key, []).append(line)
        header_lines = old_lines[:end]

    if old_lines is None or changes:
        for key, value in changes.items():
            raw[key] = [f"{key}: {v}" for v in value] if isinstance(value, list) else [f"{key}: {value}"]
        header_lines = [l for key in HEADER_ORDER for l in raw.get(key, [])]
        header_lines += [l for key, ls in raw.items() if key not in HEADER_ORDER for l in ls]

    tail = [history_line] if history_line else []
    if old_lines is None:
        out = header_lines + [""] + ([body, ""] if body else []) + [HISTORY_HEADING] + tail
    elif history_at >= len(old_lines):  # no History section: keep everything after the header as the body
        middle = ["", reasoning.strip(), ""] if reasoning.strip() else old_lines[end:] + [""]
        out = header_lines + middle + [HISTORY_HEADING] + tail
    else:
        middle = ["", reasoning.strip(), ""] if reasoning.strip() else old_lines[end:history_at]
        history = old_lines[history_at + 1:]
        if tail:
            while history and not history[-1].strip():
                history.pop()
        out = header_lines + middle + [HISTORY_HEADING] + history + tail
    return "\n".join(out) + "\n"


def split_bytes(data: bytes) -> list[str]:
    """Lines as FunctionKb reads them (BOM and CR dropped), without the empty string after a final LF."""
    lines = [l.rstrip("\r") for l in data.decode("utf-8-sig").split("\n")]
    if lines and lines[-1] == "":
        lines.pop()
    return lines


def write_atomic(path: Path, data: str | bytes) -> None:
    """Temp file beside the record (its name does not end in .md, so kb check never sees it), then os.replace,
    retried while Windows reports a sharing violation (a reader holding the file open). The temp file never
    survives. Raises OSError if the replace still fails."""
    tmp = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    try:
        with open(tmp, "wb") as f:
            f.write(data.encode("utf-8") if isinstance(data, str) else data)
        for delay in (0.01, 0.02, 0.04, 0.06, 0.08, 0):
            try:
                os.replace(tmp, path)
                return
            except PermissionError:
                if not delay:
                    raise
                time.sleep(delay)
    finally:
        try:
            tmp.unlink()
        except FileNotFoundError:
            pass
        except OSError:
            pass


def stamp(path: Path):
    try:
        st = path.stat()
        return (st.st_mtime_ns, st.st_size)
    except FileNotFoundError:
        return None


def upsert(address: str, agent: str, note: str, name: str = "", role: str = "", confidence: str = "",
           status: str = "", evidence: str = "", implements: str = "", reasoning: str = "") -> str:
    """kb_upsert's body; see its docstring."""
    try:
        addr = normalize_address(address)
    except ValueError as e:
        return f"not saved: {e}"
    who = str(agent).strip().lower()
    if not AGENT_RX.match(who):
        return f"not saved: agent '{agent}' must be lower-case letters, digits and hyphens, starting with a letter"
    text = one_line(note)
    if not text:
        return "not saved: note is empty; say what changed and why"
    confidence, status = confidence.strip().upper(), status.strip().upper()
    if confidence and confidence not in CONFIDENCES:
        return f"not saved: confidence '{confidence}' is not one of {'|'.join(CONFIDENCES)}"
    if status and status not in STATUSES:
        return f"not saved: status '{status}' is not one of {'|'.join(STATUSES)}"
    if evidence.strip() == "-":
        return "not saved: evidence cannot be cleared; every record needs at least one evidence link"
    if not tools_built():
        return ("not saved: DK2.Tools is not built (dotnet build src/DK2.Tools/DK2.Tools.csproj -c Release); "
                "the write could not be checked")

    changes: dict[str, object] = {}
    for key, value in (("name", name), ("role", role), ("confidence", confidence), ("status", status)):
        if one_line(value):
            changes[key] = one_line(value)
    if csv_list(evidence):
        changes["evidence"] = csv_list(evidence)
    if implements.strip() == "-":
        changes["implements"] = []
    elif csv_list(implements):
        changes["implements"] = csv_list(implements)
    history_line = f"- {datetime.date.today().isoformat()} {who}: {text}"
    path = record_path(addr)
    rel = f"{FOLDER_REL}/{addr}.md"

    old_bytes: bytes | None = None
    for attempt in range(2):
        before = stamp(path)
        try:
            old_bytes = path.read_bytes() if before else None
        except FileNotFoundError:  # deleted between stat and read: another writer; treat as a change
            if attempt == 0:
                continue
            return f"not saved: {rel} changed twice while this write was prepared; read it again"
        if old_bytes is None:
            if "role" not in changes or "evidence" not in changes:
                return f"not saved: {rel} is new and needs role and evidence"
            new = {"address": f"0x{addr}", "name": f"FUN_{addr}", "confidence": "GUESS", "status": "READ"}
            new.update(changes)
            content = build(None, new, reasoning, history_line)
        else:
            content = build(split_bytes(old_bytes), changes, reasoning, history_line)
        if stamp(path) != before:
            if attempt == 0:
                continue
            return f"not saved: {rel} changed twice while this write was prepared; read it again"
        try:
            write_atomic(path, content)
        except OSError as e:
            return f"not saved: could not replace {rel}: {e}"
        break
    written = stamp(path)

    code, output = run_check()
    mine = [l.strip() for l in output.splitlines() if rel.lower() in l.lower()]
    if code not in (0, 1) or mine or (code == 1 and not output):
        reason = "\n".join(mine) if mine else output
        if stamp(path) != written:
            return f"not saved (not restored: {rel} was changed by another writer meanwhile): {reason}"
        try:
            if old_bytes is None:
                path.unlink(missing_ok=True)
            else:
                write_atomic(path, old_bytes)
        except OSError as e:
            return f"not saved: {reason}; RESTORE FAILED ({e}), {rel} holds the rejected content"
        return f"not saved: {reason}"
    code_i, out_i = run_index()
    result = f"saved {rel}"
    if code == 1:
        result += f"\n(kb check has findings about other records, not this one:\n{output})"
    if code_i != 0:
        result += f"\n(kb index failed: {out_i})"
    return result


# ---------------------------------------------------------------- the server

def make_server():
    from mcp.server.fastmcp import FastMCP

    mcp = FastMCP("dk2kb", instructions=INSTRUCTIONS, log_level="WARNING")

    @mcp.tool()
    def kb_get(address: str) -> dict:
        """One function record: address, fields (header; evidence and implements as lists, other keys as strings),
        reasoning (the body) and history (the '- YYYY-MM-DD agent: text' lines). Empty dict if there is no record.
        address: 0x0050EA70, 0050ea70 or FUN_0050EA70."""
        addr = normalize_address(address)
        path = record_path(addr)
        if not path.exists():
            return {}
        return {"address": "0x" + addr, **parse(read_lines(path))}

    @mcp.tool()
    def kb_search(text: str, limit: int = 20) -> list[dict]:
        """Records whose file contains text (case-insensitive substring over header, reasoning and history;
        INDEX.md excluded), sorted by address: [{address, name, role, confidence}], at most limit."""
        if int(limit) <= 0:
            return []
        needle = str(text).lower()
        hits = []
        for path in sorted(FOLDER.glob("*.md")):
            if path.name == "INDEX.md":
                continue
            content = path.read_bytes().decode("utf-8-sig", errors="replace")
            if needle not in content.lower():
                continue
            f = parse([l.rstrip("\r") for l in content.split("\n")])["fields"]
            hits.append({"address": "0x" + path.stem, "name": f.get("name", ""),
                         "role": f.get("role", ""), "confidence": f.get("confidence", "")})
            if len(hits) >= int(limit):
                break
        return hits

    @mcp.tool()
    def kb_history(address: str) -> list[str]:
        """The History lines of one record, oldest first (continuation lines joined); empty if no record."""
        path = record_path(normalize_address(address))
        if not path.exists():
            return []
        lines = read_lines(path)
        _, _, history_at = split_record(lines)
        return parse_history(lines, history_at)

    @mcp.tool()
    def kb_upsert(address: str, agent: str, note: str, name: str = "", role: str = "", confidence: str = "",
                  status: str = "", evidence: str = "", implements: str = "", reasoning: str = "") -> str:
        """Create or change one record; only the fields passed change. evidence and implements are
        comma-separated repo paths (optional #anchor) and replace the whole list; implements='-' clears it
        (evidence cannot be cleared). reasoning replaces the body. note becomes the History line
        '- <today> <agent>: <note>'. A new record needs role and evidence and defaults to name FUN_<ADDR>,
        confidence GUESS, status READ. The file is validated with `dk2tools kb check --all`; a finding about this
        record undoes the write and returns 'not saved: ...', so a record that already has a finding cannot be
        written until that finding is fixed in the same call. Otherwise INDEX.md is regenerated (best effort;
        it is deterministic from the records on disk) and 'saved <path>' returned.

        Concurrency: per file. Two sessions on different functions never meet. The same function written twice
        is last-writer-wins and git shows both; a write that lands between this call's read and its replace is
        detected by mtime, re-read and re-applied once, and refused if it changes again. A rollback never
        overwrites another writer's later content."""
        return upsert(address, agent, note, name, role, confidence, status, evidence, implements, reasoning)

    return mcp


# ---------------------------------------------------------------- self-test

def selftest_offline(check) -> None:
    """In-process: the real check-and-restore path on a scratch folder, and build() parity over every record."""
    import shutil
    import tempfile
    global FOLDER, run_check, run_index, tools_built

    real_folder, real_check, real_index, real_built = FOLDER, run_check, run_index, tools_built
    rel = f"{FOLDER_REL}/0050EA70.md"
    with tempfile.TemporaryDirectory(prefix="dk2kb-") as scratch:
        FOLDER = Path(scratch)
        copy = FOLDER / "0050EA70.md"
        shutil.copyfile(real_folder / "0050EA70.md", copy)
        original = copy.read_bytes()
        try:
            tools_built = lambda: True
            run_index = lambda: (0, "")
            run_check = lambda: (1, f"1 KB finding(s):\n  {rel.upper()}: evidence 'docs/nope.md' does not exist")
            got = upsert("0x0050EA70", "selftest", "rejected write", evidence="docs/nope.md")
            check(got.startswith("not saved") and "nope.md" in got, f"rejected write reported: {got[:70]}")
            check(copy.read_bytes() == original, "rejected write restored byte-identical")
            check(not list(FOLDER.glob(".*.tmp")), "no .tmp left after the rejected write")

            run_check = lambda: (0, "kb check: 1 records, no findings.")
            got = upsert("0x0050EA70", "SelfTest", "note  only\n write")
            added = copy.read_bytes()[len(original):].decode("utf-8")
            check(got.startswith("saved") and copy.read_bytes().startswith(original)
                  and added.count("\n") == 1 and added.endswith(": note only write\n")
                  and added.startswith("- ") and " selftest: " in added,
                  f"note-only write adds exactly one History line: {added!r}")
            check(not list(FOLDER.glob(".*.tmp")), "no .tmp left after the saved write")

            tools_built = lambda: False
            before = copy.read_bytes()
            got = upsert("0x0050EA70", "selftest", "x")
            check(got.startswith("not saved: DK2.Tools is not built") and copy.read_bytes() == before,
                  "unbuilt DK2.Tools refuses before writing")
        finally:
            FOLDER, run_check, run_index, tools_built = real_folder, real_check, real_index, real_built

    records = [p for p in sorted(FOLDER.glob("*.md")) if p.name != "INDEX.md"]
    differ = [p.name for p in records if build(split_bytes(p.read_bytes()), {}, "", None).encode("utf-8") != p.read_bytes()]
    check(not differ and len(records) > 0, f"build() reproduces all {len(records)} records byte-for-byte"
          + (f"; differ: {differ[:5]}" if differ else ""))


async def selftest() -> int:
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    def text_of(result) -> str:
        return "\n".join(getattr(c, "text", "") for c in result.content)

    failures: list[str] = []

    def check(ok: bool, what: str):
        print(("ok   " if ok else "FAIL ") + what)
        if not ok:
            failures.append(what)

    target = record_path("0050EA70")
    original = target.read_bytes()
    params = StdioServerParameters(command=sys.executable, args=[str(Path(__file__).resolve())], cwd=str(ROOT))
    t0 = time.perf_counter()
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            print(f"initialize: {time.perf_counter() - t0:.2f} s")
            names = sorted(t.name for t in (await session.list_tools()).tools)
            check(names == ["kb_get", "kb_history", "kb_search", "kb_upsert"], f"four tools: {names}")
            got = text_of(await session.call_tool("kb_get", {"address": "0x0050EA70"}))
            check("WorldLog" in got, "kb_get 0x0050EA70 names WorldLog")
            got = text_of(await session.call_tool("kb_get", {"address": "FUN_0050ea70"}))
            check("WorldLog" in got, "kb_get FUN_0050ea70 normalises")
            bad = await session.call_tool("kb_get", {"address": "0x00123456"})
            check(bad.isError, "kb_get 0x00123456 is refused")
            got = text_of(await session.call_tool("kb_search", {"text": "MESSAGE.LOG"}))
            check("0x0050EA70" in got, "kb_search MESSAGE.LOG finds 0x0050EA70")
            got = await session.call_tool("kb_search", {"text": "MESSAGE.LOG", "limit": 0})
            check(not got.isError and "0x0050EA70" not in text_of(got), "kb_search limit=0 returns nothing")
            hist = await session.call_tool("kb_history", {"address": "0050EA70"})
            check(bool(text_of(hist).strip()) and not hist.isError, "kb_history 0050EA70 is non-empty")
            got = text_of(await session.call_tool("kb_upsert", {"address": "0x0050EA70", "agent": "selftest",
                                                                "note": "x", "confidence": "SURE"}))
            check(got.startswith("not saved"), f"kb_upsert confidence=SURE refused: {got[:80]}")
            check(target.read_bytes() == original, "0050EA70.md byte-identical after the refused write")
    selftest_offline(check)
    if failures:
        print(f"FAIL dk2kb: {len(failures)} check(s) failed")
        return 1
    print("PASS dk2kb")
    return 0


if __name__ == "__main__":
    if "--selftest" in sys.argv[1:]:
        sys.exit(asyncio.run(selftest()))
    make_server().run()

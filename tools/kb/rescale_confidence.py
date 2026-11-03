# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Moves function records from the retired KB scale to the one confidence scale (ADR-0642).

LIKELY and GUESS become HYPOTHESIS. CONFIRMED meant a binary read on the old scale and the running
game on the new one, so it becomes STRONGLY INFERRED only when the reference branch (default master)
does not hold that record as CONFIRMED: a record master keeps CONFIRMED rests on the running game.
A record already carrying a History line for ADR-0642 is left alone. Each changed record gains a
dated History line. After merging master into a branch cut before ADR-0642:

    python tools/kb/rescale_confidence.py [--reference master] [--agent NAME] [--date YYYY-MM-DD]

A record the branch made CONFIRMED on the running game should be raised back by hand afterwards.
"""
import argparse
import datetime
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "docs" / "research" / "functions"
MAPPING = {"CONFIRMED": "STRONGLY INFERRED", "LIKELY": "HYPOTHESIS", "GUESS": "HYPOTHESIS"}
CONFIDENCE_RX = re.compile(r"^confidence:[ \t]*(CONFIRMED|LIKELY|GUESS)\b(.*)$")
MARK = "(ADR-0642)"


def rescale(text: str, agent: str, date: str, keep_confirmed: bool = False) -> str | None:
    """The record's new text, or None when it needs no change. Text uses '\n' line endings."""
    if MARK in text:
        return None  # already moved once; a second run must not demote it again
    lines = text.split("\n")
    for i, line in enumerate(lines):
        if line.strip() == "":
            return None  # the header ended without an old confidence
        m = CONFIDENCE_RX.match(line)
        if m:
            old, rest = m.group(1), m.group(2)
            if old == "CONFIRMED" and keep_confirmed:
                return None
            new = MAPPING[old]
            lines[i] = f"confidence: {new}{rest}"
            break
    else:
        return None
    # The History section is last: put the line after its last non-blank line.
    end = len(lines)
    while end > 0 and lines[end - 1].strip() == "":
        end -= 1
    note = f"- {date} {agent}: Confidence {old} is {new} on the one scale {MARK}; the reading is unchanged."
    lines.insert(end, note)
    return "\n".join(lines)


def confirmed_at(reference: str, relative: str) -> bool:
    """Whether the reference branch holds this record with confidence CONFIRMED."""
    shown = subprocess.run(["git", "-C", str(ROOT), "show", f"{reference}:{relative}"],
                           capture_output=True, text=True, encoding="utf-8")
    return shown.returncode == 0 and re.search(r"^confidence:[ \t]*CONFIRMED\b", shown.stdout, re.M) is not None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--reference", default="master")
    parser.add_argument("--agent", default="claude")
    parser.add_argument("--date", default=datetime.date.today().isoformat())
    args = parser.parse_args()
    changed = 0
    for path in sorted(FOLDER.glob("*.md")):
        if path.name == "INDEX.md":
            continue
        raw = path.read_bytes()
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        crlf = "\r\n" in text
        text = text.replace("\r\n", "\n")
        keep = (re.search(r"^confidence:[ \t]*CONFIRMED\b", text, re.M) is not None
                and confirmed_at(args.reference, path.relative_to(ROOT).as_posix()))
        new = rescale(text, args.agent, args.date, keep)
        if new is None:
            continue
        if crlf:
            new = new.replace("\n", "\r\n")
        path.write_bytes((b"\xef\xbb\xbf" if bom else b"") + new.encode("utf-8"))
        changed += 1
    print(f"rescaled {changed} record(s); run `dk2tools kb index` and `kb check --all` next.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

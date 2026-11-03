# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Moves function records from the retired KB scale to the one confidence scale (ADR-0642).

CONFIRMED (a binary read) becomes STRONGLY INFERRED, LIKELY and GUESS become HYPOTHESIS, and each
changed record gains a dated History line. Idempotent: a record already on the scale is left alone,
so a branch cut before ADR-0642 can run it again after merging master.

    python tools/kb/rescale_confidence.py [--agent NAME] [--date YYYY-MM-DD]

A record whose CONFIRMED rests on the running game should be raised back by hand afterwards.
"""
import argparse
import datetime
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "docs" / "research" / "functions"
MAPPING = {"CONFIRMED": "STRONGLY INFERRED", "LIKELY": "HYPOTHESIS", "GUESS": "HYPOTHESIS"}
CONFIDENCE_RX = re.compile(r"^confidence:[ \t]*(CONFIRMED|LIKELY|GUESS)\b(.*)$")


def rescale(text: str, agent: str, date: str) -> str | None:
    """The record's new text, or None when it needs no change. Text uses '\n' line endings."""
    lines = text.split("\n")
    for i, line in enumerate(lines):
        if line.strip() == "":
            return None  # the header ended without an old confidence
        m = CONFIDENCE_RX.match(line)
        if m:
            old, rest = m.group(1), m.group(2)
            new = MAPPING[old]
            lines[i] = f"confidence: {new}{rest}"
            break
    else:
        return None
    # The History section is last: put the line after its last non-blank line.
    end = len(lines)
    while end > 0 and lines[end - 1].strip() == "":
        end -= 1
    note = f"- {date} {agent}: Confidence {old} is {new} on the one scale (ADR-0642); the reading is unchanged."
    lines.insert(end, note)
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
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
        new = rescale(text.replace("\r\n", "\n"), args.agent, args.date)
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

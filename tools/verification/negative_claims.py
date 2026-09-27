"""Inventory binary-addressed negative claims for verification sweep B.

This is a queue, not a verdict. Address columns are clues from the same paragraph;
each claim needs a positive control and a reviewed Ghidra result.
"""

from __future__ import annotations

import csv
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CLAIM = re.compile(r"\b(?:only|never|no reader|no writer|no store|no caller)\b", re.I)
ADDRESS = re.compile(r"\b0x(?:00)?[4-6][0-9a-f]{5}\b", re.I)
BINARY_CLAIM = re.compile(
    r"only.{0,35}(?:reader|writer|call|store|load|site|reference|function|routine|access)"
    r"|(?:reader|writer|call|store|load|site|reference|function|routine|access).{0,35}only"
    r"|never.{0,25}(?:read|writ|call|store|load)"
    r"|no (?:reader|writer|call|store|load)",
    re.I,
)
OUTPUT = ROOT / "build" / "verification" / "negative-claims.csv"


def main() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    previous = {}
    if OUTPUT.exists():
        with OUTPUT.open(encoding="utf-8", newline="") as stream:
            previous = {
                (row["Source"], row["Claim"]): row
                for row in csv.DictReader(stream)
            }
    rows = []
    for path in sorted((ROOT / "docs").rglob("*.md")):
        lines = path.read_text(encoding="utf-8-sig").splitlines()
        for index, line in enumerate(lines):
            if not CLAIM.search(line):
                continue
            context = "\n".join(lines[max(0, index - 2) : min(len(lines), index + 3)])
            addresses = sorted(set(ADDRESS.findall(context)), key=str.lower)
            row = {
                    "Source": path.relative_to(ROOT).as_posix(),
                    "Line": index + 1,
                    "Claim": line.strip(),
                    "NearbyAddresses": ";".join(addresses),
                    "B": "",
                    "CheckedDate": "",
                }
            old = previous.get((row["Source"], row["Claim"]))
            if old:
                row["B"], row["CheckedDate"] = old["B"], old["CheckedDate"]
            rows.append(row)
    with OUTPUT.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)
    addressed = sum(bool(row["NearbyAddresses"]) for row in rows)
    binary = [row for row in rows if row["NearbyAddresses"] and BINARY_CLAIM.search(row["Claim"])
              and (row["Source"].startswith("docs/research/") or row["Source"] == "docs/DECISIONS.md")]
    targets = {int(address, 16) for row in binary for address in row["NearbyAddresses"].split(";")}
    (OUTPUT.parent / "negative-targets.txt").write_text(
        "".join(f"{target:08x}\n" for target in sorted(targets)), encoding="ascii"
    )
    queries = {0x006F2590} | {target - delta for target in targets for delta in range(4)}
    (OUTPUT.parent / "negative-queries.txt").write_text(
        "".join(f"{query:08x}\n" for query in sorted(queries)), encoding="ascii"
    )
    print(f"{len(rows)} claims; {addressed} have a nearby address; "
          f"{len(binary)} binary candidates, {len(targets)} targets, {len(queries)} query variants")


if __name__ == "__main__":
    main()

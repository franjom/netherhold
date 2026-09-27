"""Compare check-A candidate values with Ghidra's local disassembly dump.

This only finds numeric occurrences. A reviewer must decide whether the operand
is the claimed constant; an occurrence could be an offset or unrelated code.
"""

from __future__ import annotations

import csv
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
BUILD = ROOT / "build" / "verification"
SECTION = re.compile(r"^===== ([0-9a-f]{8}) \.\. [0-9a-f]{8} =====$", re.I)
INSTRUCTION = re.compile(r"^([0-9a-f]{8})\s+(.*)$", re.I)
NUMBER = re.compile(r"(?<![\w])0x[0-9a-f]+\b", re.I)


def main() -> None:
    sections: dict[str, list[tuple[str, list[int]]]] = {}
    current = ""
    for line in (BUILD / "constant-disassembly.txt").read_text(encoding="utf-8").splitlines():
        header = SECTION.match(line)
        if header:
            current = "0x" + header[1].lower()
            sections[current] = []
        elif match := INSTRUCTION.match(line):
            sections[current].append((match[1], [int(token, 16) for token in NUMBER.findall(match[2])]))
    with (BUILD / "constant-candidates.csv").open(encoding="utf-8", newline="") as stream:
        rows = list(csv.DictReader(stream))
    for row in rows:
        value = int(row["Value"], 0)
        hits = []
        for address in row["NearbyAddresses"].split(";"):
            for instruction, operands in sections.get(address.lower(), []):
                if value in operands:
                    hits.append(instruction)
        row["NumericOccurrences"] = ";".join(sorted(set(hits)))
    output = BUILD / "constant-occurrences.csv"
    with output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)
    cited = [row for row in rows if row["NearbyAddresses"]]
    print(f"{sum(bool(row['NumericOccurrences']) for row in cited)}/{len(cited)} cited constants have numeric occurrences; {output}")


if __name__ == "__main__":
    main()

"""Build check-A candidates from literal constants and adjacent binary citations.

The CSV is a review queue. A nearby address identifies a routine, not necessarily
the exact instruction that holds the value.
"""

from __future__ import annotations

import csv
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "build" / "verification" / "constant-candidates.csv"
CONSTANT = re.compile(
    r"\b(?:const|static\s+readonly)\s+(?:int|byte|short|uint|long|ushort|sbyte|float|double)"
    r"\s+(?P<name>\w+)\s*=\s*(?P<value>-?(?:0x[0-9a-fA-F]+|\d+))\s*;"
)
ADDRESS = re.compile(r"\b0x(?:00)?[4-6][0-9a-f]{5}\b", re.I)


def main() -> None:
    previous = {}
    if OUTPUT.exists():
        with OUTPUT.open(encoding="utf-8", newline="") as stream:
            previous = {
                (row["Source"], row["Name"], row["Value"]): row
                for row in csv.DictReader(stream)
            }
    with (ROOT / "docs" / "verification-ledger.csv").open(encoding="utf-8", newline="") as stream:
        rules = list(csv.DictReader(stream))
    rows = []
    for source in sorted({rule["Source"] for rule in rules}):
        path = ROOT / source
        lines = path.read_text(encoding="utf-8-sig").splitlines()
        ids = [rule["Id"] for rule in rules if rule["Source"] == source]
        for index, line in enumerate(lines):
            match = CONSTANT.search(line)
            if not match:
                continue
            comment = []
            for preceding in reversed(lines[max(0, index - 16) : index]):
                trimmed = preceding.strip()
                if trimmed.startswith(("//", "*")) or not trimmed:
                    comment.append(trimmed)
                else:
                    break
            addresses = sorted(set(ADDRESS.findall("\n".join(comment))), key=str.lower)
            row = {
                    "RuleIds": ";".join(ids),
                    "Source": source,
                    "Line": index + 1,
                    "Name": match["name"],
                    "Value": match["value"],
                    "NearbyAddresses": ";".join(addresses),
                    "A": "",
                    "CheckedDate": "",
                }
            old = previous.get((row["Source"], row["Name"], row["Value"]))
            if old:
                row["A"], row["CheckedDate"] = old["A"], old["CheckedDate"]
            rows.append(row)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)
    print(f"{len(rows)} literal constants; {sum(bool(row['NearbyAddresses']) for row in rows)} with nearby citations; {OUTPUT}")


if __name__ == "__main__":
    main()

"""Reject additions to the named enum cast baseline after it is established."""

import argparse
from pathlib import Path
import subprocess


BASELINE = Path("tools/style/named-enum-casts.baseline")
ROOT = Path(__file__).resolve().parents[2]


def read_counts(text: str) -> dict[str, int]:
    entries = {}
    for number, line in enumerate(text.splitlines(), 1):
        if not line or line.startswith("#"):
            continue
        key, separator, count = line.rpartition("|")
        if not separator or len(key.split("|")) != 5 or not count.isdecimal() or int(count) <= 0 or key in entries:
            raise ValueError(f"Invalid named enum cast baseline entry at line {number}")
        entries[key] = int(count)
    return entries


def increases(previous: dict[str, int], current: dict[str, int]) -> list[str]:
    return [
        f"{key}: {previous.get(key, 0)} → {count}"
        for key, count in sorted(current.items())
        if count > previous.get(key, 0)
    ]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base", help="Git base revision")
    args = parser.parse_args()
    revision = subprocess.check_output(
        ["git", "rev-parse", "--verify", "--end-of-options", f"{args.base}^{{commit}}"],
        cwd=ROOT, text=True,
    ).strip()
    current = read_counts((ROOT / BASELINE).read_text(encoding="utf-8"))
    previous_path = subprocess.check_output(
        ["git", "ls-tree", "--name-only", revision, "--", BASELINE.as_posix()],
        cwd=ROOT, text=True,
    )
    if not previous_path.strip():
        print(f"Initial named enum cast baseline: {sum(current.values())} casts")
        return 0

    previous = read_counts(subprocess.check_output(
        ["git", "show", f"{revision}:{BASELINE.as_posix()}"], cwd=ROOT, encoding="utf-8",
    ))
    added = increases(previous, current)
    if added:
        print("The named enum cast baseline grew:")
        for entry in added:
            print(entry)
        return 1

    print(f"Named enum cast baseline: {sum(previous.values())} → {sum(current.values())} casts")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

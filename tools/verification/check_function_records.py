"""Mechanical check of docs/research/functions/*.md against the binary.

Usage:
  python tools/ghidra/ghidra.py FuncFacts.py reverse-engineering/fn-facts.json reverse-engineering/fn-addrs.txt
  python tools/verification/check_function_records.py [--csv build/verification/function-record-check.csv]

For each record it takes every address and every immediate or field offset the
prose cites and asks whether the function's own instructions use it. It checks
that the quoted facts are present, not that the interpretation is right: an
address a record cites about a neighbour or a caller is classified, not
flagged. A record is SUSPECT when its entry is not a function start, or most of
its cited operands are absent from the body.
"""
import argparse
import csv
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FUNCS = os.path.join(ROOT, "docs", "research", "functions")
HEX = re.compile(r"(?<![0-9A-Za-z_])0x([0-9A-Fa-f]{2,8})\b")
CODE_LO, CODE_HI = 0x00400000, 0x007FFFFF


def parse(path):
    text = open(path, encoding="utf-8-sig").read()
    head, _, rest = text.partition("\n\n")
    meta = {}
    for line in head.splitlines():
        k, _, v = line.partition(":")
        if k in ("address", "name", "role", "confidence", "status"):
            meta[k] = v.strip()
    body = rest.split("## History")[0]
    return meta, meta.get("role", "") + "\n" + body


def claims(prose, own):
    addrs, imms = set(), set()
    for m in HEX.finditer(prose):
        v = int(m.group(1), 16)
        if v == own:
            continue
        if CODE_LO <= v <= CODE_HI:
            addrs.add(v)
        elif 0x10 <= v <= 0xFFFF:
            imms.add(v)
    return addrs, imms


def classify(addr, f):
    if addr in f["calls"]:
        return "callee"
    if addr in f["refs"] or addr in f["scalars"]:
        return "operand"
    if f["low"] <= addr <= f["high"]:
        return "inside"
    if addr in f["callers"] or addr in f["callerFns"]:
        return "caller"
    if any(lo <= addr <= hi for lo, hi in f["callerRanges"]):
        return "caller"
    return "absent"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--facts", default=os.path.join(ROOT, "reverse-engineering", "fn-facts.json"))
    ap.add_argument("--csv", default=os.path.join(ROOT, "build", "verification", "function-record-check.csv"))
    args = ap.parse_args()
    facts = json.load(open(args.facts))
    rows = []
    for name in sorted(os.listdir(FUNCS)):
        if not name.endswith(".md") or name == "INDEX.md":
            continue
        meta, prose = parse(os.path.join(FUNCS, name))
        key = name[:-3].upper()
        own = int(key, 16)
        f = facts.get(key)
        row = dict(address=key, name=meta.get("name", ""), confidence=meta.get("confidence", ""),
                   status=meta.get("status", ""), verdict="", entry="", size="", callee=0, operand=0,
                   inside=0, caller=0, absent=0, imm_found=0, imm_missing=0, absent_list="", imm_missing_list="")
        if f is None or not f["isEntry"]:
            row["entry"] = "no"
            row["verdict"] = "SUSPECT: Ghidra has no function here" + (
                f" (inside {f['containing']:08X})" if f and f.get("containing") else "")
            rows.append(row)
            continue
        row["entry"] = "yes"
        row["size"] = f["size"]
        addrs, imms = claims(prose, own)
        absent = []
        for a in sorted(addrs):
            c = classify(a, f)
            row[c] += 1
            if c == "absent":
                absent.append(a)
        miss = []
        for v in sorted(imms):
            if v in f["scalars"]:
                row["imm_found"] += 1
            else:
                row["imm_missing"] += 1
                miss.append(v)
        row["absent_list"] = " ".join(f"{a:08X}" for a in absent)
        row["imm_missing_list"] = " ".join(f"{v:X}" for v in miss)
        total = len(addrs) + len(imms)
        bad = len(absent) + len(miss)
        if total == 0:
            row["verdict"] = "NOCLAIMS"
        elif len(addrs) >= 2 and len(absent) * 2 > len(addrs):
            row["verdict"] = "SUSPECT: most cited addresses absent"
        elif len(imms) >= 3 and len(miss) == len(imms) and not addrs:
            row["verdict"] = "SUSPECT: every cited immediate absent"
        elif bad:
            row["verdict"] = "CHECK"
        else:
            row["verdict"] = "OK"
        rows.append(row)
    os.makedirs(os.path.dirname(args.csv), exist_ok=True)
    with open(args.csv, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    tally = {}
    for r in rows:
        k = r["verdict"].split(":")[0]
        tally[k] = tally.get(k, 0) + 1
    print(f"{len(rows)} records:", ", ".join(f"{k} {v}" for k, v in sorted(tally.items())))
    for r in rows:
        if r["verdict"].startswith("SUSPECT"):
            print(f"  {r['address']} {r['name']} [{r['confidence']}/{r['status']}] {r['verdict']}"
                  f" absent={r['absent_list'] or '-'} immMissing={r['imm_missing_list'] or '-'}")
    print("csv:", args.csv)


if __name__ == "__main__":
    sys.exit(main())

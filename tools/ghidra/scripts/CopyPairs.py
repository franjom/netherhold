# Harvest record-loader copy pairs: a load from [EBP + imm] followed within a
# few instructions by a store to [reg + imm], which is the shape every KWD
# record loader uses (source struct -> runtime definition). Args: <out>

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    import re

    args = getScriptArgs()
    out = open(args[0], 'w')
    db = insdb.load(currentProgram, runScript)
    LOAD = re.compile(r'^MOV(SX|ZX)? ([A-Z]{2,3}),(?:\s*(?:dword|word|byte) ptr)? \[EBP \+ (0x[0-9a-f]+)\]$')
    STORE = re.compile(r'^MOV (?:dword|word|byte) ptr \[(E[A-Z]{2}) \+ (0x[0-9a-f]+)\],([A-Z]{2,3})$')
    pending = []
    rows = {}
    for addr, fe, mnem, t, op0, sc in db.instructions():
        m = LOAD.match(t)
        if m:
            pending.append((m.group(2), int(m.group(3), 16), addr))
            if len(pending) > 6:
                pending.pop(0)
            continue
        s = STORE.match(t)
        if s and s.group(1) != 'EBP':
            src = s.group(3)
            for reg, off, a in reversed(pending):
                # AX/AL alias EAX etc.
                if reg == src or (len(reg) == 2 and reg[0] == src[1:2]) or reg[-2:] == src[-2:]:
                    key = db.name(fe) if fe is not None else 'none@%08x' % addr
                    rows.setdefault(key, []).append((off, int(s.group(2), 16)))
                    break
    for k in sorted(rows):
        v = rows[k]
        if len(v) < 4:
            continue
        out.write("== %s : %d pairs\n" % (k, len(v)))
        for src, dst in v:
            out.write("   src=%04x dst=%04x delta=%d\n" % (src, dst, src - dst))
    out.close()
    print("WROTE " + args[0])


main()

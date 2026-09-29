# Every instruction whose text contains any of the given substrings (case-insensitive).
# Args: <out> <substr> [...]

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    args = getScriptArgs()
    out = open(args[0], 'w')
    pats = [a.lower() for a in args[1:]]
    db = insdb.load(currentProgram, runScript)
    n = 0
    for addr, fe, mnem, t, op0, sc in db.instructions():
        low = t.lower()
        if not any(p in low for p in pats):
            continue
        out.write('%08x  %-40s in %s\n' % (addr, t, db.name(fe)))
        n += 1
    out.close()
    print('WROTE %s (%d)' % (args[0], n))


main()

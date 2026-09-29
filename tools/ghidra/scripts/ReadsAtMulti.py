# ReadsAt.py for several offsets in one pass. Args: <out> <offsetHex> [...]

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    args = getScriptArgs()
    out = open(args[0], 'w')
    wanted = [('+ 0x%x]' % int(a, 16)) for a in args[1:]]
    db = insdb.load(currentProgram, runScript)
    n = 0
    for addr, fe, mnem, text, op0, sc in db.instructions():
        low = text.lower()
        hit = None
        for w in wanted:
            if w in low:
                hit = w
                break
        if hit is None:
            continue
        out.write('%-8s %08x  %-46s in %s\n' % (hit, addr, text, db.name(fe)))
        n += 1
    out.close()
    print('WROTE %s (%d)' % (args[0], n))


main()

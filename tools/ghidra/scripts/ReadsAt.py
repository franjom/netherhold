# List every instruction whose SOURCE operand is [reg + OFFSET], with its
# containing function. Args: <out> <offsetHex>

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    args = getScriptArgs()
    out = open(args[0], 'w')
    off = int(args[1], 16)
    want = '+ 0x%x]' % off
    db = insdb.load(currentProgram, runScript)
    n = 0
    for addr, fe, mnem, text, op0, sc in db.instructions():
        # cheap textual filter first
        if want not in text.lower():
            continue
        out.write('%08x  %-44s in %s\n' % (addr, text, db.name(fe)))
        n += 1
    out.close()
    print('WROTE %s (%d)' % (args[0], n))


main()

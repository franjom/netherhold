# How many instructions are disassembled in each executable block. Args: <out>

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    db = insdb.load(prog, runScript)
    addrs = [a for a, _, _, _, _, _ in db.instructions()]
    import bisect
    for b in prog.getMemory().getBlocks():
        if not b.isExecute():
            continue
        lo, hi = b.getStart().getOffset(), b.getEnd().getOffset()
        n = bisect.bisect_right(addrs, hi) - bisect.bisect_left(addrs, lo)
        out.write("%-10s %s - %s  instructions=%d\n" % (b.getName(), b.getStart(), b.getEnd(), n))
    out.close()
    print("WROTE " + args[0])


main()

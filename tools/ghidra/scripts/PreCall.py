# The N instructions before every call to a target, for reading its arguments.
# Args: <out> <targetHex> [<n>]

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    args = getScriptArgs()
    out = open(args[0], 'w')
    target = args[1].lower().lstrip('0') or '0'
    n = int(args[2]) if len(args) > 2 else 10
    db = insdb.load(currentProgram, runScript)
    hits = []
    window = []
    for addr, fe, mnem, t, op0, sc in db.instructions():
        if t.startswith("CALL") and target in t.lower():
            hits.append((addr, fe, list(window)))
        window.append((addr, t))
        if len(window) > n:
            window.pop(0)
    out.write("== %d call sites of %s\n\n" % (len(hits), args[1]))
    for a, fe, w in hits:
        out.write("-- call at %08x in %s\n" % (a, db.name(fe, "(no func)")))
        for aa, tt in w:
            out.write("     %08x  %s\n" % (aa, tt))
        out.write("     %08x  CALL %s\n\n" % (a, args[1]))
    out.close()
    print("WROTE " + args[0])


main()

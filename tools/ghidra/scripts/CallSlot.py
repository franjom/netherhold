# Find indirect calls through a given vtable slot offset, flagging those in
# functions that also reference a given global (e.g. the thing table).
# Args: <out> <slotHex> <globalHex>

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    args = getScriptArgs()
    out = open(args[0], 'w')
    slot = args[1].lower()
    g = args[2].lower()
    want = "+ " + slot + "]"
    db = insdb.load(currentProgram, runScript)
    touch = set()
    calls = []
    for addr, fe, mnem, t, op0, sc in db.instructions():
        if fe is not None and g in t.lower():
            touch.add(fe)
        if t.startswith("CALL dword ptr [") and want in t.lower():
            calls.append((addr, fe, t))
    for addr, fe, t in calls:
        ep = fe if fe is not None else 0
        mark = "*THING*" if ep in touch else "       "
        out.write("%s %08x %-34s in %s @ %08x\n" % (mark, addr, t, db.name(fe), ep))
    out.write("== %d indirect calls through + %s\n" % (len(calls), slot))
    out.close()
    print("WROTE " + args[0])


main()

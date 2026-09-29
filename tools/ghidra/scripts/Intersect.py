# Functions that BOTH reference a global AND call a given function.
# Args: <out> <globalHex> <calleeHex>

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    from ghidra.util.task import ConsoleTaskMonitor

    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    fm = prog.getFunctionManager()
    af = prog.getAddressFactory().getDefaultAddressSpace()
    g = args[1].lower()
    db = insdb.load(prog, runScript)
    touch = {}
    for addr, fe, mnem, t, op0, sc in db.instructions():
        if fe is not None and g in t.lower():
            touch[fe] = db.name(fe)
    callee = fm.getFunctionAt(af.getAddress(int(args[2], 16)))
    callers = set()
    for c in callee.getCallingFunctions(ConsoleTaskMonitor()):
        callers.add(c.getEntryPoint().getOffset())
    out.write("%d reference %s ; %d call %s\n" % (len(touch), g, len(callers), args[2]))
    both = sorted(set(touch.keys()) & callers)
    out.write("== %d in both\n" % len(both))
    for a in both:
        out.write("   %08x %s\n" % (a, touch[a]))
    out.close()
    print("WROTE " + args[0])


main()

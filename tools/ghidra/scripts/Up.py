# Walk the caller tree upward from an address, breadth-first, to a depth.
# Args: <out> <addrHex> <depth>
from ghidra.util.task import ConsoleTaskMonitor


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    fm = prog.getFunctionManager()
    mon = ConsoleTaskMonitor()
    depth = int(args[2])
    seen = set()
    frontier = [fm.getFunctionContaining(af.getAddress(int(args[1], 16)))]
    for d in range(depth):
        nxt = []
        out.write("== depth %d : %d functions\n" % (d, len(frontier)))
        for f in frontier:
            if f is None:
                continue
            ep = f.getEntryPoint().getOffset()
            cs = f.getCallingFunctions(mon)
            names = []
            for c in cs:
                names.append("%08x" % c.getEntryPoint().getOffset())
                if c.getEntryPoint().getOffset() not in seen:
                    seen.add(c.getEntryPoint().getOffset())
                    nxt.append(c)
            out.write("   %08x %-16s <- %d callers: %s\n"
                      % (ep, f.getName(), len(cs), " ".join(names[:12])))
        frontier = nxt
        if not frontier:
            break
    out.close()
    print("WROTE " + args[0])


main()

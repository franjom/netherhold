# Decompile the function containing each address, and list its callers
# (with their names) so a cadence question can be walked up the chain.
# Args: <out> <addrHex>...
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    fm = prog.getFunctionManager()
    iface = DecompInterface()
    iface.openProgram(prog)
    mon = ConsoleTaskMonitor()

    for h in args[1:]:
        a = af.getAddress(int(h, 16))
        f = fm.getFunctionContaining(a)
        if f is None:
            out.write("== %s : NO FUNCTION CONTAINING\n\n" % h)
            continue
        out.write("== %s in %s @ %s\n" % (h, f.getName(), f.getEntryPoint()))
        callers = f.getCallingFunctions(mon)
        out.write("   callers (%d):\n" % len(callers))
        for c in callers:
            out.write("     %s @ %s\n" % (c.getName(), c.getEntryPoint()))
        res = iface.decompileFunction(f, 120, mon)
        if res.decompileCompleted():
            out.write(res.getDecompiledFunction().getC())
        else:
            out.write("   DECOMPILE FAILED: %s\n" % res.getErrorMessage())
        out.write("\n\n")
    out.close()
    print("WROTE " + args[0])


main()

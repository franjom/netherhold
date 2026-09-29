# Decompile functions containing each address. Args: <out> <addrHex> [...]
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    fm = prog.getFunctionManager()
    af = prog.getAddressFactory().getDefaultAddressSpace()
    dec = DecompInterface()
    dec.openProgram(prog)
    mon = ConsoleTaskMonitor()
    for a in args[1:]:
        addr = af.getAddress(int(a, 16))
        f = fm.getFunctionContaining(addr)
        out.write('===== %s =====\n' % a)
        if f is None:
            out.write('no function\n')
            continue
        res = dec.decompileFunction(f, 60, mon)
        if res.decompileCompleted():
            out.write(res.getDecompiledFunction().getC())
        else:
            out.write('failed: %s\n' % res.getErrorMessage())
    out.close()


main()

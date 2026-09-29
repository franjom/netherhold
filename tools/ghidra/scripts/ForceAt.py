# Force a function at EXACTLY the given address (no prologue search) and
# decompile it. Args: <output path> <addrHex> [<addrHex> ...]
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
from ghidra.app.cmd.function import CreateFunctionCmd
from ghidra.app.cmd.disassemble import DisassembleCommand


def main():
    args = getScriptArgs()
    outpath = args[0]
    targets = args[1:]

    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    fm = prog.getFunctionManager()
    monitor = ConsoleTaskMonitor()

    decomp = DecompInterface()
    decomp.openProgram(prog)

    out = open(outpath, 'w')
    for t in targets:
        addr = af.getAddress(int(t, 16))
        out.write("/* ===== forced function at %s ===== */\n" % t)

        f = fm.getFunctionContaining(addr)
        if f is None:
            DisassembleCommand(addr, None, True).applyTo(prog, monitor)
            CreateFunctionCmd(addr).applyTo(prog, monitor)
            f = fm.getFunctionContaining(addr)

        if f is None:
            out.write("/* could not create a function there */\n\n")
            continue

        res = decomp.decompileFunction(f, 120, monitor)
        if res.decompileCompleted():
            out.write(res.getDecompiledFunction().getC())
        else:
            out.write("/* decompile failed: %s */\n" % res.getErrorMessage())
        out.write("\n\n")
    out.close()
    print("WROTE " + outpath)


main()

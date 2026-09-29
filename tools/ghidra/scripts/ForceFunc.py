# Auto-analysis only finds functions it can reach. Code reached solely through a
# vtable is disassembled-but-nameless, or not disassembled at all. Given an
# address inside such a body, walk back to the MSVC prologue (push ebp; mov
# ebp,esp), force a function there, and decompile it.
# Args: <output path> <addrHex> [<addrHex> ...]
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor
from ghidra.app.cmd.function import CreateFunctionCmd
from ghidra.app.cmd.disassemble import DisassembleCommand


def main():
    args = getScriptArgs()
    outpath = args[0]
    targets = args[1:]

    prog = currentProgram
    mem = prog.getMemory()
    fm = prog.getFunctionManager()
    af = prog.getAddressFactory()
    monitor = ConsoleTaskMonitor()

    PROLOGUE = bytes(bytearray([0x55, 0x8b, 0xec]))

    def find_start(addr):
        # Walk back in 0x40-byte steps looking for the nearest prologue before addr.
        best = None
        probe = addr.subtract(0x1000)
        while probe.getOffset() < addr.getOffset():
            found = mem.findBytes(probe, PROLOGUE, None, True, monitor)
            if found is None or found.getOffset() >= addr.getOffset():
                break
            best = found
            probe = found.add(1)
        return best

    out = open(outpath, 'w')
    for t in targets:
        addr = af.getAddress(t)
        out.write("/* ===== target %s ===== */\n" % t)

        f = fm.getFunctionContaining(addr)
        if f is None:
            start = find_start(addr)
            if start is None:
                out.write("/* no prologue found within 0x1000 before %s */\n\n" % t)
                continue
            out.write("/* forced function start at %s */\n" % start)
            DisassembleCommand(start, None, True).applyTo(prog, monitor)
            CreateFunctionCmd(start).applyTo(prog, monitor)
            f = fm.getFunctionContaining(addr)

        if f is None:
            out.write("/* could not create a function covering %s */\n\n" % t)
            continue

        decomp = DecompInterface()
        decomp.openProgram(prog)
        res = decomp.decompileFunction(f, 180, monitor)
        out.write("/* %s @ %s */\n" % (f.getName(), f.getEntryPoint()))
        if res.decompileCompleted():
            out.write(res.getDecompiledFunction().getC())
        else:
            out.write("/* decompile failed: %s */\n" % res.getErrorMessage())
        out.write("\n\n")
    out.close()
    print("WROTE %s" % outpath)


main()

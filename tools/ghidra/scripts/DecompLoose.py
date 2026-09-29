# Decomp.py, but defines a function at an address no function contains first.
# Args: <out> <addrHex> [...]
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    fm = currentProgram.getFunctionManager()
    space = currentProgram.getAddressFactory().getDefaultAddressSpace()
    dec = DecompInterface()
    dec.openProgram(currentProgram)
    mon = ConsoleTaskMonitor()
    for value in args[1:]:
        addr = space.getAddress(int(value, 16))
        function = fm.getFunctionContaining(addr)
        if function is None:
            function = createFunction(addr, None)
        out.write('===== %s =====\n' % value)
        if function is None:
            out.write('no function\n')
            continue
        result = dec.decompileFunction(function, 60, mon)
        out.write(result.getDecompiledFunction().getC() if result.decompileCompleted()
                  else result.getErrorMessage())
    out.close()


main()

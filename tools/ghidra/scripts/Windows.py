# Disassembly windows around each address. Args: <out> <addrHex>...
# Prints [addr-0x20, addr+0x48) so a folded load and its extraction are both
# in view. Reports the containing function name for each window.


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    listing = prog.getListing()
    af = prog.getAddressFactory().getDefaultAddressSpace()
    fm = prog.getFunctionManager()
    for h in args[1:]:
        a = int(h, 16)
        lo, hi = a - 0x20, a + 0x48
        f = fm.getFunctionContaining(af.getAddress(a))
        out.write("\n===== window around %08x  (function %s)\n" % (a, f.getName() if f else '-'))
        it = listing.getInstructions(af.getAddress(lo), True)
        while it.hasNext():
            ins = it.next()
            o = ins.getAddress().getOffset()
            if o >= hi: break
            mark = ' <<<' if o <= a < o + ins.getLength() else ''
            out.write("%08x  %s%s\n" % (o, ins.toString(), mark))
    out.close()
    print("WROTE %s" % args[0])


main()

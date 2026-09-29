# List functions and symbols in [start, end). Args: <out> <startHex> <endHex>


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    lo, hi = int(args[1], 16), int(args[2], 16)
    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    fm = prog.getFunctionManager()
    it = fm.getFunctions(af.getAddress(lo), True)
    out.write("== functions\n")
    while it.hasNext():
        f = it.next()
        a = f.getEntryPoint().getOffset()
        if a >= hi:
            break
        body = f.getBody()
        out.write("%08x  %-28s max=%08x size=%d\n"
                  % (a, f.getName(), body.getMaxAddress().getOffset(), body.getNumAddresses()))
    out.write("\n== symbols\n")
    st = prog.getSymbolTable()
    sit = st.getSymbolIterator(True)
    while sit.hasNext():
        s = sit.next()
        a = s.getAddress().getOffset()
        if lo <= a < hi:
            out.write("%08x  %s\n" % (a, s.getName()))
    out.close()
    print("WROTE " + args[0])


main()

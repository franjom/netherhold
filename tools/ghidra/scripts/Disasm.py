# Print disassembly for [start, end). Args: <outPath> <startHex> <endHex>


def main():
    args = getScriptArgs()
    outpath = args[0]
    start = int(args[1], 16)
    end = int(args[2], 16)

    prog = currentProgram
    listing = prog.getListing()
    af = prog.getAddressFactory().getDefaultAddressSpace()
    mem = prog.getMemory()

    out = open(outpath, 'w')
    it = listing.getInstructions(af.getAddress(start), True)
    while it.hasNext():
        ins = it.next()
        a = ins.getAddress().getOffset()
        if a >= end:
            break
        out.write("%08x  %-40s" % (a, ins.toString()))
        # If any operand references memory, try to show the value there.
        for i in range(ins.getNumOperands()):
            for ref in ins.getOperandReferences(i):
                t = ref.getToAddress()
                try:
                    out.write("  ; [%s]=" % t)
                    out.write("f32=%r " % mem.getInt(t))
                    d = listing.getDataAt(t)
                    if d is not None:
                        out.write("data=%s" % d.getValue())
                except:
                    pass
        out.write("\n")
    out.close()
    print("WROTE " + outpath)


main()

# Raw scan of EVERY initialised block for the little-endian bytes of an
# address. Replaces ScanAddr.py, whose hardcoded 0x401000-0x652c00 range
# covers .text only and misses the second executable block, cseg.
# Args: <out> <addrHex>...   Reports block name and, in code, the instruction.
from jpype import JArray, JByte


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    mem = prog.getMemory()
    listing = prog.getListing()

    blocks = []
    for b in mem.getBlocks():
        if not b.isInitialized():
            continue
        start = b.getStart().getOffset()
        size = int(b.getEnd().getOffset() - start + 1)
        buf = JArray(JByte)(size)
        got = mem.getBytes(b.getStart(), buf)
        blocks.append((b.getName(), start, bytes(buf),
                       b.isExecute(), got, size))
        out.write("block %-10s %08x len %d read %d exec=%s\n"
                  % (b.getName(), start, size, got, b.isExecute()))
    out.write("\n")

    for h in args[1:]:
        v = int(h, 16)
        needle = bytes(bytearray([v & 0xFF, (v >> 8) & 0xFF,
                                  (v >> 16) & 0xFF, (v >> 24) & 0xFF]))
        total = []
        for name, start, data, ex, got, size in blocks:
            i = data.find(needle)
            while i >= 0:
                total.append((name, start + i, ex))
                i = data.find(needle, i + 1)
        out.write("== %08x : %d hits across all initialised blocks\n" % (v, len(total)))
        for name, a, ex in total:
            ins = listing.getInstructionContaining(af.getAddress(a))
            if ins is not None:
                out.write("   %-10s %08x  %s  %s\n" % (name, a, ins.getAddress(), ins.toString()))
            else:
                out.write("   %-10s %08x  (no instruction here%s)\n"
                          % (name, a, "; EXECUTABLE block - undisassembled" if ex else ""))
        out.write("\n")
    out.close()
    print("WROTE " + args[0])


main()

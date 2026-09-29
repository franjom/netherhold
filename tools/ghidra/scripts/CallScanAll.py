# Raw scan of EVERY executable block for every E8/E9 rel32 whose target is one
# of the given addresses, plus every absolute little-endian occurrence in any
# initialised block (vtable slots, handler tables).
#
# Replaces CallScan.py, which read 0x401000-0x652c00 as ONE contiguous range.
# That is .text only; it missed cseg (0x653000-0x66bfff, 100 KB of disassembled
# code). Widening the range does NOT work - the blocks are not contiguous, and
# getBytes across the gap truncates and reports a short count rather than
# failing. Each block is read separately here for that reason.
#
# PyGhidra: JArray(JByte) for getBytes, then bytes(buf) once - indexing
# a Java array element by element crosses into Java on every access.
# Args: <out> <addrHex> [<addrHex> ...]
import re
from jpype import JArray, JByte


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    wanted = [int(a, 16) for a in args[1:]]

    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    mem = prog.getMemory()
    listing = prog.getListing()
    fm = prog.getFunctionManager()

    blocks = []
    for b in mem.getBlocks():
        if not b.isInitialized():
            continue
        start = b.getStart().getOffset()
        n = int(b.getEnd().getOffset() - start + 1)
        buf = JArray(JByte)(n)
        got = mem.getBytes(b.getStart(), buf)
        buf = bytes(buf)
        if got != n:
            out.write("!! SHORT READ %s: %d of %d - results are incomplete\n"
                      % (b.getName(), got, n))
        blocks.append((b.getName(), start, buf, n, b.isExecute()))
        out.write("block %-10s %08x len %-8d read %-8d exec=%s\n"
                  % (b.getName(), start, n, got, b.isExecute()))


    def where(a):
        try:
            f = fm.getFunctionContaining(af.getAddress(a))
            if f is not None:
                return "%s+0x%x" % (f.getName(), a - f.getEntryPoint().getOffset())
            ins = listing.getInstructionAt(af.getAddress(a))
            return "(no func) %s" % (ins.toString() if ins else "<no code>")
        except:
            return "?"


    # Every E8/E9 in the executable blocks, decoded once: target -> [(block, site, op)].
    # A per-byte Python loop is slow here even inside main(), so
    # the opcode bytes are found by the regex engine and only those are decoded.
    wanted_set = set(wanted)
    rel = dict((t, []) for t in wanted)
    OPCODE = re.compile(b'[\xe8\xe9]')
    for name, base, buf, ln, ex in blocks:
        if not ex:
            continue
        for mt in OPCODE.finditer(buf, 0, ln - 5):
            i = mt.start()
            t = base + i + 5 + int.from_bytes(buf[i + 1:i + 5], 'little', signed=True)
            if t in wanted_set:
                rel[t].append((name, base + i, buf[i]))

    for t in wanted:
        out.write("\n== target %08x\n" % t)
        for name, site, b0 in rel[t]:
            out.write("  %s %-10s %08x  %s\n" %
                      ("CALL" if b0 == 0xE8 else "JMP ", name, site, where(site)))
        out.write("  -- %d rel32 sites\n" % len(rel[t]))
        m = 0
        needle = t.to_bytes(4, 'little')
        for name, base, buf, ln, ex in blocks:
            i = buf.find(needle)
            while i != -1:
                out.write("  ABS  %-10s %08x  %s\n" % (name, base + i, where(base + i)))
                m += 1
                i = buf.find(needle, i + 1)
        out.write("  -- %d absolute occurrences\n" % m)

    out.close()
    print("WROTE " + args[0])


main()

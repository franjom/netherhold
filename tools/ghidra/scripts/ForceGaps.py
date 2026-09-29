# Define a function at the head of every uncovered run of executable bytes.
#
# Auto-analysis names a function only where it can reach one. DK2 is full of
# code reached solely through dispatch tables and vtables, and much of it uses
# an ESP frame (83 EC xx) rather than the push-ebp prologue ForceFunc.py walks
# back to, so Ghidra disassembles it and never defines it. The cost is silent:
# AtAndCallers.py prints "in -" and a caller search looks empty when it is only
# unattributed.
#
# Conservative by construction: it only touches bytes no existing function
# covers, skips alignment padding, and never moves or deletes a function that
# already exists. A run head that is really a branch target inside a larger
# body yields a function starting mid-routine -- still better than nothing for
# attribution, but it means a forced name is not proof of an entry point.
#
# Args: <out> [minRunBytes] [dry]
import re
from jpype import JArray, JByte


def main():
    UNCOVERED = re.compile(b'\x00+')
    from ghidra.app.cmd.function import CreateFunctionCmd
    from ghidra.app.cmd.disassemble import DisassembleCommand

    args = getScriptArgs()
    out = open(args[0], 'w')
    min_run = 16
    dry = False
    for a in args[1:]:
        if a == 'dry':
            dry = True
        else:
            min_run = int(a)

    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    fm = prog.getFunctionManager()
    mem = prog.getMemory()
    listing = prog.getListing()

    total_made = 0
    total_disasm = 0
    total_failed = 0
    total_runs = 0

    for block in mem.getBlocks():
        if not block.isExecute() or not block.isInitialized():
            continue

        start = block.getStart().getOffset()
        end = block.getEnd().getOffset() + 1
        size = end - start

        covered = bytearray(size)
        ONES = b'\x01' * size
        it = fm.getFunctions(block.getStart(), True)
        while it.hasNext():
            f = it.next()
            body = f.getBody()
            if body.getMinAddress().getOffset() >= end:
                break
            for rng in body:
                lo = max(rng.getMinAddress().getOffset(), start)
                hi = min(rng.getMaxAddress().getOffset() + 1, end)
                if hi > lo:
                    covered[lo - start:hi - start] = ONES[:hi - lo]

        buf = JArray(JByte)(size)
        mem.getBytes(block.getStart(), buf)
        raw = bytearray(bytes(buf))

        heads = []
        # Maximal uncovered runs via the regex engine; a per-byte CPython loop
        # here was the slow part of the script.
        for mt in UNCOVERED.finditer(covered):
            i, j = mt.start(), mt.end()
            k = i
            while k < j and raw[k] in (0x90, 0xCC, 0x00):
                k += 1
            if j - k >= min_run:
                heads.append(start + k)

        total_runs += len(heads)
        out.write("== block %s: %d uncovered runs >= %d bytes\n"
                  % (block.getName(), len(heads), min_run))
        if dry:
            continue

        for addr_off in heads:
            addr = af.getAddress(addr_off)
            if fm.getFunctionContaining(addr) is not None:
                continue
            if listing.getInstructionAt(addr) is None:
                DisassembleCommand(addr, None, True).applyTo(prog)
                total_disasm += 1
            if listing.getInstructionAt(addr) is None:
                total_failed += 1
                continue
            if CreateFunctionCmd(addr).applyTo(prog):
                total_made += 1
            else:
                total_failed += 1

    out.write("\nruns considered   %d\n" % total_runs)
    out.write("disassembled      %d\n" % total_disasm)
    out.write("functions created %d\n" % total_made)
    out.write("failed            %d\n" % total_failed)
    out.close()
    print("WROTE " + args[0])


main()

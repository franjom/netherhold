# Measure how much executable space auto-analysis never turned into functions.
# Walks every executable block, subtracts the bytes covered by defined function
# bodies, and reports the remaining runs: total size, count, and the largest
# ones, plus how many of those runs begin with an MSVC prologue (55 8B EC).
# Reports only; changes nothing. Args: <out> [minRunBytes]
import re
from jpype import JArray, JByte


def main():
    UNCOVERED = re.compile(b'\x00+')

    args = getScriptArgs()
    out = open(args[0], 'w')
    min_run = int(args[1]) if len(args) > 1 else 16

    prog = currentProgram
    fm = prog.getFunctionManager()
    mem = prog.getMemory()
    listing = prog.getListing()

    for block in mem.getBlocks():
        if not block.isExecute() or not block.isInitialized():
            continue

        start = block.getStart().getOffset()
        end = block.getEnd().getOffset() + 1
        size = end - start

        covered = bytearray(size)
        ONES = b'\x01' * size
        fcount = 0
        it = fm.getFunctions(block.getStart(), True)
        while it.hasNext():
            f = it.next()
            body = f.getBody()
            if body.getMinAddress().getOffset() >= end:
                break
            fcount += 1
            for rng in body:
                lo = max(rng.getMinAddress().getOffset(), start)
                hi = min(rng.getMaxAddress().getOffset() + 1, end)
                if hi > lo:
                    covered[lo - start:hi - start] = ONES[:hi - lo]

        buf = JArray(JByte)(size)
        mem.getBytes(block.getStart(), buf)
        raw = bytearray(bytes(buf))

        runs = []
        # Maximal uncovered runs via the regex engine; a per-byte CPython loop
        # here was the slow part of the script.
        for mt in UNCOVERED.finditer(covered):
            i, j = mt.start(), mt.end()
            # Skip the alignment padding a run inherits from the function before it.
            k = i
            while k < j and raw[k] in (0x90, 0xCC, 0x00):
                k += 1
            if j - k >= min_run:
                runs.append((start + k, j - k))

        gap = sum(n for _, n in runs)
        prologues = 0
        instr_runs = 0
        for addr, n in runs:
            o = addr - start
            if raw[o] == 0x55 and raw[o + 1] == 0x8B and raw[o + 2] == 0xEC:
                prologues += 1
            if listing.getInstructionAt(block.getStart().add(o)) is not None:
                instr_runs += 1

        out.write("== block %s %08x len %d\n" % (block.getName(), start, size))
        out.write("   defined functions      %d\n" % fcount)
        out.write("   uncovered runs >=%-4d  %d\n" % (min_run, len(runs)))
        out.write("   uncovered bytes        %d (%.1f%% of block)\n"
                  % (gap, 100.0 * gap / size))
        out.write("   runs starting 55 8B EC %d\n" % prologues)
        out.write("   runs already disasm'd  %d\n" % instr_runs)
        runs.sort(key=lambda r: -r[1])
        out.write("   largest:\n")
        for addr, n in runs[:25]:
            o = addr - start
            tag = "prologue" if (raw[o] == 0x55 and raw[o + 1] == 0x8B and raw[o + 2] == 0xEC) else ""
            out.write("     %08x  %6d  %02x %02x %02x %s\n"
                      % (addr, n, raw[o], raw[o + 1], raw[o + 2], tag))

    out.close()
    print("WROTE " + args[0])


main()

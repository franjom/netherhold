# Ghidra PyGhidra script. Args: <out> <hex-address-file>.
# The file keeps large verification batches below Windows' command-line limit.
from jpype import JArray, JByte


def main():
    output, address_file = getScriptArgs()
    with open(address_file, 'r') as stream:
        wanted = [int(line.strip(), 16) for line in stream if line.strip()]

    program = currentProgram
    memory = program.getMemory()
    listing = program.getListing()
    space = program.getAddressFactory().getDefaultAddressSpace()
    blocks = []
    for block in memory.getBlocks():
        if not block.isInitialized():
            continue
        start = block.getStart().getOffset()
        size = int(block.getEnd().getOffset() - start + 1)
        data = JArray(JByte)(size)
        count = memory.getBytes(block.getStart(), data)
        blocks.append((block.getName(), start, bytes(data), block.isExecute(), count, size))

    with open(output, 'w') as stream:
        for name, start, _, executable, count, size in blocks:
            stream.write('block %s %08x len %d read %d exec=%s\n' %
                         (name, start, size, count, executable))
        stream.write('\n')
        for value in wanted:
            needle = value.to_bytes(4, 'little')
            hits = []
            for name, start, data, executable, _, _ in blocks:
                position = data.find(needle)
                while position >= 0:
                    hits.append((name, start + position, executable))
                    position = data.find(needle, position + 1)
            stream.write('== %08x : %d hits across all initialised blocks\n' %
                         (value, len(hits)))
            for name, address, executable in hits:
                instruction = listing.getInstructionContaining(space.getAddress(address))
                if instruction is None:
                    stream.write('   %-10s %08x  (no instruction here%s)\n' %
                                 (name, address, '; EXECUTABLE block - undisassembled' if executable else ''))
                else:
                    stream.write('   %-10s %08x  %s  %s\n' %
                                 (name, address, instruction.getAddress(), instruction.toString()))
            stream.write('\n')
    print('WROTE ' + output)


main()

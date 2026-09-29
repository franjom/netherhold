# Dump signed dwords. Args: <outPath> <startHex> <count>


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    start = int(args[1], 16)
    count = int(args[2])
    mem = currentProgram.getMemory()
    space = currentProgram.getAddressFactory().getDefaultAddressSpace()
    for i in range(count):
        address = space.getAddress(start + i * 4)
        out.write("%08x %d 0x%08x\n" % (address.getOffset(), mem.getInt(address), mem.getInt(address) & 0xffffffff))
    out.close()


main()

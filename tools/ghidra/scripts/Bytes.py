# Dump words/bytes at an address. Args: <out> <addrHex> <countWords>
from jpype import JArray, JByte


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    mem = prog.getMemory()
    a = int(args[1], 16)
    n = int(args[2])
    buf = JArray(JByte)(n * 2)
    got = mem.getBytes(af.getAddress(a), buf)
    buf = bytes(buf)
    out.write("read %d bytes at %08x\n" % (got, a))
    for i in range(n):
        lo = buf[i * 2] & 0xFF
        hi = buf[i * 2 + 1] & 0xFF
        v = lo | (hi << 8)
        s = v - 0x10000 if v >= 0x8000 else v
        out.write("  [%2d] %08x  u16=%5d  s16=%6d\n" % (i, a + i * 2, v, s))
    out.close()
    print("WROTE " + args[0])


main()

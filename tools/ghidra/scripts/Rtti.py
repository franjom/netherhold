# Recover a class name from DKII.EXE's own RTTI.
#
# DKII.EXE ships MSVC run-time type information, which means any vtable can be
# traced back to the class it belongs to. The chain is:
#
#   vtable base -> the dword IMMEDIATELY BEFORE it   = complete object locator
#   locator + 0x00                                   = signature (0 for 32-bit)
#   locator + 0x04                                   = offset of this base in the object
#   locator + 0x0C                                   = type descriptor
#   descriptor + 0x08                                = decorated name, NUL-terminated
#
# An offset of 0 means the vtable is the class's primary one, the one at [this];
# a non-zero offset means it is a secondary base, i.e. multiple inheritance.
#
# Args: <out> <addrHex>...
#
# Each address may be either a vtable base or ANY code pointer stored inside a
# vtable -- for the latter the script walks backwards to the base, stopping at
# the first dword that is not a code pointer. That makes it usable straight
# from a "1 absolute reference, in .rdata" scan result, which is the situation
# it was written for.
#
# WHAT MAY LEAVE THIS FOLDER: the addresses, the offset, and whether two
# vtables share a class. The decorated name is the original's own internal
# identifier -- it is a fact, but it is not ours, and it buys nothing a scan
# consumes. Keep it out of docs/ and src/. See ADR-0046 and the README.


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')

    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    mem = prog.getMemory()


    def dword(a):
        return mem.getInt(af.getAddress(a)) & 0xFFFFFFFF


    def is_code(v):
        # Both executable blocks. A .text-only test would reject anything in cseg
        # and silently mis-locate a vtable base that starts there.
        return (0x00401000 <= v < 0x00652C00) or (0x00653000 <= v < 0x0066C000)


    def cstring(a, limit=512):
        chars = []
        for i in range(limit):
            b = mem.getByte(af.getAddress(a + i)) & 0xFF
            if b == 0:
                break
            chars.append(chr(b))
        return ''.join(chars)


    for arg in args[1:]:
        a = int(arg, 16)
        out.write("== %08x\n" % a)

        # Walk back to the vtable base if we were handed an entry rather than the base.
        base = a
        try:
            while is_code(dword(base - 4)):
                base -= 4
        except Exception:
            out.write("   could not read below %08x\n" % base)
            continue

        slot = a - base
        out.write("   vtable base %08x, slot 0x%X (index %d)\n" % (base, slot, slot // 4))

        locator = dword(base - 4)
        out.write("   complete object locator %08x\n" % locator)
        if not (0x00400000 <= locator < 0x00800000):
            out.write("   ! not a plausible locator; this may not be an RTTI vtable\n")
            continue

        signature = dword(locator + 0x00)
        offset = dword(locator + 0x04)
        descriptor = dword(locator + 0x0C)
        out.write("   signature %d, base offset in object 0x%X%s\n"
                  % (signature, offset,
                     "  (PRIMARY, at [this])" if offset == 0 else "  (SECONDARY base)"))
        out.write("   type descriptor %08x\n" % descriptor)

        if 0x00400000 <= descriptor < 0x00800000:
            out.write("   name %s\n" % cstring(descriptor + 0x08))
        else:
            out.write("   ! type descriptor out of range\n")

    out.close()
    print("WROTE %s" % args[0])


main()

# List MSVC RTTI type names containing a substring, with every vtable whose complete object
# locator points at that type descriptor, and chosen slots of each vtable.
# Args: <out> <substring> <slotHex> [...]

def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    needle = args[1]
    slots = [int(a, 16) for a in args[2:]]
    prog = currentProgram
    mem = prog.getMemory()
    refs = prog.getReferenceManager()
    af = prog.getAddressFactory().getDefaultAddressSpace()
    start = mem.getMinAddress()
    pattern = b".?AV"
    addr = mem.findBytes(start, pattern, None, True, monitor)
    while addr is not None:
        name = []
        a = addr
        for _ in range(96):
            b = mem.getByte(a) & 0xFF
            if b == 0:
                break
            name.append(chr(b))
            a = a.add(1)
        text = "".join(name)
        if needle.lower() in text.lower():
            descriptor = addr.subtract(8)
            out.write("type %s descriptor %s\n" % (text, descriptor))
            # locators hold the descriptor at +0x0C; scan memory for that dword
            d = descriptor.getOffset()
            pat = bytes([d & 0xFF, (d >> 8) & 0xFF, (d >> 16) & 0xFF, (d >> 24) & 0xFF])
            hit = mem.findBytes(start, pat, None, True, monitor)
            while hit is not None:
                locator = hit.subtract(0x0C)
                lo = locator.getOffset()
                lpat = bytes([lo & 0xFF, (lo >> 8) & 0xFF, (lo >> 16) & 0xFF, (lo >> 24) & 0xFF])
                meta = mem.findBytes(start, lpat, None, True, monitor)
                while meta is not None:
                    vt = meta.add(4)
                    vals = []
                    for s in slots:
                        try:
                            vals.append("+0x%X=0x%08X" % (s, mem.getInt(vt.add(s)) & 0xFFFFFFFF))
                        except Exception:
                            vals.append("+0x%X=?" % s)
                    out.write("  vtable %s offset %d  %s\n" % (vt, mem.getInt(locator.add(4)), " ".join(vals)))
                    meta = mem.findBytes(meta.add(1), lpat, None, True, monitor)
                hit = mem.findBytes(hit.add(1), pat, None, True, monitor)
        addr = mem.findBytes(addr.add(1), pattern, None, True, monitor)
    out.close()

main()

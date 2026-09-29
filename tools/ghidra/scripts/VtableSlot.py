# Find vtables through one known member and print another slot of each.
# For every function containing an instruction that mentions <needle>, every data reference to its
# entry is taken as a vtable entry at <knownSlot>; the dword at base + <wantSlot> is printed.
# Args: <out> <needle> <knownSlotHex> <wantSlotHex>

def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    needle = args[1].lower()
    known = int(args[2], 16)
    want = int(args[3], 16)
    prog = currentProgram
    listing = prog.getListing()
    fm = prog.getFunctionManager()
    mem = prog.getMemory()
    refs = prog.getReferenceManager()
    seen = set()
    for ins in listing.getInstructions(True):
        if needle not in ins.toString().lower():
            continue
        f = fm.getFunctionContaining(ins.getAddress())
        if f is None or f.getEntryPoint() in seen:
            continue
        seen.add(f.getEntryPoint())
        size = f.getBody().getNumAddresses()
        for r in refs.getReferencesTo(f.getEntryPoint()):
            if r.getReferenceType().isCall() or r.getReferenceType().isJump():
                continue
            base = r.getFromAddress().subtract(known)
            try:
                target = mem.getInt(base.add(want)) & 0xFFFFFFFF
            except Exception:
                continue
            out.write("fn %s (%d bytes) ref %s base %s slot+0x%X -> 0x%08X\n" % (
                f.getEntryPoint(), size, r.getFromAddress(), base, want, target))
    out.close()

main()

# Every string a function's body references, so a nameless FUN_ can be
# identified from what it prints or asserts.
# Args: <out> <funcAddrHex> [<funcAddrHex> ...]


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    listing = prog.getListing()
    fm = prog.getFunctionManager()
    af = prog.getAddressFactory().getDefaultAddressSpace()
    dm = prog.getListing()

    for a in args[1:]:
        addr = af.getAddress(int(a, 16))
        f = fm.getFunctionContaining(addr)
        if f is None:
            out.write("== %s: no function\n" % a)
            continue
        out.write("== %s %s [%s .. %s]\n"
                  % (a, f.getName(), f.getEntryPoint(), f.getBody().getMaxAddress()))
        seen = set()
        it = listing.getInstructions(f.getBody(), True)
        while it.hasNext():
            ins = it.next()
            for ref in ins.getReferencesFrom():
                t = ref.getToAddress()
                d = dm.getDataAt(t)
                if d is None:
                    continue
                v = d.getValue()
                if v is None:
                    continue
                s = str(v)
                if len(s) < 4 or not d.hasStringValue():
                    continue
                key = (str(t), s)
                if key in seen:
                    continue
                seen.add(key)
                out.write("   %s  %s  %s\n" % (ins.getAddress(), t, s))
    out.close()
    print('WROTE %s' % args[0])


main()

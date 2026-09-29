# Disasm.py over several ranges. Args: <out> <startHex>:<endHex> [...]


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    listing = prog.getListing()
    af = prog.getAddressFactory().getDefaultAddressSpace()
    for spec in args[1:]:
        s, e = spec.split(':')
        start = af.getAddress(int(s, 16))
        end = af.getAddress(int(e, 16))
        out.write('===== %s .. %s =====\n' % (s, e))
        it = listing.getInstructions(start, True)
        while it.hasNext():
            ins = it.next()
            if ins.getAddress().getOffset() > end.getOffset():
                break
            refs = ''
            for r in ins.getReferencesFrom():
                d = listing.getDataAt(r.getToAddress())
                if d is not None and d.hasStringValue():
                    refs += '   ; "%s"' % d.getValue()
            out.write('%s  %s%s\n' % (ins.getAddress(), ins.toString(), refs))
    out.close()
    print('WROTE %s' % args[0])


main()

# Every indirect CALL through a given vtable slot, with the K instructions
# before it, so the provenance of the callee object can be read.
#
# The cadence question for a virtual update is never answered by the call site
# alone: the caller never names the object. What it does do is LOAD it, from a
# field or a global, a few instructions earlier. This prints that window.
#
# Args: <out> <slotHex> [<k>]

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    args = getScriptArgs()
    out = open(args[0], 'w')
    slot = args[1].lower()
    if not slot.startswith('0x'):
        slot = '0x' + slot
    k = int(args[2]) if len(args) > 2 else 12
    want = '+ ' + slot + ']'
    db = insdb.load(currentProgram, runScript)
    listing = db.all()
    hits = []
    for i, (addr, fe, mnem, t, op0, sc) in enumerate(listing):
        if mnem != 'CALL':
            continue
        # dword ptr [REG + 0x18]  -- a vtable dispatch, not a direct call
        rep = op0.lower()
        if 'ptr [' not in rep or not rep.endswith(want):
            continue
        hits.append(i)
    out.write('%d indirect CALL sites through slot %s\n\n' % (len(hits), slot))
    for i in hits:
        addr, fe, _, t, _, _ = listing[i]
        out.write('== %08x in %s\n' % (addr, db.name(fe, '(no function)')))
        window = []
        j = i - 1
        while j >= 0 and len(window) < k:
            if fe is not None and listing[j][1] != fe:
                break
            window.append(listing[j])
            j -= 1
        window.reverse()
        for w in window:
            out.write('   %08x  %s\n' % (w[0], w[3]))
        out.write('   %08x  %s   <== the dispatch\n\n' % (addr, t))
    out.close()
    print('WROTE ' + args[0] + ' (%d sites)' % len(hits))


main()

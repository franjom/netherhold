# Find every instruction whose text mentions a given hex constant.
#
# Fixed 2026-09-11 (Q217): the argument was prefixed with '0x' unconditionally,
# so passing '0x20d' searched for the literal '0x0x20d' and reported ZERO HITS
# rather than an error -- a silent empty result that reads exactly like "this
# constant appears nowhere". Both forms are accepted now.
#
# A match must also end on a non-hex character, so 0x20d no longer reports
# 0x20d0 and 0x20da alongside it.
# Args: <out> <hexConstant> [<hexConstant> ...]     either '20d' or '0x20d'

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    import re

    args = getScriptArgs()
    out = open(args[0], 'w')
    pats = []
    for a in args[1:]:
        w = a.lower()
        if w.startswith('0x'):
            w = w[2:]
        if w == '':
            raise ValueError('empty hex constant in arguments')
        pats.append(re.compile('0x' + re.escape(w) + '(?![0-9a-f])'))
    db = insdb.load(currentProgram, runScript)
    n = 0
    for addr, fe, mnem, text, op0, sc in db.instructions():
        t = text.lower()
        if not any(p.search(t) for p in pats):
            continue
        out.write('%08x  %-46s in %s\n' % (addr, text, db.name(fe)))
        n += 1
    out.close()
    print('WROTE %s (%d)' % (args[0], n))


main()

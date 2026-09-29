# Every instruction that WRITES [reg + OFFSET], with its containing function.
# The mirror of ReadsAt.py: finds the writers of a field.
#
# Two bugs fixed 2026-09-11 (Q217). Both returned a confident wrong answer
# rather than an error, which is the dangerous kind:
#   - single-operand writes (DEC, INC, NEG, NOT) were skipped by a
#     getNumOperands() < 2 guard, so a countdown's decrement was invisible and
#     a field that is written every tick looked unwritten;
#   - CMP and TEST were reported as stores, because operand 0 was assumed to be
#     a destination. Two of the four hits for 0x20d were comparisons.
# Args: <out> <offsetHex> [<offsetHex> ...]

# Mnemonics whose operand 0 is read, not written. JMP and CALL matter because
# an indirect one through [reg + disp] reads a slot and writes nothing.

import os, sys


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import insdb

    READ_ONLY = ('CMP', 'TEST', 'PUSH', 'JMP', 'CALL', 'LEA')

    args = getScriptArgs()
    out = open(args[0], 'w')
    wanted = ['0x' + a.lower().replace('0x', '') for a in args[1:]]
    db = insdb.load(currentProgram, runScript)
    n = 0
    for addr, fe, mnem, text, op0, sc in db.instructions():
        if not op0:
            continue
        if mnem.upper() in READ_ONLY:
            continue
        dest = op0.lower()
        if '[' not in dest or '+' not in dest:
            continue
        tail = dest.split('+')[-1].strip().rstrip(']').strip()
        if tail not in wanted:
            continue
        out.write('%08x  %-46s in %s\n' % (addr, text, db.name(fe)))
        n += 1
    out.close()
    print('WROTE %s (%d)' % (args[0], n))


main()

# Instructions touching [reg + <off>] followed within 5 instructions by a TEST/AND/OR
# whose immediate has <bit> set (or clear, for AND masks). Args: <out> <offHex> <maskHex>
def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    off = '+ 0x' + args[1].lower()
    mask = int(args[2], 16)
    prog = currentProgram
    fm = prog.getFunctionManager()
    it = prog.getListing().getInstructions(True)
    pending = []
    while it.hasNext():
        ins = it.next()
        t = ins.toString()
        low = t.lower()
        keep = []
        for (a, src, n) in pending:
            hit = False
            if low.startswith('test') or low.startswith('and') or low.startswith('or') or low.startswith('bt'):
                parts = low.replace(',', ' ').split()
                for p in parts:
                    if p.startswith('0x'):
                        try:
                            v = int(p, 16)
                        except ValueError:
                            continue
                        if (v & mask) == mask or (low.startswith('and') and v & 0xFFFFFFFF == (~mask) & 0xFFFFFFFF):
                            hit = True
            if not hit and args[2].lower()=='8000':
                import re
                if re.match(r'(test|and) [abcd]h,0x80$', low) or re.match(r'shr e[a-d]x,0xf$', low):
                    hit = True
            if hit:
                f = fm.getFunctionContaining(a)
                out.write('%s\t%s\t%s\t%s\n' % (a, f.getEntryPoint() if f else '-', src, t))
            elif n < 5:
                keep.append((a, src, n + 1))
        pending = keep
        if off in low and ']' in low:
            if low.startswith('mov') or low.startswith('test') or low.startswith('and') or low.startswith('or'):
                # direct test on memory
                parts = low.replace(',', ' ').split()
                direct = False
                for p in parts:
                    if p.startswith('0x') and not p.startswith(off[2:]):
                        try:
                            v = int(p, 16)
                        except ValueError:
                            continue
                        if v != int(args[1], 16) and ((v & mask) == mask or v & 0xFFFFFFFF == (~mask) & 0xFFFFFFFF):
                            direct = True
                if direct and not low.startswith('mov'):
                    f = fm.getFunctionContaining(ins.getAddress())
                    out.write('%s\t%s\t%s\t%s\n' % (ins.getAddress(), f.getEntryPoint() if f else '-', 'direct', t))
                else:
                    pending.append((ins.getAddress(), t, 0))
    out.close()


main()

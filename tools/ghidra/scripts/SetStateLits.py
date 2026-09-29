# For every CALL to each target, the last PUSH before it (within 8 instructions) and the function.
# Args: <out> <targetHex> [...]


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    targets = [a.lower().lstrip('0') for a in args[1:]]
    prog = currentProgram
    listing = prog.getListing()
    fm = prog.getFunctionManager()
    it = listing.getInstructions(True)
    window = []
    while it.hasNext():
        ins = it.next()
        t = ins.toString()
        if t.startswith("CALL"):
            low = t.lower()
            for tg in targets:
                if tg in low:
                    push = "?"
                    for aa, tt in reversed(window):
                        if tt.startswith("PUSH"):
                            push = tt[5:]
                            break
                    f = fm.getFunctionContaining(ins.getAddress())
                    out.write("%s\t%s\t%s\t%s\n" % (tg, push, ins.getAddress(), f.getEntryPoint() if f else "-"))
            window = []
        else:
            window.append((ins.getAddress(), t))
            if len(window) > 8:
                window.pop(0)
    out.close()
    print("WROTE " + args[0])


main()

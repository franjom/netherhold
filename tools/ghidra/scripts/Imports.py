# List external/imported symbols matching a needle, with their reference sites.
# Args: <out> <needle>...


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    st = prog.getSymbolTable()
    rm = prog.getReferenceManager()
    needles = [n.lower() for n in args[1:]]
    it = st.getAllSymbols(True)
    while it.hasNext():
        s = it.next()
        nm = s.getName()
        low = nm.lower()
        if not any(n in low for n in needles):
            continue
        refs = rm.getReferencesTo(s.getAddress())
        rl = []
        for r in refs:
            rl.append(r.getFromAddress().toString())
        out.write("%s  %s  (%s)  refs=%d %s\n"
                  % (s.getAddress(), nm, s.getSymbolType(), len(rl), " ".join(rl[:20])))
    out.close()
    print("WROTE " + args[0])


main()

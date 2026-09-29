# Every memory block: name, range, r/w/x, initialised. Args: <out>


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    for b in currentProgram.getMemory().getBlocks():
        out.write("%-10s %s - %s  %s%s%s init=%s\n" % (
            b.getName(), b.getStart(), b.getEnd(),
            'r' if b.isRead() else '-', 'w' if b.isWrite() else '-',
            'x' if b.isExecute() else '-', b.isInitialized()))
    out.close()
    print("WROTE " + args[0])


main()

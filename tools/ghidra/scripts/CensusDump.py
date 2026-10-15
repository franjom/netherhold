# Census dump: function bounds, memory blocks with raw bytes, defined strings, RTTI vftables. Args: <outDir>
import json
import os


def main():
    out = getScriptArgs()[0]
    os.makedirs(out, exist_ok=True)
    prog = currentProgram
    fm = prog.getFunctionManager()
    with open(os.path.join(out, 'funcs.tsv'), 'w', encoding='utf-8') as f:
        it = fm.getFunctions(True)
        while it.hasNext():
            fn = it.next()
            body = fn.getBody()
            th = fn.getThunkedFunction(False)
            f.write('%x\t%x\t%x\t%d\t%s\t%s\n' % (
                fn.getEntryPoint().getOffset(), body.getMinAddress().getOffset(),
                body.getMaxAddress().getOffset(), body.getNumAddresses(),
                '%x' % th.getEntryPoint().getOffset() if th is not None else '-',
                fn.getName()))
    mem = prog.getMemory()
    from jpype import JArray, JByte
    with open(os.path.join(out, 'blocks.tsv'), 'w') as f:
        for b in mem.getBlocks():
            s, e = b.getStart().getOffset(), b.getEnd().getOffset()
            f.write('%s\t%x\t%x\t%d\t%d\t%d\n' % (b.getName(), s, e, int(b.isExecute()), int(b.isInitialized()), int(b.isWrite())))
            if b.isInitialized():
                n = e - s + 1
                buf = JArray(JByte)(n)
                b.getBytes(b.getStart(), buf)
                with open(os.path.join(out, 'blk_%x.bin' % s), 'wb') as g:
                    g.write(bytes(buf))
    listing = prog.getListing()
    with open(os.path.join(out, 'strings.tsv'), 'w', encoding='utf-8') as f:
        di = listing.getDefinedData(True)
        while di.hasNext():
            d = di.next()
            if d.hasStringValue():
                v = d.getValue()
                s = json.dumps(str(v))
                f.write('%x\t%s\n' % (d.getAddress().getOffset(), s))
    st = prog.getSymbolTable()
    with open(os.path.join(out, 'vft.tsv'), 'w', encoding='utf-8') as f:
        si = st.getAllSymbols(False)
        while si.hasNext():
            sym = si.next()
            nm = sym.getName()
            if nm in ('vftable', "`vftable'") or nm.startswith('vftable') or 'vftable' in nm:
                f.write('%x\t%s\t%s\n' % (sym.getAddress().getOffset(), sym.getParentNamespace().getName(True), nm))
    print('WROTE %s' % out)


main()

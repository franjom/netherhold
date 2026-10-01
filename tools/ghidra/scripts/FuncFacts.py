# Mechanical facts about functions, for tools/verification/check_function_records.py.
# Args: <outJsonPath> <addrListPath>   (addrListPath: one hex address per line)
# Writes only addresses, counts and operand values, never decompiler text.


def main():
    import json
    import re

    args = getScriptArgs()
    outpath = args[0]
    addrs = [int(x.strip(), 16) for x in open(args[1]) if x.strip()]

    prog = currentProgram
    listing = prog.getListing()
    fm = prog.getFunctionManager()
    af = prog.getAddressFactory().getDefaultAddressSpace()
    refman = prog.getReferenceManager()
    hexre = re.compile(r'0x[0-9a-fA-F]+')

    result = {}
    for a in addrs:
        entry = af.getAddress(a)
        fn = fm.getFunctionAt(entry)
        rec = {'isEntry': fn is not None}
        if fn is None:
            cont = fm.getFunctionContaining(entry)
            rec['containing'] = cont.getEntryPoint().getOffset() if cont else None
            result['%08X' % a] = rec
            continue
        calls, scalars, refs, internal = set(), set(), set(), set()
        ret_n = None
        count = 0
        body = fn.getBody()
        lo, hi = None, None
        for r in body.getAddressRanges():
            s, e = r.getMinAddress().getOffset(), r.getMaxAddress().getOffset()
            lo = s if lo is None else min(lo, s)
            hi = e if hi is None else max(hi, e)
        it = listing.getInstructions(body, True)
        while it.hasNext():
            ins = it.next()
            count += 1
            text = ins.toString()
            for m in hexre.findall(text):
                scalars.add(int(m, 16))
            mn = ins.getMnemonicString()
            if mn == 'RET' and ret_n is None:
                ret_n = 0
                if ins.getNumOperands() > 0:
                    for m in hexre.findall(text):
                        ret_n = int(m, 16)
            for ref in ins.getReferencesFrom():
                t = ref.getToAddress()
                if not t.isMemoryAddress():
                    continue
                off = t.getOffset()
                if ref.getReferenceType().isCall():
                    calls.add(off)
                elif body.contains(t):
                    internal.add(off)
                else:
                    refs.add(off)
        callers, caller_fns, caller_ranges = set(), set(), {}
        for ref in refman.getReferencesTo(entry):
            fa = ref.getFromAddress()
            callers.add(fa.getOffset())
            cf = fm.getFunctionContaining(fa)
            if cf is not None:
                caller_fns.add(cf.getEntryPoint().getOffset())
                b = cf.getBody()
                caller_ranges[cf.getEntryPoint().getOffset()] = [
                    b.getMinAddress().getOffset(), b.getMaxAddress().getOffset()]
        rec.update({
            'name': fn.getName(), 'low': lo, 'high': hi, 'size': (hi - lo + 1) if lo is not None else 0,
            'instructions': count, 'retN': ret_n,
            'calls': sorted(calls), 'refs': sorted(refs), 'internal': sorted(internal),
            'scalars': sorted(scalars), 'callers': sorted(callers), 'callerFns': sorted(caller_fns), 'callerRanges': list(caller_ranges.values()),
        })
        result['%08X' % a] = rec
    json.dump(result, open(outpath, 'w'))


main()

# Record sizes from the allocator: for every function that installs a primary
# vtable (MOV dword ptr [reg],imm with an RTTI locator before imm), find its call
# sites and the PUSH imm fed to the allocation CALL just before each one.
# Also lists every instruction that writes through a table address given.
# Args: <out> <tableHex>...
import re


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    tables = [int(a, 16) for a in args[1:]]
    prog = currentProgram
    mem = prog.getMemory()
    listing = prog.getListing()
    fm = prog.getFunctionManager()
    refs = prog.getReferenceManager()
    af = prog.getAddressFactory().getDefaultAddressSpace()

    def u32(a):
        try:
            return mem.getInt(af.getAddress(a)) & 0xFFFFFFFF
        except Exception:
            return None

    def cstr(a, n=200):
        s = []
        for i in range(n):
            try:
                b = mem.getByte(af.getAddress(a + i)) & 0xFF
            except Exception:
                return None
            if b == 0:
                break
            s.append(chr(b))
        return ''.join(s)

    name_cache = {}

    def rtti(v):
        if v in name_cache:
            return name_cache[v]
        res = None
        loc = u32(v - 4)
        if loc and u32(loc) == 0:
            off = u32(loc + 4)
            td = u32(loc + 0xC)
            if td:
                n = cstr(td + 8)
                if n and n.startswith('.?A'):
                    res = (n, off)
        name_cache[v] = res
        return res

    pat = re.compile(r'^MOV dword ptr \[(E[A-Z]{2})\],0x([0-9a-f]+)$')
    ctors = {}   # function entry -> list of (vtable, name)
    table_writes = []
    tstrs = ['0x%x' % t for t in tables]
    it = listing.getInstructions(True)
    while it.hasNext():
        ins = it.next()
        s = ins.toString()
        m = pat.match(s)
        if m:
            v = int(m.group(2), 16)
            r = rtti(v)
            if r and r[1] == 0:
                f = fm.getFunctionContaining(ins.getAddress())
                if f is not None:
                    ctors.setdefault(f.getEntryPoint().getOffset(), []).append((v, r[0]))
        if tstrs and ',' in s:
            dst = s.split(',', 1)[0]
            for t in tstrs:
                if t in dst and '[' in dst and not s.startswith(('CMP', 'TEST', 'PUSH', 'LEA')):
                    f = fm.getFunctionContaining(ins.getAddress())
                    table_writes.append((ins.getAddress().getOffset(),
                                         f.getEntryPoint().getOffset() if f else 0, s))

    out.write('== table writers\n')
    for a, fe, s in table_writes:
        out.write('%08x in %08x  %s\n' % (a, fe, s))

    out.write('\n== constructors: last vtable installed, allocation sizes at call sites\n')
    for fe in sorted(ctors):
        vts = ctors[fe]
        v, n = vts[-1]
        sizes = []
        for ref in refs.getReferencesTo(af.getAddress(fe)):
            if not ref.getReferenceType().isCall():
                continue
            site = ref.getFromAddress()
            ins = listing.getInstructionAt(site)
            push = newcall = None
            for _ in range(14):
                if ins is None:
                    break
                ins = ins.getPrevious()
                if ins is None:
                    break
                t = ins.toString()
                if t.startswith('CALL') and newcall is None:
                    newcall = t
                elif newcall is not None and t.startswith('PUSH 0x'):
                    push = int(t[7:], 16)
                    break
            sizes.append('%08x:%s/%s' % (site.getOffset(),
                                         '0x%x' % push if push is not None else '-',
                                         newcall.replace('CALL ', '') if newcall else '-'))
        out.write('%08x vt=%08x %s | installs %d | %s\n'
                  % (fe, v, n, len(vts), ' '.join(sizes[:8])))
    out.close()


main()

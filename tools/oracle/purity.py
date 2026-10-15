# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
"""Which DKII.EXE functions the oracle (ADR-0481) can run, and which of those we have ported.

Pure here means: no reference to a writable global that any instruction writes directly (one
nothing writes is reported, as UNWRITTEN-GLOBALS, and treated as a constant), no indirect or
import call, no FS or port I/O, and every direct callee pure. Pointer arguments are allowed:
the test lays out what they point at. Writes through a pointer cannot be seen, so a global
written only that way passes; read the function before trusting the verdict. Not pure is
not the same as not runnable: a function that only writes globals (Random_Next's bookkeeping)
still runs, sandboxed, but the list leaves it out. The list holds candidates, not verdicts.

Reads the instruction cache tools/ghidra/scripts/insdb.py keeps. Its output names functions of
the binary, so it goes under reverse-engineering/ (ADR-0046). Run from the repository root:

    python tools/oracle/purity.py reverse-engineering/.cache/listing-<tag>.tsv <DKII.EXE> . reverse-engineering/census/<date>-oracle

oracle-candidates.txt: pure and ported (cited in src/ or a BUILT record), closure of at least
12 instructions, untested first, then by closure size; T marks one the oracle already runs.
oracle-near-misses.txt: small ported functions with exactly one own obstacle.
"""
import sys, re, os, struct, glob, collections

listing, exe, repo, out = sys.argv[1:5]
os.makedirs(out, exist_ok=True)

# PE sections
b = open(exe, 'rb').read()
pe = struct.unpack_from('<I', b, 0x3C)[0]
nsec = struct.unpack_from('<H', b, pe + 6)[0]
optsz = struct.unpack_from('<H', b, pe + 20)[0]
base = struct.unpack_from('<I', b, pe + 24 + 28)[0]
secs = []
for i in range(nsec):
    o = pe + 24 + optsz + 40 * i
    name = b[o:o + 8].rstrip(b'\0').decode()
    vsz, va, rsz, raw, _, _, _, _, ch = struct.unpack_from('<IIIIIIHHI', b, o + 8)
    secs.append((name, base + va, base + va + max(vsz, rsz), bool(ch & 0x80000000), bool(ch & 0x20000000)))
print('sections:', [(n, hex(a), hex(e), 'W' if w else '-', 'X' if x else '-') for n, a, e, w, x in secs])

def sec(a):
    for n, s, e, w, x in secs:
        if s <= a < e: return n, w, x
    return None

funcs = {}
ins = collections.defaultdict(list)
for line in open(listing, encoding='utf-8', errors='replace'):
    p = line.rstrip('\n').split('\t')
    if p[0] == 'F': funcs[int(p[1], 16)] = p[2]
    elif p[0] == 'I' and p[2] not in ('', '-'):
        ins[int(p[2], 16)].append((int(p[1], 16), p[3], p[4]))

hexre = re.compile(r'0x([0-9a-f]+)')
owner = {a: f for f, lst in ins.items() for a, _, _ in lst}
WRITE1 = {'INC', 'DEC', 'NEG', 'NOT', 'POP', 'FNSTCW', 'FNSTSW', 'FNSTENV', 'FSTENV', 'FNSAVE', 'FSAVE'}
FPU_CONTROL = {'FLDCW', 'FLDENV', 'FRSTOR'}
REACH = 0x4000   # how far past its displacement an indexed access may land
READONLY_FIRST = {'CMP', 'TEST', 'PUSH', 'FLD', 'FILD', 'FCOM', 'FCOMP', 'FADD', 'FMUL', 'FSUB', 'FDIV', 'FSUBR', 'FDIVR', 'FIDIV', 'FIMUL', 'FICOM', 'FLDCW', 'BT', 'JMP', 'CALL'}
written = []      # (start, end) of global bytes some instruction writes directly
refs = collections.defaultdict(set)
calls = collections.defaultdict(set)
why = collections.defaultdict(set)
for f, lst in ins.items():
    for a, m, t in lst:
        if m == 'CALL' or m == 'JMP':
            tgt = t.split(' ', 1)[1] if ' ' in t else ''
            mm = re.fullmatch(r'0x([0-9a-f]+)', tgt)
            if mm:
                c = int(mm.group(1), 16)
                if c in funcs and c != f: calls[f].add(c)
                elif m == 'CALL' and c not in funcs: why[f].add('call into no function')
                elif m == 'JMP' and owner.get(c) != f: why[f].add('jump out of the function')
            elif m == 'CALL': why[f].add('indirect call')
            elif m == 'JMP' and '[' in tgt and not re.search(r'\*0x4 \+ 0x', tgt): why[f].add('indirect jmp')
            continue
        if m[0] == 'J' or m.startswith('LOOP'): continue
        if 'FS:' in t: why[f].add('FS')
        if m in ('IN', 'OUT', 'INT', 'RDTSC', 'CPUID', 'INSB', 'OUTSB', 'HLT', 'CLI', 'STI'): why[f].add(m)
        if m in FPU_CONTROL: why[f].add('FPU control word')
        ops = t.split(' ', 1)[1] if ' ' in t else ''
        first = ops.split(',', 1)[0]
        for mm in hexre.finditer(ops):
            v = int(mm.group(1), 16)
            s = sec(v)
            if not s: continue
            n, w, x = s
            if x:
                if ops[:mm.start()].count('[') == ops[:mm.start()].count(']'):
                    why[f].add('code address as data')  # function pointer taken
                continue
            if not w: continue
            depth = ops[:mm.start()].count('[') - ops[:mm.start()].count(']')
            if depth == 0:
                why[f].add('address of a global taken')   # a pointer to it may be written through
                continue
            inner = ops[ops.rfind('[', 0, mm.start()):ops.find(']', mm.start())]
            span = (v, v + (REACH if re.search(r'E[A-Z]{2}', inner) else 8))
            refs[f].add(span)
            inmem = '[' in first and mm.start() < len(first)
            if inmem and (m not in READONLY_FIRST) and (',' in ops or m in WRITE1 or m.startswith('SET') or m.startswith('FST') or m.startswith('FIST')):
                written.append(span)
# jump tables are fine; tables in .data that are never written are treated as constant
import bisect
written.sort()
starts = [a for a, _ in written]
widest = max((e - a for a, e in written), default=0)
def overlaps(a, e):
    i = bisect.bisect_left(starts, e)
    return any(written[j][1] > a for j in range(bisect.bisect_left(starts, a - widest), i))
for f in ins:
    for a, e in refs[f]:
        why[f].add('writable global' if overlaps(a, e) else '~unwritten global')

# A fixed point, so a cycle is pure only if every member is.
pure = {f: not any(not w.startswith('~') for w in why[f]) for f in ins}
changed = True
while changed:
    changed = False
    for f in ins:
        if pure[f] and any(not pure.get(c, False) for c in calls[f]):
            pure[f], changed = False, True

def closure(f):
    seen, todo = set(), [f]
    while todo:
        g = todo.pop()
        if g in seen: continue
        seen.add(g); todo.extend(calls[g])
    return seen

# what src ports, and what the oracle already runs
cited = collections.defaultdict(set)
for path in glob.glob(os.path.join(repo, 'src', '**', '*.cs'), recursive=True):
    for mm in re.finditer(r'0x00([4-6][0-9A-Fa-f]{5})\b', open(path, encoding='utf-8', errors='replace').read()):
        cited[int(mm.group(1), 16) + 0].add(os.path.relpath(path, repo).replace('\\', '/'))
tested = set()
for path in glob.glob(os.path.join(repo, 'tests', 'DK2.Oracle.Tests', '*.cs')):
    for mm in re.finditer(r'0x00([4-6][0-9A-Fa-f]{5})\b', open(path, encoding='utf-8').read()):
        tested.add(int(mm.group(1), 16))
kb = {}
for path in glob.glob(os.path.join(repo, 'docs', 'research', 'functions', '0*.md')):
    t = open(path, encoding='utf-8').read()
    a = int(os.path.basename(path)[:8], 16)
    g = lambda k: (re.search(r'^' + k + r': (.*)$', t, re.M) or [None, ''])[1]
    kb[a] = (g('name'), g('confidence'), g('status'), g('implements'))

rows = []
for f in ins:
    if not pure[f]: continue
    built = f in cited or (f in kb and kb[f][2] == 'BUILT')
    if not built: continue
    if sum(len(ins[g]) for g in closure(f)) < 12: continue
    cl = closure(f)
    soft = sorted({w for g in cl for w in why[g] if w.startswith('~')})
    rows.append((f, len(ins[f]), sum(len(ins[g]) for g in cl), len(cl) - 1, f in tested,
                 kb.get(f, ('-',) * 4), sorted(cited.get(f, [])), soft))
rows.sort(key=lambda r: (r[4], r[2]))
with open(os.path.join(out, 'oracle-candidates.txt'), 'w', encoding='utf-8') as o:
    o.write('# pure (closure) and built/cited; tested first column\n')
    for f, n, tot, nc, t, k, cs, soft in rows:
        o.write(f"{'T' if t else ' '} {f:08X} ins={n} closure={tot} callees={nc} {k[0]} [{k[1]}/{k[2]}] {'; '.join(cs)} {'UNWRITTEN-GLOBALS' if soft else ''}\n")
# why built-but-impure functions fail, for the near misses
near = []
for f in ins:
    if pure[f] or not (f in cited or (f in kb and kb[f][2] == 'BUILT')): continue
    own = {w for w in why[f] if not w.startswith('~')}
    bad_callees = [c for c in calls[f] if not pure[c]]
    if len(ins[f]) <= 120 and len(own) <= 1 and not bad_callees:
        near.append((f, len(ins[f]), sorted(own), kb.get(f, ('-',))[0]))
with open(os.path.join(out, 'oracle-near-misses.txt'), 'w', encoding='utf-8') as o:
    for f, n, w, name in sorted(near, key=lambda r: r[1]):
        o.write(f'{f:08X} ins={n} {name} {w}\n')
print('functions', len(ins), 'pure', sum(pure.values()), 'built+pure', len(rows),
      'untested', sum(1 for r in rows if not r[4]), 'near misses', len(near))

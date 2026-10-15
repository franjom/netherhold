# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
"""Four censuses of DKII.EXE against the function records (docs/research/binary-census.md).

Reachability from the turn, the action table and the level loader; RTTI classes; strings held only
by unrecorded functions; globals read by recorded functions but written only by unrecorded ones.
Code Ghidra never put in a function gets pseudo-functions. Outputs hold DKII.EXE facts and RTTI
names, so they go under reverse-engineering/ (ADR-0046), never into tracked folders.

Run from the repository root. The dump is a headless Ghidra run (the MCP server cannot hand
back raw block bytes), so it waits for no other session but costs one Ghidra start.

    D=reverse-engineering/census/<date>
    python tools/ghidra/ghidra.py CensusDump.py <absolute D>/dump
    python tools/kb/census.py reverse-engineering/.cache/listing-<tag>.tsv $D/dump docs/research/functions . $D
    python tools/kb/census_refine.py $D/graph.pkl . $D/refined.txt

The listing cache is the one tools/ghidra/scripts/insdb.py keeps (any script that loads it writes it).
census_refine.py writes refined.txt and refined-reading-list.csv, the ranked unread frontier.
"""
import sys, os, re, struct, json, glob
from collections import defaultdict, deque, Counter

if len(sys.argv) != 6:
    sys.exit(__doc__)
LIST, DUMP, KB, SRC, OUT = sys.argv[1:6]
os.makedirs(OUT, exist_ok=True)
HEX = re.compile(r'0x([0-9a-fA-F]{6,8})')


def main():
    # ---- functions
    funcs = {}            # entry -> (lo, hi, size, thunk, name)
    for line in open(os.path.join(DUMP, 'funcs.tsv'), encoding='utf-8'):
        e, lo, hi, n, th, name = line.rstrip('\n').split('\t')
        funcs[int(e, 16)] = (int(lo, 16), int(hi, 16), int(n), None if th == '-' else int(th, 16), name)
    entries = set(funcs)
    sorted_lo = sorted((v[0], v[1], e) for e, v in funcs.items())

    import bisect
    los = [x[0] for x in sorted_lo]

    def containing(a):
        i = bisect.bisect_right(los, a) - 1
        if i >= 0 and sorted_lo[i][0] <= a <= sorted_lo[i][1]:
            return sorted_lo[i][2]
        return None

    # ---- memory
    blocks = []
    for line in open(os.path.join(DUMP, 'blocks.tsv')):
        name, s, e, x, init, w = line.rstrip('\n').split('\t')
        s, e = int(s, 16), int(e, 16)
        data = None
        if init == '1':
            data = open(os.path.join(DUMP, 'blk_%x.bin' % s), 'rb').read()
        blocks.append((name, s, e, x == '1', data, w == '1'))

    def blk(a):
        for b in blocks:
            if b[1] <= a <= b[2]:
                return b
        return None

    def dword(a):
        b = blk(a)
        if not b or b[4] is None or a + 4 > b[2] + 1:
            return None
        o = a - b[1]
        return struct.unpack_from('<I', b[4], o)[0]

    def is_code(a):
        b = blk(a)
        return b is not None and b[3]

    def is_data(a):
        b = blk(a)
        return b is not None and not b[3] and b[0] not in ('Headers', '.rsrc', 'tdb')

    def is_rw(a):
        b = blk(a)
        return b is not None and not b[3] and b[5]

    strings = {}
    for line in open(os.path.join(DUMP, 'strings.tsv'), encoding='utf-8'):
        a, s = line.rstrip('\n').split('\t', 1)
        strings[int(a, 16)] = json.loads(s)

    # ---- table expansion: function pointers in a run of dwords starting at a data address
    table_cache = {}

    def table_targets(a, depth=0):
        if a in table_cache:
            return table_cache[a]
        table_cache[a] = set()
        out = set()
        p = a
        miss = 0
        for _ in range(1024):
            v = dword(p)
            if v is None:
                break
            if v in entries:
                out.add(v)
                miss = 0
            elif depth < 1 and is_data(v) and v not in strings and v != a:
                # a table of tables (e.g. vtable pointers): follow one level
                out |= table_targets(v, depth + 1)
                miss = 0
            else:
                miss += 1
                if miss > 1:
                    break
            p += 4
        table_cache[a] = out
        return out

    # ---- walk the listing
    calls = defaultdict(set)      # direct call/jump edges
    ptr = defaultdict(set)        # function-pointer and table edges
    callers_direct = defaultdict(set)
    str_refs = defaultdict(set)   # string addr -> functions
    glob_rd = defaultdict(set)    # global -> functions touching it
    glob_wr = defaultdict(set)    # global -> functions writing it
    orphan_ins = 0
    orphan_calls_into = Counter()
    indirect_calls = Counter()
    WRITE_MN = {'MOV', 'ADD', 'SUB', 'INC', 'DEC', 'OR', 'AND', 'XOR', 'MOVSX', 'MOVZX', 'SHL', 'SHR', 'SAR', 'NEG', 'NOT', 'XCHG', 'FSTP', 'FST', 'FISTP', 'SETNZ', 'SETZ', 'LEA'}
    # ---- load the listing; give orphan code (in no Ghidra function) pseudo-functions
    ins = []
    with open(LIST, encoding='utf-8') as fh:
        for line in fh:
            if line[0] != 'I':
                continue
            _, a, fe, mn, text, op0, sc = line.rstrip('\n').split('\t')
            a = int(a, 16)
            f = int(fe, 16) if fe != '-' else containing(a)
            ins.append((a, f, mn, text, op0, sc))
    orphan_set = set(x[0] for x in ins if x[1] is None)
    cand = set()
    for a, f, mn, text, op0, sc in ins:
        if (mn == 'CALL' or mn.startswith('J')) and op0.startswith('0x'):
            t = int(op0, 16)
            if t in orphan_set and (mn == 'CALL' or f is not None):
                cand.add(t)
    n_call_cand = len(cand)
    for b in blocks:
        if b[3] or b[4] is None or b[0] in ('Headers', '.rsrc'):
            continue
        d = b[4]
        for o in range(0, len(d) - 3, 4):
            v = struct.unpack_from('<I', d, o)[0]
            if v in orphan_set:
                cand.add(v)
    n_data_cand = len(cand) - n_call_cand
    for path in glob.glob(os.path.join(KB, '0*.md')):
        m = re.search(r'^address:\s*0x([0-9A-Fa-f]+)', open(path, encoding='utf-8-sig').read(), re.M)
        if m and int(m.group(1), 16) in orphan_set:
            cand.add(int(m.group(1), 16))
    pseudo_of = {}
    prev_orphan = False
    cur = None
    run_heads = 0
    order = []
    for a, f, mn, text, op0, sc in ins:
        if f is not None:
            prev_orphan = False
            continue
        if not prev_orphan:
            run_heads += 1
            cur = a
        elif a in cand:
            cur = a
        pseudo_of[a] = cur
        order.append(a)
        prev_orphan = True
    pseudo_entries = set(pseudo_of.values())
    psize = Counter()
    for i, a in enumerate(order):
        nxt = order[i + 1] if i + 1 < len(order) else a + 1
        psize[pseudo_of[a]] += min(nxt - a, 16)
    for e in pseudo_entries:
        funcs[e] = (e, e + psize[e], psize[e], None, 'ORPHAN_%08X' % e)
    entries |= pseudo_entries
    ins = [(a, f if f is not None else pseudo_of.get(a), mn, text, op0, sc) for a, f, mn, text, op0, sc in ins]
    print('pseudo-functions %d (run heads %d, call targets %d, data-pointed %d), orphan instructions %d, bytes %d' % (
        len(pseudo_entries), run_heads, n_call_cand, n_data_cand, len(orphan_set), sum(psize.values())))
    if True:
        for a, f, mn, text, op0, sc in ins:
            if f is None:
                orphan_ins += 1
            nums = set(int(x, 16) for x in HEX.findall(text))
            if sc:
                nums |= set(int(x, 16) for x in sc.split(','))
            is_branch = mn == 'CALL' or mn.startswith('J')
            if is_branch and mn == 'CALL' and '[' in text or (mn == 'CALL' and not HEX.search(op0)):
                if f is not None:
                    indirect_calls[f] += 1
            for v in nums:
                if v in entries:
                    if f is None:
                        orphan_calls_into[v] += 1
                        continue
                    if v == f:
                        continue
                    if is_branch and op0.startswith('0x'):
                        calls[f].add(v)
                        callers_direct[v].add(f)
                    else:
                        ptr[f].add(v)
                elif f is not None and v >= 0x66c000 and is_data(v):
                    if v in strings:
                        str_refs[v].add(f)
                        continue
                    ts = table_targets(v)
                    ts.discard(f)
                    if ts:
                        ptr[f] |= ts
                    if is_rw(v):
                        glob_rd[v].add(f)
                        dst = op0
                        if mn in WRITE_MN and mn != 'LEA' and ('0x%08x' % v) in dst.lower() and '[' in dst:
                            glob_wr[v].add(f)
                elif f is not None and is_code(v) and v not in entries and mn in ('PUSH', 'MOV') and not op0.startswith('0x') is False:
                    pass

    edges = defaultdict(set)
    for f in entries:
        edges[f] = calls[f] | ptr[f]
    # thunks forward
    for e, v in funcs.items():
        if v[3] is not None and v[3] in entries:
            edges[e].add(v[3])

    # ---- RTTI vtables (MSVC): dword at vt-4 -> COL {0, off, cdoff, pTD, pCHD}; TD+8 = '.?AV'
    rdata = [b for b in blocks if b[0] == '.rdata'][0]
    vtables = {}
    data = rdata[4]
    for o in range(0, len(data) - 4, 4):
        col = struct.unpack_from('<I', data, o)[0]
        if not (rdata[1] <= col <= rdata[2] - 20):
            continue
        co = col - rdata[1]
        sig, off, cdo, ptd, pchd = struct.unpack_from('<5I', data, co)
        if sig != 0:
            continue
        b = blk(ptd)
        if not b or b[4] is None:
            continue
        to = ptd - b[1] + 8
        nm = b[4][to:to + 4]
        if nm not in (b'.?AV', b'.?AU'):
            continue
        end = b[4].find(b'\0', to)
        cname = b[4][to:end].decode('latin1')
        vt = rdata[1] + o + 4
        slots = []
        p = vt
        while True:
            v = dword(p)
            if v is None or v not in entries:
                break
            slots.append(v)
            p += 4
            if p > vt + 4 and (p - rdata[1] - 4) >= 0 and dword(p - 4 + 4) is not None:
                # stop at the next vtable's COL slot
                nxt = dword(p)
                if nxt is not None and rdata[1] <= nxt <= rdata[2] and nxt not in entries:
                    break
        if slots:
            vtables[vt] = (cname, off, slots)

    # ---- KB
    kb = {}
    for path in glob.glob(os.path.join(KB, '0*.md')):
        txt = open(path, encoding='utf-8-sig').read()
        m = re.search(r'^address:\s*0x([0-9A-Fa-f]+)', txt, re.M)
        if not m:
            continue
        a = int(m.group(1), 16)
        g = lambda k: (re.search(r'^%s:\s*(.*)$' % k, txt, re.M) or [None, ''])[1].strip()
        f = a if a in entries else (containing(a) or pseudo_of.get(a))
        kb[a] = dict(fn=f, name=g('name'), status=g('status'), conf=g('confidence'),
                     implements=re.findall(r'^implements:\s*(.*)$', txt, re.M))
    rec_fn = defaultdict(list)
    for a, r in kb.items():
        if r['fn'] is not None:
            rec_fn[r['fn']].append(a)
    recorded = set(rec_fn)
    mid_records = [a for a, r in kb.items() if r['fn'] is not None and r['fn'] != a]
    no_fn_records = [a for a, r in kb.items() if r['fn'] is None]

    def kbname(f):
        if f in rec_fn:
            return kb[rec_fn[f][0]]['name']
        return funcs[f][4]

    # ---- library / trivial triage
    size = {e: v[2] for e, v in funcs.items()}
    trivial = {e for e in entries if size[e] <= 8}
    # crude subsystem tags from strings
    lib_hint = {}
    LIBPAT = re.compile(r'(DirectDraw|Direct3D|DirectSound|DirectPlay|DirectInput|D3D|DDRAW|DSOUND|dplay|Bink|Smack|mpeg|MPEG|idct|runtime error|_CRT|R6\d\d\d|msvcrt|__|locale|TLOSS|SING|DOMAIN|Microsoft Visual C)', re.I)
    for s, fs in str_refs.items():
        if LIBPAT.search(strings[s]):
            for f in fs:
                lib_hint[f] = strings[s][:40]

    # ---- reachability
    ROOTS = {
        'turn': [0x0050A450],                 # World_UpdateTurn
        'actions': [0x00511A80],              # GameActions_RegisterHandlers (the action table)
        'load': [0x0050E620],                 # World_LoadLevelKwd
        'loop': [0x00526590],                 # game loop
    }
    hdr = blocks[0][4]
    pe = struct.unpack_from('<I', hdr, 0x3C)[0]
    entry_rva = struct.unpack_from('<I', hdr, pe + 0x28)[0]
    ROOTS['exe_entry'] = [0x400000 + entry_rva]

    def bfs(roots):
        dist = {}
        q = deque()
        for r in roots:
            if r in entries:
                dist[r] = 0
                q.append(r)
        while q:
            f = q.popleft()
            for g in edges[f]:
                if g not in dist:
                    dist[g] = dist[f] + 1
                    q.append(g)
        return dist

    reach = {k: bfs(v) for k, v in ROOTS.items()}
    sim = set(reach['turn']) | set(reach['actions']) | set(reach['load'])

    # ---- direct-only reach (no pointer edges) for comparison
    def bfs_direct(roots):
        seen = set(roots)
        q = deque(roots)
        while q:
            f = q.popleft()
            for g in calls[f]:
                if g not in seen:
                    seen.add(g)
                    q.append(g)
        return seen
    sim_direct = bfs_direct(ROOTS['turn'] + ROOTS['actions'] + ROOTS['load'])

    rep = []
    W = rep.append
    W('# Census report (scratch, local only)\n')
    W('functions %d (pseudo %d), records %d (in functions %d, mid-function %d, outside any function %d)' % (
        len(entries), len(pseudo_entries), len(kb), len(recorded), len(mid_records), len(no_fn_records)))
    W('orphan instructions (in no function) %d; functions called from orphan code %d' % (orphan_ins, len(orphan_calls_into)))
    W('vtables via RTTI %d, slots %d' % (len(vtables), sum(len(v[2]) for v in vtables.values())))
    W('exe entry 0x%08x' % ROOTS['exe_entry'][0])
    W('')
    W('## Reach (functions reachable, of which recorded)')
    for k in ROOTS:
        r = set(reach[k])
        W('%-10s %5d  recorded %4d  unrecorded %5d' % (k, len(r), len(r & recorded), len(r - recorded)))
    W('sim(turn|actions|load) %d recorded %d unrecorded %d; direct-calls-only sim %d' % (
        len(sim), len(sim & recorded), len(sim - recorded), len(sim_direct)))
    unreach_all = entries - set(reach['exe_entry']) - set(reach['loop']) - sim
    W('reached from nothing (incl. exe entry) %d; of these recorded %d' % (len(unreach_all), len(unreach_all & recorded)))
    W('recorded but not reachable from sim roots %d' % len(recorded - sim))
    W('')

    # ---- census 1: unread in sim, ranked
    unread_sim = sim - recorded
    nontriv = [f for f in unread_sim if f not in trivial]
    # unread subtree weight: unread functions reachable through unread-only paths
    def unread_weight(f):
        seen = {f}
        q = deque([f])
        tot = 0
        while q:
            x = q.popleft()
            tot += size[x]
            for g in edges[x]:
                if g not in seen and g in unread_sim and g not in recorded:
                    seen.add(g)
                    q.append(g)
                    if len(seen) > 4000:
                        return tot, len(seen)
        return tot, len(seen)
    # frontier: unread functions called directly by a recorded function
    frontier = {}
    for f in recorded:
        for g in edges[f]:
            if g in unread_sim:
                frontier.setdefault(g, set()).add(f)
    W('## Census 1: simulation-reachable, unrecorded')
    W('unrecorded sim functions %d; trivial (<=8 bytes) %d; total bytes %d; with library hint %d' % (
        len(unread_sim), len(unread_sim & trivial), sum(size[f] for f in unread_sim), len(unread_sim & set(lib_hint))))
    W('frontier (called by a recorded function) %d; deep (reached only through other unread) %d' % (
        len(frontier), len(unread_sim) - len(frontier)))
    # size histogram
    hist = Counter()
    for f in unread_sim:
        s = size[f]
        hist['<=16' if s <= 16 else '<=64' if s <= 64 else '<=256' if s <= 256 else '<=1024' if s <= 1024 else '>1024'] += 1
    W('size histogram ' + ', '.join('%s:%d' % (k, hist[k]) for k in ['<=16', '<=64', '<=256', '<=1024', '>1024']))
    # cluster by the nearest recorded caller's name prefix
    def prefix(n):
        return n.split('_')[0] if '_' in n and not n.startswith('FUN_') else '?'
    clus = defaultdict(lambda: [0, 0, set()])
    owner = {}
    # multi-source BFS from recorded functions over unread nodes
    q = deque()
    for f in recorded:
        for g in edges[f]:
            if g in unread_sim and g not in owner:
                owner[g] = prefix(kbname(f))
                q.append(g)
    while q:
        x = q.popleft()
        for g in edges[x]:
            if g in unread_sim and g not in owner:
                owner[g] = owner[x]
                q.append(g)
    for f in unread_sim:
        c = clus[owner.get(f, '(no recorded ancestor)')]
        c[0] += 1
        c[1] += size[f]
    W('')
    W('clusters by nearest recorded caller prefix (count, bytes):')
    for k, v in sorted(clus.items(), key=lambda kv: -kv[1][1])[:40]:
        W('  %-28s %5d %8d' % (k, v[0], v[1]))
    W('')
    ranked = []
    for f in frontier:
        if f in trivial:
            continue
        w, n = unread_weight(f)
        ranked.append((len(frontier[f]), size[f], w, n, f))
    W('top frontier by (recorded callers, own bytes, unread subtree bytes/count):')
    for rc, s, w, n, f in sorted(ranked, key=lambda r: (-(r[0] * 400 + r[1]),))[:60]:
        cs = sorted(kbname(c) for c in frontier[f])[:3]
        W('  0x%08X  callers %2d  bytes %5d  subtree %6d/%4d  %s  <- %s%s' % (
            f, rc, s, w, n, funcs[f][4], ', '.join(cs), ' [lib:%s]' % lib_hint[f] if f in lib_hint else ''))
    W('')
    W('largest unread subtrees (unread functions reachable only through unread):')
    for rc, s, w, n, f in sorted(ranked, key=lambda r: -r[2])[:30]:
        cs = sorted(kbname(c) for c in frontier[f])[:2]
        W('  0x%08X  subtree %6d bytes /%4d fns  own %5d  <- %s' % (f, w, n, s, ', '.join(cs)))
    W('')

    # ---- census 2: classes
    W('## Census 2: RTTI classes')
    cls = defaultdict(lambda: dict(vts=[], slots=set()))
    for vt, (cn, off, slots) in vtables.items():
        cls[cn]['vts'].append(vt)
        cls[cn]['slots'] |= set(slots)
    rows = []
    for cn, d in cls.items():
        sl = d['slots']
        own = {s for s in sl if sum(1 for c in cls.values() if s in c['slots']) == 1}
        rows.append((cn, len(sl), len(sl & recorded), len(sl & sim), len(own), len(own & recorded), d['vts']))
    W('classes %d; with no recorded slot %d; with no recorded own slot %d' % (
        len(rows), sum(1 for r in rows if r[2] == 0), sum(1 for r in rows if r[5] == 0 and r[4] > 0)))
    W('classes with sim-reachable slots but no recorded slot:')
    for r in sorted(rows, key=lambda r: -r[3]):
        if r[2] == 0 and r[3] > 0:
            W('  %-50s slots %3d sim %3d own %3d  vt %s' % (r[0][:50], r[1], r[3], r[4], ','.join('0x%08X' % v for v in r[6][:2])))
    W('classes partly recorded (own slots recorded/own):')
    for r in sorted(rows, key=lambda r: -(r[4] - r[5])):
        if r[2] > 0 and r[4] - r[5] > 0:
            W('  %-50s own %3d recorded %3d  sim %3d  vt %s' % (r[0][:50], r[4], r[5], r[3], ','.join('0x%08X' % v for v in r[6][:2])))
    W('')

    # ---- census 3: strings only unread functions reference
    W('## Census 3: strings referenced only by unrecorded functions')
    only_unread = {s: fs for s, fs in str_refs.items() if not (fs & recorded)}
    in_sim = {s: fs for s, fs in only_unread.items() if fs & sim}
    W('strings referenced %d; only by unrecorded %d; of these by sim-reachable functions %d' % (
        len(str_refs), len(only_unread), len(in_sim)))
    by_fn = defaultdict(list)
    for s, fs in in_sim.items():
        for f in fs & sim:
            by_fn[f].append(strings[s])
    W('sim-reachable unrecorded functions with strings: %d' % len(by_fn))
    for f, ss in sorted(by_fn.items(), key=lambda kv: -len(kv[1]))[:80]:
        W('  0x%08X  %3d  %s' % (f, len(ss), ' | '.join(x[:50] for x in sorted(ss)[:4])))
    W('')

    # ---- census 4: globals touched by recorded functions whose writers are all unrecorded
    W('## Census 4: globals read by recorded functions, written only by unrecorded ones')
    half = []
    for g, fs in glob_rd.items():
        rd_rec = fs & recorded
        wr = glob_wr.get(g, set())
        if rd_rec and wr and not (wr & recorded):
            half.append((len(rd_rec), g, wr, rd_rec))
    nowriter = [(len(fs & recorded), g) for g, fs in glob_rd.items() if (fs & recorded) and not glob_wr.get(g)]
    W('globals touched by recorded functions %d; written only by unrecorded %d; with no direct writer %d' % (
        sum(1 for fs in glob_rd.values() if fs & recorded), len(half), len(nowriter)))
    for n, g, wr, rr in sorted(half, key=lambda h: -h[0])[:60]:
        W('  0x%08X  recorded readers %2d  writers %s  e.g. %s' % (
            g, n, ','.join('0x%08X' % w + ('*' if w in sim else '') for w in sorted(wr)[:4]),
            ', '.join(sorted(kbname(r) for r in rr)[:2])))
    W('')

    # ---- KB integrity
    W('## Record integrity')
    W('records at mid-function addresses %d; outside any function %d' % (len(mid_records), len(no_fn_records)))
    W('  outside: ' + ' '.join('0x%08X' % a for a in sorted(no_fn_records)[:40]))
    built = [a for a, r in kb.items() if r['status'] == 'BUILT']
    W('BUILT records %d; BUILT without implements line %d' % (len(built), sum(1 for a in built if not kb[a]['implements'])))
    missing_src = []
    for a in built:
        for imp in kb[a]['implements']:
            p = imp.split('#')[0].split()[0] if imp else ''
            if p and not os.path.exists(os.path.join(SRC, p)):
                missing_src.append((a, p))
    W('BUILT implements paths that do not exist %d' % len(missing_src))
    for a, p in missing_src[:20]:
        W('  0x%08X %s' % (a, p))

    import pickle
    pickle.dump(dict(funcs=funcs, size=size, edges=dict(edges), calls=dict(calls), recorded=recorded,
                     rec_fn=dict(rec_fn), kb=kb, sim=sim, turn=reach['turn'], loop=set(reach['loop']),
                     exe=set(reach['exe_entry']), str_refs=dict(str_refs), strings=strings,
                     glob_rd=dict(glob_rd), glob_wr=dict(glob_wr), vtables=vtables, pseudo=pseudo_entries,
                     indirect=dict(indirect_calls)),
                open(os.path.join(OUT, 'graph.pkl'), 'wb'))
    open(os.path.join(OUT, 'report.txt'), 'w', encoding='utf-8').write('\n'.join(rep) + '\n')
    # machine-readable
    with open(os.path.join(OUT, 'functions.csv'), 'w', encoding='utf-8') as f:
        f.write('entry,size,recorded,status,confidence,sim_reach,turn_dist,loop_reach,frontier_callers,cluster,trivial,lib_hint,callees,callers_direct,indirect_calls\n')
        for e in sorted(entries):
            r = kb[rec_fn[e][0]] if e in rec_fn else None
            f.write('0x%08X,%d,%d,%s,%s,%d,%s,%d,%d,%s,%d,%s,%d,%d,%d\n' % (
                e, size[e], int(r is not None), r['status'] if r else '', r['conf'] if r else '',
                int(e in sim), reach['turn'].get(e, ''), int(e in reach['loop']), len(frontier.get(e, ())),
                owner.get(e, ''), int(e in trivial), json.dumps(lib_hint.get(e, ''))[:40].replace(',', ' '),
                len(edges[e]), len(callers_direct[e]), indirect_calls[e]))
    print('\n'.join(rep[:20]))


main()

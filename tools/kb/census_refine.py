# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
"""The census's second pass (see tools/kb/census.py): address bands, direct-call subtrees,
address-neighbour clusters and repo mentions, and the ranked reading list.

    python tools/kb/census_refine.py <graph.pkl> <repoRoot> <outFile.txt>
"""
import sys, os, re, pickle, bisect
from collections import defaultdict, Counter, deque

if len(sys.argv) != 4:
    sys.exit(__doc__)
G = pickle.load(open(sys.argv[1], 'rb'))
ROOT, OUT = sys.argv[2], sys.argv[3]
funcs, size, edges, calls = G['funcs'], G['size'], G['edges'], G['calls']
recorded, rec_fn, kb, sim = G['recorded'], G['rec_fn'], G['kb'], G['sim']


def main():
    entries = sorted(funcs)

    def kbname(f):
        return kb[rec_fn[f][0]]['name'] if f in rec_fn else funcs[f][4]

    # ---- bands
    def band(f):
        n = funcs[f][4]
        if n.startswith('Unwind@') or n.startswith('Catch@'):
            return 'unwind'
        if f >= 0x634000 and f < 0x653000:
            return 'crt'
        if f >= 0x653000:
            return 'cseg'
        return 'game'
    bands = Counter(band(f) for f in entries)

    # ---- doc / source mentions of every address
    mention = Counter()
    pat = re.compile(r'0x00([4-6][0-9A-Fa-f]{5})')
    for base in ('docs', 'src', 'tests', 'tools'):
        for dp, dn, fn in os.walk(os.path.join(ROOT, base)):
            if 'functions' in dp.replace('\\', '/').split('/')[-1:]:
                pass
            for n in fn:
                if not n.endswith(('.md', '.cs', '.py', '.txt', '.csv', '.json')):
                    continue
                try:
                    t = open(os.path.join(dp, n), encoding='utf-8', errors='ignore').read()
                except OSError:
                    continue
                for m in set(pat.findall(t)):
                    mention[int(m, 16) + 0x00000000] += 1
    # map mentions inside a function body to that function
    los = [funcs[e][0] for e in entries]
    fmention = Counter()
    for a, c in mention.items():
        i = bisect.bisect_right(los, a) - 1
        if i >= 0 and funcs[entries[i]][0] <= a <= funcs[entries[i]][1]:
            fmention[entries[i]] += c

    # ---- neighbour clustering: nearest recorded function by address, its name prefix
    rec_sorted = sorted(recorded)

    def prefix(n):
        return n.split('_')[0] if '_' in n and not n.startswith(('FUN_', 'ORPHAN_')) else '?'

    def neighbour(f):
        i = bisect.bisect_left(rec_sorted, f)
        best = None
        for j in (i - 1, i):
            if 0 <= j < len(rec_sorted):
                d = abs(rec_sorted[j] - f)
                if best is None or d < best[0]:
                    best = (d, rec_sorted[j])
        if best is None or best[0] > 0x3000:
            return '(no recorded neighbour within 12K)'
        return prefix(kbname(best[1]))

    unread_sim = {f for f in sim if f not in recorded and band(f) == 'game'}
    unread_game = {f for f in entries if f not in recorded and band(f) == 'game'}

    def dsub(f):
        seen = {f}
        q = deque([f])
        tot = 0
        while q:
            x = q.popleft()
            tot += size[x]
            for g in calls.get(x, ()):
                if g not in seen and g not in recorded and band(g) == 'game':
                    seen.add(g)
                    q.append(g)
        return tot, len(seen)

    rep = []
    W = rep.append
    W('# Refined census')
    W('bands: ' + ', '.join('%s %d' % kv for kv in bands.most_common()))
    W('game functions %d, recorded %d, unrecorded %d (bytes %d)' % (
        bands['game'], len([f for f in recorded if band(f) == 'game']), len(unread_game), sum(size[f] for f in unread_game)))
    W('sim-reachable game functions %d, recorded %d, unrecorded %d (bytes %d)' % (
        len([f for f in sim if band(f) == 'game']), len([f for f in sim if band(f) == 'game' and f in recorded]),
        len(unread_sim), sum(size[f] for f in unread_sim)))
    men = [f for f in unread_sim if fmention[f]]
    W('  of the unrecorded sim functions, mentioned somewhere in docs/src %d, never mentioned %d (bytes %d)' % (
        len(men), len(unread_sim) - len(men), sum(size[f] for f in unread_sim if not fmention[f])))
    nonsim = unread_game - unread_sim
    W('unrecorded game functions outside the sim set %d (bytes %d)' % (len(nonsim), sum(size[f] for f in nonsim)))
    W('')
    hist = Counter()
    for f in unread_sim:
        s = size[f]
        hist['<=16' if s <= 16 else '<=64' if s <= 64 else '<=256' if s <= 256 else '<=1024' if s <= 1024 else '>1024'] += 1
    W('unrecorded sim size histogram ' + ', '.join('%s:%d' % (k, hist[k]) for k in ['<=16', '<=64', '<=256', '<=1024', '>1024']))
    W('')
    clus = defaultdict(lambda: [0, 0, 0, 0])
    for f in unread_sim:
        c = clus[neighbour(f)]
        c[0] += 1
        c[1] += size[f]
        if not fmention[f]:
            c[2] += 1
            c[3] += size[f]
    W('unrecorded sim functions by address neighbour (count, bytes, never-mentioned count, never-mentioned bytes):')
    for k, v in sorted(clus.items(), key=lambda kv: -kv[1][1])[:45]:
        W('  %-36s %5d %8d   %5d %8d' % (k, v[0], v[1], v[2], v[3]))
    W('')
    # frontier from recorded callers (direct and pointer)
    frontier = defaultdict(set)
    for f in recorded:
        for g in edges.get(f, ()):
            if g in unread_sim:
                frontier[g].add(f)
    W('frontier: %d unrecorded sim functions called or referenced by a recorded one' % len(frontier))
    ranked = []
    for g, cs in frontier.items():
        w, n = dsub(g)
        ranked.append((len(cs), size[g], w, n, g))
    W('top by recorded callers (min 4), then own size:')
    for rc, s, w, n, g in sorted(ranked, key=lambda r: (-r[0], -r[1])):
        if rc < 4:
            break
        W('  0x%08X  callers %2d  own %5d  direct-subtree %6d/%3d  mentions %3d  [%s]  <- %s' % (
            g, rc, s, w, n, fmention[g], neighbour(g), ', '.join(sorted(kbname(c) for c in frontier[g])[:3])))
    W('')
    W('largest never-mentioned unrecorded sim functions by direct-call subtree:')
    for rc, s, w, n, g in sorted(ranked, key=lambda r: -r[2])[:40]:
        if fmention[g]:
            continue
        W('  0x%08X  subtree %6d/%3d  own %5d  callers %2d  [%s]  <- %s' % (
            g, w, n, s, rc, neighbour(g), ', '.join(sorted(kbname(c) for c in frontier[g])[:3])))
    W('')
    # census 4 refined: globals with mention flags
    W('globals read by recorded functions, written only by unrecorded game functions:')
    rows = []
    for gl, fs in G['glob_rd'].items():
        rr = fs & recorded
        wr = G['glob_wr'].get(gl, set())
        if rr and wr and not (wr & recorded):
            rows.append((len(rr), gl, wr))
    for n, gl, wr in sorted(rows, key=lambda r: -r[0])[:45]:
        W('  0x%08X  readers %3d  mentions %3d  writers %s' % (
            gl, n, mention[gl], ', '.join('0x%08X%s%s' % (w, '*' if w in sim else '', '(m%d)' % fmention[w] if fmention[w] else '') for w in sorted(wr)[:4])))
    W('')
    # strings census refined: per-function, game band only, with mention
    W('sim-reachable unrecorded game functions holding strings (string count, mentions):')
    by_fn = defaultdict(list)
    for s, fs in G['str_refs'].items():
        for f in fs:
            if f in unread_sim:
                by_fn[f].append(G['strings'][s])
    for f, ss in sorted(by_fn.items(), key=lambda kv: -len(kv[1])):
        W('  0x%08X  %3d  m%-3d [%s]  %s' % (f, len(ss), fmention[f], neighbour(f),
                                              ' | '.join(x.strip()[:40] for x in sorted(ss)[:3]).replace('\n', ' ')))
    W('')
    # class census refined
    W('RTTI classes in the game band: own slots, recorded, sim')
    cls = defaultdict(set)
    for vt, (cn, off, slots) in G['vtables'].items():
        cls[cn] |= set(slots)
    for cn, sl in sorted(cls.items(), key=lambda kv: -len(kv[1] - recorded)):
        own = {s for s in sl if band(s) == 'game'}
        if len(own - recorded) >= 3:
            W('  %-44s slots %3d recorded %3d sim %3d unrecorded-sim %3d' % (
                cn[4:-2][:44], len(own), len(own & recorded), len(own & sim), len((own & sim) - recorded)))
    open(OUT, 'w', encoding='utf-8').write('\n'.join(rep) + '\n')
    # reading list csv
    with open(OUT.replace('.txt', '-reading-list.csv'), 'w', encoding='utf-8') as fh:
        fh.write('entry,own_bytes,recorded_callers,direct_subtree_bytes,direct_subtree_fns,mentions,neighbour,turn_depth,callers\n')
        for rc, s, w, n, g in sorted(ranked, key=lambda r: (-r[0], -r[2])):
            fh.write('0x%08X,%d,%d,%d,%d,%d,%s,%s,%s\n' % (g, s, rc, w, n, fmention[g], neighbour(g),
                     G['turn'].get(g, ''), ' '.join(sorted(kbname(c) for c in frontier[g])[:5])))
    print('\n'.join(rep[:12]))


main()

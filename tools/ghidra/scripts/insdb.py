# Instruction cache for the PyGhidra helpers.
#
# Every Java call from CPython crosses JPype. Even inside main(), a walk over
# all ~743k instructions calling toString() on each took 7-20 s per script.
# ListingDump.java writes the whole listing once, natively, in ~5 s. This
# module streams it back as plain Python tuples in ~1 s, so a script that
# walks the listing finishes in 1-3 s.
#
# Scripts must keep their loops inside a function (main()). PyGhidra's script
# globals are a PyGhidraScript mapping, and a module-level loop pays ~12x per
# name lookup: InsGrep took 51 s at module level and 2 s inside main().
#
# The cache is keyed on the project directory, the program path, its last
# saved time, its modification number and its instruction and function
# counts, and is rebuilt whenever any of them changes. A run that modifies
# the program (ForceGaps, ForceAt) bumps the modification number, so a later
# script in the same run sees a fresh dump.
#
# Usage inside a GhidraScript:
#     import insdb
#     db = insdb.load(currentProgram, runScript)
#     for addr, fentry, mnem, text, op0, scalars in db.instructions():
#         ...                       # fentry is None outside any function
#     db.name(fentry)               # function name, '-' for None
import os
import zlib

_HERE = os.path.dirname(os.path.abspath(__file__))
# The listing is DKII.EXE's own disassembly, so the cache stays in the ignored
# reverse-engineering/ folder (ADR-0046), never next to these tracked scripts.
# The tag hashes the project directory, so the two agents' projects never share a file.
_CACHE_DIR = os.environ.get('DK2_GHIDRA_CACHE') or os.path.normpath(
    os.path.join(_HERE, '..', '..', '..', 'reverse-engineering', '.cache'))


class InsDb(object):
    def __init__(self, path, names):
        self.path = path
        self.names = names

    def name(self, fentry, default='-'):
        if fentry is None:
            return default
        return self.names.get(fentry, default)

    def instructions(self):
        """Yield (addr, fentry, mnem, text, op0, scalars) in listing order."""
        with open(self.path, encoding='utf-8') as f:
            for line in f:
                if line[0] != 'I':
                    continue
                _, a, fe, mnem, text, op0, sc = line.rstrip('\n').split('\t')
                yield (int(a, 16), None if fe == '-' else int(fe, 16), mnem, text, op0,
                       tuple(int(x, 16) for x in sc.split(',')) if sc else ())

    def all(self):
        """The whole listing as a list, for scripts that look at neighbours."""
        return list(self.instructions())


def _key(program):
    df = program.getDomainFile()
    return '|'.join(str(x) for x in (
        df.getProjectLocator().getProjectDir(), df.getPathname(),
        df.getLastModifiedTime(), program.getModificationNumber(),
        program.getListing().getNumInstructions(),
        program.getFunctionManager().getFunctionCount()))


def load(program, run_script):
    key = _key(program)
    if not os.path.isdir(_CACHE_DIR):
        os.makedirs(_CACHE_DIR)
    tag = '%08x' % (zlib.crc32(key.rsplit('|', 4)[0].encode('utf-8')) & 0xffffffff)
    path = os.path.join(_CACHE_DIR, 'listing-%s.tsv' % tag)
    fresh = False
    if os.path.exists(path):
        with open(path, encoding='utf-8') as f:
            fresh = f.readline().rstrip('\n') == '#key\t' + key
    if not fresh:
        print('insdb: rebuilding %s' % path)
        run_script('ListingDump.java', [path, key])
    names = {}
    with open(path, encoding='utf-8') as f:
        if f.readline().rstrip('\n') != '#key\t' + key:
            raise RuntimeError('listing cache %s does not match the program' % path)
        for line in f:
            if line[0] == 'F':
                _, e, n = line.rstrip('\n').split('\t', 2)
                names[int(e, 16)] = n
            elif line[0] == 'I':
                break
    return InsDb(path, names)

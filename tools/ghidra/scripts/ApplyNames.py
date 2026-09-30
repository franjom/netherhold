# Give the functions the names the knowledge base records for them.
# Args: <out> <names.tsv>   rows ADDRESS<TAB>name, ADDRESS eight hex digits.
# Names starting FUN_ are skipped; only a function that already exists at the
# address is renamed (USER_DEFINED). Run with ghidra.py --write to save, as
# dk2tools kb apply-names does. Writes RENAMED/MISSING/FAILED lines and a final
# "renamed=N same=N missing=N failed=N".
from ghidra.program.model.symbol import SourceType


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    space = currentProgram.getAddressFactory().getDefaultAddressSpace()
    functions = currentProgram.getFunctionManager()
    renamed = same = missing = failed = 0
    for raw in open(args[1]):
        line = raw.rstrip('\r\n')
        if not line.strip():
            continue
        address, _, name = line.partition('\t')
        address, name = address.strip(), name.strip()
        if not name or name.startswith('FUN_'):
            continue
        try:
            function = functions.getFunctionAt(space.getAddress(int(address, 16)))
        except Exception as error:  # a malformed address
            failed += 1
            out.write('FAILED %s %s: %s\n' % (address, name, error))
            continue
        if function is None:
            missing += 1
            out.write('MISSING %s %s\n' % (address, name))
            continue
        old = function.getName()
        if old == name:
            same += 1
            continue
        try:
            function.setName(name, SourceType.USER_DEFINED)
        except Exception as error:  # DuplicateNameException, InvalidInputException
            failed += 1
            out.write('FAILED %s %s -> %s: %s\n' % (address, old, name, error))
            continue
        renamed += 1
        out.write('RENAMED %s %s -> %s\n' % (address, old, name))
    out.write('renamed=%d same=%d missing=%d failed=%d\n' % (renamed, same, missing, failed))
    out.close()
    print('WROTE ' + args[0])


main()

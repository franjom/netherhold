# Ghidra PyGhidra script. Args: <out> <hex-address-file>.
# Matches exact numeric operands, including padded direct and unpadded indexed forms.
import os
import re
import sys


def main():
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    '..', '..', 'reverse-engineering'))
    import insdb

    output, address_file = getScriptArgs()
    with open(address_file, 'r') as stream:
        wanted = set(int(line.strip(), 16) for line in stream if line.strip())
    found = dict((value, []) for value in wanted)
    pattern = re.compile(r'(?<![0-9a-z_])0x[0-9a-f]+\b', re.I)
    db = insdb.load(currentProgram, runScript)
    for address, entry, _, instruction, _, _ in db.instructions():
        operands = set(int(token, 16) for token in pattern.findall(instruction))
        for value in operands.intersection(wanted):
            found[value].append((address, db.name(entry), instruction))
    with open(output, 'w') as stream:
        for value in sorted(wanted):
            hits = found[value]
            stream.write('== %08x : %d instruction hits\n' % (value, len(hits)))
            for address, function, instruction in hits:
                stream.write('   %08x %s %s\n' % (address, function, instruction))
    print('WROTE %s (%d targets)' % (output, len(wanted)))


main()

# Resolve a record type to its handler for DKII.EXE's two-level dispatchers.
#
# The trigger dispatchers do not jump straight from the type. They index a byte
# table with (type - 1), then use that byte to index a dword jump table, so
# several types share one arm. Reading either table alone tells you nothing.
#
# Args: <out> <byteTableHex> <count> <jumpTableHex> [<targetHex>]
#
# With <targetHex>, also reports which types reach the arm containing it --
# the arm being the largest jump target that is <= the address.
from jpype import JArray, JByte


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    prog = currentProgram
    af = prog.getAddressFactory().getDefaultAddressSpace()
    mem = prog.getMemory()


    def read(addr, n):
        # PyGhidra: JArray(JByte) for getBytes, then one bulk copy to bytes.
        buf = JArray(JByte)(n)
        mem.getBytes(af.getAddress(addr), buf)
        return list(bytes(buf))


    byte_table = int(args[1], 16)
    count = int(args[2])
    jump_table = int(args[3], 16)
    target = int(args[4], 16) if len(args) > 4 else None

    index = read(byte_table, count)
    slots = max(index) + 1
    raw = read(jump_table, slots * 4)
    targets = [raw[i * 4] | (raw[i * 4 + 1] << 8) | (raw[i * 4 + 2] << 16)
               | (raw[i * 4 + 3] << 24) for i in range(slots)]

    out.write('%d types -> %d distinct arms\n\n' % (count, slots))
    by_arm = {}
    for i in range(count):
        t = i + 1
        arm = targets[index[i]]
        by_arm.setdefault(arm, []).append(t)
        out.write('type %3d -> slot %2d -> %08x\n' % (t, index[i], arm))

    out.write('\n-- arms, with the types that share them --\n')
    for arm in sorted(by_arm):
        out.write('%08x : %s\n' % (arm, ' '.join(str(t) for t in by_arm[arm])))

    if target is not None:
        below = [a for a in sorted(by_arm) if a <= target]
        if not below:
            out.write('\nno arm at or before %08x\n' % target)
        else:
            arm = below[-1]
            out.write('\n%08x lies in the arm at %08x, reached by types: %s\n'
                      % (target, arm, ' '.join(str(t) for t in by_arm[arm])))

    out.close()
    print('WROTE ' + args[0])


main()

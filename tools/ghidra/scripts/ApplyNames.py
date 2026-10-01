# Give the functions the names the knowledge base records for them.
# Args: <out> <names.tsv>   rows ADDRESS<TAB>name, ADDRESS eight hex digits.
# Names starting FUN_ are skipped. A function that exists at the address is
# renamed (USER_DEFINED). With none there and none containing the address, the
# record's address is taken as the entry: auto-analysis misses bodies reached
# only through a vtable or a dispatch table, so the code is disassembled and a
# function created there, then named. An address inside another function's body
# is left alone (MISSING): splitting a body is a decision, not a rename. Run with
# ghidra.py --write to save, as dk2tools kb apply-names does. Writes
# RENAMED/CREATED/MISSING/FAILED lines and a final
# "renamed=N created=N same=N missing=N failed=N".
from ghidra.app.cmd.disassemble import DisassembleCommand
from ghidra.app.cmd.function import CreateFunctionCmd
from ghidra.program.model.symbol import SourceType
from ghidra.util.task import ConsoleTaskMonitor


def create(address, functions, monitor):
    """The function made at address, or None with the reason."""
    inside = functions.getFunctionContaining(address)
    if inside is not None:
        return None, 'inside %s at %s' % (inside.getName(), inside.getEntryPoint())
    if currentProgram.getListing().getInstructionAt(address) is None:
        DisassembleCommand(address, None, True).applyTo(currentProgram, monitor)
        if currentProgram.getListing().getInstructionAt(address) is None:
            return None, 'does not disassemble'
    CreateFunctionCmd(address).applyTo(currentProgram, monitor)
    function = functions.getFunctionAt(address)
    return (function, None) if function is not None else (None, 'CreateFunctionCmd failed')


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    space = currentProgram.getAddressFactory().getDefaultAddressSpace()
    functions = currentProgram.getFunctionManager()
    monitor = ConsoleTaskMonitor()
    renamed = created = same = missing = failed = 0
    for raw in open(args[1]):
        line = raw.rstrip('\r\n')
        if not line.strip():
            continue
        address, _, name = line.partition('\t')
        address, name = address.strip(), name.strip()
        if not name or name.startswith('FUN_'):
            continue
        try:
            at = space.getAddress(int(address, 16))
        except Exception as error:  # a malformed address
            failed += 1
            out.write('FAILED %s %s: %s\n' % (address, name, error))
            continue
        function = functions.getFunctionAt(at)
        if function is None:
            function, why = create(at, functions, monitor)
            if function is None:
                missing += 1
                out.write('MISSING %s %s: %s\n' % (address, name, why))
                continue
            created += 1
            out.write('CREATED %s %s\n' % (address, function.getName()))
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
    out.write('renamed=%d created=%d same=%d missing=%d failed=%d\n'
              % (renamed, created, same, missing, failed))
    out.close()
    print('WROTE ' + args[0])


main()

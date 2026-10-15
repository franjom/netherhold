# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""dk2oracle: runs single DKII.EXE functions on Unicorn for tests/DK2.Oracle.Tests.

Usage: python -I dk2oracle.py <path to DKII.EXE> [<memory image>] < requests > outcomes

Unicorn (GPLv2) is used, never committed (owner, 2026-10-15): it is loaded through ctypes from
the DK2_UNICORN_DLL environment variable. It runs here rather than inside the test process
because a .NET host fail-fasts (0xC0000409) the moment Unicorn starts emulating, while the
same DLL works from CPython.

Input is read whole before anything runs, so the caller may write every request and close
stdin before reading. One request per line, space-separated hex:
    <function> <arg>... [| <address>=<dword>... ecx=<dword>] [@ <address>...]
calls the function with the args on the stack (cdecl, or a thiscall given ecx) after writing
each dword, and reads each address after it returns. One outcome per line: `<eax> <edx> <read>...`, or `fault <message>`.

The image is mapped as the file has it: no imports bound, no globals initialised. A function
that reaches outside its arguments and the image's own bytes ends in a fault, not an answer.
A memory image (tools/oracle/capture_memory.py writes one from a capture tick) is a sequence of
`<u32 address> <u32 length> <bytes>` regions (process_dump.py's arrive as one zlib stream),
written over the image and mapped where it has no pages, before the first request; every
request then runs against that same state.
"""
import ctypes as C
import os
import struct
import sys
import zlib

ARCH_X86, MODE_32, PROT_ALL, UC_ERR_EXCEPTION = 4, 4, 7, 21
EAX, EBP, EBX, ECX, EDI, EDX, EIP, ESI, ESP = 19, 20, 21, 22, 23, 24, 26, 29, 30
STACK, STACK_SIZE, RETURN, SCRATCH, SCRATCH_SIZE = 0x00100000, 0x10000, 0x00200000, 0x00300000, 0x10000


def load():
    dll = C.CDLL(os.environ["DK2_UNICORN_DLL"])
    dll.uc_strerror.restype = C.c_char_p
    sig = {
        "uc_open": [C.c_int, C.c_int, C.POINTER(C.c_void_p)],
        "uc_mem_map": [C.c_void_p, C.c_uint64, C.c_uint64, C.c_uint32],
        "uc_mem_write": [C.c_void_p, C.c_uint64, C.c_char_p, C.c_uint64],
        "uc_mem_read": [C.c_void_p, C.c_uint64, C.c_void_p, C.c_uint64],
        "uc_reg_write": [C.c_void_p, C.c_int, C.c_void_p],
        "uc_reg_read": [C.c_void_p, C.c_int, C.c_void_p],
        "uc_emu_start": [C.c_void_p, C.c_uint64, C.c_uint64, C.c_uint64, C.c_size_t],
    }
    for name, args in sig.items():
        getattr(dll, name).argtypes = args
        getattr(dll, name).restype = C.c_int
    return dll


def main():
    dll = load()

    def check(err, what):
        if err:
            raise RuntimeError(f"{what}: {dll.uc_strerror(err).decode()}")

    uc = C.c_void_p()
    check(dll.uc_open(ARCH_X86, MODE_32, C.byref(uc)), "uc_open")

    image = open(sys.argv[1], "rb").read()
    pe = struct.unpack_from("<I", image, 0x3C)[0]
    sections = struct.unpack_from("<H", image, pe + 6)[0]
    optional = struct.unpack_from("<H", image, pe + 20)[0]
    base, = struct.unpack_from("<I", image, pe + 24 + 28)
    size, = struct.unpack_from("<I", image, pe + 24 + 56)
    check(dll.uc_mem_map(uc, base, (size + 0xFFF) & ~0xFFF, PROT_ALL), "map image")
    for i in range(sections):
        _, _, va, raw_size, raw_ptr = struct.unpack_from("<8sIIII", image, pe + 24 + optional + 40 * i)
        if raw_size:
            check(dll.uc_mem_write(uc, base + va, image[raw_ptr:raw_ptr + raw_size], raw_size), "write section")
    mapped = set(range(base >> 12, (base + size + 0xFFF) >> 12))
    if len(sys.argv) > 2:
        memory = open(sys.argv[2], "rb").read()
        if sys.argv[2].endswith(".z"):   # process_dump.py's whole-process image, one zlib stream
            memory = zlib.decompress(memory)
        at = 0
        while at < len(memory):
            if at + 8 > len(memory) or at + 8 + struct.unpack_from("<I", memory, at + 4)[0] > len(memory):
                raise RuntimeError(f"memory image cut short at byte {at}")
            address, length = struct.unpack_from("<II", memory, at)
            at += 8
            for page in range(address >> 12, (address + length + 0xFFF) >> 12):
                if page not in mapped:
                    check(dll.uc_mem_map(uc, page << 12, 0x1000, PROT_ALL), "map memory image")
                    mapped.add(page)
            check(dll.uc_mem_write(uc, address, memory[at:at + length], length), "write memory image")
            at += length

    def place(preferred, n):
        """The oracle's own pages at their usual address, or below 0x7F000000 where a game image
        has memory there (a whole-process dump does)."""
        for start in [preferred] + list(range(0x7F000000 - n, 0x01000000, -0x100000)):
            pages = range(start >> 12, (start + n + 0xFFF) >> 12)
            if not any(page in mapped for page in pages):
                check(dll.uc_mem_map(uc, start, (n + 0xFFF) & ~0xFFF, PROT_ALL), "map oracle page")
                mapped.update(pages)
                return start
        raise RuntimeError("no free address for the oracle's own pages")

    stack, ret = place(STACK, STACK_SIZE), place(RETURN, 0x1000)
    # Scratch is addressed by the tests themselves, so it never moves: on an image that has
    # memory there it is the game's own, and a request that writes or passes an address in it
    # is refused rather than run on corrupted state.
    scratch_is_game = any(page in mapped for page in range(SCRATCH >> 12, (SCRATCH + SCRATCH_SIZE) >> 12))
    if not scratch_is_game:
        check(dll.uc_mem_map(uc, SCRATCH, SCRATCH_SIZE, PROT_ALL), "map scratch")

    def write32(address, value):
        check(dll.uc_mem_write(uc, address, struct.pack("<I", value), 4), "uc_mem_write")

    def reg(r, value=None):
        v = C.c_uint32(value or 0)
        check((dll.uc_reg_write if value is not None else dll.uc_reg_read)(uc, r, C.byref(v)), "reg")
        return v.value

    out = []
    for line in sys.stdin.read().splitlines():
        line, _, reads = line.partition("@")
        line, _, writes = line.partition("|")
        call = line.split()
        function, args = int(call[0], 16), [int(a, 16) for a in call[1:]]
        registers = {}
        if scratch_is_game and any(SCRATCH <= int(w.split("=")[0], 16) < SCRATCH + SCRATCH_SIZE
                                   for w in writes.split() if not w.startswith("ecx"))                 or scratch_is_game and any(SCRATCH <= a < SCRATCH + SCRATCH_SIZE for a in args):
            out.append("fault scratch is game memory in this image")
            continue
        for pair in writes.split():
            address, value = pair.split("=")
            if address == "ecx":  # a thiscall's this
                registers[ECX] = int(value, 16)
            else:
                write32(int(address, 16), int(value, 16))

        esp = stack + STACK_SIZE - 0x100 - 4 * (len(args) + 1)
        for i, value in enumerate([ret] + args):
            write32(esp + 4 * i, value)
        reg(ESP, esp)
        for r in (EAX, EBX, ECX, EDX, ESI, EDI, EBP):
            reg(r, registers.get(r, 0xCCCCCCCC))

        # No timeout: Unicorn starts a timer thread for one, about 15 ms a call. The instruction
        # count bounds a function that never returns instead.
        err = dll.uc_emu_start(uc, function, ret, 0, 1_000_000)
        eip = reg(EIP)
        if err or eip != ret:
            # A divide error stops the run at the DIV/IDIV itself (F7 /6, /7), seen here both
            # with UC_ERR_EXCEPTION and with no error code. A memory fault on the instruction
            # reports its own error and is not filed as a divide.
            op = C.create_string_buffer(2)
            readable = dll.uc_mem_read(uc, eip, op, 2) == 0
            if err in (0, UC_ERR_EXCEPTION) and readable and op.raw[0] == 0xF7 and (op.raw[1] >> 3) & 7 in (6, 7):
                out.append(f"fault divide at {eip:x}")
            elif err:
                out.append(f"fault {dll.uc_strerror(err).decode()} at {eip:x}")
            else:
                out.append(f"fault did not return within 1000000 instructions, at {eip:x}")
            continue
        words = [reg(EAX), reg(EDX)]
        for address in reads.split():
            v = C.c_uint32()
            check(dll.uc_mem_read(uc, int(address, 16), C.byref(v), 4), "uc_mem_read")
            words.append(v.value)
        out.append(" ".join(f"{w:x}" for w in words))
    sys.stdout.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main()

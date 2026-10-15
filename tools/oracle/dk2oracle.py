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
each dword, and reads each address after it returns. A token `s<hex>` is that offset into a
64 KB scratch block the helper places wherever the memory image leaves room, so a request on a
whole-process dump has somewhere of its own to write. FS addresses a thread-environment page
whose exception chain is empty, for functions that install a handler. One outcome per line: `<eax> <edx> <read>...`, or `fault <message>`.

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
DS, ES, FS, SS, GDTR = 17, 28, 32, 49, 243
STACK, STACK_SIZE, RETURN, SCRATCH, SCRATCH_SIZE = 0x00100000, 0x10000, 0x00200000, 0x00300000, 0x10000


class Mmr(C.Structure):
    """uc_x86_mmr: a descriptor-table register."""
    _fields_ = [("selector", C.c_uint16), ("base", C.c_uint64), ("limit", C.c_uint32), ("flags", C.c_uint32)]


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
            page, end = address >> 12, (address + length + 0xFFF) >> 12
            while page < end:   # unmapped pages in runs: one uc_mem_map per run, not per page
                if page in mapped:
                    page += 1
                    continue
                run = page
                while run < end and run not in mapped:
                    run += 1
                check(dll.uc_mem_map(uc, page << 12, (run - page) << 12, PROT_ALL), "map memory image")
                mapped.update(range(page, run))
                page = run
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
    movable = place(SCRATCH, SCRATCH_SIZE) if any(
        page in mapped for page in range(SCRATCH >> 12, (SCRATCH + SCRATCH_SIZE) >> 12)) else SCRATCH

    # FS through a GDT entry (Unicorn ignores an FS base in 32-bit mode): a thread-environment
    # page whose first dword, the exception chain, is the end marker and whose +0x18 is itself.
    teb, gdt = place(0x7FFDE000, 0x1000), place(0x7FFD0000, 0x1000)
    check(dll.uc_mem_write(uc, teb, struct.pack("<I", 0xFFFFFFFF), 4), "teb")
    check(dll.uc_mem_write(uc, teb + 0x18, struct.pack("<I", teb), 4), "teb self")
    # Loading any segment register makes the CPU re-derive its 16/32-bit modes from the cached
    # descriptors, so the flat data segment goes in too and SS, DS and ES are loaded from it.
    def descriptor(base, limit, access, flags):
        return struct.pack("<HHBBBB", limit & 0xFFFF, base & 0xFFFF, (base >> 16) & 0xFF, access,
                           ((limit >> 16) & 0xF) | (flags << 4), base >> 24)
    table = bytes(8) + descriptor(0, 0xFFFFF, 0x9A, 0xC) + descriptor(0, 0xFFFFF, 0x92, 0xC) \
        + descriptor(teb, 0xFFF, 0x92, 0x4)
    check(dll.uc_mem_write(uc, gdt, table, len(table)), "gdt")
    check(dll.uc_reg_write(uc, GDTR, C.byref(Mmr(0, gdt, len(table) - 1, 0))), "gdtr")
    for register, index in ((SS, 2), (DS, 2), (ES, 2), (FS, 3)):
        selector = C.c_uint32(index << 3)
        check(dll.uc_reg_write(uc, register, C.byref(selector)), "segment")

    def token(t):
        return movable + int(t[1:], 16) if t.startswith("s") else int(t, 16)
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
        function, args = int(call[0], 16), [token(a) for a in call[1:]]
        registers = {}
        if scratch_is_game and any(SCRATCH <= a < SCRATCH + SCRATCH_SIZE for a in
                                   args + [token(w.split("=")[0]) for w in writes.split() if not w.startswith("ecx")]):
            out.append("fault scratch is game memory in this image; use s<offset>")
            continue
        for pair in writes.split():
            address, value = pair.split("=")
            if address == "ecx":  # a thiscall's this
                registers[ECX] = token(value)
            else:
                write32(token(address), token(value))

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
            check(dll.uc_mem_read(uc, token(address), C.byref(v), 4), "uc_mem_read")
            words.append(v.value)
        out.append(" ".join(f"{w:x}" for w in words))
    sys.stdout.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main()

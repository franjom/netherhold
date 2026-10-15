# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""dk2oracle: runs single DKII.EXE functions on Unicorn for tests/DK2.Oracle.Tests.

Usage: python -I dk2oracle.py <path to DKII.EXE> < requests > outcomes

Unicorn (GPLv2) is used, never committed (owner, 2026-10-15): it is loaded through ctypes from
the DK2_UNICORN_DLL environment variable. It runs here rather than inside the test process
because a .NET host fail-fasts (0xC0000409) the moment Unicorn starts emulating, while the
same DLL works from CPython.

Input is read whole before anything runs, so the caller may write every request and close
stdin before reading. One request per line, space-separated hex:
    <function> <arg>... [| <address>=<dword>...] [@ <address>...]
calls the cdecl function with the args after writing each dword, and reads each address after
it returns. One outcome per line: `<eax> <edx> <read>...`, or `fault <message>`.

The image is mapped as the file has it: no imports bound, no globals initialised. A function
that reaches outside its arguments and the image's own bytes ends in a fault, not an answer.
"""
import ctypes as C
import os
import struct
import sys

ARCH_X86, MODE_32, PROT_ALL = 4, 4, 7
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
    check(dll.uc_mem_map(uc, STACK, STACK_SIZE, PROT_ALL), "map stack")
    check(dll.uc_mem_map(uc, RETURN, 0x1000, PROT_ALL), "map return")
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
        for pair in writes.split():
            address, value = pair.split("=")
            write32(int(address, 16), int(value, 16))

        esp = STACK + STACK_SIZE - 0x100 - 4 * (len(args) + 1)
        for i, value in enumerate([RETURN] + args):
            write32(esp + 4 * i, value)
        reg(ESP, esp)
        for r in (EAX, EBX, ECX, EDX, ESI, EDI, EBP):
            reg(r, 0xCCCCCCCC)

        # No timeout: Unicorn starts a timer thread for one, about 15 ms a call. The instruction
        # count bounds a function that never returns instead.
        err = dll.uc_emu_start(uc, function, RETURN, 0, 1_000_000)
        if err:
            out.append(f"fault {dll.uc_strerror(err).decode()}")
            continue
        if reg(EIP) != RETURN:  # the count ran out: Unicorn stops without an error
            out.append("fault did not return within 1000000 instructions")
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

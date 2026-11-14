# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""texture_blocks: decodes one whole image with DKII.EXE's own 8x8 block decoders on Unicorn,
the oracle for src/DK2.Art/Dk2TextureDecoder.cs.

Usage: python -I texture_blocks.py <DKII.EXE> engine|loading <x87 control word, hex> <payload> <width> <height> <alpha 0|1> <out.rgba>
<payload> is the compressed stream after its header (EngineTextures.dat after the 20-byte entry
header, or a .444 after its 8 bytes); width and height are multiples of 8. engine runs 0x0065CA50
once per block, loading runs 0x00668810. The globals their setup routines (0x005B2450, 0x005D7360)
would fill are written here: dequantisation steps (the image's table times 8), clamp tables for
RGBA8888 and the bitstream state. 37F is the 64-bit precision 0x005B26F0 sets for engine textures;
27F and 07F select 53- and 24-bit. Unicorn is loaded from DK2_UNICORN_DLL (reverse-engineering/README.md).
Output is RGBA, rows top first, the layout Dk2TextureDecoder returns.
"""
import ctypes as C
import os
import struct
import sys
ARCH_X86, MODE_32, PROT_ALL = 4, 4, 7
EAX, EBP, EBX, ECX, EDI, EDX, EIP, ESI, ESP = 19, 20, 21, 22, 23, 24, 26, 29, 30
dll = C.CDLL(os.environ["DK2_UNICORN_DLL"])
dll.uc_strerror.restype = C.c_char_p
for name, args in {"uc_open": [C.c_int, C.c_int, C.POINTER(C.c_void_p)], "uc_mem_map": [C.c_void_p, C.c_uint64, C.c_uint64, C.c_uint32],
                   "uc_mem_write": [C.c_void_p, C.c_uint64, C.c_char_p, C.c_uint64], "uc_mem_read": [C.c_void_p, C.c_uint64, C.c_void_p, C.c_uint64],
                   "uc_reg_write": [C.c_void_p, C.c_int, C.c_void_p], "uc_reg_read": [C.c_void_p, C.c_int, C.c_void_p],
                   "uc_emu_start": [C.c_void_p, C.c_uint64, C.c_uint64, C.c_uint64, C.c_size_t]}.items():
    getattr(dll, name).argtypes = args; getattr(dll, name).restype = C.c_int
def chk(e, w):
    if e: raise RuntimeError(w + ": " + dll.uc_strerror(e).decode())
uc = C.c_void_p(); chk(dll.uc_open(ARCH_X86, MODE_32, C.byref(uc)), "open")
exe, mode, cw, inp, w, h, alpha, outp = sys.argv[1], sys.argv[2], int(sys.argv[3], 16), sys.argv[4], int(sys.argv[5]), int(sys.argv[6]), int(sys.argv[7]), sys.argv[8]
image = open(exe, "rb").read()
pe = struct.unpack_from("<I", image, 0x3C)[0]; nsec = struct.unpack_from("<H", image, pe + 6)[0]; opt = struct.unpack_from("<H", image, pe + 20)[0]
base, = struct.unpack_from("<I", image, pe + 52); size, = struct.unpack_from("<I", image, pe + 80)
chk(dll.uc_mem_map(uc, base, (size + 0xFFF) & ~0xFFF, PROT_ALL), "map")
for i in range(nsec):
    _, _, va, rs, rp = struct.unpack_from("<8sIIII", image, pe + 24 + opt + 40 * i)
    if rs: chk(dll.uc_mem_write(uc, base + va, image[rp:rp + rs], rs), "sec")
STACK, RET, STREAM, OUT, STUB = 0x00100000, 0x00200000, 0x01000000, 0x02000000, 0x00210000
chk(dll.uc_mem_map(uc, STACK, 0x10000, PROT_ALL), "s"); chk(dll.uc_mem_map(uc, RET, 0x20000, PROT_ALL), "r")
data = open(inp, "rb").read() + bytes(16)
chk(dll.uc_mem_map(uc, STREAM, (len(data) + 0xFFFF) & ~0xFFF, PROT_ALL), "st")
chk(dll.uc_mem_map(uc, OUT, (w * h * 4 + 0x10FFF) & ~0xFFF, PROT_ALL), "o")
def wr(a, b): chk(dll.uc_mem_write(uc, a, b, len(b)), "w %x" % a)
def w32(a, v): wr(a, struct.pack("<I", v & 0xFFFFFFFF))
def reg(r, v): x = C.c_uint32(v & 0xFFFFFFFF); chk(dll.uc_reg_write(uc, r, C.byref(x)), "reg")
def rreg(r): x = C.c_uint32(); chk(dll.uc_reg_read(uc, r, C.byref(x)), "rreg"); return x.value
wr(STREAM, data)
def va2off(va):
    for i in range(nsec):
        _, vs, sva, rs, rp = struct.unpack_from("<8sIIII", image, pe + 24 + opt + 40 * i)
        if base + sva <= va < base + sva + max(vs, rs): return rp + va - base - sva
quant = [v * 8 for v in struct.unpack_from("<64i", image, va2off(0x6C10C0 if mode == "engine" else 0x672608))]
def clamp_table(shift):
    return b"".join(struct.pack("<I", ((0 if i < 0x180 else 255 if i > 0x27F else i - 0x180) << shift) & 0xFFFFFFFF) for i in range(1024))
# Control word stub: FLDCW [STUB+0x10]
wr(STUB, bytes([0xD9, 0x2D]) + struct.pack("<I", STUB + 0x10)); wr(STUB + 0x10, struct.pack("<H", cw))
chk(dll.uc_emu_start(uc, STUB, STUB + 6, 0, 10), "cw")
if mode == "engine":
    wr(0x7982F0, struct.pack("<64i", *quant))
    wr(0x798F90, clamp_table(24)); wr(0x799F90, clamp_table(0)); wr(0x79AF90, clamp_table(8)); wr(0x79BF90, clamp_table(16))
    w32(0x7AF058, STREAM); w32(0x7AF05C, 0); [w32(0x7AF060 + 4 * i, 0) for i in range(4)]
    w32(0x7AF0D0, alpha); w32(0x7AF090, 0xFF000000); fn = 0x0065CA50
else:
    wr(0x79F8A0, struct.pack("<64i", *quant))
    wr(0x7A0440, clamp_table(24)); wr(0x7A1440, clamp_table(0)); wr(0x7A2440, clamp_table(8)); wr(0x7A3440, clamp_table(16))
    w32(0x7AF664, STREAM); w32(0x7AF668, 0); w32(0x7AF660, alpha); w32(0x7AF65C, 0); w32(0x7AF628, 0xFF000000); w32(0x7AF658, 0)
    fn = 0x00668810
pitch = w * 4
for by in range(0, h, 8):
    for bx in range(0, w, 8):
        esp = STACK + 0x8000
        for i, v in enumerate([RET, OUT + by * pitch + bx * 4, pitch, 8, 8]): w32(esp + 4 * i, v)
        reg(ESP, esp)
        e = dll.uc_emu_start(uc, fn, RET, 0, 5_000_000)
        if e or rreg(EIP) != RET: raise RuntimeError("fault at %x block %d,%d: %s" % (rreg(EIP), bx, by, dll.uc_strerror(e).decode()))
buf = C.create_string_buffer(w * h * 4); chk(dll.uc_mem_read(uc, OUT, buf, w * h * 4), "read")
open(outp, "wb").write(buf.raw)

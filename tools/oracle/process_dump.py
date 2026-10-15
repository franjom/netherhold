# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""process_dump: every committed, readable page of a running 32-bit process, as a memory image.

The image is capture_memory.py's format (`<u32 address> <u32 length> <bytes>` regions) inside
one zlib stream, which dk2oracle.py loads directly, so a function that reads definitions, path
buffers or any other heap the full-state captures leave out can run on the game's real state.

    python -I process_dump.py <pid> <out.mem.z>        (elevated, to open the game)

The process is suspended for the dump, so every region comes from one moment, and resumed
after. Private memory and the writable sections of mapped images are taken; read-only image
pages, guard pages and anything over 64 MB in one region (texture memory) are left out, and
nothing above 0x7FFF0000 (DKII.EXE is not large-address-aware). A region that cannot be read
whole keeps what was read and is counted. The recorder (reverse-engineering/dk2record.py
--dump-ticks) calls dump() at chosen ticks; the game stands still meanwhile, so a recording
with dumps is not a timing run.
"""
import ctypes
import ctypes.wintypes as wt
import struct
import sys
import time
import zlib

PROCESS_VM_READ, PROCESS_QUERY_INFORMATION, PROCESS_SUSPEND_RESUME = 0x0010, 0x0400, 0x0800
MEM_COMMIT, MEM_PRIVATE, MEM_IMAGE = 0x1000, 0x20000, 0x1000000
PAGE_NOACCESS, PAGE_GUARD = 0x01, 0x100
WRITABLE = 0x04 | 0x08 | 0x40 | 0x80   # READWRITE, WRITECOPY, EXECUTE_READWRITE, EXECUTE_WRITECOPY
LARGEST = 64 << 20


class MEMORY_BASIC_INFORMATION(ctypes.Structure):
    _fields_ = [("BaseAddress", ctypes.c_void_p), ("AllocationBase", ctypes.c_void_p),
                ("AllocationProtect", wt.DWORD), ("PartitionId", wt.WORD), ("RegionSize", ctypes.c_size_t),
                ("State", wt.DWORD), ("Protect", wt.DWORD), ("Type", wt.DWORD)]


def regions(handle, k32):
    """(address, size) of every page range worth taking, lowest first."""
    info = MEMORY_BASIC_INFORMATION()
    address = 0x10000
    while address < 0x7FFF0000:
        if not k32.VirtualQueryEx(handle, ctypes.c_void_p(address), ctypes.byref(info), ctypes.sizeof(info)):
            break
        size = info.RegionSize
        protect = info.Protect
        if (info.State == MEM_COMMIT and not protect & (PAGE_NOACCESS | PAGE_GUARD) and size <= LARGEST
                and (info.Type == MEM_PRIVATE or info.Type == MEM_IMAGE and protect & WRITABLE)):
            yield address, size
        address += size


def dump(pid, path, while_suspended=None):
    """Suspends the process, calls while_suspended() (the recorder reads the tick there, so the
    dump is named for the tick it holds), writes the image, resumes. Returns a dict: regions,
    bytes, short (regions read only in part or not at all), seconds suspended, and what
    while_suspended returned. Raises if the process cannot be suspended or resumed."""
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    ntdll = ctypes.WinDLL("ntdll")
    k32.OpenProcess.restype = wt.HANDLE
    k32.VirtualQueryEx.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t]
    k32.VirtualQueryEx.restype = ctypes.c_size_t
    k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                      ctypes.POINTER(ctypes.c_size_t)]
    handle = k32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION | PROCESS_SUSPEND_RESUME, False, pid)
    if not handle:
        raise OSError(f"OpenProcess({pid}) failed ({ctypes.get_last_error()}); run elevated")
    count = total = short = 0
    seen = None
    packer = zlib.compressobj(6)
    try:
        status = ntdll.NtSuspendProcess(wt.HANDLE(handle))
        if status:
            raise OSError(f"NtSuspendProcess failed (0x{status & 0xFFFFFFFF:08X}); no dump taken")
        frozen = time.perf_counter()
        try:
            if while_suspended is not None:
                seen = while_suspended()
            with open(path, "wb") as out:
                for address, size in list(regions(handle, k32)):
                    buffer = ctypes.create_string_buffer(size)
                    got = ctypes.c_size_t(0)
                    if not k32.ReadProcessMemory(handle, ctypes.c_void_p(address), buffer, size, ctypes.byref(got))                             or got.value != size:
                        short += 1
                        if not got.value:
                            continue
                    out.write(packer.compress(struct.pack("<II", address, got.value) + buffer.raw[:got.value]))
                    count += 1
                    total += got.value
                out.write(packer.flush())
        finally:
            status = ntdll.NtResumeProcess(wt.HANDLE(handle))
            seconds = time.perf_counter() - frozen
            if status:
                raise OSError(f"NtResumeProcess failed (0x{status & 0xFFFFFFFF:08X}): the game is still suspended")
    finally:
        k32.CloseHandle(wt.HANDLE(handle))
    return {"regions": count, "bytes": total, "short": short, "seconds": seconds, "seen": seen}


if __name__ == "__main__":
    d = dump(int(sys.argv[1]), sys.argv[2])
    print(f"{d['regions']} regions ({d['short']} short), {d['bytes'] / 1e6:.1f} MB, "
          f"suspended {d['seconds']:.2f} s -> {sys.argv[2]}")

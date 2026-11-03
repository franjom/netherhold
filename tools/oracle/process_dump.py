# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""process_dump: every committed, readable page of a running 32-bit process, as a memory image.

The image is capture_memory.py's format (`<u32 address> <u32 length> <bytes>` regions) inside
one zlib stream, which dk2oracle.py loads directly, so a function that reads definitions, path
buffers or any other heap the full-state captures leave out can run on the game's real state.

    python -I process_dump.py <pid> <out.mem.z>        (elevated, to open the game)

The 64-bit Windows tool suspends an i386 WOW64 process and reads every committed readable
region across its complete 32-bit address domain, including mapped/read-only/large regions.
Guard, no-access and uncommitted pages are not readable. Query or read failures abort; none
are silently treated as a complete dump. The output uses exclusive creation and the process
is resumed even after failure. Prefer record_native.py for verified turn boundaries, complete
per-turn data, address/provenance manifests and automatic seed/map controls. This low-level
single image is a stopped-process read window, not a wall-clock performance measurement.
"""
import ctypes
import ctypes.wintypes as wt
import struct
import sys
import time
import zlib
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))

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
    """Every readable committed region in the complete 32-bit address domain."""
    from decision_boundary import stopped_regions
    yield from stopped_regions(handle, k32)


def dump(pid, path, while_suspended=None):
    """Suspends the process, calls while_suspended() (the recorder reads the tick there, so the
    dump is named for the tick it holds), writes the image, resumes. Returns a dict: regions,
    bytes, short (regions read only in part or not at all), seconds suspended, and what
    while_suspended returned. Raises if the process cannot be suspended or resumed."""
    if Path(path).exists():
        raise FileExistsError(path)
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
    packer = zlib.compressobj(1)
    try:
        k32.IsWow64Process2.argtypes = [wt.HANDLE, ctypes.POINTER(wt.WORD), ctypes.POINTER(wt.WORD)]
        k32.IsWow64Process2.restype = wt.BOOL
        process_machine, native_machine = wt.WORD(), wt.WORD()
        if not k32.IsWow64Process2(handle, ctypes.byref(process_machine), ctypes.byref(native_machine)):
            raise OSError('Cannot establish the target address width')
        if process_machine.value != 0x14C:
            raise ValueError('This complete-memory profile requires an i386 WOW64 target')
        status = ntdll.NtSuspendProcess(wt.HANDLE(handle))
        if status:
            raise OSError(f"NtSuspendProcess failed (0x{status & 0xFFFFFFFF:08X}); no dump taken")
        frozen = time.perf_counter()
        try:
            if while_suspended is not None:
                seen = while_suspended()
            with open(path, "xb") as out:
                for address, size in list(regions(handle, k32)):
                    out.write(packer.compress(struct.pack('<II', address, size)))
                    for offset in range(0, size, 65536):
                        length = min(65536, size - offset)
                        buffer = ctypes.create_string_buffer(length)
                        got = ctypes.c_size_t()
                        if not k32.ReadProcessMemory(handle, ctypes.c_void_p(address + offset), buffer, length, ctypes.byref(got)) or got.value != length:
                            raise OSError(f'Incomplete process memory at {address + offset:08X}: {got.value} of {length}')
                        out.write(packer.compress(buffer.raw[:length]))
                    count += 1
                    total += size
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
    if sys.argv[1:] in (['--help'], ['-h']):
        print(__doc__)
        sys.exit(0)
    output = Path(sys.argv[2]).resolve()
    evidence = Path('F:/DK2Evidence').resolve()
    if not output.is_relative_to(evidence) or output == evidence:
        sys.exit('Output must be a new file beneath F:/DK2Evidence')
    d = dump(int(sys.argv[1]), sys.argv[2])
    print(f"{d['regions']} regions ({d['short']} short), {d['bytes'] / 1e6:.1f} MB, "
          f"suspended {d['seconds']:.2f} s -> {sys.argv[2]}")

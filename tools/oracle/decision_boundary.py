# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Capture a 32-bit chooser at entry/return using hardware execution breakpoints.

Launches its own process; never attaches to an existing game, patches code,
or calls game functions. Requires 64-bit CPython on Windows. Dumps are oracle
memory images, not saves; see decision-boundary.md for scope and invocation.
"""
import argparse
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import time
import zlib

U32, U16, PTR = C.c_uint32, C.c_uint16, C.c_void_p
CHOOSER = 0x00463DF0
RETAIL_MD5 = '2f3bbc21eae8c3aa468136baa303036d'
DX_MD5 = 'b9709c6c9084d3060e4ac7ac9764c70b'
WORLD = 0x006F2A08
CONTEXT_FLAGS = 0x10013
CONTINUE, NOT_HANDLED = 0x10002, 0x80010001
BREAKPOINTS = {0x80000003, 0x4000001F}
SINGLE_STEPS = {0x80000004, 0x4000001E}
DEBUG_REGISTERS = ('Dr0', 'Dr1', 'Dr2', 'Dr3', 'Dr6', 'Dr7')


class FloatSave(C.Structure):
    _fields_ = [(name, U32) for name in ('ControlWord', 'StatusWord', 'TagWord',
                'ErrorOffset', 'ErrorSelector', 'DataOffset', 'DataSelector')]
    _fields_ += [('RegisterArea', C.c_ubyte * 80), ('Cr0NpxState', U32)]


class WowContext(C.Structure):
    _fields_ = [('ContextFlags', U32)] + [(n, U32) for n in DEBUG_REGISTERS]
    _fields_ += [('FloatSave', FloatSave)]
    _fields_ += [(n, U32) for n in ('SegGs', 'SegFs', 'SegEs', 'SegDs', 'Edi', 'Esi',
                  'Ebx', 'Edx', 'Ecx', 'Eax', 'Ebp', 'Eip', 'SegCs', 'EFlags', 'Esp', 'SegSs')]
    _fields_ += [('ExtendedRegisters', C.c_ubyte * 512)]


class EventData(C.Union):
    _fields_ = [('raw', C.c_ubyte * 160), ('alignment', C.c_uint64)]


class DebugEvent(C.Structure):
    _fields_ = [('code', U32), ('pid', U32), ('tid', U32), ('data', EventData)]


class ProcessEntry(C.Structure):
    _fields_ = [('size', U32), ('usage', U32), ('pid', U32), ('heap', PTR),
                ('module', U32), ('threads', U32), ('parent', U32),
                ('priority', C.c_int32), ('flags', U32), ('name', C.c_wchar * 260)]


def running_games():
    api = C.WinDLL('kernel32', use_last_error=True)
    api.CreateToolhelp32Snapshot.argtypes, api.CreateToolhelp32Snapshot.restype = [U32, U32], PTR
    api.CloseHandle.argtypes, api.CloseHandle.restype = [PTR], C.c_int
    for name in ('Process32FirstW', 'Process32NextW'):
        function = getattr(api, name)
        function.argtypes, function.restype = [PTR, C.POINTER(ProcessEntry)], C.c_int
    handle = api.CreateToolhelp32Snapshot(2, 0)
    if handle == PTR(-1).value:
        raise OSError(C.get_last_error(), 'CreateToolhelp32Snapshot')
    try:
        entry = ProcessEntry()
        entry.size = C.sizeof(entry)
        found = []
        ok = api.Process32FirstW(handle, C.byref(entry))
        while ok:
            if entry.name.casefold() in ('dkii.exe', 'dkii-dx.exe'):
                found.append(entry.pid)
            ok = api.Process32NextW(handle, C.byref(entry))
        if C.get_last_error() != 18:
            raise OSError(C.get_last_error(), 'Process enumeration')
        return found
    finally:
        api.CloseHandle(handle)


def registers(context):
    return {n: getattr(context, n) for n in ('Eax', 'Ebx', 'Ecx', 'Edx', 'Esi', 'Edi',
                                           'Ebp', 'Esp', 'Eip', 'EFlags')}


class BoundaryDebugger:
    """Callbacks run while a debug event holds every debuggee thread stopped."""
    def __init__(self, entry):
        if sys.platform != 'win32' or C.sizeof(PTR) != 8:
            raise RuntimeError('64-bit Windows CPython is required')
        self.entry = entry
        self.process = None
        self.api = C.WinDLL('kernel32', use_last_error=True)
        signatures = {
            'WaitForDebugEvent': ([C.POINTER(DebugEvent), U32], C.c_int),
            'ContinueDebugEvent': ([U32, U32, U32], C.c_int),
            'DebugSetProcessKillOnExit': ([C.c_int], C.c_int),
            'DebugActiveProcessStop': ([U32], C.c_int),
            'Wow64GetThreadContext': ([PTR, C.POINTER(WowContext)], C.c_int),
            'Wow64SetThreadContext': ([PTR, C.POINTER(WowContext)], C.c_int),
            'SuspendThread': ([PTR], U32), 'ResumeThread': ([PTR], U32),
            'WaitForSingleObject': ([PTR, U32], U32),
            'CloseHandle': ([PTR], C.c_int),
            'ReadProcessMemory': ([PTR, PTR, PTR, C.c_size_t, C.POINTER(C.c_size_t)], C.c_int),
            'VirtualQueryEx': ([PTR, PTR, PTR, C.c_size_t], C.c_size_t),
        }
        for name, (args, result) in signatures.items():
            function = getattr(self.api, name)
            function.argtypes, function.restype = args, result
        self.threads, self.original, self.active = {}, {}, {}
        self.handle, self.pending, self.alive = None, None, True
        self.completed = 0
        self.event_counts = {}
        self.last_exception = None
        assert C.sizeof(WowContext) == 716 and C.sizeof(DebugEvent) == 176

    @staticmethod
    def checked(ok, operation):
        if not ok:
            raise OSError(C.get_last_error(), operation)

    def context(self, tid):
        context = WowContext()
        context.ContextFlags = CONTEXT_FLAGS
        self.checked(self.api.Wow64GetThreadContext(self.threads[tid], C.byref(context)),
                     'Wow64GetThreadContext')
        return context

    def set_context(self, tid, context):
        self.checked(self.api.Wow64SetThreadContext(self.threads[tid], C.byref(context)),
                     'Wow64SetThreadContext')

    def read(self, address, size):
        if self.pending is None:
            raise RuntimeError('A boundary read requires an outstanding debug event')
        buffer, got = C.create_string_buffer(size), C.c_size_t()
        self.checked(self.api.ReadProcessMemory(self.handle, PTR(address), buffer, size,
                                                C.byref(got)), 'ReadProcessMemory')
        if got.value != size:
            raise OSError('Incomplete boundary read')
        return buffer.raw

    def arm(self, tid):
        if tid in self.original:
            return
        context = self.context(tid)
        if context.Dr7 & 0xFF:
            raise RuntimeError('Thread already has hardware breakpoints')
        self.original[tid] = tuple(getattr(context, n) for n in DEBUG_REGISTERS)
        context.Dr0, context.Dr1 = self.entry, 0
        context.Dr6, context.Dr7 = 0, (context.Dr7 & ~0xFFFF000F) | 1
        self.set_context(tid, context)

    def continue_event(self, status):
        event = self.pending
        self.checked(self.api.ContinueDebugEvent(event.pid, event.tid, status), 'ContinueDebugEvent')
        self.pending = None

    def restore(self):
        errors = []
        for tid, values in self.original.items():
            if tid not in self.threads:
                continue
            suspended = False
            try:
                if self.api.WaitForSingleObject(self.threads[tid], 0) == 0:
                    continue
                if self.pending is None:
                    if self.api.SuspendThread(self.threads[tid]) == 0xFFFFFFFF:
                        raise OSError(C.get_last_error(), 'SuspendThread during cleanup')
                    suspended = True
                context = self.context(tid)
                for name, value in zip(DEBUG_REGISTERS, values):
                    setattr(context, name, value)
                self.set_context(tid, context)
            except BaseException as error:
                errors.append(f'thread {tid}: {error}')
            finally:
                if suspended and self.api.ResumeThread(self.threads[tid]) == 0xFFFFFFFF:
                    errors.append(f'thread {tid}: ResumeThread failed ({C.get_last_error()})')
        return errors

    def drain_exit(self):
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            event = DebugEvent()
            if not self.api.WaitForDebugEvent(C.byref(event), 100):
                if C.get_last_error() in (121, 258):
                    continue
                raise OSError(C.get_last_error(), 'WaitForDebugEvent during termination')
            self.pending = event
            if event.code in (3, 6):
                handle = struct.unpack_from('<Q', bytes(event.data.raw))[0]
                if handle:
                    self.api.CloseHandle(handle)
            exited = event.code == 5
            self.continue_event(CONTINUE)
            if exited:
                self.alive = False
                return
        raise RuntimeError('Owned replay termination did not complete; debugger kill-on-exit remains enabled')

    def cleanup(self, status):
        if not self.alive:
            return
        errors = self.restore()
        if errors:
            self.checked(self.api.DebugSetProcessKillOnExit(True), 'Enable owned replay kill-on-exit')
            self.process.terminate()
            if self.pending is not None:
                self.continue_event(CONTINUE)
            self.drain_exit()
            raise RuntimeError('Terminated recorder-owned replay after breakpoint restoration failure: '
                               + '; '.join(errors))
        if self.pending is not None:
            self.continue_event(status)
        self.checked(self.api.DebugActiveProcessStop(self.process.pid), 'DebugActiveProcessStop')
        for handle in [*self.threads.values(), self.handle]:
            if handle:
                self.api.CloseHandle(handle)

    def run(self, executable, arguments, select, capture, timeout=120, pairs=1):
        if pairs < 1 or timeout <= 0:
            raise ValueError('Positive pair count and timeout required')
        startup = subprocess.STARTUPINFO()
        startup.dwFlags = subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
        self.process = subprocess.Popen([str(executable), *arguments], cwd=Path(executable).parent,
                                        creationflags=2, startupinfo=startup)
        deadline = time.monotonic() + timeout
        status = CONTINUE
        try:
            self.checked(self.api.DebugSetProcessKillOnExit(False), 'DebugSetProcessKillOnExit')
            armed = False
            while time.monotonic() < deadline:
                event = DebugEvent()
                if not self.api.WaitForDebugEvent(C.byref(event), 100):
                    error = C.get_last_error()
                    if error in (121, 258):
                        continue
                    raise OSError(error, 'WaitForDebugEvent')
                self.pending, status = event, CONTINUE
                self.event_counts[event.code] = self.event_counts.get(event.code, 0) + 1
                raw = bytes(event.data.raw)
                if event.code == 3:
                    image_file, self.handle, thread, base = struct.unpack_from('<4Q', raw)
                    if image_file:
                        self.api.CloseHandle(image_file)
                    self.threads[event.tid] = thread
                    if base != 0x400000:
                        raise RuntimeError('Unexpected executable image base')
                elif event.code == 2:
                    self.threads[event.tid] = struct.unpack_from('<Q', raw)[0]
                    if armed:
                        self.arm(event.tid)
                elif event.code == 4:
                    self.threads.pop(event.tid, None)
                    self.active.pop(event.tid, None)
                    self.original.pop(event.tid, None)
                elif event.code == 5:
                    self.alive = False
                    self.continue_event(CONTINUE)
                    break
                elif event.code == 6:
                    handle = struct.unpack_from('<Q', raw)[0]
                    if handle:
                        self.api.CloseHandle(handle)
                elif event.code == 1:
                    exception = struct.unpack_from('<I', raw)[0]
                    self.last_exception = exception
                    status = NOT_HANDLED
                    if exception in BREAKPOINTS and not armed:
                        for tid in self.threads:
                            self.arm(tid)
                        armed, status = True, CONTINUE
                    elif exception in SINGLE_STEPS and event.tid in self.original:
                        context = self.context(event.tid)
                        if context.Dr6 & 2 and event.tid in self.active:
                            status = CONTINUE
                            expected_return = self.active[event.tid]
                            if context.Eip != expected_return:
                                raise RuntimeError('Return breakpoint address mismatch')
                            capture('after', context, self.read)
                            self.active.pop(event.tid)
                            self.completed += 1
                            context.Dr0, context.Dr1 = self.entry, 0
                            context.Dr7 = (context.Dr7 & ~0xF) | 1
                            status = CONTINUE
                        elif context.Dr6 & 1 and context.Eip == self.entry:
                            status = CONTINUE
                            if not self.active and select(context, self.read):
                                return_address = struct.unpack('<I', self.read(context.Esp, 4))[0]
                                if return_address == self.entry or not return_address:
                                    raise RuntimeError('Invalid chooser return address')
                                capture('before', context, self.read)
                                self.active[event.tid] = return_address
                                context.Dr0, context.Dr1 = 0, return_address
                                context.Dr7 = (context.Dr7 & ~0xF) | 4
                            status = CONTINUE
                        if status == CONTINUE:
                            context.Dr6 = 0
                            context.EFlags |= 0x10000
                            self.set_context(event.tid, context)
                            if self.completed == pairs:
                                break
                self.continue_event(status)
            if self.completed != pairs:
                raise RuntimeError(f'Captured {self.completed} of {pairs} required pairs; '
                                   f'exit={self.process.poll()}, exception={self.last_exception}, '
                                   f'events={self.event_counts}')
        finally:
            self.cleanup(status)


def stopped_regions(handle, api):
    """Strict counterpart of process_dump.regions for a complete boundary image."""
    import process_dump as layout
    info = layout.MEMORY_BASIC_INFORMATION()
    address = 0x10000
    while address < 0x7FFF0000:
        if not api.VirtualQueryEx(handle, PTR(address), C.byref(info), C.sizeof(info)):
            raise OSError(C.get_last_error(), f'VirtualQueryEx failed at {address:08X}')
        end = (info.BaseAddress or 0) + info.RegionSize
        if info.RegionSize <= 0 or end <= address:
            raise RuntimeError('VirtualQueryEx returned a non-advancing region')
        if (info.State == layout.MEM_COMMIT and not info.Protect & (layout.PAGE_NOACCESS | layout.PAGE_GUARD)
                and info.RegionSize <= layout.LARGEST
                and (info.Type == layout.MEM_PRIVATE
                     or info.Type == layout.MEM_IMAGE and info.Protect & layout.WRITABLE)):
            yield address, end - address
        address = end


def dump_stopped(debugger, destination):
    """Use the oracle dump format while the debugger already holds the process."""
    path = Path(destination)
    partial = path.with_suffix(path.suffix + '.partial')
    count = total = 0
    compressor = zlib.compressobj(1)
    with partial.open('xb') as stream:
        for address, size in stopped_regions(debugger.handle, debugger.api):
            data = debugger.read(address, size)
            stream.write(compressor.compress(struct.pack('<II', address, size) + data))
            count, total = count + 1, total + size
        stream.write(compressor.flush())
    if not count:
        raise RuntimeError('Empty boundary dump')
    partial.rename(path)
    return {'regions': count, 'bytes': total, 'short': 0}


def imp_state(read, pointer):
    creature = read(pointer, 0x53F)
    clock = struct.unpack('<I', read(WORLD + 4, 4))[0]
    return {
        'creature': pointer, 'slot': struct.unpack_from('<H', creature, 4)[0],
        'owner': struct.unpack_from('<H', creature, 0x28)[0], 'type': creature[0x36F],
        'tick': struct.unpack('<I', read(clock + 4, 4))[0],
        'rng': struct.unpack('<I', read(WORLD + 0x15A0, 4))[0],
        'state': struct.unpack_from('<I', creature, 0x25E)[0],
        'health': struct.unpack_from('<I', creature, 0x40)[0],
        'position': list(struct.unpack_from('<ii', creature, 0x1A)),
        'task_id': struct.unpack_from('<H', creature, 0x2D8)[0],
        'task_kind': creature[0x2E6], 'task_position': list(struct.unpack_from('<ii', creature, 0x2DA)),
        'gold': struct.unpack_from('<I', creature, 0x3FB)[0],
        'action': struct.unpack_from('<I', creature, 0x3A9)[0],
        'action_epoch': struct.unpack_from('<I', creature, 0x3A5)[0],
        'animation_phase': struct.unpack_from('<I', creature, 0xD8)[0],
        'animation_flags': creature[0xF1],
        'route_target': list(struct.unpack_from('<ii', creature, 0x124 + 0xA4)),
        'route_count': creature[0x124 + 0x10C],
        'route_flags': creature[0x124 + 0x10D],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--slot', type=int, required=True)
    parser.add_argument('--from-tick', type=int, default=0)
    parser.add_argument('--pairs', type=int, default=1)
    parser.add_argument('--timeout', type=float, default=120)
    parser.add_argument('game_args', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if sys.platform != 'win32' or C.sizeof(PTR) != 8:
        parser.error('64-bit Windows CPython is required')
    if not 0 < args.slot < 4096 or args.from_tick < 0 or args.pairs < 1 or args.timeout <= 0:
        parser.error('Require slot 1..4095, nonnegative tick, positive pairs and timeout')
    existing = running_games()
    if existing:
        parser.error(f'A DK2 process is already running: {existing}; no new game launched')
    executable = args.exe.resolve()
    digest = hashlib.md5(executable.read_bytes()).hexdigest()
    if digest not in (RETAIL_MD5, DX_MD5):
        parser.error('Executable does not match pinned retail DKII.EXE or its verified DX variant')
    output = args.out.resolve()
    allowed = Path(__file__).resolve().parents[2] / 'reverse-engineering'
    if not output.is_relative_to(allowed) or output == allowed:
        parser.error(f'Output must be a new subdirectory of {allowed}')
    output.mkdir(parents=True, exist_ok=False)
    debugger = BoundaryDebugger(CHOOSER)
    selected = {}
    def select(context, read):
        nonlocal selected
        selected = imp_state(read, context.Ecx - 0x24E)
        return (selected['slot'] == args.slot and selected['owner'] == 3 and selected['type'] == 1
                and selected['state'] == 10 and selected['health'] > 0
                and selected['tick'] >= args.from_tick)
    with (output / 'boundaries.jsonl').open('x', encoding='utf-8') as manifest:
        def capture(phase, context, read):
            state = imp_state(read, selected['creature'])
            filename = f'{debugger.completed + 1:06}.{phase}.mem.z'
            dump = dump_stopped(debugger, output / filename)
            record = dict(schema=1, pair=debugger.completed + 1, phase=phase,
                          exe_md5=digest, entry=CHOOSER, pid=debugger.process.pid,
                          compatibility_layer=os.environ.get('__COMPAT_LAYER'),
                          tid=debugger.pending.tid, registers=registers(context),
                          imp=state, memory=filename, dump=dump)
            manifest.write(json.dumps(record) + '\n')
            manifest.flush()
            print(json.dumps({'phase': phase, 'imp': state, 'memory': filename}), flush=True)
        game_args = args.game_args[1:] if args.game_args[:1] == ['--'] else args.game_args
        debugger.run(executable, game_args, select, capture, args.timeout, args.pairs)
    print(f'Captured {debugger.completed} complete selection pairs; debugger detached', flush=True)


if __name__ == '__main__':
    main()

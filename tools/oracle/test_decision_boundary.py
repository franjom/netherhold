# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Live debugger tests use a generated, unmodified 32-bit PE, never DK2."""
import ctypes
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import zlib
import subprocess
from unittest import mock

import decision_boundary as recorder


def pe32(path):
    """Two calls with different ECX/EAX values; the callee increments EAX."""
    image = bytearray(1024)
    image[:2] = b'MZ'
    struct.pack_into('<I', image, 0x3C, 0x80)
    image[0x80:0x84] = b'PE\0\0'
    struct.pack_into('<HHIIIHH', image, 0x84, 0x14C, 1, 0, 0, 0, 224, 0x102)
    opt = 0x98
    struct.pack_into('<H', image, opt, 0x10B)
    struct.pack_into('<III', image, opt + 4, 512, 0, 0)
    struct.pack_into('<III', image, opt + 16, 0x1000, 0x1000, 0)
    struct.pack_into('<III', image, opt + 28, 0x400000, 4096, 512)
    struct.pack_into('<HHHHHH', image, opt + 40, 4, 0, 0, 0, 4, 0)
    struct.pack_into('<II', image, opt + 56, 8192, 512)
    struct.pack_into('<H', image, opt + 68, 3)
    struct.pack_into('<IIIIII', image, opt + 72, 0x100000, 4096, 0x100000, 4096, 0, 16)
    sec = opt + 224
    struct.pack_into('<8sIIIIIIHHI', image, sec, b'.text', 512, 4096, 512, 512, 0, 0, 0, 0, 0x60000020)
    code = bytearray()
    for eax, ecx in [(7, 0x12345678), (21, 0x87654321)]:
        code += b'\xB8' + struct.pack('<I', eax) + b'\xB9' + struct.pack('<I', ecx)
        code += b'\xE8' + struct.pack('<i', 0x40 - (len(code) + 5))
    code += b'\x31\xC0\xC3'
    image[512:512 + len(code)] = code
    image[512 + 0x40:512 + 0x42] = b'\x40\xC3'
    path.write_bytes(image)


@unittest.skipUnless(sys.platform == 'win32' and ctypes.sizeof(ctypes.c_void_p) == 8,
                     '64-bit Windows debugger required')
class BoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.exe = Path(self.temp.name) / 'boundary-fixture.exe'
        pe32(self.exe)

    def test_pair_is_at_entry_and_after_ret_and_detach_leaves_child_running(self):
        original = self.exe.read_bytes()
        frames = []
        debug = recorder.BoundaryDebugger(0x401040)
        debug.run(self.exe, [], lambda c, read: c.Ecx == 0x12345678,
                  lambda phase, c, read: frames.append((phase, c.Eax, c.Ecx, c.Eip)),
                  timeout=10, pairs=1)
        self.assertEqual([('before', 7, 0x12345678, 0x401040),
                          ('after', 8, 0x12345678, 0x40100F)], frames)
        self.assertEqual(0, debug.process.wait(timeout=5))
        self.assertEqual(original, self.exe.read_bytes())

    def test_skipped_entry_does_not_produce_a_pair(self):
        frames = []
        debug = recorder.BoundaryDebugger(0x401040)
        debug.run(self.exe, [], lambda c, read: c.Ecx == 0x87654321,
                  lambda phase, c, read: frames.append((phase, c.Eax)), timeout=10, pairs=1)
        self.assertEqual([('before', 21), ('after', 22)], frames)
        self.assertEqual(0, debug.process.wait(timeout=5))

    def _slow_capture_keeps_native_wait_budget(self, slow_phase):
        debug = recorder.BoundaryDebugger(0x401040)
        original_clock = recorder.time.monotonic
        paused_seconds = 0
        phases = []

        def capture(phase, context, read):
            nonlocal paused_seconds
            phases.append(phase)
            if phase == slow_phase:
                paused_seconds += 1000

        try:
            with mock.patch.object(recorder.time, 'monotonic',
                                   side_effect=lambda: original_clock() + paused_seconds):
                debug.run(self.exe, [], lambda *a: True, capture, timeout=5, pairs=2)
        finally:
            if debug.process is not None:
                exit_code = debug.process.wait(timeout=5)
        self.assertEqual(0, exit_code)
        self.assertEqual(['before', 'after', 'before', 'after'], phases)

    def test_entry_capture_time_does_not_spend_native_wait_budget(self):
        self._slow_capture_keeps_native_wait_budget('before')

    def test_return_capture_time_does_not_spend_native_wait_budget(self):
        self._slow_capture_keeps_native_wait_budget('after')

    def test_writer_failure_restores_breakpoints_and_resumes_child(self):
        debug = recorder.BoundaryDebugger(0x401040)
        def fail(phase, context, read):
            raise RuntimeError('writer failed')
        with self.assertRaisesRegex(RuntimeError, 'writer failed'):
            debug.run(self.exe, [], lambda c, read: True, fail, timeout=10, pairs=1)
        self.assertEqual(0, debug.process.wait(timeout=5))

    def test_after_writer_failure_also_resumes_child(self):
        debug = recorder.BoundaryDebugger(0x401040)
        def fail(phase, context, read):
            if phase == 'after':
                raise RuntimeError('after writer failed')
        with self.assertRaisesRegex(RuntimeError, 'after writer failed'):
            debug.run(self.exe, [], lambda c, read: True, fail, timeout=10, pairs=1)
        self.assertEqual(0, debug.process.wait(timeout=5))

    def test_unmatched_selector_reports_no_pair_instead_of_success(self):
        debug = recorder.BoundaryDebugger(0x401040)
        with self.assertRaisesRegex(RuntimeError, '0.*1'):
            debug.run(self.exe, [], lambda c, read: False, lambda *a: None, timeout=10, pairs=1)
        self.assertEqual(0, debug.process.wait(timeout=5))

    def test_boundary_dump_retains_the_actual_return_stack(self):
        debug = recorder.BoundaryDebugger(0x401040)
        saved = []
        def capture(phase, context, read):
            path = Path(self.temp.name) / (phase + '.mem.z')
            stats = recorder.dump_stopped(debug, path)
            self.assertGreater(stats['bytes'], 0)
            self.assertEqual(0, stats['short'])
            raw = zlib.decompress(path.read_bytes())
            at, stack, value = 0, context.Esp if phase == 'before' else context.Esp - 4, None
            while at < len(raw):
                address, length = struct.unpack_from('<II', raw, at)
                at += 8
                if address <= stack and stack + 4 <= address + length:
                    value = struct.unpack_from('<I', raw, at + stack - address)[0]
                at += length
            saved.append((phase, value))
        debug.run(self.exe, [], lambda c, read: True, capture, timeout=10, pairs=1)
        self.assertEqual([('before', 0x40100F), ('after', 0x40100F)], saved)
        self.assertEqual(0, debug.process.wait(timeout=5))

    def test_short_dump_leaves_an_incomplete_stream_without_renaming(self):
        import process_dump
        path = Path(self.temp.name) / 'incomplete.mem.z'
        debug = mock.Mock(handle=1)
        debug.read.side_effect = [b'abcd', OSError('short read')]
        with mock.patch.object(recorder, 'stopped_regions', return_value=[(16, 4), (32, 4)]):
            with self.assertRaisesRegex(OSError, 'short read'):
                recorder.dump_stopped(debug, path)
        self.assertTrue(path.exists())
        with self.assertRaises(zlib.error):
            zlib.decompress(path.read_bytes())
        self.assertFalse(path.with_suffix('.z.partial').exists())

    def test_timeout_detaches_without_terminating_the_child(self):
        image = bytearray(self.exe.read_bytes())
        image[512:514] = b'\xEB\xFE'
        self.exe.write_bytes(image)
        debug = recorder.BoundaryDebugger(0x401040)
        try:
            with self.assertRaisesRegex(RuntimeError, '0.*1'):
                debug.run(self.exe, [], lambda *a: True, lambda *a: None, timeout=0.2, pairs=1)
            self.assertIsNone(debug.process.poll())
        finally:
            if debug.process and debug.process.poll() is None:
                debug.process.terminate()
                debug.process.wait(timeout=5)

    def test_restoration_attempts_every_thread_after_a_context_failure(self):
        debug = recorder.BoundaryDebugger(0x401040)
        debug.api = mock.Mock()
        debug.api.WaitForSingleObject.return_value = 258
        debug.threads = {11: 111, 22: 222}
        debug.original = {11: (0,) * 6, 22: (0,) * 6}
        debug.pending = recorder.DebugEvent()
        debug.context = mock.Mock(side_effect=[OSError('context failed'), recorder.WowContext()])
        debug.set_context = mock.Mock()
        errors = debug.restore()
        self.assertEqual(1, len(errors))
        self.assertEqual(2, debug.context.call_count)
        self.assertEqual(22, debug.set_context.call_args.args[0])

    def test_unclearable_breakpoints_terminate_only_the_owned_child(self):
        debug = recorder.BoundaryDebugger(0x401040)
        debug.restore = mock.Mock(return_value=['injected context failure'])
        try:
            with self.assertRaisesRegex(RuntimeError, 'Terminated recorder-owned replay'):
                debug.run(self.exe, [], lambda *a: True, lambda *a: None, timeout=10, pairs=1)
            self.assertFalse(debug.alive)
            self.assertEqual(1, debug.process.wait(timeout=5))
        finally:
            if debug.process and debug.process.poll() is None:
                debug.process.terminate()
                debug.process.wait(timeout=5)

    def test_failed_region_query_cannot_publish_a_complete_dump(self):
        import process_dump
        path = Path(self.temp.name) / 'query-failed.mem.z'
        debug = mock.Mock(handle=1)
        debug.read.return_value = b'a' * 4096
        calls = 0
        def query(handle, address, pointer, size):
            nonlocal calls
            calls += 1
            if calls > 1:
                ctypes.set_last_error(5)
                return 0
            info = ctypes.cast(pointer, ctypes.POINTER(process_dump.MEMORY_BASIC_INFORMATION)).contents
            info.BaseAddress, info.RegionSize = address.value or 0, 4096
            info.State, info.Type, info.Protect = 0x1000, 0x20000, 4
            return ctypes.sizeof(info)
        debug.api.VirtualQueryEx.side_effect = query
        with self.assertRaisesRegex(OSError, 'VirtualQueryEx'):
            recorder.dump_stopped(debug, path)
        self.assertTrue(path.exists())
        with self.assertRaises(zlib.error):
            zlib.decompress(path.read_bytes())

    def test_legacy_process_dump_retains_readonly_headers_and_refuses_existing_output(self):
        import process_dump
        startup = subprocess.STARTUPINFO()
        startup.dwFlags = subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
        child = subprocess.Popen([str(self.exe)], creationflags=4, startupinfo=startup)
        try:
            path = Path(self.temp.name) / 'legacy-complete.mem.z'
            stats = process_dump.dump(child.pid, path)
            self.assertEqual(0, stats['short'])
            raw, offset, header = zlib.decompress(path.read_bytes()), 0, None
            while offset < len(raw):
                address, length = struct.unpack_from('<II', raw, offset)
                offset += 8
                if address <= 0x400000 < address + length:
                    header = raw[offset + 0x400000 - address:offset + 0x400002 - address]
                offset += length
            self.assertEqual(b'MZ', header)
            original = path.read_bytes()
            with self.assertRaises(FileExistsError):
                process_dump.dump(child.pid, path)
            self.assertEqual(original, path.read_bytes())
        finally:
            if child.poll() is None:
                child.terminate()
                child.wait(timeout=10)


# ---- Q737 preset (was test_q737_boundary.py, 2026-11-16) ----
import json
from types import SimpleNamespace
import decision_boundary as capture


class TrollMemory:
    def __init__(self):
        self.data = {}
        self.actor, self.definition, self.container, self.boulder = 0x10000, 0x20000, 0x30000, 0x40000
        self.put(self.actor, bytes(0x53F))
        self.put(self.definition, bytes(0x800))
        self.value(0x006F2A08+4, self.container)
        self.value(self.container+4, 12)
        self.value(0x006F2A08+0x15A0, 123)
        for offset,value,fmt in [(4,453,'<H'),(0x28,3,'<H'),(0x36F,10,'<B'),(0x403,1,'<B'),
            (0x370,self.definition,'<I'),(0x25E,256,'<I'),(0x40,500,'<i'),(0x266,11,'<I'),
            (0x526,1752,'<H'),(0x524,1752,'<H'),(0x533,1155,'<H'),(0x3EA,0x40,'<I')]:
            self.value(self.actor+offset,value,fmt)
        self.value(self.definition+0x74C,8)
        self.value(self.definition+0x696,1000,'<H')
        self.value(0x006ED540+1155,1,'<B')
        self.value(0x006EE540+1155*4,self.boulder)
        self.put(self.boulder,bytes(0x189))
        self.value(self.boulder+4,1155,'<H')
        self.value(self.boulder+0x12,2)
        self.value(self.boulder+0x12C,74,'<B')

    def put(self,address,data):
        self.data.update((address+i,value) for i,value in enumerate(data))

    def value(self,address,value,fmt='<I'):
        self.put(address,struct.pack(fmt,value))

    def read(self,address,size):
        try:
            return bytes(self.data[address+i] for i in range(size))
        except KeyError as error:
            raise OSError('unreadable synthetic memory') from error


class Q737PresetTests(unittest.TestCase):
    def module(self):
        self.assertIsNotNone(capture,'Q737 boundary adapter has not been implemented')
        return capture

    def test_loaded_interval_and_entry_stamp_are_read_from_the_live_definition(self):
        m = TrollMemory()
        state = self.module().subject_state(m.read,m.actor)
        self.assertEqual((256,500,8,1),(state['state'],state['health'],state['stunInterval'],state['elapsed']))
        self.assertTrue(state['holdWouldContinue'])
        self.assertEqual(74,state['threat']['objectType'])

    def test_first_selection_requires_boulder_stun_but_following_dying_updates_remain_selected(self):
        m = TrollMemory()
        window = self.module().TrollWindow()
        m.value(m.actor+0x25E,255)
        self.assertFalse(window.select(m.actor,m.read))
        m.value(m.actor+0x25E,256)
        self.assertTrue(window.select(m.actor,m.read))
        window.sample('before',m.read)
        m.value(m.actor+0x25E,6)
        m.value(m.actor+0x32D,221)
        self.assertEqual(6,window.sample('after',m.read)['state'])
        m.value(m.container+4,13)
        m.value(m.actor+0x25E,216)
        m.value(m.actor+0x40,240,'<i')
        self.assertTrue(window.select(m.actor,m.read))
        self.assertEqual(216,window.sample('before',m.read)['state'])
        self.assertFalse(window.select(m.actor+0x1000,m.read))

    def test_pair_clock_drift_is_rejected(self):
        m = TrollMemory()
        window = self.module().TrollWindow()
        self.assertTrue(window.select(m.actor,m.read))
        window.sample('before',m.read)
        m.value(m.container+4,13)
        with self.assertRaises(ValueError):
            window.sample('after',m.read)

    def test_identity_drift_is_rejected(self):
        m = TrollMemory()
        window = self.module().TrollWindow()
        self.assertTrue(window.select(m.actor,m.read))
        window.sample('before',m.read)
        m.value(m.actor+4,454,'<H')
        with self.assertRaises(ValueError):
            window.sample('after',m.read)

    def test_unreadable_loaded_interval_fails_closed(self):
        m = TrollMemory()
        del m.data[m.definition+0x74C]
        with self.assertRaises(OSError):
            self.module().subject_state(m.read,m.actor)
    def test_capture_error_survives_cleanup_error_and_both_are_recorded(self):
        process = SimpleNamespace(pid=42,poll=lambda: None,
            terminate=lambda: (_ for _ in ()).throw(OSError('cleanup failed')))
        debugger = SimpleNamespace(process=process,completed=0)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            with self.assertRaisesRegex(ValueError,'capture failed'):
                self.module().run_owned(debugger,
                    lambda: (_ for _ in ()).throw(ValueError('capture failed')),lambda: None,output)
            receipt = json.loads((output/'run-status.json').read_text())
            self.assertIn('capture failed',receipt['error'])
            self.assertIn('cleanup failed',receipt['cleanupError'])
            self.assertFalse(receipt['ownedClosed'])
    def test_validation_failure_is_recorded_after_owned_child_shutdown(self):
        closed = []
        process = SimpleNamespace(pid=42,poll=lambda: 0 if closed else None,
            terminate=lambda: closed.append(True),wait=lambda timeout: 0)
        debugger = SimpleNamespace(process=process,completed=4)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            with self.assertRaisesRegex(ValueError,'level changed'):
                self.module().run_owned(debugger,lambda: None,
                    lambda: (_ for _ in ()).throw(ValueError('level changed')),output)
            receipt = json.loads((output/'run-status.json').read_text())
            self.assertIn('level changed',receipt['error'])
            self.assertTrue(receipt['ownedClosed'])
    def test_cleanup_error_prevents_success_and_has_a_receipt(self):
        process = SimpleNamespace(pid=42,poll=lambda: None,
            terminate=lambda: (_ for _ in ()).throw(OSError('cleanup failed')))
        debugger = SimpleNamespace(process=process,completed=4)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            with self.assertRaisesRegex(OSError,'cleanup failed'):
                self.module().run_owned(debugger,lambda: None,lambda: None,output)
            receipt = json.loads((output/'run-status.json').read_text())
            self.assertIn('cleanup failed',receipt['cleanupError'])
            self.assertFalse(receipt['ownedClosed'])


class OwnSourcesTests(unittest.TestCase):
    def test_each_copied_source_is_named_once(self):
        # The copy into the evidence folder is exclusive ('xb'): a name listed twice crashed the Q737 preset (2026-11-16).
        names = [path.name for path in capture.own_sources()]
        self.assertEqual(len(names), len(set(names)))
        self.assertEqual(names, ['decision_boundary.py', 'process_dump.py'])


if __name__ == '__main__':
    unittest.main()

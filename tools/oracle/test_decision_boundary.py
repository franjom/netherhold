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

    def test_short_dump_does_not_publish_a_complete_image(self):
        import process_dump
        path = Path(self.temp.name) / 'incomplete.mem.z'
        debug = mock.Mock(handle=1)
        debug.read.side_effect = [b'abcd', OSError('short read')]
        with mock.patch.object(recorder, 'stopped_regions', return_value=[(16, 4), (32, 4)]):
            with self.assertRaisesRegex(OSError, 'short read'):
                recorder.dump_stopped(debug, path)
        self.assertFalse(path.exists())
        self.assertTrue(path.with_suffix('.z.partial').exists())

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
            info.BaseAddress, info.RegionSize = 0x10000, 4096
            info.State, info.Type, info.Protect = 0x1000, 0x20000, 4
            return ctypes.sizeof(info)
        debug.api.VirtualQueryEx.side_effect = query
        with self.assertRaisesRegex(OSError, 'VirtualQueryEx'):
            recorder.dump_stopped(debug, path)
        self.assertFalse(path.exists())


if __name__ == '__main__':
    unittest.main()

# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Completeness regressions against the existing boundary-recorder API."""
import ctypes
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import decision_boundary as recorder
import process_dump as layout


class RegionTests(unittest.TestCase):
    def regions(self, kind, protect, size=4096, reader=recorder.stopped_regions):
        def query(handle, address, pointer, length):
            at = address.value or 0
            info = ctypes.cast(pointer, ctypes.POINTER(layout.MEMORY_BASIC_INFORMATION)).contents
            info.BaseAddress = at
            info.RegionSize = 0x10000 if at == 0 else size if at == 0x10000 else (1 << 32) - at
            info.State = layout.MEM_COMMIT if at == 0x10000 else 0x10000
            info.Type = kind
            info.Protect = protect
            return length
        api = mock.Mock(VirtualQueryEx=mock.Mock(side_effect=query))
        return list(reader(1, api))

    def test_mapped_readable_memory_is_not_silently_omitted(self):
        self.assertEqual([(0x10000, 4096)], self.regions(0x40000, 4))

    def test_readonly_image_memory_is_retained_without_a_flag(self):
        self.assertEqual([(0x10000, 4096)], self.regions(layout.MEM_IMAGE, 2))

    def test_large_readable_regions_are_retained_without_a_size_cutoff(self):
        size = 65 * 1024 * 1024
        self.assertEqual([(0x10000, size)], self.regions(layout.MEM_PRIVATE, 4, size))

    def test_legacy_process_dump_uses_the_same_complete_memory_policy(self):
        self.assertEqual([(0x10000, 4096)], self.regions(0x40000, 2, reader=layout.regions))

    def test_guard_and_noaccess_are_excluded(self):
        self.assertEqual([], self.regions(layout.MEM_PRIVATE, 0x104))
        self.assertEqual([], self.regions(layout.MEM_PRIVATE, 1))

    def test_query_failure_cannot_be_treated_as_the_end_of_memory(self):
        api = mock.Mock(VirtualQueryEx=mock.Mock(return_value=0))
        with self.assertRaisesRegex(OSError, 'VirtualQueryEx failed'):
            list(recorder.memory_regions(1, api))

    def test_short_query_metadata_cannot_reuse_fields_from_a_previous_region(self):
        api = mock.Mock(VirtualQueryEx=mock.Mock(return_value=1))
        with self.assertRaisesRegex(OSError, 'incomplete metadata'):
            list(recorder.memory_regions(1, api))

    def test_metadata_covers_the_entire_32bit_address_domain(self):
        def query(handle, address, pointer, length):
            info = ctypes.cast(pointer, ctypes.POINTER(layout.MEMORY_BASIC_INFORMATION)).contents
            at = address.value or 0
            info.BaseAddress = at
            info.RegionSize = 0xFFFE0000 if at == 0 else 0x1000 if at == 0xFFFE0000 else (1 << 32) - at
            info.State = layout.MEM_COMMIT if at == 0xFFFE0000 else 0x10000
            info.Type, info.Protect = 0x40000, 2
            return length
        regions = list(recorder.memory_regions(1, mock.Mock(VirtualQueryEx=mock.Mock(side_effect=query))))
        self.assertEqual([(0, 0xFFFE0000), (0xFFFE0000, 4096), (0xFFFE1000, 0x1F000)],
                         [(r['base'], r['size']) for r in regions])
        self.assertEqual([False, True, False], [r['readable'] for r in regions])


class ExclusiveDumpTests(unittest.TestCase):
    def test_completed_dump_does_not_rename_an_evidence_file(self):
        with tempfile.TemporaryDirectory() as folder:
            destination = Path(folder) / 'capture.mem.z'
            debug = mock.Mock(handle=1, read=mock.Mock(return_value=b'abcd'))
            with mock.patch.object(recorder, 'stopped_regions', return_value=[(16, 4)]):
                with mock.patch.object(Path, 'rename', side_effect=AssertionError('evidence rename')):
                    recorder.dump_stopped(debug, destination)
            self.assertTrue(destination.is_file())


if __name__ == '__main__':
    unittest.main()

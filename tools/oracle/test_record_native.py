# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
import struct
import unittest

from record_native import packet_info, requested_ticks, structured_roundtrip
from unittest import mock


class ContractTests(unittest.TestCase):
    def test_packet_derives_level_and_parses_all_records_including_the_last(self):
        data = b'level5\0' + bytes(57) + struct.pack('<I', 123)
        data += b'\0' + struct.pack('<I', 2) + bytes(12) + bytes(12)
        data += b'\1' + struct.pack('<I', 3) + bytes(12) + bytes(18) + bytes(12)
        info = packet_info(data)
        self.assertEqual('level5', info['level'])
        self.assertEqual(2, info['records'])
        self.assertEqual([2, 3], [r['stamp'] for r in info['turns']])

    def test_truncated_packet_fails_before_launch(self):
        with self.assertRaises(ValueError):
            packet_info(b'level5\0' + bytes(61) + bytes(28))

    def test_complete_memory_preflight_cannot_be_disabled(self):
        self.assertEqual([1, 2, 1400, 1401], requested_ticks(1400, 1401))
        self.assertEqual([1, 2, 3], requested_ticks(1, 3))
        with self.assertRaises(ValueError):
            requested_ticks(0, 3)

    def test_structured_preflight_rejects_a_codec_that_drops_a_region(self):
        layout = mock.Mock()
        layout.FrameEncoder.return_value.encode.return_value = b'fixture'
        layout.apply_frame.return_value = (0, 1, 1, 0, 0, 1)
        with self.assertRaisesRegex(ValueError, 'round-trip'):
            structured_roundtrip((1, 1, 0, {9: (0x10000, b'original')}, {}), layout)


if __name__ == '__main__':
    unittest.main()

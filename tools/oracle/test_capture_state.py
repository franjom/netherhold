# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""capture_state.py's own reads; the capture decoder is stubbed, so no capture or reverse-engineering/ is needed."""
import struct
import sys
import types
import unittest

sys.modules.setdefault("capture_memory", types.SimpleNamespace(dk2capture=None))
import capture_state  # noqa: E402


class Record(object):
    """A creature record's raw bytes, read as dk2capture.Record reads them."""

    def __init__(self, health):
        self.raw = bytearray(0x400)
        struct.pack_into("<i", self.raw, 0x40, health)

    def u(self, off, fmt="<I"):
        return struct.unpack_from(fmt, self.raw, off)[0]


class CreatureHealthTests(unittest.TestCase):
    def test_a_dead_pool_slots_negative_health_stays_negative(self):
        # Level 4 state 2 at 5000, slot 415: -360, which the unsigned field wrote as 4294966936.
        self.assertEqual(capture_state.creature_health(Record(-360)), -360)

    def test_a_living_creatures_health_is_unchanged(self):
        self.assertEqual(capture_state.creature_health(Record(1250)), 1250)


if __name__ == "__main__":
    unittest.main()

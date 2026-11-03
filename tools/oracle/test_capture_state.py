# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""capture_state.py's own reads; the capture decoder is stubbed, so no capture or reverse-engineering/ is needed."""
import struct
import json
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest import mock

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


class BodyWindowTests(unittest.TestCase):
    def test_native_window_stops_before_the_radius_and_limits(self):
        # 004D5C00 logs five vectors at body+B4; the next bytes are radius+DC.
        # Seed2/1544 Imp455 supplies these captured points and scalar sentinels.
        r = Record(625)
        r.ptr = 0x100000
        points = [[1015808, 2490368], [1015808, 2424832], [1081344, 2228224],
                  [1081344, 2162688], [917504, 2064384]]
        for i, point in enumerate(points):
            struct.pack_into("<ii", r.raw, 0x124 + 0xB4 + 8 * i, *point)
        struct.pack_into("<hii", r.raw, 0x124 + 0xDC, 4576, 98304, 30144)
        result = capture_state.body(r)
        self.assertEqual(result["window"], points)
        self.assertEqual(result["radius"], 4576)


class CompletenessTests(unittest.TestCase):
    def snapshot(self):
        return types.SimpleNamespace(tick=7, torn=False, regions={1: (0x1000, None)})

    def memory(self, clock=7, width=2):
        words = {0x1004: 0x2000, 0x2004: clock, 0x19F2: 0x3000, 0x3008: width, 0x3010: 0x4000}
        return types.SimpleNamespace(u=lambda at: words[at], read=lambda at, size: bytes(range(size)))

    def test_wrong_memory_clock_is_rejected_before_exporting_navigation(self):
        with mock.patch.object(capture_state, 'dk2capture', types.SimpleNamespace(REGION_WORLD=1)):
            with self.assertRaisesRegex(ValueError, 'clock'):
                capture_state.with_dump(dict(width=2, height=2, creatures=[]), self.snapshot(), self.memory(clock=8))

    def test_wrong_navigation_width_is_rejected_instead_of_silently_omitted(self):
        with mock.patch.object(capture_state, 'dk2capture', types.SimpleNamespace(REGION_WORLD=1)):
            with self.assertRaisesRegex(ValueError, 'width'):
                capture_state.with_dump(dict(width=2, height=2, creatures=[]), self.snapshot(), self.memory(width=3))

    def test_complete_memory_fields_are_automatic_even_for_state_only(self):
        with tempfile.TemporaryDirectory() as output:
            snap = self.snapshot()
            cap = types.SimpleNamespace(ticks=[7], at=lambda tick: snap, header={})
            layout = types.SimpleNamespace(Capture=lambda path: cap, REGION_WORLD=1)
            memory = self.memory()
            memory.folder = Path(output) / 'memory'
            module = types.SimpleNamespace(for_capture=lambda path, tick, header: memory)
            with mock.patch.object(capture_state, 'dk2capture', layout), \
                    mock.patch.object(capture_state, 'state', return_value=dict(tick=7, width=2, height=2, creatures=[])), \
                    mock.patch.dict(sys.modules, native_memory=module), \
                    mock.patch.object(sys, 'argv', ['capture_state.py', 'level.dk2cap', output, '7', '--state-only']):
                capture_state.main()
            result = json.loads((Path(output) / '7.json').read_text())
            self.assertIn('navBytes', result)
            self.assertEqual([0, 1, 2, 3], result['navBytes'])
            self.assertEqual('complete-readable-process-memory', result['captureCompleteness'])


if __name__ == "__main__":
    unittest.main()

# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""capture_export.py state's own reads; the capture decoder is stubbed, so no capture or reverse-engineering/ is needed."""
import struct
import json
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import capture_export  # noqa: E402


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
        self.assertEqual(capture_export.creature_health(Record(-360)), -360)

    def test_a_living_creatures_health_is_unchanged(self):
        self.assertEqual(capture_export.creature_health(Record(1250)), 1250)


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
        result = capture_export.body(r)
        self.assertEqual(result["window"], points)
        self.assertEqual(result["radius"], 4576)


class TerminalStateTests(unittest.TestCase):
    def heart_snapshot(self, present=True):
        player = bytearray(0x1125)
        struct.pack_into('<HH', player, 0xB43, 1, 1)
        room = Record(0)
        room.kind, room.slot = 'room', 77
        room.f = lambda name: {'tag': 77, 'type_id': 5, 'player': 93}[name]
        room.raw[0x56] = 3
        room.raw[0x9A] = 1
        struct.pack_into('<H', room.raw, 0x0E, 88 if present else 0)
        heart = Record(-25)
        heart.kind, heart.slot = 'object', 88
        heart.f = lambda name: {'type_id': 13}[name]
        struct.pack_into('<H', heart.raw, 0x36, 99)
        struct.pack_into('<H', heart.raw, 0x138, 77)
        active = bytearray(100)
        active[77], active[88] = 3, 2
        tile = bytearray(12)
        struct.pack_into('<H', tile, 2, 77)
        snap = types.SimpleNamespace(map_size=lambda: (3, 3), tile=lambda x, y: tile,
                                     region=lambda kind: active)
        return player, room, heart, snap

    def test_heart_comes_from_recorded_site_and_linked_object_with_translated_source(self):
        player, room, heart, snap = self.heart_snapshot()
        with mock.patch.object(capture_export, 'dk2capture', types.SimpleNamespace(REGION_ACTIVE=1)):
            result = capture_export.player_heart(snap, player, [room, heart], {93: 3, 99: 4})
        self.assertEqual(result, dict(health=-25, lastAttacker=4, roomRuined=True, regenCountdown=3, roomTag=77))

    def test_captured_absent_heart_is_zero_health_not_unavailable(self):
        player, room, heart, snap = self.heart_snapshot(False)
        with mock.patch.object(capture_export, 'dk2capture', types.SimpleNamespace(REGION_ACTIVE=1)):
            result = capture_export.player_heart(snap, player, [room, heart], {93: 3})
        self.assertEqual(result['health'], 0)
        self.assertEqual(result['lastAttacker'], 0)

    def test_uncaptured_or_wrong_site_is_unavailable_instead_of_a_dead_heart(self):
        player, room, heart, snap = self.heart_snapshot()
        self.assertIsNone(capture_export.player_heart(snap, player, [], {}))

    def test_wrong_room_membership_is_rejected(self):
        player, room, heart, snap = self.heart_snapshot()
        struct.pack_into('<H', heart.raw, 0x138, 78)
        with mock.patch.object(capture_export, 'dk2capture', types.SimpleNamespace(REGION_ACTIVE=1)):
            with self.assertRaisesRegex(ValueError, 'room'):
                capture_export.player_heart(snap, player, [room, heart], {93: 3, 99: 4})

    def test_another_owned_ruin_does_not_replace_the_heart_at_the_recorded_site(self):
        player, room, heart, snap = self.heart_snapshot()
        other = Record(0)
        other.kind, other.slot = 'room', 78
        other.f = lambda name: {'tag': 78, 'type_id': 5, 'player': 93}[name]
        other.raw[0x9A] = 1
        with mock.patch.object(capture_export, 'dk2capture', types.SimpleNamespace(REGION_ACTIVE=1)):
            result = capture_export.player_heart(snap, player, [other, room, heart], {93: 3, 99: 4})
        self.assertEqual(result['roomTag'], 77)
        self.assertEqual(result['health'], -25)

    def test_a_claimed_ruin_at_the_original_site_is_not_discarded_by_new_ownership(self):
        player, room, heart, snap = self.heart_snapshot(False)
        struct.pack_into('<H', player, 4, 93)
        room.f = lambda name: {'tag': 77, 'type_id': 5, 'player': 99}[name]
        with mock.patch.object(capture_export, 'dk2capture', types.SimpleNamespace(REGION_ACTIVE=1)):
            result = capture_export.player_heart(snap, player, [room, heart], {93: 3, 99: 4})
        self.assertTrue(result['roomRuined'])
        self.assertEqual(result['health'], 0)

    def test_active_player_list_excludes_allocated_but_uninitialized_slots(self):
        raw = [bytearray(0x1125) for _ in range(3)]
        for index, tag in enumerate([93, 94, 95]):
            struct.pack_into('<H', raw[index], 4, tag)
        struct.pack_into('<H', raw[0], 0x3A, 95)
        world = bytearray(0x80)
        struct.pack_into('<H', world, 0x7A, 93)
        snap = types.SimpleNamespace(player=lambda index: raw[index], region=lambda kind: world)
        with mock.patch.object(capture_export, 'dk2capture', types.SimpleNamespace(REGION_WORLD=1, PLAYER_COUNT=3)):
            self.assertEqual(capture_export.active_player_tags(snap), {93, 95})

    def test_full_player_status_and_killer_tags_are_read_without_a_fake_verdict_tick(self):
        raw = bytearray(0x1125)
        struct.pack_into('<I', raw, 0x19A, 3)
        struct.pack_into('<H', raw, 0x132, 99)
        struct.pack_into('<I', raw, 0xEE2, 124)
        struct.pack_into('<I', raw, 0x76, 1 << 22)
        result = capture_export.player_terminal(raw, {99: 4})
        self.assertEqual(result, dict(status=3, killedByPlayer=4, heartDiedTick=124, keeperFrameAfterDefeat=True))
        self.assertNotIn('statusTick', result)

    def test_short_player_head_cannot_silently_become_playing(self):
        with self.assertRaisesRegex(ValueError, 'full player'):
            capture_export.player_terminal(bytes(0x40), {})

    def test_unresolved_nonzero_killer_tag_is_rejected(self):
        raw = bytearray(0x1125)
        struct.pack_into('<H', raw, 0x132, 99)
        with self.assertRaisesRegex(ValueError, 'player tag'):
            capture_export.player_terminal(raw, {})

    def test_zero_killer_stays_zero_in_a_playing_capture(self):
        self.assertEqual(capture_export.player_terminal(bytes(0x1125), {})['killedByPlayer'], 0)


class CompletenessTests(unittest.TestCase):
    def snapshot(self):
        return types.SimpleNamespace(tick=7, torn=False, regions={1: (0x1000, None)})

    def memory(self, clock=7, width=2):
        words = {0x1004: 0x2000, 0x2004: clock, 0x19F2: 0x3000, 0x3008: width, 0x3010: 0x4000}
        return types.SimpleNamespace(u=lambda at: words[at], read=lambda at, size: bytes(range(size)))

    def test_wrong_memory_clock_is_rejected_before_exporting_navigation(self):
        with mock.patch.object(capture_export, 'dk2capture', types.SimpleNamespace(REGION_WORLD=1)):
            with self.assertRaisesRegex(ValueError, 'clock'):
                capture_export.with_dump(dict(width=2, height=2, creatures=[]), self.snapshot(), self.memory(clock=8))

    def test_wrong_navigation_width_is_rejected_instead_of_silently_omitted(self):
        with mock.patch.object(capture_export, 'dk2capture', types.SimpleNamespace(REGION_WORLD=1)):
            with self.assertRaisesRegex(ValueError, 'width'):
                capture_export.with_dump(dict(width=2, height=2, creatures=[]), self.snapshot(), self.memory(width=3))

    def test_complete_memory_fields_are_automatic_even_for_state_only(self):
        with tempfile.TemporaryDirectory() as output:
            snap = self.snapshot()
            cap = types.SimpleNamespace(ticks=[7], at=lambda tick: snap, header={})
            layout = types.SimpleNamespace(Capture=lambda path: cap, REGION_WORLD=1)
            memory = self.memory()
            memory.folder = Path(output) / 'memory'
            module = types.SimpleNamespace(for_capture=lambda path, tick, header: memory)
            with mock.patch.object(capture_export, 'dk2capture', layout), \
                    mock.patch.object(capture_export, 'load_decoder', return_value=layout), \
                    mock.patch.object(capture_export, 'state', return_value=dict(tick=7, width=2, height=2, creatures=[])), \
                    mock.patch.dict(sys.modules, native_memory=module):
                capture_export.main(['state', 'level.dk2cap', output, '7', '--state-only'])
            result = json.loads((Path(output) / '7.json').read_text())
            self.assertIn('navBytes', result)
            self.assertEqual([0, 1, 2, 3], result['navBytes'])
            self.assertEqual('complete-readable-process-memory', result['captureCompleteness'])


class CliTests(unittest.TestCase):
    def test_too_few_arguments_or_help_prints_the_usage(self):
        for argv in (['state'], ['state', '--help'], ['memory'], ['memory', '-h'], ['state', 'a.dk2cap', 'out']):
            with self.assertRaises(SystemExit) as stop:
                capture_export.main(argv)
            self.assertIn('Usage:', str(stop.exception.code))

    def test_state_loads_the_captures_decoder_before_making_the_output_folder(self):
        with tempfile.TemporaryDirectory() as root:
            out = Path(root) / 'out'
            with mock.patch.object(capture_export, 'load_decoder', side_effect=ValueError('no decoder')) as load:
                with self.assertRaises(ValueError):
                    capture_export.main(['state', '--state-only', 'level.dk2cap', str(out), '7'])
            load.assert_called_once_with('level.dk2cap')
            self.assertFalse(out.exists())


if __name__ == "__main__":
    unittest.main()

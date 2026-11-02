# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib
import hashlib

from native_memory import MemoryReader, MemoryWriter, for_capture


def regions(size=131072, readable=True):
    result = [dict(base=0, size=65536, readable=False, reason='uncommitted'),
              dict(base=65536, size=size, readable=readable, reason='captured' if readable else 'uncommitted'),
              dict(base=65536 + size, size=(1 << 32) - 65536 - size, readable=False, reason='uncommitted')]
    for region in result:
        region.update(state=0x1000 if region['readable'] else 0x10000,
                      protect=4 if region['readable'] else 0, type=0x20000)
    return result


class TimelineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name) / 'memory'

    def test_roundtrip_crosses_storage_blocks_and_preserves_native_region_boundaries(self):
        writer = MemoryWriter(self.folder)
        data = bytes(range(256)) * 512
        writer.capture(1400, regions(), lambda at, size: data[at - 65536:at - 65536 + size])
        reader = MemoryReader(self.folder, 1400)
        self.assertEqual(data[65530:65550], reader.read(65536 + 65530, 20))
        destination = Path(self.temp.name) / '1400.mem.z'
        reader.export(destination)
        raw = zlib.decompress(destination.read_bytes())
        self.assertEqual((65536, len(data)), struct.unpack_from('<II', raw))
        self.assertEqual(data, raw[8:])
        with self.assertRaises(FileExistsError):
            reader.export(destination)
        plain = Path(self.temp.name) / '1400.mem'
        reader.export(plain, compressed=False)
        self.assertEqual(raw, plain.read_bytes())
        with self.assertRaises(FileNotFoundError):
            for_capture(Path(self.temp.name) / 'nonexistent.dk2cap', 1400)

    def test_unchanged_memory_is_deduplicated_and_decommits_remove_old_bytes(self):
        writer = MemoryWriter(self.folder)
        writer.capture(1, regions(), lambda at, size: b'a' * size)
        count = len(list((self.folder / 'blocks').rglob('*.z')))
        writer.capture(2, regions(), lambda at, size: b'a' * size)
        self.assertEqual(count, len(list((self.folder / 'blocks').rglob('*.z'))))
        changed = writer.capture(3, regions(readable=False), lambda *_: self.fail('read uncommitted page'))
        self.assertEqual(0, changed['bytes'])
        with self.assertRaisesRegex(ValueError, 'not captured'):
            MemoryReader(self.folder, 3).read(65536, 1)
        self.assertEqual(b'aa', MemoryReader(self.folder, 1).read(65536, 2))

    def test_short_read_never_publishes_a_complete_frame(self):
        writer = MemoryWriter(self.folder)
        with self.assertRaisesRegex(OSError, 'short'):
            writer.capture(1, regions(), lambda at, size: b'a')
        with self.assertRaises((FileNotFoundError, ValueError)):
            MemoryReader(self.folder, 1)

    def test_corrupt_or_truncated_block_is_detected(self):
        writer = MemoryWriter(self.folder)
        writer.capture(1, regions(), lambda at, size: b'a' * size)
        blob = next((self.folder / 'blocks').rglob('*.z'))
        blob.write_bytes(blob.read_bytes()[:-1])
        with self.assertRaisesRegex(ValueError, 'block'):
            MemoryReader(self.folder, 1).validate()

    def test_incomplete_domain_and_duplicate_turns_are_rejected(self):
        writer = MemoryWriter(self.folder)
        with self.assertRaisesRegex(ValueError, 'coverage'):
            writer.capture(1, regions()[1:], lambda at, size: b'a' * size)
        writer.capture(1, regions(), lambda at, size: b'a' * size)
        with self.assertRaises(ValueError):
            writer.capture(1, regions(), lambda at, size: b'a' * size)

    def test_readable_pages_cannot_be_marked_as_an_optional_omission(self):
        writer = MemoryWriter(self.folder)
        description = regions()
        description[1]['readable'] = False
        with self.assertRaisesRegex(ValueError, 'protection'):
            writer.capture(1, description, lambda at, size: b'a' * size)

    def test_missing_completion_marker_or_changed_index_is_rejected(self):
        writer = MemoryWriter(self.folder)
        writer.capture(1, regions(), lambda at, size: b'a' * size)
        index = self.folder / 'frames' / '0000000001.json'
        data = json.loads(index.read_text())
        data['tick'] = 99
        index.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'index'):
            MemoryReader(self.folder, 1)

    def published_capture(self):
        writer = MemoryWriter(self.folder)
        for tick in (1, 2):
            writer.capture(tick, regions(), lambda at, size: b'a' * size)
        root = Path(self.temp.name)
        capture = root / 'level.dk2cap'
        header = json.dumps(dict(complete_memory='memory')).encode()
        capture.write_bytes(b'DK2CAP\x01\n' + struct.pack('<IB', len(header), ord('H')) + header)
        (root / 'run-status.json').write_text(json.dumps(dict(requested=[1, 2], completed=[1, 2],
                                                            error=None, ownedClosed=True, packetUnchanged=True)))
        (root / 'verified.json').write_text(json.dumps(dict(decodedTurns=[1, 2], memoryTurns=[1, 2],
                                                          captureFile=capture.name,
                                                          captureSHA256=hashlib.sha256(capture.read_bytes()).hexdigest())))
        manifest = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                    for p in root.rglob('*') if p.is_file()}
        (root / 'sha256-final.json').write_text(json.dumps(manifest))
        return capture

    def test_verified_capture_identity_and_turn_are_bound_to_the_timeline(self):
        capture = self.published_capture()
        self.assertEqual(b'aa', for_capture(capture, 2).read(65536, 2))
        with self.assertRaisesRegex(ValueError, 'turn'):
            for_capture(capture, 3)
        capture.write_bytes(capture.read_bytes() + b'replaced')
        with self.assertRaisesRegex(ValueError, 'identity'):
            for_capture(capture, 2)

    def test_missing_verification_or_timeline_cannot_downgrade_to_partial(self):
        capture = self.published_capture()
        (Path(self.temp.name) / 'verified.json').unlink()
        with self.assertRaises(FileNotFoundError):
            for_capture(capture, 2)
        with self.assertRaisesRegex(ValueError, 'profile'):
            for_capture(capture, 2, dict(complete_memory='unknown'))


if __name__ == '__main__':
    unittest.main()

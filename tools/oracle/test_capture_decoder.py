# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Synthetic envelopes exercise decoder selection before any decoder code runs."""
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest


class DecoderTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.capture = self.root / 'level.dk2cap'
        header = json.dumps(dict(complete_memory='memory')).encode()
        self.capture.write_bytes(b'DK2CAP\x01\n' + struct.pack('<IB', len(header), ord('H')) + header)
        source = self.root / 'sources'
        source.mkdir()
        self.decoder = source / 'dk2capture.py'
        self.decoder.write_text("SENTINEL = 'frozen'\n")
        (self.root / 'sha256-final.json').write_text(json.dumps({
            'sources/dk2capture.py': hashlib.sha256(self.decoder.read_bytes()).hexdigest()}))

    def invoke(self, override=None):
        tools = str(Path(__file__).resolve().parent)
        code = ('import sys;sys.path.insert(0,' + repr(tools) + ');'
                'sys.argv=["capture_state.py","--state-only",' + repr(str(self.capture)) +
                ',"unused","1"];import capture_memory;print(capture_memory.dk2capture.SENTINEL)')
        env = {k: v for k, v in os.environ.items() if k != 'DK2_CAPTURE_DECODER'}
        if override is not None:
            env['DK2_CAPTURE_DECODER'] = str(override)
        return subprocess.run([sys.executable, '-I', '-c', code], env=env,
                              capture_output=True, text=True, timeout=10)

    def test_leading_state_only_and_isolation_still_use_frozen_decoder(self):
        result = self.invoke()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('frozen', result.stdout.strip())

    def test_changed_or_missing_frozen_decoder_cannot_fall_back_to_archive(self):
        self.decoder.write_text("SENTINEL = 'wrong'\n")
        result = self.invoke()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('decoder is missing or has a different identity', result.stderr)
        self.decoder.unlink()
        result = self.invoke()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('decoder is missing or has a different identity', result.stderr)

    def test_override_cannot_replace_complete_capture_decoder_with_other_code(self):
        override = self.root / 'override'
        override.mkdir()
        (override / 'dk2capture.py').write_text("SENTINEL = 'override'\n")
        result = self.invoke(override)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('override differs', result.stderr)


if __name__ == '__main__':
    unittest.main()

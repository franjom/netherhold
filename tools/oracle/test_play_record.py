# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""play-record.ps1 -Decode on a synthetic packet: every action layout lands in its named columns."""
import csv
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parent / 'play-record.ps1'


def tile(x, y):
    return x | (y << 16)


def packet(turns):
    """turns: list of (stamp, seed, [(d1, d2, d3, action)], (camera_x, camera_y))."""
    data = bytearray(b'Level6a'.ljust(64, b'\0')) + struct.pack('<I', 7)
    for stamp, seed, commands, (cx, cy) in turns:
        data += struct.pack('<BIII', len(commands), stamp, seed, 0) + bytes([1, 1, 0, 0])
        for d1, d2, d3, action in commands:
            data += struct.pack('<IIIIH', d1, d2, d3, action, 3)
        data += struct.pack('<ii', int(cx * 4096), int(cy * 4096)) + bytes([0xFF, 0x0F, 0, 0])
    return bytes(data)


@unittest.skipUnless(sys.platform == 'win32' and shutil.which('powershell'), 'needs Windows PowerShell')
class DecodeTests(unittest.TestCase):
    def decode(self, data):
        work = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, work, True)
        sav = work / 'test.sav'
        sav.write_bytes(data)
        out = work / 'out'
        result = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(SCRIPT),
                                 '-Decode', str(sav), '-Out', str(out)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        with (out / 'commands.tsv').open(newline='') as f:
            commands = list(csv.DictReader(f, delimiter='\t'))
        with (out / 'camera.tsv').open(newline='') as f:
            camera = list(csv.DictReader(f, delimiter='\t'))
        return commands, camera, result.stdout

    def test_each_action_layout(self):
        commands, camera, out = self.decode(packet([
            (2, 0x1234ABCD, [], (16.5, 25.5)),
            (5, 2, [(tile(33, 3), tile(34, 5), 0, 40),            # tag a rectangle
                    (8, tile(32, 9), tile(33, 9), 47),            # build a Wooden Bridge (8)
                    (tile(19, 25), 3, 0, 52),                     # Sentry trap (3)
                    (tile(46, 21), 6, 0, 50),                     # Barricade (6)
                    (10, 409, 0, 55),                             # Heal (10) on thing 409
                    (4, 35598, 59875, 54),                        # Create Imp (4) at a world point
                    (424, 0, 0, 57),                              # pick up thing 424
                    (156533, 63262, 16777216, 61),                # drop at a world point
                    (1, 8, 0, 114)], (17.25, 26.0)),              # panel click: rooms (1), id 8
        ]))
        by = {c['name']: c for c in commands}
        self.assertEqual(len(commands), 9)
        self.assertEqual((by['tag']['x'], by['tag']['y'], by['tag']['x2'], by['tag']['y2']), ('33', '3', '34', '5'))
        self.assertEqual((by['build-room']['type'], by['build-room']['x'], by['build-room']['y'], by['build-room']['x2']), ('8', '32', '9', '33'))
        self.assertEqual((by['place-trap']['x'], by['place-trap']['y'], by['place-trap']['type']), ('19', '25', '3'))
        self.assertEqual((by['place-door']['x'], by['place-door']['y'], by['place-door']['type']), ('46', '21', '6'))
        self.assertEqual((by['cast-on-creature']['type'], by['cast-on-creature']['thing']), ('10', '409'))
        self.assertEqual((by['cast-at-place']['type'], by['cast-at-place']['wx'], by['cast-at-place']['wy']), ('4', '8.691', '14.618'))
        self.assertEqual(by['pick-up-creature']['thing'], '424')
        self.assertEqual((by['drop']['wx'], by['drop']['wy']), ('38.216', '15.445'))
        self.assertEqual(by['panel-click']['type'], '1/8')
        self.assertEqual(by['tag']['turn'], '5')
        self.assertEqual(camera[0], {'ordinal': '1', 'turn': '2', 'commands': '0', 'camera_x': '16.500', 'camera_y': '25.500'})
        self.assertEqual(by['tag']['ordinal'], '2')
        self.assertEqual(camera[1]['camera_x'], '17.250')
        self.assertIn('Level6a: 2 turns, 9 commands', out)

    def test_the_rest_of_the_layouts_and_odd_values(self):
        commands, camera, _ = self.decode(packet([
            (7, 1, [(tile(1, 2), tile(3, 4), 0, 41),             # untag
                    (tile(5, 6), tile(7, 8), 0, 49),             # sell
                    (77, 0, 0, 59),                              # pick up gold
                    (392, 1120, 0, 62),                          # slap (d2 kept raw)
                    (1920, 1, 0, 93),                            # door lock toggle on door slot 1920
                    (0, 0, 0, 61),                               # a drop at world (0, 0)
                    (5, 6, 7, 0x90000000)], (0, 0)),             # a corrupt action id: named, not fatal
        ]))
        by = {c['name']: c for c in commands}
        self.assertEqual((by['untag']['x'], by['untag']['y2']), ('1', '4'))
        self.assertEqual((by['sell']['x'], by['sell']['x2']), ('5', '7'))
        self.assertEqual(by['pick-up-gold']['thing'], '77')
        self.assertEqual((by['slap']['thing'], by['slap']['d2']), ('392', '1120'))
        self.assertEqual(by['door-lock-toggle']['thing'], '1920')
        self.assertEqual((by['drop']['wx'], by['drop']['wy']), ('0.000', '0.000'))
        self.assertIn('action-2415919104', by)

    def test_a_stamp_repeated_while_the_window_was_unfocused_keeps_distinct_ordinals(self):
        _, camera, _ = self.decode(packet([(964, 0, [], (1, 1))] * 3 + [(965, 0, [], (1, 1))]))
        self.assertEqual([(c['ordinal'], c['turn']) for c in camera], [('1', '964'), ('2', '964'), ('3', '964'), ('4', '965')])

    def test_a_truncated_last_record_is_dropped_not_misread(self):
        full = packet([(2, 1, [], (1, 1)), (3, 1, [(424, 0, 0, 57)], (1, 1))])
        commands, camera, _ = self.decode(full[:-5])
        self.assertEqual(len(camera), 1)
        self.assertEqual(commands, [])


def powershell(command):
    return subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', command],
                          capture_output=True, text=True)


# Loads only the named functions from the script's syntax tree; running the script itself would start a play session.
LOAD = ("$ast = [Management.Automation.Language.Parser]::ParseFile('{script}', [ref]$null, [ref]$null); "
        "foreach ($f in $ast.FindAll({{ $args[0] -is [Management.Automation.Language.FunctionDefinitionAst] }}, $true)) "
        "{{ if ($f.Name -in 'Find-Desync', 'Read-GameLog') {{ Invoke-Expression $f.Extent.Text }} }}; ")


@unittest.skipUnless(sys.platform == 'win32' and shutil.which('powershell'), 'needs Windows PowerShell')
class ReplayCheckTests(unittest.TestCase):
    # Modelled on the 2026-11-16 Level 6a -Replay run's MESSAGE.replay.LOG (its lines 1-2 and 7-9, real); the Seed line is
    # invented, to show the first hit wins.
    LOG =('LOG CREATED  @ 18:16:48  16-11-2026\r\n\r\nEngine Heap Size: 64Mb\r\n'
           'Gone Out Of Sync (Checksum) GT 631 [482DC5CE:482D9AA2]\r\nOut Of Sync(on GT 631) GT 632\r\n'
           'Gone Out Of Sync (Seed) GT 900 [1:2]\r\n')

    def check(self, log):
        work = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, work, True)
        path = work / 'MESSAGE.LOG'
        path.write_bytes(log.encode('ascii'))
        result = powershell(LOAD.format(script=SCRIPT) +
                            f"$d = Find-Desync (Read-GameLog '{path}'); if ($d) {{ \"$($d.Turn)|$($d.Kind)|$($d.Line)\" }} else {{ 'none' }}")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def test_the_first_gone_out_of_sync_line_gives_the_turn_and_kind(self):
        self.assertEqual(self.check(self.LOG), '631|Checksum|Gone Out Of Sync (Checksum) GT 631 [482DC5CE:482D9AA2]')

    def test_a_line_still_being_written_is_not_read(self):
        # A read that ends mid-write must not turn 'GT 631' into 'GT 6'.
        self.assertEqual(self.check('LOG CREATED\r\nGone Out Of Sync (Checksum) GT 6'), 'none')

    def test_a_log_in_sync_and_a_missing_log_give_nothing(self):
        self.assertEqual(self.check('LOG CREATED\r\nEngine Time:\r\n'), 'none')
        result = powershell(LOAD.format(script=SCRIPT) + "if (Find-Desync (Read-GameLog 'Z:\\no\\such.LOG')) { 'hit' } else { 'none' }")
        self.assertEqual(result.stdout.strip(), 'none', result.stderr)

    def test_verify_without_a_kept_study_fails_before_asking_for_elevation(self):
        result = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(SCRIPT), '-Verify'],
                                capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('-Verify needs -Study', result.stdout + result.stderr)


if __name__ == '__main__':
    unittest.main()

# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

import struct
import unittest

from pck_contents import read_entries, refusal


def pack(paths, version=3):
    """A PCK directory as Godot writes it, with empty file data."""
    directory = struct.pack("<I", len(paths))
    for path in paths:
        name = path.encode("utf-8")
        name += b"\0" * (-len(name) % 4)
        directory += struct.pack("<I", len(name)) + name + struct.pack("<QQ", 0, 7) + bytes(16) + struct.pack("<I", 0)
    header = b"GDPC" + struct.pack("<5I", version, 4, 8, 0, 2) + struct.pack("<Q", 0)
    if version >= 3:
        header += struct.pack("<Q", len(header) + 8 + 64) + bytes(64)
        return header + directory
    return header + bytes(64) + directory


class PckContentsTests(unittest.TestCase):
    def test_reads_formats_two_to_four(self):
        for version in (2, 3, 4):
            entries = read_entries(pack(["InGameHud.tscn", "res://Shaders/World.gdshaderinc"], version))
            self.assertEqual([("InGameHud.tscn", 7), ("Shaders/World.gdshaderinc", 7)], entries, version)

    def test_ours_pass(self):
        for path in (".godot/exported/133200997/export-1-InGameHud.scn", "Shaders/WorldOpaque.gdshader",
                     "Launcher/AlegreyaSans-Regular.ttf", "Launcher/UnifrakturMaguntia-OFL.txt",
                     ".godot/imported/AlegreyaSans-Medium.ttf-0123456789abcdef.fontdata", "project.binary"):
            self.assertIsNone(refusal(path), path)

    def test_dk2_material_is_refused(self):
        for path in ("Fonts/DungeonKeeperII.ttf", ".godot/imported/DungeonKeeperII.ttf-0123.fontdata",
                     "Data/Meshes.wad", "Movies/Intro.tgq", "art/Frontend.png", ".godot/imported/a.png-1.ctex",
                     "Sound/hit.wav", "Other.otf"):
            self.assertIsNotNone(refusal(path), path)

    def test_not_a_pck(self):
        with self.assertRaises(ValueError):
            read_entries(b"MZ" + bytes(100))


if __name__ == "__main__":
    unittest.main()

# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Lists a Godot PCK's files and refuses one that could hold original Dungeon Keeper 2 material.

The game reads every DK2 file from the player's install at run time, so the exported pack holds only our
scenes, shaders, C# script stubs and the launcher's OFL fonts. Refused: DK2's own formats, any image,
sound or movie (the project has none of its own), a font other than the launcher's, and any path naming
DungeonKeeper. Exit 0 clean, 1 refused, 2 unreadable.
"""
import argparse
import struct
import sys
from pathlib import Path, PurePosixPath

MAGIC = b"GDPC"
DIR_ENCRYPTED = 1
REL_FILEBASE = 2
REFUSED_EXTENSIONS = {
    ".kwd", ".kld", ".wad", ".kmf", ".sdt", ".tgq", ".bf4",
    ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".dds", ".webp", ".svg", ".ctex", ".ctexarray", ".ctex3d",
    ".wav", ".mp3", ".ogg", ".oggvorbisstr", ".sample", ".avi", ".mp4", ".ogv",
}
FONT_EXTENSIONS = {".ttf", ".otf", ".woff", ".woff2", ".fnt", ".fontdata"}
ALLOWED_FONTS = ("AlegreyaSans-Medium.ttf", "AlegreyaSans-Regular.ttf", "UnifrakturMaguntia-Book.ttf")


def read_entries(data):
    """The (path, size) of each file in a standalone PCK (format 2, or 3 and 4, which keep the directory at an offset)."""
    if data[:4] != MAGIC:
        raise ValueError("not a Godot PCK (no GDPC magic at the start)")
    version, _major, _minor, _patch, flags = struct.unpack_from("<5I", data, 4)
    if version not in (2, 3, 4):
        raise ValueError(f"PCK format {version} is not one this reader knows")
    if flags & DIR_ENCRYPTED:
        raise ValueError("the PCK directory is encrypted")
    at = 24 + 8  # magic, version, engine version, flags; then the file base
    if version >= 3:
        (at,) = struct.unpack_from("<Q", data, at)
    else:
        at += 16 * 4  # reserved
    (count,) = struct.unpack_from("<I", data, at)
    at += 4
    entries = []
    for _ in range(count):
        (length,) = struct.unpack_from("<I", data, at)
        at += 4
        path = data[at:at + length].rstrip(b"\0").decode("utf-8")
        at += length
        _offset, size = struct.unpack_from("<QQ", data, at)
        at += 16 + 16 + 4  # offset and size, MD5, flags
        entries.append((path.removeprefix("res://"), size))
    return entries


def refusal(path):
    """Why a packed path could be DK2 material, or None."""
    pure = PurePosixPath(path)
    lower = path.lower()
    if "dungeonkeeper" in lower or lower.startswith("fonts/") or "/fonts/" in lower:
        return "names DK2's font or a Fonts folder"
    suffixes = [s.lower() for s in pure.suffixes]
    if any(s in REFUSED_EXTENSIONS for s in suffixes):
        return "a DK2 format, image, sound or movie"
    if any(s in FONT_EXTENSIONS for s in suffixes) and not any(pure.name.startswith(f) for f in ALLOWED_FONTS):
        return "a font other than the launcher's"
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pck", type=Path)
    parser.add_argument("--list", action="store_true", help="print every packed path")
    args = parser.parse_args()
    try:
        entries = read_entries(args.pck.read_bytes())
    except (OSError, ValueError, struct.error, UnicodeDecodeError) as error:
        print(f"{args.pck}: {error}", file=sys.stderr)
        return 2
    refused = [(path, reason) for path, _ in entries if (reason := refusal(path))]
    if args.list:
        for path, size in entries:
            print(f"{size:>12} {path}")
    for path, reason in refused:
        print(f"REFUSED {path}: {reason}", file=sys.stderr)
    print(f"{len(entries)} files, {sum(size for _, size in entries)} bytes, {len(refused)} refused")
    return 1 if refused else 0


if __name__ == "__main__":
    sys.exit(main())

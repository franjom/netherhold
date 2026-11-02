# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""capture_memory: one tick of a full-state capture as a memory image for dk2oracle.py.

Usage: python -I capture_memory.py <capture.dk2cap> <tick> <out.mem> [<address>=<hex bytes>...]

New record_native.py captures automatically supply their verified full-memory timeline and
frozen decoder; no environment or dump flag is needed. Oracle region boundaries are preserved.
The .z suffix selects compression. Legacy captures without a complete-memory declaration
produce an explicitly PARTIAL structured image. Extra address=bytes arguments remain explicit
analysis overlays. All outputs are exclusively created; existing evidence is never replaced.
"""
import os
import struct
import sys
import json
import hashlib
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from native_memory import capture_header, for_capture


def decoder_path():
    explicit = os.environ.get('DK2_CAPTURE_DECODER')
    args, positional = iter(sys.argv[1:]), []
    for arg in args:
        if arg == '--dump':
            next(args, None)
        elif arg != '--state-only':
            positional.append(arg)
    capture = Path(positional[0]).resolve() if positional else None
    captured_sources = capture.parent / 'sources' if capture else None
    declared = False
    if capture is not None and capture.is_file():
        with capture.open('rb') as stream:
            is_capture = stream.read(8) == b'DK2CAP\x01\n'
        declared = is_capture and capture_header(capture).get('complete_memory') is not None
    if declared:
        source = captured_sources / 'dk2capture.py'
        manifest = json.loads((capture.parent / 'sha256-final.json').read_bytes())
        if not source.is_file() or manifest.get('sources/dk2capture.py') != hashlib.sha256(source.read_bytes()).hexdigest():
            raise ValueError('Declared complete capture decoder is missing or has a different identity')
        candidates = [Path(explicit)] if explicit else [captured_sources]
        if explicit:
            override = Path(explicit) / 'dk2capture.py'
            if not override.is_file() or hashlib.sha256(override.read_bytes()).hexdigest() != manifest['sources/dk2capture.py']:
                raise ValueError('Decoder override differs from the complete capture source')
    else:
        candidates = [Path(explicit)] if explicit else ([captured_sources] if captured_sources else [])
        if not explicit:
            candidates += [Path('F:/DK2Evidence/archive/main-clone--unknown-date--001/payload/reverse-engineering')]
    for candidate in candidates:
        if (candidate / 'dk2capture.py').is_file():
            return candidate
    raise RuntimeError('No retained capture decoder found; expected capture/sources or the F:/DK2Evidence archive')


if sys.argv[1:] in (['--help'], ['-h']):
    dk2capture = None
else:
    sys.path.insert(0, str(decoder_path()))
    import dk2capture  # noqa: E402


def image(snapshot, extra=()):
    """(address, bytes) for every stored region and record, then the extras."""
    for _rid, (address, data) in sorted(snapshot.regions.items()):
        if data:
            yield address, data
    for record in snapshot.records():
        if record.raw:
            yield record.ptr, record.raw
    yield from extra


def write(path, regions):
    with open(path, "xb") as out:
        for address, data in regions:
            out.write(struct.pack("<II", address, len(data)))
            out.write(data)


def main():
    if sys.argv[1:] in (['--help'], ['-h']):
        print(__doc__)
        return
    capture, tick, path = sys.argv[1], int(sys.argv[2]), sys.argv[3]
    extra = [(int(a, 16), bytes.fromhex(b)) for a, b in (x.split("=") for x in sys.argv[4:])]
    cap = dk2capture.Capture(capture)
    snapshot = cap.at(tick)
    if snapshot is None or snapshot.tick != tick or snapshot.torn:
        sys.exit(f"{capture} has no exact untorn frame at tick {tick}")
    complete = for_capture(capture, tick, cap.header)
    if complete is not None:
        complete.export(path, compressed=path.endswith('.z'), extra=extra)
    else:
        print('PARTIAL legacy image: this capture has no complete memory timeline.', file=sys.stderr)
        write(path, image(snapshot, extra))
    print(f"tick {snapshot.tick}: {len(snapshot.regions)} regions, {sum(1 for _ in snapshot.records())} records -> {path}")


if __name__ == "__main__":
    main()

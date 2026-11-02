# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Add-only complete 32-bit memory timelines; oracle images are offline views."""
import bisect
import hashlib
import json
from pathlib import Path
import struct
import zlib

DOMAIN = 1 << 32
BLOCK_BYTES = 65536
SCHEMA = 2


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode('utf-8')


def write_json(path, value):
    with path.open('xb') as stream:
        stream.write(encoded(value))


def validate_map(regions):
    cursor = 0
    for region in regions:
        if region['base'] != cursor or region['size'] <= 0:
            raise ValueError('Memory coverage has a gap, overlap or empty region')
        cursor += region['size']
        if cursor > DOMAIN or not isinstance(region['readable'], bool):
            raise ValueError('Invalid memory coverage metadata')
        if not {'state', 'protect', 'type'} <= region.keys():
            raise ValueError('Memory coverage lacks page protection/type metadata')
        expected = (region['state'] == 0x1000 and not region['protect'] & 0x100
                    and region['protect'] & 0xFF in {2, 4, 8, 0x20, 0x40, 0x80})
        if region['readable'] != expected:
            raise ValueError('Memory coverage disagrees with page protection')
    if cursor != DOMAIN:
        raise ValueError('Memory coverage does not span the complete 32-bit domain')


def ranges(regions):
    for region in regions:
        if region['readable']:
            for offset in range(0, region['size'], BLOCK_BYTES):
                yield region['base'] + offset, min(BLOCK_BYTES, region['size'] - offset)


def key(address, length):
    return f'{address:08X}:{length:X}'


def state_digest(regions, blocks):
    return digest(encoded(dict(regions=regions, blocks=blocks)))


def block_path(folder, sha):
    if len(sha) != 64 or any(c not in '0123456789abcdef' for c in sha):
        raise ValueError('Invalid memory block digest')
    return folder / 'blocks' / sha[:2] / (sha + '.z')


def decode_block(compressed, sha, length):
    try:
        decoder = zlib.decompressobj()
        raw = decoder.decompress(compressed, length + 1)
        if (len(raw) != length or not decoder.eof or decoder.unused_data
                or decoder.unconsumed_tail or digest(raw) != sha):
            raise ValueError('Memory block length, compression or hash mismatch')
    except zlib.error as error:
        raise ValueError('Corrupt memory block') from error
    return raw


def pack_path(folder, location):
    path = Path(location['file'])
    if path.is_absolute() or len(path.parts) != 2 or path.parts[0] != 'blocks' or '..' in path.parts:
        raise ValueError('Invalid memory block pack path')
    if location['offset'] < 0 or not 0 < location['compressedLength'] <= BLOCK_BYTES * 2:
        raise ValueError('Invalid memory block pack span')
    return folder / path


def read_block(folder, sha, length, location=None):
    if location is None:
        compressed = block_path(folder, sha).read_bytes()
    else:
        if location['rawLength'] != length:
            raise ValueError('Memory block length metadata mismatch')
        with pack_path(folder, location).open('rb') as stream:
            stream.seek(location['offset'])
            compressed = stream.read(location['compressedLength'])
        if len(compressed) != location['compressedLength']:
            raise ValueError('Truncated memory block pack')
    return decode_block(compressed, sha, length)


class MemoryWriter:
    def __init__(self, folder):
        self.folder = Path(folder)
        self.folder.mkdir(exist_ok=False)
        (self.folder / 'blocks').mkdir()
        (self.folder / 'frames').mkdir()
        self.previous, self.previous_tick, self.count = {}, None, 0
        self.locations = {}

    def capture(self, tick, regions, read):
        if tick < 1 or self.previous_tick is not None and tick <= self.previous_tick:
            raise ValueError('Memory turns must be positive and strictly increasing')
        regions = list(regions)
        validate_map(regions)
        blocks, total, added, locations, stream = {}, 0, 0, {}, None
        pack = self.folder / 'blocks' / f'{tick:010}.z'
        try:
            for address, length in ranges(regions):
                raw = read(address, length)
                if len(raw) != length:
                    raise OSError(f'short memory read at {address:08X}: {len(raw)} of {length}')
                sha = digest(raw)
                blocks[key(address, length)] = sha
                total += length
                if sha not in self.locations and sha not in locations:
                    if stream is None:
                        stream = pack.open('xb')
                    compressed = zlib.compress(raw, 1)
                    locations[sha] = dict(file=pack.relative_to(self.folder).as_posix(), offset=stream.tell(),
                                          compressedLength=len(compressed), rawLength=length)
                    stream.write(compressed)
                    added += len(compressed)
        finally:
            if stream is not None:
                stream.close()
        if locations:
            with pack.open('rb') as stream:
                for sha, location in locations.items():
                    compressed = stream.read(location['compressedLength'])
                    decode_block(compressed, sha, location['rawLength'])
            self.locations.update(locations)
        full = self.count % 128 == 0
        index = dict(schema=SCHEMA, tick=tick, domain=[0, DOMAIN], regions=regions,
                     parent=None if full else self.previous_tick, keyframe=full,
                     blocks=blocks if full else {k: v for k, v in blocks.items() if self.previous.get(k) != v},
                     locations=self.locations if full else locations,
                     removed=[] if full else sorted(set(self.previous) - set(blocks)),
                     stateSHA256=state_digest(regions, blocks))
        path = self.folder / 'frames' / f'{tick:010}.json'
        write_json(path, index)
        checked = MemoryReader(self.folder, tick, require_completion=False)
        if checked.blocks != blocks:
            raise ValueError('Saved memory index does not round-trip')
        write_json(path.with_suffix('.complete.json'), dict(schema=SCHEMA, tick=tick, indexSHA256=digest(path.read_bytes())))
        self.previous, self.previous_tick = blocks, tick
        self.count += 1
        return dict(tick=tick, bytes=total, blocks=len(blocks), newCompressedBytes=added,
                    index=str(path.relative_to(self.folder)), indexSHA256=digest(path.read_bytes()))


class MemoryReader:
    def __init__(self, folder, tick, require_completion=True):
        self.folder, self.tick = Path(folder), tick
        chain, seen, current = [], set(), tick
        while current is not None:
            if current in seen or current > tick:
                raise ValueError('Invalid memory index parent chain')
            seen.add(current)
            path = self.folder / 'frames' / f'{current:010}.json'
            raw = path.read_bytes()
            if require_completion or current != tick:
                marker = json.loads(path.with_suffix('.complete.json').read_bytes())
                if marker.get('schema') not in (1, SCHEMA) or marker.get('tick') != current or marker.get('indexSHA256') != digest(raw):
                    raise ValueError('Memory index completion/hash mismatch')
            value = json.loads(raw)
            if value.get('schema') not in (1, SCHEMA) or value.get('tick') != current or value.get('domain') != [0, DOMAIN]:
                raise ValueError('Invalid memory index identity/schema/domain')
            validate_map(value['regions'])
            parent = value['parent']
            if value['keyframe'] != (parent is None) or parent is not None and (parent < 1 or parent >= current):
                raise ValueError('Invalid memory index parent')
            chain.append(value)
            current = parent
        blocks, locations = {}, {}
        for value in reversed(chain):
            locations.update(value.get('locations', {}))
            for removed in value['removed']:
                if removed not in blocks:
                    raise ValueError('Invalid removed memory block')
                del blocks[removed]
            blocks.update(value['blocks'])
            expected = {key(at, length) for at, length in ranges(value['regions'])}
            if set(blocks) != expected or state_digest(value['regions'], blocks) != value['stateSHA256']:
                raise ValueError('Memory index coverage/hash mismatch')
            if value['schema'] >= 2 and any(sha not in locations for sha in blocks.values()):
                raise ValueError('Memory index lacks a block location')
        self.blocks, self.regions = blocks, chain[0]['regions']
        self.index_files = [p for value in chain for p in
                            (self.folder / 'frames' / f"{value['tick']:010}.json",
                             self.folder / 'frames' / f"{value['tick']:010}.complete.json")]
        self.locations = locations
        self.spans = sorted((int(k.split(':')[0], 16), int(k.split(':')[1], 16), sha) for k, sha in blocks.items())
        self.starts = [row[0] for row in self.spans]

    def validate(self, verified=None):
        verified = set() if verified is None else verified
        ordered = sorted(self.spans, key=lambda row: (self.locations.get(row[2], {}).get('file', row[2]),
                                                      self.locations.get(row[2], {}).get('offset', 0)))
        source, name = None, None
        try:
            for address, length, sha in ordered:
                if (sha, length) in verified:
                    continue
                location = self.locations.get(sha)
                if location is None:
                    read_block(self.folder, sha, length)
                else:
                    if location['rawLength'] != length:
                        raise ValueError('Memory block length metadata mismatch')
                    path = pack_path(self.folder, location)
                    if path != name:
                        if source is not None:
                            source.close()
                        source, name = path.open('rb'), path
                    source.seek(location['offset'])
                    compressed = source.read(location['compressedLength'])
                    decode_block(compressed, sha, length)
                verified.add((sha, length))
        finally:
            if source is not None:
                source.close()
        return dict(tick=self.tick, blocks=len(self.spans), bytes=sum(row[1] for row in self.spans))

    def read(self, address, size):
        if address < 0 or size < 0 or address + size > DOMAIN:
            raise ValueError('Memory read is outside the captured domain')
        result = bytearray()
        while size:
            index = bisect.bisect_right(self.starts, address) - 1
            if index < 0:
                raise ValueError(f'Address {address:08X} was not captured')
            start, length, sha = self.spans[index]
            offset = address - start
            if not 0 <= offset < length:
                raise ValueError(f'Address {address:08X} was not captured')
            count = min(size, length - offset)
            result.extend(read_block(self.folder, sha, length, self.locations.get(sha))[offset:offset + count])
            address, size = address + count, size - count
        return bytes(result)

    def u(self, address, fmt='<I'):
        return struct.unpack(fmt, self.read(address, struct.calcsize(fmt)))[0]

    def export(self, path, compressed=True, extra=()):
        compressor = zlib.compressobj(1) if compressed else None
        encode = compressor.compress if compressed else lambda raw: raw
        total = count = 0
        with Path(path).open('xb') as stream:
            for region in self.regions:
                if not region['readable']:
                    continue
                stream.write(encode(struct.pack('<II', region['base'], region['size'])))
                for address, length in ranges([region]):
                    stream.write(encode(self.read(address, length)))
                count += 1
                total += region['size']
            for address, data in extra:
                stream.write(encode(struct.pack('<II', address, len(data)) + data))
            if compressor is not None:
                stream.write(compressor.flush())
        return dict(regions=count, bytes=total, short=0)


def capture_header(capture):
    """Read only our stable capture envelope, without selecting a decoder."""
    with Path(capture).open('rb') as stream:
        if stream.read(8) != b'DK2CAP\x01\n':
            raise ValueError('Not a DK2 state capture')
        chunk = stream.read(5)
        if len(chunk) != 5:
            raise ValueError('Missing capture header')
        length, kind = struct.unpack('<IB', chunk)
        if kind != ord('H') or not 0 < length <= 16 * 1024 * 1024:
            raise ValueError('Invalid capture header envelope')
        raw = stream.read(length)
        if len(raw) != length:
            raise ValueError('Truncated capture header')
        return json.loads(raw)


def for_capture(capture, tick, header=None):
    """Fail closed for declared complete captures without verified bound data."""
    capture = Path(capture).resolve()
    header = capture_header(capture) if header is None else header
    declared = header.get('complete_memory')
    if declared is None:
        return None
    if declared != 'memory':
        raise ValueError('Unknown complete capture memory profile')
    root, folder = capture.parent, capture.parent / 'memory'
    if not capture.is_file() or not folder.is_dir() or (root / 'validation-failed.json').exists():
        raise ValueError('Declared complete capture is missing or failed validation')
    verified = json.loads((root / 'verified.json').read_bytes())
    status = json.loads((root / 'run-status.json').read_bytes())
    manifest = json.loads((root / 'sha256-final.json').read_bytes())
    def check(path):
        name = path.relative_to(root).as_posix()
        if name not in manifest or digest(path.read_bytes()) != manifest[name]:
            raise ValueError(f'Complete capture manifest identity mismatch: {name}')
    for path in (capture, root / 'verified.json', root / 'run-status.json'):
        check(path)
    first, last = status['requested']
    expected = sorted({1, 2, *range(first, last + 1)})
    if (status.get('error') is not None or not status.get('ownedClosed') or not status.get('packetUnchanged')
            or status['completed'] != expected or verified['memoryTurns'] != expected
            or verified['decodedTurns'] != list(range(first, last + 1)) or tick not in verified['decodedTurns']):
        raise ValueError('Complete capture receipt does not cover the requested turn')
    if verified.get('captureFile', capture.name) != capture.name or verified.get('captureSHA256', manifest[capture.name]) != manifest[capture.name]:
        raise ValueError('Complete capture receipt names a different capture')
    memory = MemoryReader(folder, tick)
    for path in memory.index_files:
        check(path)
    memory.validate()
    return memory

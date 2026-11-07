# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Bounded native work-action clock probe; terrain/room side effects are stubs.

Runs the original initializer, starter, handlers, runner and animation advance.
Eligibility and work appliers use a synthetic application counter. This is not
a captured game trajectory. Every result names its inputs and replaced callees.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys


CREATURE = 0x300000
WORLD, VTABLE, CLOCK = 0x301000, 0x302000, 0x303000
DEFINITION, TILE, ROOM = 0x304000, 0x305000, 0x306000
COUNT, LIMIT, DELTA = 0x307000, 0x307004, 0x307008
STATE, LOOKUP, SOUND = 0x308000, 0x308100, 0x308200
ART = DEFINITION + 13 * 27


def u32(value):
    return struct.pack('<I', value & 0xffffffff)


def stores(address, data):
    data += bytes((-len(data)) % 4)
    return [(address + i, struct.unpack_from('<I', data, i)[0])
            for i in range(0, len(data), 4)]


def request(address, writes=(), args=(), reads=()):
    return (f'{address:x} ' + ' '.join(f'{a:x}' for a in args) + ' | ' +
            ' '.join(f'{a:x}={v & 0xffffffff:x}' if isinstance(a, int)
                     else f'{a}={v:x}' for a, v in writes) + ' @ ' +
            ' '.join(f'{a:x}' for a in reads))


def eligibility(pop):
    return (b'\xa1' + u32(COUNT) + b'\x3b\x05' + u32(LIMIT) +
            b'\x0f\x9c\xc0\x0f\xb6\xc0' +
            (b'\xc2' + struct.pack('<H', pop) if pop else b'\xc3'))


def applier(delta_argument, pop):
    return (b'\xff\x05' + u32(COUNT) + b'\x8b\x44\x24' +
            bytes([delta_argument]) + b'\x8b\x00\xa3' + u32(DELTA) +
            b'\x31\xc0\xc2' + struct.pack('<H', pop))


def state_stub(action):
    code = b'\xa1' + u32(COUNT) + b'\x3b\x05' + u32(LIMIT)
    first = len(code)
    code += b'\x7d\x00\xb9' + u32(CREATURE)
    code += b'\x83\xb9' + u32(0x3a9) + b'\x00'
    second = len(code)
    code += b'\x75\x00' + b'\x6a\x00' * 3 + b'\x6a' + bytes([action])
    code += b'\xe8' + u32(0x496700 - (STATE + len(code) + 5))
    end = len(code)
    code += b'\xc3'
    code = bytearray(code)
    code[first + 1] = end - first - 2
    code[second + 1] = end - second - 2
    return bytes(code)


def read_art(install):
    path = install / 'Data/editor/Creatures.kwd'
    data = path.read_bytes()
    header = struct.unpack_from('<I', data, 16)[0]
    count = struct.unpack_from('<I', data, 20)[0]
    start = 28 + header
    length = struct.unpack_from('<I', data, start - 4)[0]
    stride = length // count
    assert stride == 5449 and length % count == 0
    records = [data[start + i * stride:start + (i + 1) * stride] for i in range(count)]
    imp = next(r for r in records if r[:32].split(b'\0')[0] == b'Imp')
    art = imp[32 + 13 * 84:32 + 14 * 84]
    frames = struct.unpack_from('<H', art, 0x44)[0]
    fps = struct.unpack_from('<H', art, 0x48)[0]
    duration = frames * 4096 // fps
    return {'file': str(path), 'sha256': hashlib.sha256(data).hexdigest(),
            'name': art[:64].split(b'\0')[0].decode('ascii'), 'frames': frames,
            'fps': fps, 'flags': struct.unpack_from('<I', art, 64)[0],
            'type': art[80], 'start_frames': art[81], 'end_frames': art[82],
            'rate': min(4095, 4096 * 4096 // (duration * 4))}


def probe(install, dll, action, phase, flags, applications, art):
    writes = [(CREATURE + i, 0) for i in range(0, 0x800, 4)]
    writes += [(0x6e5050, WORLD), (WORLD, VTABLE), (WORLD + 4, CLOCK),
               (VTABLE + 0x1d4, LOOKUP), (0x75b468, WORLD), (VTABLE + 0x20, SOUND),
               (CREATURE + 0x370, DEFINITION), (CREATURE + 0x6c, ART),
               (ART + 4, art['fps'] | art['frames'] << 16),
               (ART + 0xa, art['type'] | art['flags'] << 8), (ART + 0xf, 123),
               (CREATURE + 0xd8, phase), (CREATURE + 0xdc, art['rate']),
               (CREATURE + 0xf1, flags | 13 << 8), (CREATURE + 0x3af, 3276),
               (CREATURE + 0x28, 3), (COUNT, 0), (LIMIT, applications),
               (0x6f2554, 206), (0x6f2558, -35), (0x6f257c, -5000),
               (0x6f2574, -1000), (0x6f2548, 0)]
    stubs = {LOOKUP: b'\xb8' + u32(TILE) + b'\xc2\x04\x00',
             SOUND: b'\x31\xc0\xc2\x10\x00', STATE: state_stub(action),
             0x453fc0: eligibility(0), 0x454000: eligibility(4),
             0x454040: eligibility(4), 0x457880: b'\xc2\x04\x00',
             0x4508d0: applier(12, 16), 0x4e5300: applier(4, 8),
             0x4559b0: b'\xb8' + u32(ROOM) + b'\xc3'}
    for address, code in stubs.items():
        writes += stores(address, code)
    lines = [request(0x4950c0, writes)]
    reads = [COUNT, CREATURE + 0x3a9, CREATURE + 0x3ad,
             CREATURE + 0xd8, CREATURE + 0xdc, CREATURE + 0xf1, DELTA]
    tags = []
    for tick in range(100):
        if tick:
            lines.append(request(0x496aa0, [('ecx', CREATURE)], reads=reads))
            tags.append((len(lines) - 1, tick, 'poll'))
        lines.append(request(STATE, [(CLOCK + 4, tick)], reads=reads))
        tags.append((len(lines) - 1, tick, 'state'))
        lines.append(request(0x4b3c10, [('ecx', CREATURE + 0x44)], reads=reads))
        tags.append((len(lines) - 1, tick, 'advance'))
    env = dict(os.environ, DK2_UNICORN_DLL=str(dll))
    result = subprocess.run([sys.executable, '-I', str(Path(__file__).with_name('dk2oracle.py')),
                             str(install / 'DKII.EXE')], input='\n'.join(lines),
                            capture_output=True, text=True, env=env, timeout=60)
    if result.returncode:
        raise RuntimeError(result.stderr)
    outputs = result.stdout.splitlines()
    assert len(outputs) == len(lines)
    for index, output in enumerate(outputs):
        if output.startswith('fault'):
            raise RuntimeError(f'{index} {lines[index]}: {output}')
    trace = []
    for index, tick, stage in tags:
        values = [int(x, 16) for x in outputs[index].split()][2:]
        trace.append(dict(tick=tick, stage=stage, count=values[0], action=values[1],
                          completion=values[2] & 0xffff, phase=values[3], rate=values[4],
                          flags=values[5] & 255, delta=struct.unpack('<i', u32(values[6]))[0]))
    hits, previous = [], 0
    for row in trace:
        if row['stage'] == 'poll' and row['count'] > previous:
            assert row['delta'] == {4: 164, 5: -28, 10: -4000}[action], row
            hits.append(row['tick'])
            previous = row['count']
    ended = next((r['tick'] for r in trace if r['stage'] == 'poll'
                  and r['count'] == applications and r['action'] == 0), None)
    assert len(hits) == applications and ended is not None
    return dict(action=action, initial_phase=phase, initial_flags=flags,
                applications=applications, hits=hits, ended=ended,
                replaced_callees=[f'{a:08X}' for a in stubs if a < 0x300000 or a >= 0x400000],
                trace=trace, requests=lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--install', type=Path, default=Path('E:/GogGames/Dungeon Keeper 2'))
    parser.add_argument('--dll', type=Path, default=os.environ.get('DK2_UNICORN_DLL'))
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    if args.out.exists():
        parser.error('output already exists; choose a new path')
    if args.dll is None or not args.dll.is_file():
        parser.error('--dll or DK2_UNICORN_DLL must name the installed Unicorn DLL')
    executable = (args.install / 'DKII.EXE').read_bytes()
    if hashlib.md5(executable).hexdigest() != '2f3bbc21eae8c3aa468136baa303036d':
        parser.error('executable is not the pinned DKII.EXE 1.70')
    art = read_art(args.install)
    assert art['type'] == 6 and art['start_frames'] == art['end_frames'] == 0
    cases = [probe(args.install, args.dll, action, phase, flags, n, art)
             for action, n in [(4, 5), (5, 24), (10, 23)]
             for phase, flags in [(0, 1), (2000, 1), (0, 5)]]
    output = dict(scope='synthetic native function controls; not running-game observations',
                  executable_sha256=hashlib.sha256(executable).hexdigest(),
                  unicorn_sha256=hashlib.sha256(args.dll.read_bytes()).hexdigest(),
                  art=art, cases=cases)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open('x', encoding='utf-8') as target:
        json.dump(output, target, indent=2)
    for case in cases:
        print({k: case[k] for k in ['action', 'initial_phase', 'initial_flags', 'hits', 'ended']})


if __name__ == '__main__':
    main()

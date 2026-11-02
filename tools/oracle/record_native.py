# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Owned DK2 replay: atomic decoded frames AND complete memory on every saved turn."""
import argparse
import ctypes as C
import hashlib
import importlib
import json
import os
from pathlib import Path
import re
import struct
import sys
import time
import traceback

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import decision_boundary as boundary
from native_memory import MemoryReader, MemoryWriter, write_json

EVIDENCE = Path('F:/DK2Evidence')
DEFAULT_DECODER = EVIDENCE / 'archive/main-clone--unknown-date--001/payload/reverse-engineering'
WORLD_UPDATE = 0x0050A450


def sha(path):
    result = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def packet_info(data):
    if len(data) < 68:
        raise ValueError('Packet lacks its complete 68-byte file header')
    level = data[:64].split(b'\0')[0].decode('ascii')
    if not re.fullmatch(r'[A-Za-z0-9_-]+', level):
        raise ValueError('Packet has an invalid level name')
    offset, turns = 68, []
    while offset < len(data):
        count = data[offset]
        size = 17 + count * 18 + 12
        if offset + size > len(data):
            raise ValueError(f'Packet record at {offset} is truncated')
        turns.append(dict(ordinal=len(turns) + 1, countOffset=offset,
                          stamp=struct.unpack_from('<I', data, offset + 1)[0], actions=count))
        offset += size
    if not turns:
        raise ValueError('Packet contains no complete records')
    return dict(level=level, records=len(turns), metadata=struct.unpack_from('<I', data, 64)[0], turns=turns)


def requested_ticks(first, last):
    if first < 1 or last < first:
        raise ValueError('Require a positive, ordered completed-turn interval')
    return sorted({1, 2, *range(first, last + 1)})


def u32(read, address):
    return struct.unpack('<I', read(address, 4))[0]


class StoppedMemory:
    def __init__(self, read):
        self.reader, self.optional_failures = read, []

    def read(self, address, size):
        if not address or size <= 0:
            return None
        try:
            return self.reader(address, size)
        except OSError:
            self.optional_failures.append([address, size])
            return None

    def u32(self, address):
        raw = self.read(address, 4)
        return None if raw is None else struct.unpack('<I', raw)[0]


def thread_contexts(debugger):
    result = []
    for tid, handle in sorted(debugger.threads.items()):
        if debugger.api.WaitForSingleObject(handle, 0) == 0:
            result.append(dict(tid=tid, status='exited'))
            continue
        context = boundary.WowContext()
        context.ContextFlags = 0x1003F
        debugger.checked(debugger.api.Wow64GetThreadContext(handle, C.byref(context)),
                         f'Complete WOW64 context for thread {tid}')
        result.append(dict(tid=tid, status='captured', flags=context.ContextFlags,
                           registers=boundary.registers(context), rawHex=bytes(context).hex(),
                           originalDebugRegisters=list(debugger.original.get(tid, ()))))
    return result


def validate_snapshot(raw, layout, tick, shape, memory):
    if raw is None or raw[0] != tick or raw[1] != tick:
        raise ValueError('Structured snapshot is missing or has a different turn')
    _, _, _, regions, records = raw
    core = {layout.REGION_THING_TABLE, layout.REGION_ACTIVE, layout.REGION_WORLD,
            layout.REGION_PLAYERS, layout.REGION_TILES}
    if not core <= set(regions) or any(regions[k][1] is None for k in core):
        raise ValueError('Missing required structured region')
    world_address, world = regions[layout.REGION_WORLD]
    actual = (layout.field('world', 'map_width', world), layout.field('world', 'map_height', world))
    if actual != shape or u32(memory.read, u32(memory.read, world_address + 4) + 4) != tick:
        raise ValueError('Saved complete memory has a different map/clock')
    active, seen, pools = struct.unpack_from('<H', world, 0x7A)[0], set(), []
    while active:
        if active in seen:
            raise ValueError('Cyclic active player chain')
        seen.add(active)
        pointer = u32(memory.read, layout.THING_TABLE + 4 * active)
        player = memory.read(pointer, layout.PLAYER_SIZE)
        number = player[0x10FC]
        if not 1 <= number <= layout.PLAYER_COUNT:
            raise ValueError('Invalid active player number')
        pool = layout.field('player', 'pool', player)
        if pool:
            key = layout.REGION_POOL + number - 1
            if key not in regions or regions[key][0] != pool or regions[key][1] is None:
                raise ValueError('Missing nonzero active player pool')
            pools.append(number)
        active = struct.unpack_from('<H', player, 0x3A)[0]
    if not seen or any(data is None for _, _, data in records.values()):
        raise ValueError('Missing active players or thing records')
    for address, data in regions.values():
        if data is not None and memory.read(address, len(data)) != data:
            raise ValueError('Structured region differs from complete memory')
    for pointer, _, data in records.values():
        if memory.read(pointer, len(data)) != data:
            raise ValueError('Structured thing record differs from complete memory')
    return dict(activePlayerSlots=sorted(seen), activePools=pools,
                optionalMissingRegions=[k for k, (_, data) in regions.items() if data is None])


def structured_roundtrip(raw, layout):
    tb, ta, elapsed, regions, records = raw
    payload = layout.FrameEncoder(128, 6).encode(tb, ta, 0, elapsed, regions, records)
    decoded_regions, decoded_records = {}, {}
    head = layout.apply_frame(payload, decoded_regions, decoded_records)
    if tuple(head[1:3]) != (tb, ta) or decoded_regions != regions or decoded_records != records:
        raise ValueError('Structured preflight codec does not round-trip every byte/marker')
    return dict(bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest())


def run(args):
    if sys.platform != 'win32' or C.sizeof(C.c_void_p) != 8:
        raise ValueError('64-bit Windows CPython is required')
    ticks = requested_ticks(args.first, args.last)
    if not 0 <= args.seed <= 0xFFFFFFFF:
        raise ValueError('Seed must fit an unsigned 32-bit word')
    existing = boundary.running_games()
    if existing:
        raise ValueError(f'A native game is already running: {existing}; nothing launched')
    exe, packet, output = args.exe.resolve(), args.packet.resolve(), args.out.resolve()
    if hashlib.md5(exe.read_bytes()).hexdigest() not in (boundary.RETAIL_MD5, boundary.DX_MD5):
        raise ValueError('Executable is not a pinned DK2 retail/DX binary')
    if packet.parent != (exe.parent / 'PacketRecord').resolve():
        raise ValueError('Packet must already exist in this install\'s PacketRecord directory')
    if packet.suffix.lower() != '.sav' or packet != (exe.parent / 'PacketRecord' / (packet.stem + '.sav')).resolve():
        raise ValueError('Packet must be the exact .sav file the native loader will open')
    info = packet_info(packet.read_bytes())
    if max(ticks) > info['records']:
        raise ValueError('Packet ends before the requested/preflight turns')
    if not output.is_relative_to(EVIDENCE.resolve()) or output == EVIDENCE.resolve():
        raise ValueError('Output must be a NEW subdirectory of F:/DK2Evidence')
    decoder = Path(os.environ.get('DK2_CAPTURE_DECODER', str(DEFAULT_DECODER))).resolve()
    for name in ('dk2capture.py', 'dk2record.py'):
        if not (decoder / name).is_file():
            raise ValueError(f'Missing retained decoder source: {decoder / name}')
    sys.path.insert(0, str(decoder))
    layout, recording = importlib.import_module('dk2capture'), importlib.import_module('dk2record')
    output.mkdir(parents=True, exist_ok=False)
    source_dir = output / 'sources'
    source_dir.mkdir()
    sources = [Path(__file__), Path(boundary.__file__), Path(__file__).with_name('native_memory.py'),
               Path(__file__).with_name('process_dump.py'), decoder / 'dk2capture.py', decoder / 'dk2record.py']
    for source in sources:
        with (source_dir / source.name).open('xb') as stream:
            stream.write(source.read_bytes())
    with (output / packet.name).open('xb') as stream:
        stream.write(packet.read_bytes())
    os.environ.setdefault('__COMPAT_LAYER', 'RunAsInvoker')
    launch = dict(schema=1, executable=str(exe), executableSHA256=sha(exe), packet=str(packet),
                  packetSHA256=sha(packet), packetInfo=info, seed=args.seed, requested=[args.first, args.last],
                  savedTurns=ticks, python=sys.version, compatibilityLayer=os.environ['__COMPAT_LAYER'],
                  sources={str(p): sha(p) for p in sources}, domain=[0, 1 << 32],
                  memoryPolicy='All committed readable regions; no type/size/category opt-in.',
                  observation='Completed world turn; debuggee threads stopped. External writers to shared pages are not frozen.')
    write_json(output / 'launch.json', launch)
    with (output / 'README.md').open('x', encoding='utf-8') as guide:
        guide.write('# Complete native capture\n\n'
                    'Inputs and source identities are in launch.json and sources/. '
                    'Every selected completed turn retains a structured frame and all readable 32-bit memory. '
                    'Early turns1/2 are mandatory full-memory preflight controls.\n\n'
                    'run-status.json records capture/shutdown only. A complete run requires verified.json '
                    'AND sha256-final.json. Missing/failed verification is never a successful capture. '
                    'Do not overwrite, rename or clean evidence; choose a new directory for another attempt.\n\n'
                    'Memory is read while game threads are stopped; external shared-page writers are not frozen. '
                    'Read-window pauses are measured and can affect real-time presentation. '
                    'These are analysis captures, not performance or older-trajectory parity controls.\n\n'
                    'Use tools/oracle/capture_state.py or capture_memory.py: the frozen decoder and '
                    'complete timeline load automatically, without --dump or an environment setting. '
                    'The operational guide is tools/oracle/native-capture.md.\n')
    debugger, timeline = boundary.BoundaryDebugger(WORLD_UPDATE), MemoryWriter(output / 'memory')
    desired, completed, shape, entry, rows, failure = set(ticks), [], None, None, [], None
    seed_proof, encoder = None, layout.FrameEncoder(128, 6)
    target = output / 'level.dk2cap'
    header = dict(format=layout.FORMAT_VERSION, created=time.strftime('%Y-%m-%d %H:%M:%S'),
                  world_base=boundary.WORLD, keyframe_every=128, zlib_level=6, settle_ms=0, gap=512,
                  classes={'0x%08X' % k: list(v) for k, v in layout.CLASSES.items()},
                  boundary='World_UpdateTurn return; tick names completed executed turn', complete_memory='memory')
    header['sizes'] = dict(world=layout.WORLD_SIZE, player=layout.PLAYER_SIZE, players=layout.PLAYER_COUNT,
                           pool=layout.POOL_SIZE, tile=layout.TILE_SIZE, slots=layout.SLOT_COUNT)
    with target.open('xb') as stream, (output / 'boundaries.jsonl').open('x', encoding='utf-8') as log:
        stream.write(layout.MAGIC)
        layout.write_chunk(stream, layout.KIND_HEADER, json.dumps(header).encode())

        def select(context, read):
            nonlocal shape, entry, seed_proof
            if context.Ecx != boundary.WORLD:
                raise ValueError('Unexpected world receiver')
            tick = u32(read, u32(read, boundary.WORLD + 4) + 4)
            if seed_proof is None:
                if tick != 1:
                    raise ValueError(f'Missed stopped seed initialization at turn1: got{tick}')
                world = read(boundary.WORLD, layout.WORLD_SIZE)
                shape = (layout.field('world', 'map_width', world), layout.field('world', 'map_height', world))
                if not all(1 <= v <= 1024 for v in shape):
                    raise ValueError(f'Invalid initialized map dimensions: {shape}')
                old = u32(read, boundary.WORLD + 0x15A0)
                api = debugger.api
                api.WriteProcessMemory.argtypes = [C.c_void_p, C.c_void_p, C.c_void_p, C.c_size_t, C.POINTER(C.c_size_t)]
                api.WriteProcessMemory.restype = C.c_int
                word, got = C.c_uint32(args.seed), C.c_size_t()
                debugger.checked(api.WriteProcessMemory(debugger.handle, C.c_void_p(boundary.WORLD + 0x15A0),
                                                        C.byref(word), 4, C.byref(got)), 'Set stopped initial seed')
                if got.value != 4 or u32(read, boundary.WORLD + 0x15A0) != args.seed:
                    raise ValueError('Initial seed write/readback failed')
                seed_proof = dict(tick=1, phase='before', old=old, seed=args.seed, readback=args.seed, map=list(shape))
                write_json(output / 'seed.json', seed_proof)
            expected = ticks[len(completed)]
            if tick > expected:
                raise ValueError(f'Missing/repeated completed turn: expected{expected}, got{tick}')
            if tick < expected or tick not in desired:
                return False
            entry = dict(tick=tick, esp=context.Esp, returnIP=u32(read, context.Esp), tid=debugger.pending.tid)
            return True

        def capture(phase, context, read):
            start = time.perf_counter()
            tick = u32(read, u32(read, boundary.WORLD + 4) + 4)
            if tick != entry['tick'] or debugger.pending.tid != entry['tid']:
                raise ValueError('Clock/thread changed within world update')
            row = dict(tick=tick, phase=phase, entry=entry, registers=boundary.registers(context),
                       threads=thread_contexts(debugger), pid=debugger.process.pid, readStarted=start)
            if phase == 'after':
                if context.Eip != entry['returnIP'] or context.Esp != entry['esp'] + 8:
                    raise ValueError('World return stack/address guard failed')
                row['memory'] = timeline.capture(tick, boundary.memory_regions(debugger.handle, debugger.api), read)
                memory = MemoryReader(output / 'memory', tick)
                if tick in (1, 2):
                    row['memoryPreflight'] = memory.validate()
                stopped = StoppedMemory(read)
                recorder = recording.Recorder(stopped, boundary.WORLD)
                raw = recorder.snapshot()
                row['structuredValidation'] = validate_snapshot(raw, layout, tick, shape, memory)
                if tick in (1, 2):
                    row['structuredCodecPreflight'] = structured_roundtrip(raw, layout)
                if recorder.missing_reads:
                    raise ValueError('Missing structured thing reads')
                row['optionalDecoderReadFailures'] = stopped.optional_failures
                if args.first <= tick <= args.last:
                    tb, ta, elapsed, regions, records = raw
                    layout.write_chunk(stream, layout.KIND_FRAME, encoder.encode(tb, ta, time.time(), elapsed, regions, records))
                    stream.flush()
                completed.append(tick)
            row['readFinished'] = time.perf_counter()
            row['pauseSeconds'] = row['readFinished'] - start
            log.write(json.dumps(row) + '\n')
            log.flush()
            rows.append(row)
            if phase == 'after':
                print(json.dumps(dict(tick=tick, map=shape, pauseSeconds=row['pauseSeconds'], memory=row['memory'])), flush=True)

        try:
            debugger.run(exe, ['-LEVEL', info['level'], '-PLOAD', packet.stem, '0'], select, capture,
                         timeout=args.timeout or max(120, args.last * 2 + 120), pairs=len(ticks))
        except BaseException:
            failure = traceback.format_exc()
        finally:
            try:
                if debugger.process is not None and debugger.process.poll() is None:
                    debugger.process.terminate()
                    debugger.process.wait(timeout=15)
            except BaseException:
                failure = (failure or '') + '\nOwned child shutdown failed:\n' + traceback.format_exc()
            status = dict(error=failure, validationPending=True, completed=completed, requested=[args.first, args.last],
                          seed=seed_proof, map=shape, ownedPid=debugger.process.pid if debugger.process else None,
                          ownedClosed=debugger.process is not None and debugger.process.poll() is not None,
                          packetUnchanged=sha(packet) == launch['packetSHA256'])
            layout.write_chunk(stream, layout.KIND_END, json.dumps(status).encode())
    write_json(output / 'run-status.json', status)
    if failure or completed != ticks or not status['ownedClosed'] or not status['packetUnchanged']:
        raise RuntimeError(f'Capture incomplete; retained evidence/status at {output}')
    try:
        verified, decoded = set(), []
        for tick in ticks:
            MemoryReader(output / 'memory', tick).validate(verified)
        for frame in layout.Capture(str(target)).frames():
            if frame.torn or frame.tick != frame.tick_after or frame.map_size() != shape:
                raise ValueError('Saved structured capture failed final readback')
            decoded.append(frame.tick)
        if decoded != list(range(args.first, args.last + 1)):
            raise ValueError('Saved structured capture has missing turns')
        exports = {}
        for tick in sorted({args.first, args.last}):
            name = f'{tick}.after.mem.z'
            exports[name] = MemoryReader(output / 'memory', tick).export(output / name)
        write_json(output / 'verified.json', dict(schema=1, decodedTurns=decoded, memoryTurns=ticks,
                                                captureFile=target.name, captureSHA256=sha(target),
                                                verifiedUniqueBlocks=len(verified), exports=exports,
                                                maxPauseSeconds=max(r['pauseSeconds'] for r in rows)))
        files = {p.relative_to(output).as_posix(): sha(p) for p in output.rglob('*') if p.is_file()}
        write_json(output / 'sha256-final.json', files)
    except BaseException:
        write_json(output / 'validation-failed.json', dict(error=traceback.format_exc()))
        raise
    print(f'Verified complete capture: {output}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--packet', type=Path, required=True)
    parser.add_argument('--seed', type=lambda value: int(value, 0), required=True)
    parser.add_argument('--first', type=int, default=1)
    parser.add_argument('--last', type=int, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--timeout', type=float)
    args = parser.parse_args()
    if args.timeout is not None and args.timeout <= 0:
        parser.error('Timeout must be positive')
    run(args)


if __name__ == '__main__':
    main()

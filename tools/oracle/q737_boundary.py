"""Capture the Troll's update immediately after a boulder stun (Q737).

Uses the documented BoundaryDebugger and complete stopped-memory dumper.
No game state, code, executable file, or input is patched.
"""
from pathlib import Path
import argparse
import base64
import ctypes as C
import hashlib
import json
import os
import re
import struct
import sys
import traceback

from decision_boundary import (BoundaryDebugger, DX_MD5, RETAIL_MD5, WORLD,
    dump_stopped, memory_regions, registers, running_games)

ENTRY = 0x00488930
LIVE = 0x006ED540
THINGS = 0x006EE540


def value(read,address,fmt='<I'):
    return struct.unpack(fmt,read(address,struct.calcsize(fmt)))[0]


def subject_state(read,pointer):
    raw = read(pointer,0x53F)
    def at(offset,fmt='<I'):
        return struct.unpack_from(fmt,raw,offset)[0]
    tick = value(read,value(read,WORLD+4)+4)
    definition = at(0x370)
    interval = value(read,definition+0x74C)
    elapsed = (tick-at(0x266)) & 0xFFFFFFFF
    tag = at(0x533,'<H')
    threat = None
    if tag and value(read,LIVE+tag,'<B'):
        source = value(read,THINGS+tag*4)
        kind = value(read,source+0x12)
        threat = dict(pointer=source,tag=tag,kind=kind,x=value(read,source+0x1A,'<i'),
            y=value(read,source+0x1E,'<i'),health=value(read,source+0x40,'<i'))
        if kind==2:
            threat.update(objectType=value(read,source+0x12C,'<B'),wear=value(read,source+0x156,'<i'))
    return dict(pointer=pointer,slot=at(4,'<H'),owner=at(0x28,'<H'),type=at(0x36F,'<B'),
        level=at(0x403,'<B'),tick=tick,rng=value(read,WORLD+0x15A0),state=at(0x25E),
        health=at(0x40,'<i'),entry=at(0x266),age=at(0x26A),resume=at(0x32D),
        stunInterval=interval,elapsed=elapsed,holdWouldContinue=elapsed<interval,
        fear=at(0x526,'<H'),tempFear=at(0x524,'<H'),fearThreshold=value(read,definition+0x696,'<H'),
        fearlessUntil=at(0x3D7),attackable=bool(at(0x3EA)&0x40),flags=at(0x3E6),
        position=list(struct.unpack_from('<iii',raw,0x1A)),
        physical=[at(offset,'<i') for offset in [0x104,0x108,0x10C]],threat=threat)


class TrollWindow:
    def __init__(self):
        self.pointer = self.slot = self.first_tick = self.pending_tick = None
        self.completed = 0

    def select(self,pointer,read):
        if self.pointer is not None and pointer!=self.pointer:
            return False
        if self.pointer is None:
            if value(read,pointer+0x36F,'<B')!=10 or value(read,pointer+0x28,'<H')!=3:
                return False
        state = subject_state(read,pointer)
        if self.pointer is None:
            source = state['threat']
            if state['state']!=256 or state['health']<=0 or state['level']!=1 or not source:
                return False
            if source['kind']!=2 or source.get('objectType')!=74:
                return False
            self.pointer,self.slot,self.first_tick = pointer,state['slot'],state['tick']
        self.identity(state)
        if state['tick']!=((self.first_tick+self.completed)&0xFFFFFFFF):
            raise ValueError('Selected Troll updates are not consecutive native turns')
        return True

    def identity(self,state):
        if (state['pointer'],state['slot'],state['owner'],state['type'])!=(self.pointer,self.slot,3,10):
            raise ValueError('Selected Troll identity changed')

    def sample(self,phase,read):
        if self.pointer is None:
            raise ValueError('No Troll selected')
        state = subject_state(read,self.pointer)
        self.identity(state)
        if phase=='before':
            if self.pending_tick is not None:
                raise ValueError('Previous boundary pair is incomplete')
            self.pending_tick = state['tick']
        elif phase=='after':
            if self.pending_tick is None or state['tick']!=self.pending_tick:
                raise ValueError('Native clock changed within the Troll update')
            self.pending_tick = None
            self.completed += 1
        else:
            raise ValueError('Unknown boundary phase')
        return state


def write_json(path,data):
    with path.open('x',encoding='utf-8') as stream:
        json.dump(data,stream,indent=2)


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream,'sha256').hexdigest()
def run_owned(debugger,run,validate,output):
    failure = cleanup_error = None
    try:
        run()
        validate()
    except BaseException:
        failure = traceback.format_exc()
        raise
    finally:
        try:
            if debugger.process is not None and debugger.process.poll() is None:
                debugger.process.terminate()
                debugger.process.wait(timeout=15)
        except BaseException:
            cleanup_error = traceback.format_exc()
            if failure is None:
                raise
        finally:
            write_json(output/'run-status.json',dict(error=failure,cleanupError=cleanup_error,
                completedPairs=debugger.completed,
                ownedPid=debugger.process.pid if debugger.process else None,
                ownedClosed=debugger.process is not None and debugger.process.poll() is not None))
    if debugger.process is None or debugger.process.poll() is None:
        raise ValueError('Owned native child is still running')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--exe',type=Path,required=True)
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--level',default='M7Q513a')
    parser.add_argument('--pairs',type=int,default=4)
    parser.add_argument('--timeout',type=float,default=180)
    args = parser.parse_args()
    if sys.platform!='win32' or C.sizeof(C.c_void_p)!=8:
        parser.error('64-bit Windows CPython required')
    if not 1<=args.pairs<=16 or args.timeout<=0 or not re.fullmatch(r'[A-Za-z0-9_-]{1,30}',args.level):
        parser.error('Require pairs1..16, positive timeout and a simple level name')
    existing = running_games()
    if existing:
        parser.error(f'A DK2 process is already running: {existing}; nothing launched')
    executable = args.exe.resolve()
    digest = hashlib.md5(executable.read_bytes()).hexdigest()
    if digest not in (RETAIL_MD5,DX_MD5):
        parser.error('Executable does not match the pinned retail/DX binary')
    output = args.out.resolve()
    evidence = Path('F:/DK2Evidence').resolve()
    if output==evidence or not output.is_relative_to(evidence):
        parser.error('Output must be a new subdirectory under F:/DK2Evidence')
    level_files = [executable.parent/'Data/editor/maps'/(args.level+suffix)
        for suffix in ['.kwd','Map.kld','Players.kld','Things.kld','Triggers.kld','Variables.kld']]
    hashes = {str(path):sha(path) for path in level_files}
    output.mkdir(parents=True,exist_ok=False)
    sources = [Path(__file__),Path(__file__).with_name('decision_boundary.py'),Path(__file__).with_name('process_dump.py')]
    for source in sources:
        with (output/source.name).open('xb') as stream:
            stream.write(source.read_bytes())
    os.environ['__COMPAT_LAYER'] = 'RunAsInvoker'
    write_json(output/'launch.json',dict(schema=1,entry=ENTRY,executable=str(executable),
        executableMD5=digest,executableSHA256=sha(executable),arguments=['-LEVEL',args.level],
        levelFilesSHA256=hashes,compatibilityLayer=os.environ['__COMPAT_LAYER'],pairs=args.pairs,
        scope='Fresh native no-input run; not a replay of the legacy capture or its unknown seed.'))
    debugger = BoundaryDebugger(ENTRY)
    window = TrollWindow()
    first_thread = None
    with (output/'boundaries.jsonl').open('x',encoding='utf-8') as log:
        def capture(phase,context,read):
            nonlocal first_thread
            tid = debugger.pending.tid
            if first_thread is None:
                first_thread = tid
            if tid!=first_thread:
                raise ValueError('Selected Troll moved between native threads')
            state = window.sample(phase,read)
            pair = debugger.completed+1
            filename = f'{pair:03}.{phase}.mem.z'
            map_name = f'{pair:03}.{phase}.address-map.json'
            address_map = list(memory_regions(debugger.handle,debugger.api))
            write_json(output/map_name,dict(domain=[0,1<<32],regions=address_map))
            contexts = {str(thread):dict(registers=registers(current),
                wow64ContextBase64=base64.b64encode(bytes(current)).decode('ascii'))
                for thread in debugger.threads for current in [debugger.context(thread)]}
            dump = dump_stopped(debugger,output/filename)
            if dump['bytes']!=sum(region['size'] for region in address_map if region['readable']):
                raise ValueError('Stopped memory coverage differs from the complete address map')
            record = dict(schema=1,pair=pair,phase=phase,pid=debugger.process.pid,tid=tid,
                subject=state,registers=registers(context),threads=contexts,
                memory=filename,addressMap=map_name,dump=dump)
            log.write(json.dumps(record)+'\n')
            log.flush()
            print(json.dumps(dict(pair=pair,phase=phase,tick=state['tick'],state=state['state'],
                health=state['health'],interval=state['stunInterval'],elapsed=state['elapsed'])),flush=True)
        def validate():
            if not all(sha(Path(path))==expected for path,expected in hashes.items()):
                raise ValueError('Level files changed during the native run')
            if debugger.completed!=args.pairs or window.pending_tick is not None:
                raise ValueError('Incomplete native boundary window')
        run_owned(debugger,lambda: debugger.run(executable,['-LEVEL',args.level],
            lambda context,read: window.select(context.Ecx,read),capture,args.timeout,args.pairs),validate,output)
    write_json(output/'sha256-final.json',{str(path.relative_to(output)):sha(path)
        for path in output.rglob('*') if path.is_file()})
    write_json(output/'verified.json',dict(pairs=debugger.completed,levelFilesUnchanged=True,
        ownedClosed=True,scope='Native update boundaries, not legacy-run timing parity.'))


if __name__=='__main__':
    main()

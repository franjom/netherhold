#!/usr/bin/env python3
"""Serve DKII.EXE to an agent over MCP (pyghidra-mcp), on a clone of the canonical project.

Claude Code (.mcp.json) and Codex (~/.codex/config.toml) run this over stdio
when a session opens:

    python tools/ghidra/mcp-serve.py --agent claude|codex

pyghidra-mcp holds its Ghidra project open for its whole life and Ghidra
allows one holder per project, so the server never opens the canonical
project (ghidra.py's canonical_project()). Every session clones it to
D:/Ghidra/mcp/<agent>-<pid>/ (instant on ReFS), serves the clone and deletes
it at exit, so two sessions of one agent never collide and names given
through `dk2tools kb apply-names` (ghidra.py --write) or the GUI reach the
next session. Renames made through the MCP land in the clone only and are
lost at exit. A clone left behind by a killed session is swept at the next
start (and by the next read-only ghidra.py run) once its process is gone. A
clone taken while the GUI or a --write run is saving may be torn; restart the
session after the save.

The semantic index (chromadb) is built once into D:/Ghidra/mcp/index/ with
--index-only and copied beside each clone as DK2_RE-pyghidra-mcp/. While no
index exists, every session embeds the whole binary in the background, which
slows other Ghidra starts and delays shutdown; build it first. Names change
the embeddings only slowly, so rebuild with --index-only after many renames.
There is no --refresh-only or --reindex any more: every session clones
fresh, and --index-only always builds from scratch.

Toolchain paths (Ghidra, JDK, the canonical project) are read from ghidra.py,
so there is one place to change them.

Other uses:
    --index-only            clone to D:/Ghidra/mcp/index-build-<pid>, run the
                            server until the semantic index is complete, then
                            replace D:/Ghidra/mcp/index with it (~10 min)
    --http [--port N] [--gui]
                            long-running HTTP server on http://127.0.0.1:N/mcp
                            (claude 8000, codex 8001); --gui opens the
                            CodeBrowser in the same JVM. Point the client at
                            the URL by hand; stdio is the everyday path.
Environment: DK2_MCP_ROOT overrides D:/Ghidra/mcp (mcp-check.py uses a
sibling root so it never collides with a live session); DK2_GHIDRA_HOME,
DK2_JAVA_HOME and DK2_GHIDRA_PROJECT as for ghidra.py.
"""
import argparse
import atexit
import importlib.util
import os
import re
import shutil
import signal
import subprocess
import sys
import time

sys.dont_write_bytecode = True  # loading ghidra.py must not leave a __pycache__ in tools/

HERE = os.path.dirname(os.path.abspath(__file__))
MCP_ROOT = os.environ.get('DK2_MCP_ROOT', 'D:/Ghidra/mcp')
INDEX_DIR = os.path.join(MCP_ROOT, 'index')
INDEX_NAME = None  # <canonical project name>-pyghidra-mcp, set in main()
PORTS = {'claude': 8000, 'codex': 8001}
PINNED = 'pyghidra-mcp==0.2.7'
INDEX_DONE = ('Background indexing completed successfully', 'already complete; skipping')
INDEX_FAILED = ('LockException', 'Traceback')


def load_runner():
    spec = importlib.util.spec_from_file_location('dk2_ghidra_runner', os.path.join(HERE, 'ghidra.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def log(message):
    sys.stderr.write('[mcp-serve.py] %s\n' % message)
    sys.stderr.flush()


class Session:
    """One clone and the server process on it; close() stops the server and deletes the clone."""

    def __init__(self, runner, clone_dir):
        self.runner = runner
        self.clone_dir = os.path.normpath(clone_dir)
        self.process = None
        self.closed = False

    def open(self, with_index):
        self.runner.remove_tree(self.clone_dir)  # ours by pid: a leftover of a dead process with this pid
        base = self.runner.clone_project(os.path.normpath(self.runner.CANONICAL), self.clone_dir)
        index = os.path.join(INDEX_DIR, INDEX_NAME)
        if with_index and os.path.isdir(index):
            shutil.copytree(index, os.path.join(self.clone_dir, INDEX_NAME))
        elif with_index:
            log('no semantic index at %s; this session embeds in the background '
                '(build it once with --index-only)' % index)
        return base

    def close(self):
        if self.closed:
            return
        self.closed = True
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        if not self.runner.remove_tree(self.clone_dir):
            log('could not delete %s; the next start sweeps it' % self.clone_dir)


def start_reaper(pids, clone_dir):
    """Start a detached process that deletes clone_dir once every pid has exited.

    MCP clients do not wait for pyghidra-mcp's shutdown: the Python SDK kills
    the process tree two seconds after closing stdin, a Node client kills this
    launcher alone, and neither runs our finally. The reaper breaks away from
    the client's job object when the job allows it; when it does not, it dies
    with the tree and the next start's sweep deletes the clone instead."""
    command = [sys.executable, os.path.abspath(__file__), '--reap', clone_dir] + [str(p) for p in pids]
    base_flags = 0
    if os.name == 'nt':
        base_flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
    for flags in ((base_flags | 0x01000000, base_flags) if os.name == 'nt' else (0,)):  # CREATE_BREAKAWAY_FROM_JOB
        try:
            subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL, close_fds=True, creationflags=flags,
                             env=dict(os.environ, PYTHONDONTWRITEBYTECODE='1'))
            return
        except OSError:
            continue
    log('could not start the reaper; the next start sweeps %s' % clone_dir)


def reap(runner, clone_dir, pids):
    """The reaper's body: wait for the launcher (pids[0]); give the server (pids[1:]) 10 s
    to follow, since a server without its launcher has lost its client and may be busy
    embedding for minutes; end its tree; then delete the clone, retrying while files are held.

    Both processes are opened at once and the handles held, which pins their pids: the
    server is killed only when its own handle says it is still running, never a stranger
    that reused the pid. If either cannot be opened, nothing is killed and the reaper
    only waits (up to 30 min) for the clone to come free."""
    if not (os.path.normcase(os.path.dirname(os.path.abspath(clone_dir))) == os.path.normcase(os.path.abspath(MCP_ROOT))
            and runner.CLONE_NAME.match(os.path.basename(clone_dir))):
        log('--reap refuses %s: not a clone folder under %s' % (clone_dir, MCP_ROOT))
        return 2
    ended = False
    if os.name == 'nt':
        k = runner.kernel32()
        access = 0x00100000 | 0x0001 | 0x1000  # SYNCHRONIZE | PROCESS_TERMINATE | QUERY_LIMITED_INFORMATION
        handles = [k.OpenProcess(access, False, pid) for pid in pids]
        try:
            if all(handles):
                k.WaitForSingleObject(handles[0], 0xFFFFFFFF)  # INFINITE
                deadline = time.time() + 10
                for pid, handle in zip(pids[1:], handles[1:]):
                    left = max(0, int((deadline - time.time()) * 1000))
                    if k.WaitForSingleObject(handle, left) == 0x102:  # WAIT_TIMEOUT: still running, pid pinned
                        subprocess.call(['taskkill', '/T', '/F', '/PID', str(pid)],
                                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                        k.WaitForSingleObject(handle, 30000)
                    ended = True
        finally:
            for handle in handles:
                if handle:
                    k.CloseHandle(handle)
    if not ended:
        while runner._alive(pids[0]):  # never delete while the launcher may still be serving
            time.sleep(2)
    for _ in range(60 if ended else 900):
        if not os.path.exists(clone_dir) or runner.remove_tree(clone_dir):
            return 0
        time.sleep(2)
    return 1


def server_command(project_base):
    exe = shutil.which('pyghidra-mcp')
    if not exe:
        raise SystemExit('pyghidra-mcp is not installed; run: uv tool install %s' % PINNED)
    return [exe, '--project-path', project_base + '.gpr']


def server_environment(runner):
    return dict(os.environ,
                GHIDRA_INSTALL_DIR=runner.GHIDRA_HOME,
                JAVA_HOME=runner.JAVA_HOME,
                PYTHONDONTWRITEBYTECODE='1')


def build_index(session, project_base):
    """Run the stdio server until its semantic index is complete, then close it."""
    command = server_command(project_base) + ['--transport', 'stdio']
    started = time.time()
    process = session.process = subprocess.Popen(
        command, env=server_environment(session.runner),
        stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    outcome = None
    for raw in process.stderr:
        line = raw.decode('utf-8', 'replace').rstrip()
        if 'indexing_mixin' in line or 'Opening existing project' in line:
            log('%5.0f s  %s' % (time.time() - started, line.split(':', 2)[-1]))
        if any(marker in line for marker in INDEX_DONE):
            outcome = 'complete'
            break
        if any(marker in line for marker in INDEX_FAILED):
            outcome = line
            break
    process.stdin.close()
    try:
        process.wait(timeout=120)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
    if outcome != 'complete':
        raise SystemExit('indexing did not finish: %s' % outcome)
    log('index complete after %.0f s' % (time.time() - started))


def _rename(source, target):
    """os.rename, retried while a session's copytree may briefly hold the folder."""
    for attempt in range(10):
        try:
            os.rename(source, target)
            return
        except OSError:
            if attempt == 9:
                raise
            time.sleep(1)


def install_index(runner, built):
    """Replace INDEX_DIR by the finished index at `built`: build aside, old away, new in, old deleted."""
    pid = os.getpid()
    fresh = os.path.join(MCP_ROOT, 'index-new-%d' % pid)
    old = os.path.join(MCP_ROOT, 'index-old-%d' % pid)
    os.makedirs(fresh)
    _rename(built, os.path.join(fresh, INDEX_NAME))
    if os.path.isdir(INDEX_DIR):
        _rename(INDEX_DIR, old)
    _rename(fresh, INDEX_DIR)
    if os.path.isdir(old):
        runner.remove_tree(old)
    log('semantic index installed at %s' % INDEX_DIR)


def main():
    runner = load_runner()
    global INDEX_NAME
    INDEX_NAME = os.path.basename(os.path.normpath(runner.CANONICAL)) + '-pyghidra-mcp'
    if runner.is_under(runner.CANONICAL, MCP_ROOT):
        raise SystemExit('the canonical project %s is under the clone root %s; clones are '
                         'deleted, so it cannot be the project' % (runner.CANONICAL, MCP_ROOT))
    if len(sys.argv) >= 3 and sys.argv[1] == '--reap':  # internal: see start_reaper
        return reap(runner, sys.argv[2], [int(p) for p in sys.argv[3:]])
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--agent', required=True, help='names the clone (claude, codex, ...)')
    parser.add_argument('--index-only', action='store_true', help='build the semantic index to completion and exit')
    parser.add_argument('--http', action='store_true', help='serve streamable HTTP instead of stdio')
    parser.add_argument('--port', type=int, default=0, help='HTTP port (claude 8000, codex 8001)')
    parser.add_argument('--gui', action='store_true', help='with --http: open the CodeBrowser in the same JVM')
    parser.add_argument('--wait-for-analysis', action='store_true')
    options = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9_]+', options.agent) or options.agent.startswith('index'):
        parser.error('--agent must be letters, digits or _, and not start with "index"')
    if options.gui and not options.http:
        parser.error('--gui needs --http')
    if options.http and not options.port and options.agent not in PORTS:
        parser.error('--http for %s needs --port' % options.agent)

    os.makedirs(MCP_ROOT, exist_ok=True)
    runner.sweep_stale_clones(MCP_ROOT)
    name = ('index-build-%d' if options.index_only else options.agent + '-%d') % os.getpid()
    session = Session(runner, os.path.join(MCP_ROOT, name))
    atexit.register(session.close)

    def stop(signum, _frame):
        raise SystemExit(128 + signum)
    for signame in ('SIGTERM', 'SIGBREAK', 'SIGHUP'):
        if hasattr(signal, signame):
            signal.signal(getattr(signal, signame), stop)

    try:
        base = session.open(with_index=not options.index_only)
        if options.index_only:
            build_index(session, base)
            install_index(runner, os.path.join(session.clone_dir, INDEX_NAME))
            return 0

        command = server_command(base)
        if options.http:
            port = options.port or PORTS[options.agent]
            command += ['--transport', 'streamable-http', '--host', '127.0.0.1', '--port', str(port)]
            if options.gui:
                command.append('--gui')
            log('serving %s on http://127.0.0.1:%d/mcp' % (options.agent, port))
        else:
            command += ['--transport', 'stdio']
        if options.wait_for_analysis:
            command.append('--wait-for-analysis')

        session.process = subprocess.Popen(command, env=server_environment(runner))
        start_reaper([os.getpid(), session.process.pid], session.clone_dir)
        return session.process.wait()
    except KeyboardInterrupt:
        return 130
    finally:
        session.close()


if __name__ == '__main__':
    sys.exit(main())

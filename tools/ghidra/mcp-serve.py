#!/usr/bin/env python3
"""Serve DKII.EXE to an agent over MCP (pyghidra-mcp), on a fresh copy of its project.

Claude Code (.mcp.json) and Codex (~/.codex/config.toml) run this over stdio
when a session opens:

    python tools/ghidra/mcp-serve.py --agent claude|codex

pyghidra-mcp holds its Ghidra project open for its whole life and Ghidra
allows one holder per project, so the server never opens the agent's real
project (the one tools/ghidra/ghidra.py uses). It opens a copy under
D:/Ghidra/mcp/<agent>/, and this launcher refreshes that copy from the real
project whenever the real project's database changed since the last refresh,
so names given through the runner (--write) or the GUI reach the MCP at the
next session. Renames made through the MCP land in the copy only and are
replaced by the next refresh.

The semantic index (chromadb, next to the copy in DK2_RE-pyghidra-mcp/) is
built once with --index-only and then kept across refreshes: while it is
incomplete, every session start embeds the whole binary in the background,
which slows other Ghidra starts and delays shutdown until the in-flight work
is done. A complete index is skipped at start-up.

Toolchain paths (Ghidra, JDK, the agents' projects) are read from ghidra.py,
so there is one place to change them.

Other uses:
    --refresh-only          force a refresh of the copy and exit
    --index-only            run the server until the semantic index is complete
    --reindex               with --refresh-only or --index-only: discard the
                            index and build it again (after many renames)
    --http [--port N] [--gui]
                            long-running HTTP server on http://127.0.0.1:N/mcp
                            (claude 8000, codex 8001); --gui opens the
                            CodeBrowser in the same JVM. Point the client at
                            the URL by hand; stdio is the everyday path.
Environment: DK2_MCP_ROOT overrides D:/Ghidra/mcp (mcp-check.py uses a
sibling root so it never collides with a live session); DK2_GHIDRA_HOME and
DK2_JAVA_HOME as for ghidra.py.
"""
import argparse
import importlib.util
import os
import shutil
import subprocess
import sys
import time

sys.dont_write_bytecode = True  # loading ghidra.py must not leave a __pycache__ in tools/

HERE = os.path.dirname(os.path.abspath(__file__))
MCP_ROOT = os.environ.get('DK2_MCP_ROOT', 'D:/Ghidra/mcp')
PORTS = {'claude': 8000, 'codex': 8001}
PINNED = 'pyghidra-mcp==0.2.7'
# Files whose change means the program database changed. Ghidra touches the
# ~index.* files on every open, even read-only, so those are ignored.
CONTENT_SUFFIXES = ('.gbf', '.prp')
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


def fingerprint(project_base):
    """Sorted (relative path, size, mtime) of every content file of a project."""
    items = []
    gpr = project_base + '.gpr'
    if os.path.exists(gpr):
        st = os.stat(gpr)
        items.append(('DK2_RE.gpr', st.st_size, int(st.st_mtime)))
    for root, _dirs, files in os.walk(project_base + '.rep'):
        for name in files:
            if not name.endswith(CONTENT_SUFFIXES):
                continue
            path = os.path.join(root, name)
            st = os.stat(path)
            items.append((os.path.relpath(path, project_base + '.rep').replace('\\', '/'), st.st_size, int(st.st_mtime)))
    return '\n'.join('%s %d %d' % item for item in sorted(items))


def locked(project_base):
    return os.path.exists(project_base + '.lock')


def copy_base_for(agent):
    return os.path.normpath(os.path.join(MCP_ROOT, agent, 'DK2_RE'))


def refresh_copy(agent, source_base, force, reindex=False):
    """Bring <MCP_ROOT>/<agent>/DK2_RE up to date with the agent's project.

    Returns 'fresh', 'refreshed' or 'stale' (kept an old copy because a lock
    was in the way). Raises if there is no copy and none can be made.
    """
    copy_base = copy_base_for(agent)
    copy_dir = os.path.dirname(copy_base)
    stamp_path = os.path.join(copy_dir, 'refreshed-from.txt')
    have_copy = os.path.exists(copy_base + '.gpr')

    current = fingerprint(source_base)
    recorded = open(stamp_path).read() if os.path.exists(stamp_path) else None
    if have_copy and not force and current == recorded:
        return 'fresh'

    if locked(source_base):
        if have_copy:
            log('%s is held by a headless run or the GUI; serving the existing copy unrefreshed' % source_base)
            return 'stale'
        raise SystemExit('%s is locked and there is no copy to serve yet' % source_base)

    os.makedirs(copy_dir, exist_ok=True)
    doomed_dirs = [copy_base + '.rep']
    if reindex:
        doomed_dirs.append(copy_base + '-pyghidra-mcp')
    try:
        for path in doomed_dirs:
            if os.path.isdir(path):
                shutil.rmtree(path)
        for path in (copy_base + '.gpr', copy_base + '.lock', copy_base + '.lock~'):
            if os.path.exists(path):
                os.remove(path)
    except OSError as error:
        if have_copy:
            log('could not replace the copy (%s); a live session of %s holds it; serving as is' % (error, agent))
            return 'stale'
        raise
    shutil.copytree(source_base + '.rep', copy_base + '.rep')
    shutil.copy2(source_base + '.gpr', copy_base + '.gpr')
    for root, _dirs, files in os.walk(copy_base + '.rep'):
        for name in files:
            if name.endswith(('.lock', '.lock~')):
                os.remove(os.path.join(root, name))
    with open(stamp_path, 'w') as stamp:
        stamp.write(current)
    log('refreshed %s from %s' % (copy_base, source_base))
    return 'refreshed'


def server_command(agent):
    exe = shutil.which('pyghidra-mcp')
    if not exe:
        raise SystemExit('pyghidra-mcp is not installed; run: uv tool install %s' % PINNED)
    return [exe, '--project-path', copy_base_for(agent) + '.gpr']


def server_environment(runner):
    return dict(os.environ,
                GHIDRA_INSTALL_DIR=runner.GHIDRA_HOME,
                JAVA_HOME=runner.JAVA_HOME,
                PYTHONDONTWRITEBYTECODE='1')


def build_index(agent, runner):
    """Run the stdio server until its semantic index is complete, then close it."""
    command = server_command(agent) + ['--transport', 'stdio']
    started = time.time()
    process = subprocess.Popen(command, env=server_environment(runner),
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
    if outcome != 'complete':
        raise SystemExit('indexing did not finish: %s' % outcome)
    log('index for %s complete after %.0f s' % (agent, time.time() - started))


def main():
    runner = load_runner()
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--agent', choices=sorted(runner.PROJECTS), required=True)
    parser.add_argument('--refresh-only', action='store_true', help='force a refresh of the copy and exit')
    parser.add_argument('--index-only', action='store_true', help='build the semantic index to completion and exit')
    parser.add_argument('--reindex', action='store_true', help='discard the semantic index before refreshing or indexing')
    parser.add_argument('--http', action='store_true', help='serve streamable HTTP instead of stdio')
    parser.add_argument('--port', type=int, default=0, help='HTTP port (claude 8000, codex 8001)')
    parser.add_argument('--gui', action='store_true', help='with --http: open the CodeBrowser in the same JVM')
    parser.add_argument('--wait-for-analysis', action='store_true')
    options = parser.parse_args()

    source_base = runner.PROJECTS[options.agent]
    state = refresh_copy(options.agent, source_base, options.refresh_only, options.reindex)
    if options.refresh_only:
        print('copy for %s: %s' % (options.agent, state))
        if not options.index_only:
            return 0
    if options.index_only:
        build_index(options.agent, runner)
        return 0

    command = server_command(options.agent)
    if options.http:
        port = options.port or PORTS[options.agent]
        command += ['--transport', 'streamable-http', '--host', '127.0.0.1', '--port', str(port)]
        if options.gui:
            command.append('--gui')
        log('serving %s on http://127.0.0.1:%d/mcp' % (options.agent, port))
    else:
        if options.gui:
            raise SystemExit('--gui needs --http')
        command += ['--transport', 'stdio']
    if options.wait_for_analysis:
        command.append('--wait-for-analysis')

    try:
        return subprocess.call(command, env=server_environment(runner))
    except KeyboardInterrupt:
        return 130


if __name__ == '__main__':
    sys.exit(main())

#!/usr/bin/env python
"""Run the Ghidra query scripts against DKII.EXE, headless, in one Ghidra start.

    python tools/ghidra/ghidra.py Blocks.py OUT
    python tools/ghidra/ghidra.py --agent codex InsGrep.py OUT "0x6ee540" :: Up.py OUT2 004b5a7e 3

Steps are separated by a standalone `::` and run in order in one headless
session, which pays Ghidra's ~20 s start-up once. Scripts are found in
tools/ghidra/scripts/ and in every --script-path directory.

There is one canonical project (canonical_project()): DK2_GHIDRA_PROJECT if
set, else D:/Ghidra/DK2_RE, else the legacy D:/Ghidra/ghidra projects/DK2_RE
until the owner has moved it. Ghidra allows one holder per project, so a
read-only run never opens it: it clones the project to
<DK2_MCP_ROOT or D:/Ghidra/mcp>/run-<agent>-<pid> (instant on ReFS), runs on
the clone and deletes the clone at exit. Any number of read-only runs can go
at once, and a run still works while the canonical project is locked (the GUI,
a --write run): the lock file is not copied. A clone taken while the GUI or a
--write run is saving may be torn; rerun after the save.

Runs are -readOnly unless --write is given: ForceAt/ForceGaps changes still
apply within the run, so a later step sees them, but nothing is saved. A
--write run opens the canonical project itself, one at a time: it refuses
while the project's .lock exists or another runner holds it (dk2tools kb
apply-names is the everyday writer).

--agent (default 'any') only names the log file and the clone. --project names
an explicit project, <dir>/<name> without .gpr, instead of the canonical one.

Only the scripts' own output and Ghidra's errors are printed. The full log is
kept in reverse-engineering/.cache/ghidra-<agent>.log. Environment overrides:
DK2_GHIDRA_HOME, DK2_JAVA_HOME, DK2_GHIDRA_AGENT, DK2_GHIDRA_PROJECT,
DK2_MCP_ROOT.
"""
import argparse
import atexit
import os
import re
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
SCRIPTS = os.path.join(HERE, 'scripts')
CACHE = os.path.join(REPO, 'reverse-engineering', '.cache')

GHIDRA_HOME = os.environ.get('DK2_GHIDRA_HOME', 'D:/Ghidra/ghidra_12.1.4_PUBLIC')
JAVA_HOME = os.environ.get('DK2_JAVA_HOME', 'C:/Program Files/Java/jdk-26.0.2.1')
CLONES = os.environ.get('DK2_MCP_ROOT', 'D:/Ghidra/mcp')
PROGRAM = 'DKII.EXE'


def canonical_project():
    """The one project every run and MCP session clones, <dir>/<name> without .gpr."""
    explicit = os.environ.get('DK2_GHIDRA_PROJECT')
    if explicit:
        return explicit
    if os.path.exists('D:/Ghidra/DK2_RE.gpr') and os.path.isdir('D:/Ghidra/DK2_RE.rep'):
        return 'D:/Ghidra/DK2_RE'
    return 'D:/Ghidra/ghidra projects/DK2_RE'  # legacy, until tools/ghidra/cleanup-ghidra-folders.ps1 moves it


CANONICAL = canonical_project()

# What the filter shows: a header per script ("INFO  SCRIPT: <path>"), the
# lines a script prints (PyGhidra writes them raw, not as log lines), and
# anything that looks like a failure. Log lines and the launcher's own
# chatter are kept in the log file only.
_SCRIPT_START = re.compile(r'^INFO\s+SCRIPT:\s+(.*?)\s+\(HeadlessAnalyzer\)')
_LOG_LINE = re.compile(r'^(INFO|WARN|DEBUG)\s')
_LAUNCHER = ('Last used Python', 'Using Python command', 'Switching to Ghidra')
_ERROR_LINE = re.compile(r'\b(ERROR|SEVERE)\b|Exception|Traceback|Unable to lock|LockException')


def split_steps(tokens):
    steps, current = [], []
    for token in tokens:
        if token == '::':
            if current:
                steps.append(current)
            current = []
        else:
            current.append(token)
    if current:
        steps.append(current)
    return steps


def find_script(name, paths):
    for directory in paths:
        if os.path.isfile(os.path.join(directory, name)):
            return True
    return False


def kernel32():
    """kernel32 with GetLastError kept and HANDLE-typed returns (a truncated int handle is wrong on x64)."""
    import ctypes
    from ctypes import wintypes
    k = ctypes.WinDLL('kernel32', use_last_error=True)
    k.OpenProcess.restype = wintypes.HANDLE
    k.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
    k.GetExitCodeProcess.argtypes = (wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD))
    k.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
    k.WaitForSingleObject.restype = wintypes.DWORD
    k.CloseHandle.argtypes = (wintypes.HANDLE,)
    return k


def _alive(pid):
    if os.name == 'nt':
        import ctypes
        from ctypes import wintypes
        k = kernel32()
        handle = k.OpenProcess(0x1000, False, pid)  # QUERY_LIMITED_INFORMATION
        if not handle:
            return ctypes.get_last_error() == 5  # access denied: it exists, it is someone else's
        code = wintypes.DWORD()
        ok = k.GetExitCodeProcess(handle, ctypes.byref(code))
        k.CloseHandle(handle)
        return not ok or code.value == 259  # STILL_ACTIVE
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def clone_project(source, target_dir):
    """Copy <source>.gpr/.rep under target_dir; instant on ReFS. Returns the clone's base path."""
    os.makedirs(target_dir, exist_ok=True)
    base = os.path.join(target_dir, os.path.basename(source))
    shutil.copy2(source + '.gpr', base + '.gpr')
    shutil.copytree(source + '.rep', base + '.rep')
    for root, _dirs, files in os.walk(base + '.rep'):
        for name in files:
            if name.endswith(('.lock', '.lock~')):
                os.remove(os.path.join(root, name))
    return base


def is_under(path, root):
    path, root = os.path.normcase(os.path.abspath(path)), os.path.normcase(os.path.abspath(root))
    return path == root or path.startswith(root.rstrip(os.sep) + os.sep)


def remove_tree(path):
    """Delete a clone folder; Java may hold its files a moment after exit, so retry once."""
    for attempt in range(2):
        shutil.rmtree(path, ignore_errors=True)
        if not os.path.exists(path):
            return True
        if attempt == 0:
            time.sleep(1)
    return False


# The only folder names a clone gets: run-<agent>-<pid> (ghidra.py), <agent>-<pid>
# and index-build-<pid> (mcp-serve.py). Agents are [A-Za-z0-9_]+ for mcp-serve.py;
# ghidra.py also allows '-', which only the run- form can carry.
CLONE_NAME = re.compile(r'^(?:run-[A-Za-z0-9_-]+|index-build|(?!index)[A-Za-z0-9_]+)-(\d+)$')


def sweep_stale_clones(root=None):
    """Delete clones under root whose owning process is gone (killed runs and sessions).

    Only folders named as CLONE_NAME that hold nothing but the canonical
    project's files (<name>.gpr/.rep/.lock and <name>-pyghidra-mcp) are
    touched; a folder still in use by a leftover JVM fails to delete and is
    left for the next sweep."""
    root = root or CLONES
    project = os.path.basename(os.path.normpath(CANONICAL))
    try:
        entries = os.listdir(root)
    except OSError:
        return
    for name in entries:
        match = CLONE_NAME.match(name)
        path = os.path.join(root, name)
        if not match or not os.path.isdir(path):
            continue
        pid = int(match.group(1))
        if pid == os.getpid() or _alive(pid):
            continue
        try:
            contents = os.listdir(path)
        except OSError:
            continue
        if all(item.startswith(project) for item in contents):
            shutil.rmtree(path, ignore_errors=True)


def claim(project):
    """Take this runner's lock on a project, or exit naming the holder.

    Ghidra writes <project>.lock only ~10 s after launch, so two runs started
    close together would both pass a check of that file. This marker is taken
    at once; a marker whose process is gone is stale and is replaced."""
    marker = os.path.join(CACHE, 'run-%s.pid' % re.sub(r'[^A-Za-z0-9]+', '_', project).strip('_'))
    for _ in range(2):
        try:
            descriptor = os.open(marker, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            try:
                with open(marker) as stream:
                    holder = int(stream.read().strip() or 0)
            except (OSError, ValueError):
                holder = 0
            if holder and _alive(holder):
                sys.exit('ghidra.py: process %d is already running on %s. Runs on one '
                         'project are sequential: wait, or batch steps with ::.' % (holder, project))
            os.remove(marker)
            continue
        os.write(descriptor, str(os.getpid()).encode())
        os.close(descriptor)
        return marker
    sys.exit('ghidra.py: could not take %s' % marker)


def main():
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--agent', default=os.environ.get('DK2_GHIDRA_AGENT') or 'any',
                        help='names the log file and the clone (default: any)')
    parser.add_argument('--project', default=None,
                        help='explicit project path, <dir>/<name> without .gpr (default: the canonical one)')
    parser.add_argument('--write', action='store_true',
                        help='save program changes to the project itself (default: -readOnly on a clone)')
    parser.add_argument('--script-path', action='append', default=[],
                        help='extra script directory, searched after tools/ghidra/scripts')
    parser.add_argument('--log', help='full Ghidra log (default: reverse-engineering/.cache)')
    parser.add_argument('--verbose', action='store_true', help='echo the whole Ghidra log')
    parser.add_argument('--dry-run', action='store_true', help='print the command only')
    parser.add_argument('steps', nargs=argparse.REMAINDER,
                        help='Script.py arg... [:: Script2.py arg...]')
    options = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9_-]+', options.agent):
        parser.error('--agent must be letters, digits, _ or -')

    source = os.path.normpath((options.project or CANONICAL).rstrip('/\\'))
    if is_under(source, CLONES):
        parser.error('%s is under the clone root %s; clones are deleted, so it cannot be '
                     'the project' % (source, CLONES))
    steps = split_steps(options.steps)
    if not steps:
        parser.error('no script given')

    script_paths = [SCRIPTS] + [os.path.abspath(p) for p in options.script_path]
    for step in steps:
        if not find_script(step[0], script_paths):
            parser.error('%s is not in %s' % (step[0], '; '.join(script_paths)))

    if not options.dry_run and not os.path.exists(source + '.gpr'):
        sys.exit('ghidra.py: no project at %s.gpr' % source)

    if options.write:
        lock = source + '.lock'
        if os.path.exists(lock) and not options.dry_run:
            sys.exit('ghidra.py: %s exists - another process holds this project. --write runs '
                     'are sequential; wait for it, or delete the file only if no Ghidra process '
                     'is alive.' % lock)
        project = source
    else:
        clone_dir = os.path.normpath(os.path.join(CLONES, 'run-%s-%d' % (options.agent, os.getpid())))
        project = os.path.join(clone_dir, os.path.basename(source))

    project_dir, project_name = os.path.split(project)
    command = [os.path.join(GHIDRA_HOME, 'support', 'pyghidraRun.bat'), '-H',
               project_dir, project_name, '-process', PROGRAM, '-noanalysis',
               '-scriptPath', ';'.join(script_paths)]
    if not options.write:
        command.append('-readOnly')
    for step in steps:
        command += ['-postScript'] + step

    if options.dry_run:
        if not options.write:
            print('clone %s -> %s' % (source, clone_dir))
        print(subprocess.list2cmdline(command))
        return 0

    if not os.path.isdir(CACHE):
        os.makedirs(CACHE)
    if options.write:
        marker = claim(source)
        try:
            return run(command, options, project_name)
        finally:
            os.remove(marker)

    # A clone cannot collide with anything, so no marker and no lock check.
    sweep_stale_clones()
    atexit.register(remove_tree, clone_dir)
    try:
        remove_tree(clone_dir)  # ours by pid: a leftover of a dead process with this pid
        clone_project(source, clone_dir)
        return run(command, options, project_name)
    finally:
        if not remove_tree(clone_dir):
            print('ghidra.py: could not delete %s; mcp-serve.py sweeps it once this process is gone'
                  % clone_dir, file=sys.stderr)


def run(command, options, project_name):
    log_path = options.log or os.path.join(CACHE, 'ghidra-%s.log' % options.agent)
    # No __pycache__ beside the tracked scripts.
    environment = dict(os.environ, JAVA_HOME=JAVA_HOME, PYTHONDONTWRITEBYTECODE='1')
    errors = 0
    in_script = False
    # stdin is closed so a launcher 'pause' ends at once instead of hanging the caller.
    with open(log_path, 'w', encoding='utf-8', errors='replace') as log:
        process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, env=environment,
                                   encoding='utf-8', errors='replace')
        for line in process.stdout:
            log.write(line)
            text = line.rstrip('\n')
            if options.verbose:
                print(text)
                continue
            start = _SCRIPT_START.match(text)
            if start:
                in_script = True
                print('== %s' % os.path.basename(start.group(1)))
            elif _ERROR_LINE.search(text):
                errors += 1
                print(text)
            elif (in_script and text.strip() and not _LOG_LINE.match(text)
                  and not text.startswith(_LAUNCHER)):
                print(text)
        code = process.wait()
    if code != 0 or errors:
        print('ghidra.py: exit %d, %d error line(s); full log %s' % (code, errors, log_path),
              file=sys.stderr)
        return code or 1
    return 0


if __name__ == '__main__':
    sys.exit(main())

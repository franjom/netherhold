#!/usr/bin/env python
"""Run the Ghidra query scripts against DKII.EXE, headless, in one Ghidra start.

    python tools/ghidra/ghidra.py --agent claude Blocks.py OUT
    python tools/ghidra/ghidra.py --agent codex InsGrep.py OUT "0x6ee540" :: Up.py OUT2 004b5a7e 3

Steps are separated by a standalone `::` and run in order in one headless
session, which pays Ghidra's ~20 s start-up once. Scripts are found in
tools/ghidra/scripts/ and in every --script-path directory.

The project is chosen by agent, because one project must never serve two
concurrent runs (the second waits on the lock at 'Press any key'):

    claude  D:/Ghidra/ghidra projects claude/DK2_RE
    codex   D:/Ghidra/ghidra projects/DK2_RE

Runs are -readOnly unless --write is given: ForceAt/ForceGaps changes still
apply within the run, so a later step sees them, but nothing is saved.

Only the scripts' own output and Ghidra's errors are printed. The full log is
kept in reverse-engineering/.cache/ghidra-<agent>.log. Environment overrides:
DK2_GHIDRA_HOME, DK2_JAVA_HOME, DK2_GHIDRA_AGENT, DK2_GHIDRA_PROJECT.
"""
import argparse
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
SCRIPTS = os.path.join(HERE, 'scripts')
CACHE = os.path.join(REPO, 'reverse-engineering', '.cache')

GHIDRA_HOME = os.environ.get('DK2_GHIDRA_HOME', 'D:/Ghidra/ghidra_12.1.4_PUBLIC')
JAVA_HOME = os.environ.get('DK2_JAVA_HOME', 'C:/Program Files/Java/jdk-26.0.2.1')
PROJECTS = {
    'claude': 'D:/Ghidra/ghidra projects claude/DK2_RE',
    'codex': 'D:/Ghidra/ghidra projects/DK2_RE',
}
PROGRAM = 'DKII.EXE'

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


def _alive(pid):
    if os.name == 'nt':
        import ctypes
        handle = ctypes.windll.kernel32.OpenProcess(0x1000, False, pid)  # QUERY_LIMITED_INFORMATION
        if not handle:
            return False
        code = ctypes.c_ulong()
        ctypes.windll.kernel32.GetExitCodeProcess(handle, ctypes.byref(code))
        ctypes.windll.kernel32.CloseHandle(handle)
        return code.value == 259  # STILL_ACTIVE
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


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
    parser.add_argument('--agent', choices=sorted(PROJECTS),
                        default=os.environ.get('DK2_GHIDRA_AGENT'),
                        help='whose Ghidra project to open (required unless --project)')
    parser.add_argument('--project', default=os.environ.get('DK2_GHIDRA_PROJECT'),
                        help='explicit project path, <dir>/<name> without .gpr')
    parser.add_argument('--write', action='store_true',
                        help='save program changes (default: -readOnly)')
    parser.add_argument('--script-path', action='append', default=[],
                        help='extra script directory, searched after tools/ghidra/scripts')
    parser.add_argument('--log', help='full Ghidra log (default: reverse-engineering/.cache)')
    parser.add_argument('--verbose', action='store_true', help='echo the whole Ghidra log')
    parser.add_argument('--dry-run', action='store_true', help='print the command only')
    parser.add_argument('steps', nargs=argparse.REMAINDER,
                        help='Script.py arg... [:: Script2.py arg...]')
    options = parser.parse_args()

    project = options.project or PROJECTS.get(options.agent)
    if not project:
        parser.error('pass --agent claude|codex (or --project): the two agents '
                     'must never open the same project at once')
    steps = split_steps(options.steps)
    if not steps:
        parser.error('no script given')

    script_paths = [SCRIPTS] + [os.path.abspath(p) for p in options.script_path]
    for step in steps:
        if not find_script(step[0], script_paths):
            parser.error('%s is not in %s' % (step[0], '; '.join(script_paths)))

    project_dir, project_name = os.path.split(project.rstrip('/\\'))
    lock = os.path.join(project_dir, project_name + '.lock')
    if os.path.exists(lock) and not options.dry_run:
        sys.exit('ghidra.py: %s exists - another run holds this project. Runs on one '
                 'project are sequential; wait for it, or delete the file only if no '
                 'Ghidra process is alive.' % lock)

    command = [os.path.join(GHIDRA_HOME, 'support', 'pyghidraRun.bat'), '-H',
               project_dir, project_name, '-process', PROGRAM, '-noanalysis',
               '-scriptPath', ';'.join(script_paths)]
    if not options.write:
        command.append('-readOnly')
    for step in steps:
        command += ['-postScript'] + step

    if options.dry_run:
        print(subprocess.list2cmdline(command))
        return 0

    if not os.path.isdir(CACHE):
        os.makedirs(CACHE)
    marker = claim(os.path.normpath(project))
    try:
        return run(command, options, project_name)
    finally:
        os.remove(marker)


def run(command, options, project_name):
    log_path = options.log or os.path.join(
        CACHE, 'ghidra-%s.log' % (options.agent or project_name))
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

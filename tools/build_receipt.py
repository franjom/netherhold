# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""Fresh-build receipt for an acceptance run (campaign player plan, task 7).

Builds the solution in Release from scratch (--no-incremental) and writes, into a NEW directory:
  head.txt        the commit
  status.txt      git status --porcelain=v1 (a dirty tree is allowed and recorded)
  tracked.patch   git diff HEAD --binary, with tracked.patch.sha256 beside it
  source.sha256   every tracked or untracked, not ignored file under src/, tests/, scenarios/ and the
                  root build files, hashed before the build and checked again after it
  build.log       the build's output
  output.sha256   every file in the harness output folder but .pdb symbols (assemblies, deps.json,
                  runtimeconfig.json): a stale file there is hashed too, as the verifier checks it
  environment.txt dotnet version and UTC time
`DK2.Harness run --campaign-player <p> --player-receipt <dir>` refuses a harness or tree that does
not match the receipt, and claims acceptance only with one that does.

Usage: python -I tools/build_receipt.py <new receipt directory>
"""
import hashlib
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ['src', 'tests', 'scenarios', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props',
           'DK2.slnx', 'global.json', '.editorconfig']
HARNESS = ROOT / 'src/DK2.Harness/bin/Release/net10.0'


def run(*command, **kwargs):
    return subprocess.run(command, cwd=ROOT, check=True, capture_output=True, **kwargs)


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1 << 20), b''):
            digest.update(block)
    return digest.hexdigest()


def source_lines():
    listed = run('git', 'ls-files', '-co', '--exclude-standard', '-z', '--', *SOURCES).stdout.decode('utf-8').split('\0')
    names = sorted({name for name in listed if name and (ROOT / name).is_file()})
    return ''.join(f'{sha(ROOT / name)}  {name}\n' for name in names)


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    out = Path(sys.argv[1]).resolve()
    if out.exists():
        raise SystemExit(f'{out} exists; a receipt is always written to a new directory')
    # Everything git can fail on comes before the directory exists, so a failure leaves nothing behind.
    head = run('git', 'rev-parse', 'HEAD').stdout
    status = run('git', 'status', '--porcelain=v1').stdout
    patch = run('git', 'diff', 'HEAD', '--binary').stdout
    out.mkdir(parents=True)

    def write(name, data):
        with (out / name).open('xb') as stream:
            stream.write(data if isinstance(data, bytes) else data.encode('utf-8'))

    write('head.txt', head)
    write('status.txt', status)
    write('tracked.patch', patch)
    write('tracked.patch.sha256', f'{hashlib.sha256(patch).hexdigest()}  tracked.patch\n')
    before = source_lines()
    write('source.sha256', before)
    version = run('dotnet', '--version').stdout.decode().strip()
    write('environment.txt', f'dotnet: {version}\nutc: {time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}\n')

    build = subprocess.run(['dotnet', 'build', 'DK2.slnx', '-c', 'Release', '--no-incremental'], cwd=ROOT,
                           capture_output=True)
    write('build.log', build.stdout + build.stderr)
    if build.returncode != 0:
        raise SystemExit(f'Build failed (exit {build.returncode}); see {out / "build.log"}. No output.sha256 written.')
    if source_lines() != before:
        raise SystemExit('The source changed during the build; this receipt describes no single tree. No output.sha256 written.')

    # Every file beside the harness but symbols: assemblies, deps.json and runtimeconfig.json decide what loads.
    outputs = sorted(p for p in HARNESS.iterdir() if p.is_file() and p.suffix.lower() != '.pdb')
    if not any(p.name == 'DK2.Harness.dll' for p in outputs):
        raise SystemExit(f'No DK2.Harness.dll under {HARNESS}')
    write('output.sha256', ''.join(f'{sha(p)}  {p.name}\n' for p in outputs))
    print(f'Receipt written: {out} ({len(before.splitlines())} source files, {len(outputs)} output files)')


if __name__ == '__main__':
    main()

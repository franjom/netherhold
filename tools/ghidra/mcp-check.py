# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=1.26,<2"]
# ///
"""Does the DKII.EXE MCP work for this agent? Answer in about ten seconds.

    uv run tools/ghidra/mcp-check.py claude
    uv run tools/ghidra/mcp-check.py codex

Spawns tools/ghidra/mcp-serve.py over stdio exactly as Claude Code or Codex would,
times the initialize handshake (the client's start-up timeout must exceed it),
lists the tools, and asks for the xrefs and the decompilation of the world-log
dumper at 0x0050EA70. Prints PASS or FAIL and exits accordingly. The server's
own log goes to reverse-engineering/.cache/mcp-check-<agent>.log.
"""
import asyncio
import os
import sys
import time

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
PROBE = '0x0050EA70'
STARTUP_BUDGET_SEC = 25  # Claude Code's default MCP start-up timeout is 30 s


def text_of(result):
    return '\n'.join(c.text for c in result.content if getattr(c, 'text', None))


async def check(agent):
    log_dir = os.path.join(REPO, 'reverse-engineering', '.cache')
    os.makedirs(log_dir, exist_ok=True)
    errlog = open(os.path.join(log_dir, 'mcp-check-%s.log' % agent), 'w')
    # Every session serves its own clone, <root>/<agent>-<pid>; the check
    # uses a sibling root so its clones and sweep never touch a live session's.
    env = dict(os.environ, DK2_MCP_ROOT=os.environ.get('DK2_MCP_CHECK_ROOT', 'D:/Ghidra/mcp/check'))
    params = StdioServerParameters(command=sys.executable,
                                   args=[os.path.join(HERE, 'mcp-serve.py'), '--agent', agent],
                                   env=env)
    failures = []
    t0 = time.time()
    async with stdio_client(params, errlog=errlog) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            startup = time.time() - t0
            print('initialize: %.1f s' % startup)
            if startup > STARTUP_BUDGET_SEC:
                failures.append('start-up took %.1f s, over the %d s budget' % (startup, STARTUP_BUDGET_SEC))

            tools = {t.name for t in (await session.list_tools()).tools}
            print('tools: %d' % len(tools))
            for needed in ('decompile_function', 'list_xrefs', 'disassemble', 'search_symbols_by_name'):
                if needed not in tools:
                    failures.append('tool %s is missing' % needed)

            binaries = text_of(await session.call_tool('list_project_binaries', {}))
            if 'DKII.EXE' not in binaries:
                failures.append('DKII.EXE is not in the project copy')

            xrefs = text_of(await session.call_tool('list_xrefs', {'binary_name': 'DKII.EXE', 'name_or_address': PROBE}))
            print('xrefs of %s: %d chars' % (PROBE, len(xrefs)))
            if '"error": null' not in xrefs and 'xref' not in xrefs.lower():
                failures.append('list_xrefs did not answer for %s' % PROBE)

            decomp = text_of(await session.call_tool('decompile_function', {'binary_name': 'DKII.EXE', 'name_or_address': PROBE}))
            print('decompile of %s: %d chars' % (PROBE, len(decomp)))
            if 'Logging_World' not in decomp:
                failures.append('decompile of %s does not show the world-log strings' % PROBE)
    print('total: %.1f s' % (time.time() - t0))
    left = clones_left(env['DK2_MCP_ROOT'], agent)
    if left:
        failures.append('clone(s) left behind under %s: %s' % (env['DK2_MCP_ROOT'], ', '.join(left)))
    return failures


def clones_left(root, agent, budget_sec=90):
    """Wait for the session's clone to go: the reaper deletes it, or, when the
    client's job killed the reaper too, a sweep does once the processes are gone."""
    import importlib.util
    sys.dont_write_bytecode = True  # no __pycache__ in tools/
    spec = importlib.util.spec_from_file_location('dk2_ghidra_runner', os.path.join(HERE, 'ghidra.py'))
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    deadline = time.time() + budget_sec
    while True:
        left = sorted(n for n in os.listdir(root) if n.startswith(agent + '-')) if os.path.isdir(root) else []
        if not left or time.time() > deadline:
            if not left:
                print('clone deleted after %.1f s' % (time.time() - deadline + budget_sec))
            return left
        runner.sweep_stale_clones(root)
        time.sleep(1)


def main():
    agent = sys.argv[1] if len(sys.argv) > 1 else 'claude'
    failures = asyncio.run(check(agent))
    if failures:
        print('FAIL (%s):' % agent)
        for f in failures:
            print('  - ' + f)
        return 1
    print('PASS (%s)' % agent)
    return 0


if __name__ == '__main__':
    sys.exit(main())

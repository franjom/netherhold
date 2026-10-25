# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

"""Exercise context-mode over Docker MCP stdio, including a gateway restart."""

import argparse
from collections import deque
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time
import uuid


class Gateway:
    def __init__(self, profile, project):
        env = os.environ.copy()
        roots = env.get("MCP_GATEWAY_DOCKER_BIND_ALLOWED_PATHS", "")
        env["MCP_GATEWAY_DOCKER_BIND_ALLOWED_PATHS"] = os.pathsep.join(
            root for root in (roots, str(project)) if root
        )
        self.process = subprocess.Popen(
            ["docker", "mcp", "gateway", "run", "--profile", profile],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding="utf-8", errors="replace", bufsize=1, env=env,
        )
        self.responses = queue.Queue()
        self.diagnostics = deque(maxlen=20)
        self.next_id = 0
        threading.Thread(target=self.read_stdout, daemon=True).start()
        threading.Thread(target=self.read_stderr, daemon=True).start()

    def read_stdout(self):
        for line in self.process.stdout:
            try:
                self.responses.put(json.loads(line))
            except json.JSONDecodeError:
                self.diagnostics.append(line.rstrip())
        self.responses.put(None)

    def read_stderr(self):
        for line in self.process.stderr:
            self.diagnostics.append(line.rstrip())

    def send(self, message):
        self.process.stdin.write(json.dumps({"jsonrpc": "2.0", **message}) + "\n")
        self.process.stdin.flush()

    def request(self, method, params):
        self.next_id += 1
        request_id = self.next_id
        self.send({"id": request_id, "method": method, "params": params})
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            try:
                response = self.responses.get(timeout=max(0.01, deadline - time.monotonic()))
            except queue.Empty:
                break
            if response is None:
                break
            if response.get("id") != request_id:
                continue
            if "error" in response:
                raise RuntimeError(str(response["error"]))
            return response["result"]
        raise RuntimeError(f"No response to {method}: " + "\n".join(self.diagnostics))

    def initialize(self):
        self.request("initialize", {
            "protocolVersion": "2024-11-05", "capabilities": {},
            "clientInfo": {"name": "context-mode-docker-smoke", "version": "1"},
        })
        self.send({"method": "notifications/initialized"})
        names = set()
        params = {}
        while True:
            result = self.request("tools/list", params)
            names.update(tool["name"] for tool in result["tools"])
            if not result.get("nextCursor"):
                break
            params = {"cursor": result["nextCursor"]}
        return names

    def call(self, name, arguments, expected=None):
        result = self.request("tools/call", {"name": name, "arguments": arguments})
        text = "\n".join(c.get("text", "") for c in result.get("content", []))
        if result.get("isError") or (expected is not None and expected not in text):
            raise RuntimeError(f"{name} failed: {text[:2000]}")
        print(f"PASS {name}", flush=True)
        return text

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            self.process.terminate()
            self.process.wait(timeout=10)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", default="local")
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    marker = "docker-context-marker-" + uuid.uuid4().hex
    gateway = Gateway(args.profile, args.project.resolve())
    try:
        names = gateway.initialize()
        expected = {
            "ctx_execute", "ctx_execute_file", "ctx_batch_execute", "ctx_index",
            "ctx_search", "ctx_fetch_and_index", "ctx_stats",
        }
        missing = expected - names
        unexpected = {name for name in names if name.startswith('ctx_')} - expected
        if missing or unexpected:
            raise RuntimeError(f"Missing tools: {sorted(missing)}; extra tools: {sorted(unexpected)}\n" +
                               "\n".join(gateway.diagnostics))
        print("PASS tool discovery", flush=True)
        for language, code in [
            ("javascript", "console.log('javascript-ready')"),
            ("python", "print('python-ready')"),
            ("shell", "printf 'shell-ready\\n'"),
        ]:
            gateway.call("ctx_execute", {"language": language, "code": code},
                         language + "-ready")
        gateway.call("ctx_execute_file", {
            "path": "/workspace/README.md", "language": "javascript",
            "code": "console.log(FILE_CONTENT.length > 0 ? 'file-readable' : 'empty')",
        }, "file-readable")
        gateway.call("ctx_execute", {
            "language": "python",
            "code": "import os, errno\ntry:\n fd = os.open('/workspace/README.md', os.O_WRONLY)\n os.close(fd)\n print('unexpected-writable')\nexcept OSError as e:\n print('read-only-confirmed' if e.errno in (errno.EROFS, errno.EACCES) else str(e))",
        }, "read-only-confirmed")
        gateway.call("ctx_batch_execute", {
            "commands": [{"label": "batch-marker", "command": "printf 'batch-marker-ready\\n'"}],
            "queries": ["batch-marker-ready"],
        }, "batch-marker-ready")
        gateway.call("ctx_index", {"content": "# Docker persistence\n\n" + marker,
                                   "source": marker})
        gateway.call("ctx_search", {"queries": [marker], "source": marker}, marker)
        gateway.call("ctx_fetch_and_index", {
            "url": "https://example.com", "source": "docker-smoke-example", "force": True,
        }, "Example Domain")
        gateway.call("ctx_stats", {})
    finally:
        gateway.close()
    gateway = Gateway(args.profile, args.project.resolve())
    try:
        gateway.initialize()
        gateway.call("ctx_search", {"queries": [marker], "source": marker}, marker)
        print("PASS persistent search after gateway restart", flush=True)
    finally:
        gateway.close()


if __name__ == "__main__":
    main()

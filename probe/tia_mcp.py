#!/usr/bin/env python3
"""Minimal stdio client for the TIA MCP Host.

The TIA MCP Host speaks newline-delimited JSON-RPC 2.0 on stdin/stdout
(ModelContextProtocol stdio transport).  This module starts a private Host,
initializes the session, calls one tool at a time, and shuts the Host down.
It is used by ``tia_snapshot.py``; it can also be imported by other lab tools.

The Host path is not hard-coded: pass --bundle / TIA_MCP_BUNDLE or set the
``bundle`` argument.  ``--no-native-workers`` is used for offline review tools
such as ``project_snapshot_compare``.
"""
from __future__ import annotations

import json
import os
import queue
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Any

DEFAULT_BUNDLE = os.environ.get(
    "TIA_MCP_BUNDLE",
    r"C:\TiaMcp\releases\tia-mcp-v21-0.9.0_20261112_122441_bb02d6cf",
)
DEFAULT_ARTIFACTS = os.environ.get(
    "TIA_MCP_ARTIFACTS",
    r"C:\Programs\04_Programme\lab\tia-mcp-artifacts",
)
PROTOCOL_VERSION = "2025-06-18"


class McpError(RuntimeError):
    """Protocol, transport or tool error."""


class McpToolError(McpError):
    """The Host returned a tool result with isError=true."""

    def __init__(self, message: str, payload: Any = None):
        super().__init__(message)
        self.payload = payload


class McpHost:
    def __init__(
        self,
        bundle: str = DEFAULT_BUNDLE,
        artifacts: str = DEFAULT_ARTIFACTS,
        native: bool = True,
        timeout_seconds: int = 600,
        command: list[str] | None = None,
        log_path: str | None = None,
    ):
        self.bundle = Path(bundle)
        self.artifacts = Path(artifacts)
        self.native = native
        self.timeout_seconds = int(timeout_seconds)
        self.command = command
        self.log_path = Path(log_path) if log_path else None
        self.proc: subprocess.Popen | None = None
        self._queue: queue.Queue = queue.Queue()
        self._stderr_tail: list[str] = []
        self._next_id = 0
        self._stderr_file = None

    # -- lifecycle ---------------------------------------------------------
    def start(self) -> None:
        if self.command is None:
            host = self.bundle / "host" / "TiaMcp.Host.exe"
            worker = self.bundle / "workers" / "openness" / "TiaMcp.Openness.V21.exe"
            sim = self.bundle / "workers" / "simulator" / "TiaMcp.Simulator.V8.exe"
            for path in (host, worker, sim):
                if not path.is_file():
                    raise McpError(f"missing TIA MCP file: {path}")
            cmd = [
                str(host),
                "--worker", str(worker),
                "--sim-worker", str(sim),
                "--artifacts", str(self.artifacts),
                "--timeout-seconds", str(self.timeout_seconds),
            ]
            if not self.native:
                cmd.append("--no-native-workers")
            self.command = cmd

        if self.log_path:
            self.log_path.parent.mkdir(parents=True, exist_ok=True)
            self._stderr_file = open(self.log_path, "a", encoding="utf-8")
        self.proc = subprocess.Popen(
            self.command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
            cwd=str(self.bundle) if self.bundle.is_dir() else None,
        )
        threading.Thread(target=self._read_stdout, daemon=True).start()
        threading.Thread(target=self._read_stderr, daemon=True).start()

        self._request("initialize", {
            "protocolVersion": PROTOCOL_VERSION,
            "capabilities": {},
            "clientInfo": {"name": "siemens-re-lab", "version": "0.1"},
        }, timeout=60)
        self._notify("notifications/initialized")

    def _read_stdout(self) -> None:
        assert self.proc and self.proc.stdout
        for line in self.proc.stdout:
            self._queue.put(line)

    def _read_stderr(self) -> None:
        assert self.proc and self.proc.stderr
        for line in self.proc.stderr:
            text = line.rstrip("\r\n")
            self._stderr_tail.append(text)
            if len(self._stderr_tail) > 200:
                del self._stderr_tail[:100]
            if self._stderr_file:
                self._stderr_file.write(text + "\n")
                self._stderr_file.flush()

    def close(self) -> None:
        if self.proc is None:
            return
        try:
            if self.proc.stdin:
                self.proc.stdin.close()
        except Exception:
            pass
        try:
            self.proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            try:
                self.proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                pass
        for stream in (self.proc.stdin, self.proc.stdout, self.proc.stderr):
            try:
                if stream:
                    stream.close()
            except Exception:
                pass
        if self._stderr_file:
            self._stderr_file.close()
        self.proc = None

    def __enter__(self):
        self.start()
        return self

    def __exit__(self, *_exc):
        self.close()

    # -- JSON-RPC ----------------------------------------------------------
    def _send(self, message: dict) -> None:
        if self.proc is None or self.proc.stdin is None:
            raise McpError("Host is not running")
        self.proc.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
        self.proc.stdin.flush()

    def _notify(self, method: str, params: dict | None = None) -> None:
        message = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            message["params"] = params
        self._send(message)

    def _request(self, method: str, params: dict | None,
                 timeout: float = 600) -> dict:
        self._next_id += 1
        request_id = self._next_id
        message = {"jsonrpc": "2.0", "id": request_id, "method": method}
        if params is not None:
            message["params"] = params
        self._send(message)

        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                line = self._queue.get(timeout=0.5)
            except queue.Empty:
                continue
            try:
                response = json.loads(line)
            except json.JSONDecodeError:
                continue
            if response.get("id") != request_id:
                continue
            if "error" in response:
                error = response["error"]
                raise McpError(
                    f"Host error {error.get('code')}: {error.get('message')}")
            return response
        tail = "\n".join(self._stderr_tail[-20:])
        raise McpError(
            f"timeout waiting for {method} after {timeout:.0f}s; "
            f"last Host output:\n{tail}")

    def call_tool(self, name: str, arguments: dict | None = None,
                  timeout: float = 600) -> Any:
        response = self._request(
            "tools/call", {"name": name, "arguments": arguments or {}},
            timeout=timeout)
        result = response.get("result") or {}
        if result.get("isError"):
            text = self._content_text(result)
            raise McpToolError(f"{name} failed: {text}", result)
        if "structuredContent" in result:
            return result["structuredContent"]
        return self._parse_text(self._content_text(result))

    def list_tools(self, timeout: float = 60) -> list[dict]:
        response = self._request("tools/list", {}, timeout=timeout)
        return (response.get("result") or {}).get("tools") or []

    @staticmethod
    def _content_text(result: dict) -> str:
        parts = []
        for item in result.get("content") or []:
            if item.get("type") == "text":
                parts.append(item.get("text", ""))
        return "\n".join(parts)

    @staticmethod
    def _parse_text(text: str) -> Any:
        text = text.strip()
        if not text:
            return None
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return {"text": text}


def main(argv=None) -> int:
    import argparse

    ap = argparse.ArgumentParser(description="Call one TIA MCP tool over stdio")
    ap.add_argument("tool")
    ap.add_argument("--arguments", default="{}",
                    help="JSON object for the tool arguments")
    ap.add_argument("--bundle", default=DEFAULT_BUNDLE)
    ap.add_argument("--artifacts", default=DEFAULT_ARTIFACTS)
    ap.add_argument("--native", action="store_true",
                    help="start the native worker (default: offline)")
    ap.add_argument("--timeout", type=float, default=600)
    ap.add_argument("--log", help="append Host stderr to this file")
    a = ap.parse_args(argv)

    arguments = json.loads(a.arguments)
    with McpHost(a.bundle, a.artifacts, native=a.native,
                 timeout_seconds=int(a.timeout), log_path=a.log) as host:
        result = host.call_tool(a.tool, arguments, timeout=a.timeout)
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())

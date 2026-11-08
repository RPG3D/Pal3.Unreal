#!/usr/bin/env python3
"""unity_mcp.py - minimal stdio MCP client for Unity's relay (relay_win.exe --mcp).

ZCode has this server configured, but the tools are not exposed in this session,
so this script speaks JSON-RPC to it directly:

    python unity_mcp.py list
    python unity_mcp.py call <tool> '<json-args>'
"""
import json
import subprocess
import sys
import time

EXE = r"C:\Users\imxql\.unity\relay\relay_win.exe"


class Relay:
    def __init__(self):
        self.p = subprocess.Popen([EXE, "--mcp"], stdin=subprocess.PIPE,
                                  stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                  bufsize=0)
        self.next_id = 1
        self.initialize()

    def _send(self, obj):
        self.p.stdin.write(json.dumps(obj).encode() + b"\n")
        self.p.stdin.flush()

    def _read(self, timeout=15):
        deadline = time.time() + timeout
        buf = b""
        while time.time() < deadline:
            ch = self.p.stdout.read(1)
            if not ch:
                time.sleep(0.02)
                continue
            if ch == b"\n":
                return json.loads(buf.decode(errors="ignore")) if buf else None
            buf += ch
        return None

    def initialize(self):
        self._send({"jsonrpc": "2.0", "id": self.next_id, "method": "initialize",
                    "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                               "clientInfo": {"name": "zcode", "version": "1.0"}}})
        self.next_id += 1
        self._read()
        self._send({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}})

    def request(self, method, params, timeout=20):
        rid = self.next_id
        self.next_id += 1
        self._send({"jsonrpc": "2.0", "id": rid, "method": method, "params": params})
        deadline = time.time() + timeout
        while time.time() < deadline:
            msg = self._read(timeout - (time.time() - deadline + timeout))
            if msg and msg.get("id") == rid:
                return msg
        return None

    def close(self):
        try:
            self.p.kill()
        except Exception:
            pass


def main(argv):
    relay = Relay()
    try:
        if len(argv) < 2 or argv[1] == "list":
            resp = relay.request("tools/list", {})
            tools = (resp or {}).get("result", {}).get("tools", [])
            for tool in tools:
                print(f"{tool['name']}: {tool.get('description','')[:120]}")
            print(f"-- {len(tools)} tools")
            return 0

        if argv[1] == "call":
            name = argv[2]
            args = json.loads(argv[3]) if len(argv) > 3 else {}
            resp = relay.request("tools/call", {"name": name, "arguments": args})
            out = json.dumps(resp, indent=2, ensure_ascii=False)
            sys.stdout.buffer.write(out.encode("utf-8", errors="replace"))
            return 0
    finally:
        relay.close()
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))

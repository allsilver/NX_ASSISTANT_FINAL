from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent
SERVER = ROOT / "nx_mcp_server.py"

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except AttributeError:
    pass


def send(proc: subprocess.Popen[str], request: dict) -> dict:
    assert proc.stdin is not None
    assert proc.stdout is not None
    proc.stdin.write(json.dumps(request, ensure_ascii=False) + "\n")
    proc.stdin.flush()
    line = proc.stdout.readline()
    if not line:
        raise RuntimeError("MCP server closed stdout")
    return json.loads(line)


def call_tool(proc: subprocess.Popen[str], request_id: int, name: str, arguments: dict | None = None) -> dict:
    response = send(
        proc,
        {
            "jsonrpc": "2.0",
            "id": request_id,
            "method": "tools/call",
            "params": {"name": name, "arguments": arguments or {}},
        },
    )
    result = response.get("result", {})
    if result.get("isError"):
        raise RuntimeError(result["content"][0]["text"])
    return result.get("structuredContent") or {}


def main() -> None:
    action = sys.argv[1] if len(sys.argv) > 1 else "status"
    proc = subprocess.Popen(
        [sys.executable, str(SERVER)],
        cwd=str(ROOT),
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    try:
        send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2025-06-18",
                    "capabilities": {},
                    "clientInfo": {"name": "codex-bridge-client", "version": "0.1"},
                },
            },
        )

        if action == "info":
            result = call_tool(proc, 2, "nx_bridge_info")
        elif action == "status":
            result = call_tool(proc, 2, "nx_bridge_status")
        elif action == "read":
            result = call_tool(proc, 2, "nx_bridge_read_response")
        elif action == "sketch":
            sketch_name = sys.argv[2] if len(sys.argv) > 2 else "MCP Bridge Sketch"
            width = float(sys.argv[3]) if len(sys.argv) > 3 else 50.0
            height = float(sys.argv[4]) if len(sys.argv) > 4 else width
            result = call_tool(
                proc,
                2,
                "nx_bridge_create_basic_sketch",
                {
                    "sketch_name": sketch_name,
                    "width_mm": width,
                    "height_mm": height,
                },
            )
        elif action == "stop":
            result = call_tool(proc, 2, "nx_bridge_stop")
        else:
            raise SystemExit("Usage: bridge_client_via_mcp.py [info|status|sketch|stop] [name] [width] [height]")

        print(json.dumps(result, ensure_ascii=False, indent=2))
    finally:
        proc.kill()


if __name__ == "__main__":
    main()

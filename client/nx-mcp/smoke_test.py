from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent
SERVER = ROOT / "nx_mcp_server.py"


def send(proc: subprocess.Popen[str], request: dict) -> dict:
    assert proc.stdin is not None
    assert proc.stdout is not None
    proc.stdin.write(json.dumps(request, ensure_ascii=False) + "\n")
    proc.stdin.flush()
    line = proc.stdout.readline()
    if not line:
        raise RuntimeError("server closed stdout")
    return json.loads(line)


def main() -> None:
    proc = subprocess.Popen(
        [sys.executable, str(SERVER)],
        cwd=str(ROOT),
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
    )
    try:
        init = send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2025-06-18",
                    "capabilities": {},
                    "clientInfo": {"name": "smoke-test", "version": "0.1"},
                },
            },
        )
        assert init["result"]["serverInfo"]["name"] == "nx-local-mcp-demo", init

        tools = send(proc, {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}})
        names = {tool["name"] for tool in tools["result"]["tools"]}
        assert {
            "nx_status",
            "nx_create_journal",
            "nx_list_journals",
            "nx_run_journal",
            "nx_bridge_info",
            "nx_bridge_status",
            "nx_bridge_create_basic_sketch",
            "nx_remoting_info",
            "nx_remoting_status",
            "nx_remoting_list_bodies",
            "nx_remoting_list_features",
            "nx_remoting_analyze_bodies",
            "nx_remoting_validate_body_dimensions",
            "nx_remoting_color_thinnest_wall_face",
            "nx_remoting_create_basic_sketch",
            "nx_remoting_create_rectangle_curves",
            "nx_remoting_create_line_curve",
            "nx_remoting_create_circle_curve",
            "nx_remoting_create_reference_cross",
            "nx_remoting_create_box_body",
            "nx_remoting_create_extruded_rectangle",
            "nx_remoting_create_hinge_housing_section",
        } <= names

        created = send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 3,
                "method": "tools/call",
                "params": {
                    "name": "nx_create_journal",
                    "arguments": {
                        "template": "report_work_part",
                        "requester": "smoke",
                        "note": "smoke test",
                    },
                },
            },
        )
        payload = created["result"]["structuredContent"]
        assert Path(payload["journal_path"]).exists(), payload

        dry_run = send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 4,
                "method": "tools/call",
                "params": {
                    "name": "nx_run_journal",
                    "arguments": {
                        "journal_path": payload["journal_path"],
                        "execute": False,
                    },
                },
            },
        )
        assert dry_run["result"]["structuredContent"]["execute"] is False, dry_run

        sketch = send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 5,
                "method": "tools/call",
                "params": {
                    "name": "nx_create_journal",
                    "arguments": {
                        "template": "create_basic_sketch",
                        "requester": "smoke",
                        "note": "sketch smoke test",
                        "parameters": {
                            "sketch_name": "MCP Smoke Sketch",
                            "size_mm": "25",
                        },
                    },
                },
            },
        )
        sketch_payload = sketch["result"]["structuredContent"]
        assert Path(sketch_payload["journal_path"]).exists(), sketch_payload

        print("NX MCP smoke tests passed")
    finally:
        proc.kill()


if __name__ == "__main__":
    main()

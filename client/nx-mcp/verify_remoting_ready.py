from __future__ import annotations

import argparse
import json
import socket
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent
SERVER_DLL = ROOT / "remoting_bridge" / "bin" / "NxMcpSessionServer.dll"
CLIENT_EXE = ROOT / "remoting_bridge" / "bin" / "NxMcpSessionClient.exe"
MCP_HELPER = ROOT / "remoting_client_via_mcp.py"
PORT = 8792


try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except AttributeError:
    pass


def port_open() -> bool:
    try:
        with socket.create_connection(("127.0.0.1", PORT), timeout=0.7):
            return True
    except OSError:
        return False


def run_helper(args: list[str]) -> dict:
    completed = subprocess.run(
        [sys.executable, str(MCP_HELPER), *args],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
    )
    text = (completed.stdout or "").strip()
    if not text:
        return {
            "ok": False,
            "error": (completed.stderr or "No output").strip(),
            "returncode": completed.returncode,
        }
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return {
            "ok": False,
            "raw_stdout": text,
            "stderr": completed.stderr,
            "returncode": completed.returncode,
        }


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify the local NX Remoting MCP demo.")
    parser.add_argument("--sketch", action="store_true", help="Create a test sketch after status succeeds.")
    parser.add_argument("--curves", action="store_true", help="Create rectangle curves after status succeeds.")
    parser.add_argument("--box", action="store_true", help="Create a test 3D box body after status succeeds.")
    parser.add_argument("--extrude", action="store_true", help="Create a test extruded rectangle after status succeeds.")
    parser.add_argument("--analyze", action="store_true", help="Read body bbox/size/count information after status succeeds.")
    parser.add_argument("--hinge-section", action="store_true", help="Create a MEG-style hinge housing section after status succeeds.")
    parser.add_argument("--name", default="MCP Ready Check Rectangle")
    parser.add_argument("--width", default="60")
    parser.add_argument("--height", default="40")
    args = parser.parse_args()

    print("NX Remoting MCP readiness check")
    print(f"server_dll: {SERVER_DLL}")
    print(f"client_exe: {CLIENT_EXE}")
    print(f"server_dll_exists: {SERVER_DLL.exists()}")
    print(f"client_exe_exists: {CLIENT_EXE.exists()}")
    print(f"port_127_0_0_1_{PORT}_open: {port_open()}")

    if not SERVER_DLL.exists() or not CLIENT_EXE.exists():
        print("Build artifacts are missing. Run remoting_bridge\\build.ps1 first.")
        return 2

    if not port_open():
        print("NX bridge is not loaded yet.")
        print("In NX: Ctrl+U -> select the server_dll path above -> retry this script.")
        return 1

    status = run_helper(["status"])
    print(json.dumps({"status": status}, ensure_ascii=False, indent=2))
    if not status.get("ok"):
        return 3

    if args.sketch:
        sketch = run_helper(["sketch", args.name, str(args.width), str(args.height)])
        print(json.dumps({"sketch": sketch}, ensure_ascii=False, indent=2))
        if not sketch.get("ok"):
            return 4

    if args.curves:
        curves = run_helper(["curves", args.name, str(args.width), str(args.height)])
        print(json.dumps({"curves": curves}, ensure_ascii=False, indent=2))
        if not curves.get("ok"):
            return 5

    if args.box:
        box = run_helper(["box", "MCP Ready Check Box", "0", "0", "0", "30", "20", "8"])
        print(json.dumps({"box": box}, ensure_ascii=False, indent=2))
        if not box.get("ok"):
            return 6

    if args.extrude:
        extrude = run_helper(["extrude-rectangle", "MCP Ready Check Extrude", "0", "0", "0", "30", "20", "8"])
        print(json.dumps({"extrude": extrude}, ensure_ascii=False, indent=2))
        if not extrude.get("ok"):
            return 7

    if args.analyze:
        analysis = run_helper(["analyze"])
        print(json.dumps({"analysis": analysis}, ensure_ascii=False, indent=2))
        if not analysis.get("ok"):
            return 8

    if args.hinge_section:
        hinge = run_helper(["hinge-section", "MEG Hinge Housing Section POC", "80", "12", "0.38", "0.50", "0.40"])
        print(json.dumps({"hinge_section": hinge}, ensure_ascii=False, indent=2))
        if not hinge.get("ok"):
            return 9

    print("Ready.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

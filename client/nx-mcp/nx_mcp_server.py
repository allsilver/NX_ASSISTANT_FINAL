from __future__ import annotations

import json
import os
import re
import socket
import subprocess
import sys
from datetime import datetime
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent
WORKSPACE = Path(os.getenv("NX_MCP_WORKSPACE", ROOT / "workspace")).resolve()
JOURNAL_DIR = (WORKSPACE / "generated_journals").resolve()
REMOTING_BIN = (ROOT / "remoting_bridge" / "bin").resolve()
REMOTING_PORT = 8792
REMOTING_SERVER_DLL = (REMOTING_BIN / "NxMcpSessionServer.dll").resolve()
DEFAULT_REMOTING_CLIENT_EXE = (REMOTING_BIN / "NxMcpSessionClient.exe").resolve()
LATEST_REMOTING_CLIENT_EXE = (REMOTING_BIN / "NxMcpSessionClientSection.exe").resolve()
REMOTING_CLIENT_EXE = Path(
    os.getenv(
        "NX_MCP_REMOTING_CLIENT_EXE",
        LATEST_REMOTING_CLIENT_EXE if LATEST_REMOTING_CLIENT_EXE.exists() else DEFAULT_REMOTING_CLIENT_EXE,
    )
).resolve()
BRIDGE_JOURNAL_PATH = (ROOT / "nx_bridge_journal.py").resolve()
BRIDGE_TOKEN_PATH = (WORKSPACE / "nx_bridge_token.txt").resolve()
BRIDGE_COMMAND_PATH = (WORKSPACE / "nx_bridge_command.json").resolve()
BRIDGE_RESPONSE_PATH = (WORKSPACE / "nx_bridge_response.json").resolve()
ALLOW_EXECUTE = os.getenv("NX_MCP_ALLOW_EXECUTE", "0") == "1"
RUN_JOURNAL_COMMAND = os.getenv("NX_MCP_RUN_JOURNAL_COMMAND", "")
PROTOCOL_VERSION = "2025-06-18"

try:
    sys.stdin.reconfigure(encoding="utf-8", errors="replace")
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except AttributeError:
    pass


class ToolError(Exception):
    pass


def log(message: str) -> None:
    print(f"[nx-mcp] {message}", file=sys.stderr, flush=True)


def json_response(request_id: Any, result: Any) -> dict[str, Any]:
    return {"jsonrpc": "2.0", "id": request_id, "result": result}


def json_error(request_id: Any, code: int, message: str) -> dict[str, Any]:
    return {"jsonrpc": "2.0", "id": request_id, "error": {"code": code, "message": message}}


def send(message: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def safe_slug(text: str) -> str:
    slug = re.sub(r"[^A-Za-z0-9_.-]+", "_", text.strip())[:80].strip("._")
    return slug or "journal"


def ensure_workspace() -> None:
    JOURNAL_DIR.mkdir(parents=True, exist_ok=True)


def nx_env_summary() -> dict[str, Any]:
    interesting = [
        "UGII_BASE_DIR",
        "UGII_ROOT_DIR",
        "UGII_USER_DIR",
        "UGII_SITE_DIR",
        "NXBIN",
        "PATH",
    ]
    env = {key: os.getenv(key, "") for key in interesting if os.getenv(key)}
    return {
        "workspace": str(WORKSPACE),
        "journal_dir": str(JOURNAL_DIR),
        "remoting_server_dll": str(REMOTING_SERVER_DLL),
        "remoting_client_exe": str(REMOTING_CLIENT_EXE),
        "remoting_port": REMOTING_PORT,
        "remoting_built": REMOTING_SERVER_DLL.exists() and REMOTING_CLIENT_EXE.exists(),
        "bridge_journal_path": str(BRIDGE_JOURNAL_PATH),
        "bridge_token_path": str(BRIDGE_TOKEN_PATH),
        "bridge_command_path": str(BRIDGE_COMMAND_PATH),
        "bridge_response_path": str(BRIDGE_RESPONSE_PATH),
        "bridge_token_exists": BRIDGE_TOKEN_PATH.exists(),
        "allow_execute": ALLOW_EXECUTE,
        "has_run_journal_command": bool(RUN_JOURNAL_COMMAND),
        "nx_environment": {key: value for key, value in env.items() if key != "PATH"},
        "path_mentions_nx": "nx" in os.getenv("PATH", "").lower()
        or "ugii" in os.getenv("PATH", "").lower(),
    }


def is_remoting_port_open() -> bool:
    try:
        with socket.create_connection(("127.0.0.1", REMOTING_PORT), timeout=0.5):
            return True
    except OSError:
        return False


def run_remoting_client(args: list[str]) -> dict[str, Any]:
    if not REMOTING_CLIENT_EXE.exists():
        raise ToolError(
            f"Remoting client not built: {REMOTING_CLIENT_EXE}. "
            "Run remoting_bridge\\build.ps1 first."
        )
    if not is_remoting_port_open():
        return {
            "ok": False,
            "error": (
                f"NX Remoting bridge is not reachable on 127.0.0.1:{REMOTING_PORT}. "
                "Load NxMcpSessionServer.dll inside NX first."
            ),
            "server_dll": str(REMOTING_SERVER_DLL),
            "next_step": "In NX, press Ctrl+U and select the server_dll path, then retry status.",
        }
    nx_base_dir = os.getenv("NX_MCP_NX_BASE_DIR", r"C:\SCAD\NX2406")
    env = os.environ.copy()
    env["UGII_BASE_DIR"] = nx_base_dir
    env.setdefault("DISPLAY", "LOCALPC:0.0")
    nxbin = str(Path(nx_base_dir) / "nxbin")
    ugii = str(Path(nx_base_dir) / "ugii")
    env["PATH"] = nxbin + os.pathsep + ugii + os.pathsep + env.get("PATH", "")
    timeout_seconds = int(os.getenv("NX_MCP_REMOTING_TIMEOUT_SEC", "180"))
    try:
        completed = subprocess.run(
            [str(REMOTING_CLIENT_EXE), *args],
            cwd=str(REMOTING_BIN),
            env=env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout_seconds,
        )
    except subprocess.TimeoutExpired as exc:
        return {
            "ok": False,
            "error": f"Remoting client timed out after {timeout_seconds} seconds.",
            "args": args,
            "partial_stdout": (exc.stdout or "").strip() if isinstance(exc.stdout, str) else "",
            "partial_stderr": (exc.stderr or "").strip() if isinstance(exc.stderr, str) else "",
            "hint": "Large face-count wall-thickness checks can take longer. Increase NX_MCP_REMOTING_TIMEOUT_SEC or target a smaller body.",
        }
    output = (completed.stdout or "").strip()
    if not output:
        output = json.dumps(
            {
                "ok": False,
                "error": completed.stderr.strip() or "No output from remoting client",
                "returncode": completed.returncode,
            }
        )
    try:
        payload = json.loads(output)
    except json.JSONDecodeError:
        payload = {
            "ok": False,
            "raw_stdout": output,
            "stderr": completed.stderr,
            "returncode": completed.returncode,
        }
    if completed.returncode != 0 and payload.get("ok") is not True:
        payload.setdefault("returncode", completed.returncode)
    return payload


def read_bridge_token() -> str:
    if not BRIDGE_TOKEN_PATH.exists():
        raise ToolError(
            "Bridge token file was not found. Run nx_bridge_journal.py inside NX first."
        )
    token = BRIDGE_TOKEN_PATH.read_text(encoding="utf-8").strip()
    if not token:
        raise ToolError("Bridge token file is empty. Restart the NX bridge journal.")
    return token


def write_bridge_command(command: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
    WORKSPACE.mkdir(parents=True, exist_ok=True)
    token = read_bridge_token()
    payload = {
        "token": token,
        "command": command,
        "params": params or {},
        "created_at": datetime.now().isoformat(timespec="seconds"),
    }
    BRIDGE_COMMAND_PATH.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return {
        "queued": True,
        "command": command,
        "command_path": str(BRIDGE_COMMAND_PATH),
        "response_path": str(BRIDGE_RESPONSE_PATH),
        "next_step": f"Run this journal in NX to process the command: {BRIDGE_JOURNAL_PATH}",
    }


def read_bridge_response(_args: dict[str, Any] | None = None) -> dict[str, Any]:
    if not BRIDGE_RESPONSE_PATH.exists():
        raise ToolError(
            f"No bridge response found yet. Run this journal in NX: {BRIDGE_JOURNAL_PATH}"
        )
    return json.loads(BRIDGE_RESPONSE_PATH.read_text(encoding="utf-8"))


def bridge_command(command: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
    return write_bridge_command(command, params)


def journal_report_work_part(author: str, note: str) -> str:
    return f'''# Generated by nx-local MCP demo.
# Purpose: report the current NX work part in the Listing Window.
# Author/requester: {author}
# Note: {note}

import NXOpen


def main():
    session = NXOpen.Session.GetSession()
    listing_window = session.ListingWindow
    listing_window.Open()

    work_part = session.Parts.Work
    display_part = session.Parts.Display

    listing_window.WriteLine("NX MCP report_work_part")
    listing_window.WriteLine("-----------------------")

    if work_part is None:
        listing_window.WriteLine("No work part is currently loaded.")
    else:
        listing_window.WriteLine("Work part name: " + work_part.Name)
        listing_window.WriteLine("Work part full path: " + work_part.FullPath)

    if display_part is None:
        listing_window.WriteLine("No display part is currently loaded.")
    else:
        listing_window.WriteLine("Display part name: " + display_part.Name)


if __name__ == "__main__":
    main()
'''


def journal_create_expression(author: str, note: str, name: str, rhs: str) -> str:
    expression_name = re.sub(r"[^A-Za-z0-9_]+", "_", name.strip())[:40] or "mcp_expr"
    rhs_literal = rhs.strip() or "1"
    return f'''# Generated by nx-local MCP demo.
# Purpose: create or update a simple NX expression in the work part.
# Author/requester: {author}
# Note: {note}

import NXOpen


def main():
    session = NXOpen.Session.GetSession()
    listing_window = session.ListingWindow
    listing_window.Open()
    work_part = session.Parts.Work

    if work_part is None:
        listing_window.WriteLine("No work part is currently loaded.")
        return

    expression_name = "{expression_name}"
    expression_rhs = "{rhs_literal}"

    try:
        expr = work_part.Expressions.FindObject(expression_name)
        expr.RightHandSide = expression_rhs
        listing_window.WriteLine("Updated expression: " + expression_name + " = " + expression_rhs)
    except Exception:
        work_part.Expressions.CreateExpression("Number", expression_name + "=" + expression_rhs)
        listing_window.WriteLine("Created expression: " + expression_name + " = " + expression_rhs)

    session.UpdateManager.DoUpdate(session.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, "MCP expression update"))


if __name__ == "__main__":
    main()
'''


def journal_create_basic_sketch(
    author: str,
    note: str,
    sketch_name: str,
    size_mm: str,
) -> str:
    safe_name = re.sub(r"[^A-Za-z0-9_ -]+", "_", sketch_name.strip())[:60] or "MCP Sketch"
    try:
        size = float(size_mm)
    except ValueError:
        size = 50.0
    half = max(size, 1.0) / 2.0

    return f'''# Generated by nx-local MCP demo.
# Purpose: create a simple sketch with a centered rectangle on the absolute XY plane.
# Author/requester: {author}
# Note: {note}
#
# Review before running on production CAD data.

import NXOpen


def enum_value(enum_class, *candidate_names):
    for name in candidate_names:
        if hasattr(enum_class, name):
            return getattr(enum_class, name)
    raise AttributeError("None of these enum names exist: " + ", ".join(candidate_names))


def main():
    session = NXOpen.Session.GetSession()
    listing_window = session.ListingWindow
    listing_window.Open()
    work_part = session.Parts.Work

    if work_part is None:
        listing_window.WriteLine("No work part is currently loaded. Create or open a part first.")
        return

    mark_id = session.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, "MCP create basic sketch")

    # Create a datum plane on absolute XY for the sketch.
    origin = NXOpen.Point3d(0.0, 0.0, 0.0)
    normal = NXOpen.Vector3d(0.0, 0.0, 1.0)
    plane = work_part.Planes.CreatePlane(origin, normal, NXOpen.SmartObject.UpdateOption.WithinModeling)

    sketch_builder = work_part.Sketches.CreateSketchInPlaceBuilder2(None)
    sketch_builder.PlaneReference = plane
    try:
        sketch_builder.PlaneOption = NXOpen.Sketch.PlaneOption.ExistingPlane
    except Exception:
        pass
    sketch = sketch_builder.Commit()
    sketch_builder.Destroy()

    try:
        sketch.SetName("{safe_name}")
    except Exception:
        pass

    view_reorient_false = enum_value(NXOpen.Sketch.ViewReorient, "False", "FalseValue")
    update_model = enum_value(NXOpen.Sketch.UpdateLevel, "Model")
    sketch.Activate(view_reorient_false)

    p1 = NXOpen.Point3d(-{half}, -{half}, 0.0)
    p2 = NXOpen.Point3d({half}, -{half}, 0.0)
    p3 = NXOpen.Point3d({half}, {half}, 0.0)
    p4 = NXOpen.Point3d(-{half}, {half}, 0.0)

    lines = [
        work_part.Curves.CreateLine(p1, p2),
        work_part.Curves.CreateLine(p2, p3),
        work_part.Curves.CreateLine(p3, p4),
        work_part.Curves.CreateLine(p4, p1),
    ]

    for line in lines:
        try:
            session.ActiveSketch.AddGeometry(line)
        except Exception:
            sketch.AddGeometry(line)

    sketch.Update()
    session.ActiveSketch.Deactivate(view_reorient_false, update_model)
    session.UpdateManager.DoUpdate(mark_id)

    listing_window.WriteLine("Created sketch: {safe_name}")
    listing_window.WriteLine("Rectangle size: {size} mm x {size} mm")


if __name__ == "__main__":
    main()
'''


TEMPLATE_DESCRIPTIONS = {
    "report_work_part": "Read-only journal that reports the current work/display part.",
    "create_expression": "Creates or updates a simple numeric expression in the work part.",
    "create_basic_sketch": "Creates a simple centered rectangle sketch on the absolute XY plane.",
}


def tool_nx_status(_args: dict[str, Any]) -> dict[str, Any]:
    ensure_workspace()
    return nx_env_summary()


def tool_nx_create_journal(args: dict[str, Any]) -> dict[str, Any]:
    ensure_workspace()
    template = str(args.get("template", "report_work_part"))
    requester = str(args.get("requester", "unknown"))
    note = str(args.get("note", ""))

    if template not in TEMPLATE_DESCRIPTIONS:
        raise ToolError(f"Unknown template: {template}")

    if template == "report_work_part":
        code = journal_report_work_part(requester, note)
    elif template == "create_expression":
        params = args.get("parameters") or {}
        code = journal_create_expression(
            requester,
            note,
            name=str(params.get("name", "mcp_test_expr")),
            rhs=str(params.get("rhs", "1")),
        )
    elif template == "create_basic_sketch":
        params = args.get("parameters") or {}
        code = journal_create_basic_sketch(
            requester,
            note,
            sketch_name=str(params.get("sketch_name", "MCP Basic Sketch")),
            size_mm=str(params.get("size_mm", "50")),
        )
    else:
        raise ToolError(f"Unhandled template: {template}")

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    path = JOURNAL_DIR / f"{timestamp}_{safe_slug(template)}.py"
    path.write_text(code, encoding="utf-8")

    return {
        "journal_path": str(path),
        "template": template,
        "description": TEMPLATE_DESCRIPTIONS[template],
        "execute_default": False,
        "next_step": "Review this journal, then run it manually in NX or call nx_run_journal with execute=false for a command preview.",
    }


def tool_nx_list_journals(args: dict[str, Any]) -> dict[str, Any]:
    ensure_workspace()
    limit = max(1, min(int(args.get("limit", 20)), 100))
    paths = sorted(JOURNAL_DIR.glob("*.py"), key=lambda path: path.stat().st_mtime, reverse=True)
    return {
        "journal_dir": str(JOURNAL_DIR),
        "journals": [
            {
                "path": str(path),
                "name": path.name,
                "modified": datetime.fromtimestamp(path.stat().st_mtime).isoformat(timespec="seconds"),
                "size": path.stat().st_size,
            }
            for path in paths[:limit]
        ],
    }


def resolve_journal_path(path_text: str) -> Path:
    path = Path(path_text).resolve()
    try:
        path.relative_to(JOURNAL_DIR)
    except ValueError as exc:
        raise ToolError(f"Journal must be under {JOURNAL_DIR}") from exc
    if not path.exists() or path.suffix.lower() != ".py":
        raise ToolError("Journal path must point to an existing .py file")
    return path


def build_run_command(journal_path: Path) -> str:
    if not RUN_JOURNAL_COMMAND:
        return f"<configure NX_MCP_RUN_JOURNAL_COMMAND with a {{journal}} placeholder> {journal_path}"
    if "{journal}" not in RUN_JOURNAL_COMMAND:
        raise ToolError("NX_MCP_RUN_JOURNAL_COMMAND must contain {journal}")
    return RUN_JOURNAL_COMMAND.replace("{journal}", str(journal_path))


def tool_nx_run_journal(args: dict[str, Any]) -> dict[str, Any]:
    journal_path = resolve_journal_path(str(args.get("journal_path", "")))
    execute = bool(args.get("execute", False))
    command = build_run_command(journal_path)

    if not execute:
        return {
            "journal_path": str(journal_path),
            "execute": False,
            "command_preview": command,
            "message": "Dry run only. Set execute=true after review, and enable NX_MCP_ALLOW_EXECUTE=1.",
        }

    if not ALLOW_EXECUTE:
        raise ToolError("Execution blocked. Set NX_MCP_ALLOW_EXECUTE=1 only after local approval.")
    if not RUN_JOURNAL_COMMAND:
        raise ToolError("Execution blocked. NX_MCP_RUN_JOURNAL_COMMAND is not configured.")

    completed = subprocess.run(
        command,
        shell=True,
        cwd=str(WORKSPACE),
        capture_output=True,
        text=True,
        errors="replace",
        timeout=int(args.get("timeout_sec", 120)),
    )
    return {
        "journal_path": str(journal_path),
        "execute": True,
        "returncode": completed.returncode,
        "stdout": completed.stdout[-4000:],
        "stderr": completed.stderr[-4000:],
    }


def tool_nx_bridge_info(_args: dict[str, Any]) -> dict[str, Any]:
    return {
        "bridge_journal_path": str(BRIDGE_JOURNAL_PATH),
        "bridge_token_path": str(BRIDGE_TOKEN_PATH),
        "bridge_command_path": str(BRIDGE_COMMAND_PATH),
        "bridge_response_path": str(BRIDGE_RESPONSE_PATH),
        "bridge_token_exists": BRIDGE_TOKEN_PATH.exists(),
        "setup": [
            "Open or create a part in NX.",
            "Run nx_bridge_journal.py inside NX once to create the token file.",
            "Call nx_bridge_queue_* from MCP to write a command file.",
            "Run nx_bridge_journal.py inside NX again to process the pending command.",
            "Call nx_bridge_read_response from MCP to read the result.",
        ],
    }


def tool_nx_bridge_status(_args: dict[str, Any]) -> dict[str, Any]:
    return write_bridge_command("status")


def tool_nx_bridge_report_work_part(_args: dict[str, Any]) -> dict[str, Any]:
    return bridge_command("report_work_part")


def tool_nx_bridge_create_basic_sketch(args: dict[str, Any]) -> dict[str, Any]:
    return bridge_command(
        "create_basic_sketch",
        {
            "sketch_name": str(args.get("sketch_name", "MCP Bridge Sketch")),
            "width_mm": float(args.get("width_mm", args.get("size_mm", 50))),
            "height_mm": float(args.get("height_mm", args.get("size_mm", 50))),
        },
    )


def tool_nx_bridge_stop(_args: dict[str, Any]) -> dict[str, Any]:
    return {"message": "File bridge is short-lived and does not need a stop command."}


def tool_nx_remoting_info(_args: dict[str, Any]) -> dict[str, Any]:
    return {
        "server_dll": str(REMOTING_SERVER_DLL),
        "client_exe": str(REMOTING_CLIENT_EXE),
        "built": REMOTING_SERVER_DLL.exists() and REMOTING_CLIENT_EXE.exists(),
        "port": REMOTING_PORT,
        "bind_to": "127.0.0.1",
        "load_in_nx": [
            "Open NX and create/open a part.",
            "Press Ctrl+U or use File/Tools -> Execute NX Open.",
            f"Select this DLL: {REMOTING_SERVER_DLL}",
            "After loading once, call nx_remoting_status or nx_remoting_create_basic_sketch.",
            "Use File -> Utilities -> Unload Shared Images to unload the DLL when needed.",
        ],
    }


def tool_nx_remoting_status(_args: dict[str, Any]) -> dict[str, Any]:
    return run_remoting_client(["status"])


def tool_nx_remoting_list_bodies(_args: dict[str, Any]) -> dict[str, Any]:
    return run_remoting_client(["list_bodies"])


def tool_nx_remoting_list_features(args: dict[str, Any]) -> dict[str, Any]:
    limit = str(int(args.get("limit", 20)))
    return run_remoting_client(["list_features", limit])


def tool_nx_remoting_analyze_bodies(args: dict[str, Any]) -> dict[str, Any]:
    target_body_name = str(args.get("target_body_name", ""))
    return run_remoting_client(["analyze_bodies", target_body_name])


def tool_nx_remoting_validate_body_dimensions(args: dict[str, Any]) -> dict[str, Any]:
    target_body_name = str(args.get("target_body_name", ""))
    expected_x = str(float(args.get("expected_x_mm", 0)))
    expected_y = str(float(args.get("expected_y_mm", 0)))
    expected_z = str(float(args.get("expected_z_mm", 0)))
    tolerance = str(float(args.get("tolerance_mm", 0.01)))
    return run_remoting_client(
        ["validate_body_dimensions", target_body_name, expected_x, expected_y, expected_z, tolerance]
    )


def tool_nx_remoting_color_thinnest_wall_face(args: dict[str, Any]) -> dict[str, Any]:
    target_body_name = str(args.get("target_body_name", ""))
    color_index = str(int(args.get("color_index", 186)))
    min_candidate_thickness = str(float(args.get("min_candidate_thickness_mm", 0.01)))
    max_candidates = str(int(args.get("max_candidates", 5000)))
    max_exact_pairs = str(int(args.get("max_exact_pairs", 1500)))
    skip_blend_faces = "1" if bool(args.get("skip_blend_faces", True)) else "0"
    source_hole_faces_only = "1" if bool(args.get("source_hole_faces_only", True)) else "0"
    report_candidate_count = str(int(args.get("report_candidate_count", 12)))
    debug_expected_thickness = str(float(args.get("debug_expected_thickness_mm", 0.0)))
    debug_expected_tolerance = str(float(args.get("debug_expected_tolerance_mm", 0.05)))
    min_face_uv_margin = str(float(args.get("min_face_uv_margin", 0.0)))
    stable_wall_faces_only = "1" if bool(args.get("stable_wall_faces_only", True)) else "0"
    max_runtime_sec = str(float(args.get("max_runtime_sec", 240.0)))
    return run_remoting_client(
        [
            "color_thinnest_wall_face",
            target_body_name,
            color_index,
            min_candidate_thickness,
            max_candidates,
            max_exact_pairs,
            skip_blend_faces,
            source_hole_faces_only,
            report_candidate_count,
            debug_expected_thickness,
            debug_expected_tolerance,
            min_face_uv_margin,
            stable_wall_faces_only,
            max_runtime_sec,
        ]
    )


def tool_nx_remoting_create_section_slice_report(args: dict[str, Any]) -> dict[str, Any]:
    target_body_name = str(args.get("target_body_name", ""))
    plane_x = str(float(args.get("plane_x_mm", 0.0)))
    plane_y = str(float(args.get("plane_y_mm", 58.0)))
    plane_z = str(float(args.get("plane_z_mm", 0.0)))
    normal_x = str(float(args.get("normal_x", 0.0)))
    normal_y = str(float(args.get("normal_y", 1.0)))
    normal_z = str(float(args.get("normal_z", 0.0)))
    samples_per_curve = str(int(args.get("samples_per_curve", 48)))
    min_candidate_thickness = str(float(args.get("min_candidate_thickness_mm", 0.03)))
    output_dir = str(args.get("output_dir", WORKSPACE / "section_images"))
    return run_remoting_client(
        [
            "section_slice",
            target_body_name,
            plane_x,
            plane_y,
            plane_z,
            normal_x,
            normal_y,
            normal_z,
            samples_per_curve,
            min_candidate_thickness,
            output_dir,
        ]
    )


def tool_nx_remoting_create_basic_sketch(args: dict[str, Any]) -> dict[str, Any]:
    sketch_name = str(args.get("sketch_name", "MCP Remoting Sketch"))
    width = str(float(args.get("width_mm", args.get("size_mm", 50))))
    height = str(float(args.get("height_mm", args.get("size_mm", 50))))
    return run_remoting_client(["sketch", sketch_name, width, height])


def tool_nx_remoting_create_rectangle_curves(args: dict[str, Any]) -> dict[str, Any]:
    curve_set_name = str(args.get("name", args.get("curve_set_name", "MCP Rectangle Curves")))
    width = str(float(args.get("width_mm", args.get("size_mm", 50))))
    height = str(float(args.get("height_mm", args.get("size_mm", 50))))
    return run_remoting_client(["curves", curve_set_name, width, height])


def tool_nx_remoting_create_line_curve(args: dict[str, Any]) -> dict[str, Any]:
    name = str(args.get("name", "MCP Line"))
    x1 = str(float(args.get("x1_mm", 0)))
    y1 = str(float(args.get("y1_mm", 0)))
    x2 = str(float(args.get("x2_mm", 50)))
    y2 = str(float(args.get("y2_mm", 0)))
    return run_remoting_client(["line_curve", name, x1, y1, x2, y2])


def tool_nx_remoting_create_circle_curve(args: dict[str, Any]) -> dict[str, Any]:
    name = str(args.get("name", "MCP Circle"))
    center_x = str(float(args.get("center_x_mm", 0)))
    center_y = str(float(args.get("center_y_mm", 0)))
    radius = str(float(args.get("radius_mm", 10)))
    return run_remoting_client(["circle_curve", name, center_x, center_y, radius])


def tool_nx_remoting_create_reference_cross(args: dict[str, Any]) -> dict[str, Any]:
    name = str(args.get("name", "MCP Reference Cross"))
    size = str(float(args.get("size_mm", 50)))
    return run_remoting_client(["reference_cross", name, size])


def tool_nx_remoting_create_box_body(args: dict[str, Any]) -> dict[str, Any]:
    name = str(args.get("name", "MCP Box Body"))
    origin_x = str(float(args.get("origin_x_mm", 0)))
    origin_y = str(float(args.get("origin_y_mm", 0)))
    origin_z = str(float(args.get("origin_z_mm", 0)))
    length = str(float(args.get("length_mm", args.get("x_mm", 50))))
    width = str(float(args.get("width_mm", args.get("y_mm", 30))))
    height = str(float(args.get("height_mm", args.get("z_mm", 10))))
    return run_remoting_client(["box_body", name, origin_x, origin_y, origin_z, length, width, height])


def tool_nx_remoting_create_extruded_rectangle(args: dict[str, Any]) -> dict[str, Any]:
    name = str(args.get("name", "MCP Extruded Rectangle"))
    center_x = str(float(args.get("center_x_mm", 0)))
    center_y = str(float(args.get("center_y_mm", 0)))
    origin_z = str(float(args.get("origin_z_mm", 0)))
    width = str(float(args.get("width_mm", 50)))
    height = str(float(args.get("height_mm", 30)))
    depth = str(float(args.get("depth_mm", args.get("extrude_depth_mm", 10))))
    return run_remoting_client(["extrude_rectangle", name, center_x, center_y, origin_z, width, height, depth])


def tool_nx_remoting_create_hinge_housing_section(args: dict[str, Any]) -> dict[str, Any]:
    section_name = str(args.get("section_name", "MECH Hinge Housing Section"))
    overall_width = str(float(args.get("overall_width_mm", args.get("width_mm", 80))))
    overall_height = str(float(args.get("overall_height_mm", args.get("height_mm", 12))))
    spring_wall = str(float(args.get("spring_wall_mm", 0.38)))
    screw_wall = str(float(args.get("screw_wall_mm", 0.50)))
    fpcb_floor = str(float(args.get("fpcb_floor_mm", 0.40)))
    side_wall = str(float(args.get("side_wall_mm", args.get("screw_wall_mm", 0.50))))
    source_note = str(args.get("source_note", "MECH DB 기준값을 사용한 POC 단면"))
    payload = run_remoting_client(
        [
            "hinge_section",
            section_name,
            overall_width,
            overall_height,
            spring_wall,
            screw_wall,
            fpcb_floor,
            side_wall,
            source_note,
        ]
    )
    payload["input_standards"] = {
        "spring_wall_mm": float(spring_wall),
        "screw_wall_mm": float(screw_wall),
        "fpcb_floor_mm": float(fpcb_floor),
        "side_wall_mm": float(side_wall),
    }
    payload["input_evidence"] = args.get("evidence", [])
    payload["workflow_hint"] = (
        "Use this tool after querying the MECH DB for wall-thickness evidence. "
        "Pass the extracted minimum values and summarize the DB sources in source_note/evidence."
    )
    return payload


def tool_nx_remoting_stop(_args: dict[str, Any]) -> dict[str, Any]:
    return {
        "ok": True,
        "message": (
            "The session remoting bridge is unloaded from NX, not stopped from the client. "
            "Use File -> Utilities -> Unload Shared Images in NX, or restart NX."
        ),
    }


TOOLS = {
    "nx_status": {
        "description": "Check local NX MCP workspace and NX-related environment variables.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_status,
    },
    "nx_create_journal": {
        "description": "Generate an allowlisted NXOpen Python journal file for review or manual NX playback.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "template": {
                    "type": "string",
                    "enum": sorted(TEMPLATE_DESCRIPTIONS),
                    "default": "report_work_part",
                },
                "requester": {"type": "string"},
                "note": {"type": "string"},
                "parameters": {
                    "type": "object",
                    "description": "Template parameters. For create_expression: name, rhs.",
                },
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_create_journal,
    },
    "nx_list_journals": {
        "description": "List generated NXOpen journals in the local MCP workspace.",
        "inputSchema": {
            "type": "object",
            "properties": {"limit": {"type": "integer", "default": 20, "minimum": 1, "maximum": 100}},
            "additionalProperties": False,
        },
        "handler": tool_nx_list_journals,
    },
    "nx_run_journal": {
        "description": "Preview or optionally run a generated journal through a locally configured NX runner command.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "journal_path": {"type": "string"},
                "execute": {"type": "boolean", "default": False},
                "timeout_sec": {"type": "integer", "default": 120, "minimum": 10, "maximum": 600},
            },
            "required": ["journal_path"],
            "additionalProperties": False,
        },
        "handler": tool_nx_run_journal,
    },
    "nx_bridge_info": {
        "description": "Show how to start the NX in-process localhost bridge journal.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_bridge_info,
    },
    "nx_bridge_status": {
        "description": "Queue a status command for the NX file bridge.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_bridge_status,
    },
    "nx_bridge_report_work_part": {
        "description": "Queue a work-part report command for the NX file bridge.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_bridge_report_work_part,
    },
    "nx_bridge_create_basic_sketch": {
        "description": "Queue a rectangle sketch command for the NX file bridge.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "sketch_name": {"type": "string", "default": "MCP Bridge Sketch"},
                "width_mm": {"type": "number", "default": 50},
                "height_mm": {"type": "number", "default": 50},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_bridge_create_basic_sketch,
    },
    "nx_bridge_read_response": {
        "description": "Read the latest response written by the NX file bridge journal.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": read_bridge_response,
    },
    "nx_bridge_stop": {
        "description": "Stop the live NX localhost bridge.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_bridge_stop,
    },
    "nx_remoting_info": {
        "description": "Show how to load the persistent NX Remoting bridge DLL.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_remoting_info,
    },
    "nx_remoting_status": {
        "description": "Check the persistent NX Remoting bridge and current work part.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_remoting_status,
    },
    "nx_remoting_list_bodies": {
        "description": "List solid/sheet bodies in the current NX work part so later tools can target named bodies.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_remoting_list_bodies,
    },
    "nx_remoting_list_features": {
        "description": "List recent features in the current NX work part for target selection and verification.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "limit": {"type": "integer", "default": 20, "minimum": 1, "maximum": 200},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_list_features,
    },
    "nx_remoting_analyze_bodies": {
        "description": (
            "Read geometric information from NX bodies: edge bbox, exact NX bbox, XYZ size, face/edge count, "
            "surface area, volume, centroid, mass, and weight. Use this after creating geometry to verify dimensions."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "target_body_name": {
                    "type": "string",
                    "default": "",
                    "description": "Optional body name or journal_id from nx_remoting_list_bodies. Empty returns all bodies.",
                },
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_analyze_bodies,
    },
    "nx_remoting_validate_body_dimensions": {
        "description": (
            "Validate a target NX body against expected exact-bbox XYZ dimensions and return PASS/FAIL with per-axis deltas."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "target_body_name": {
                    "type": "string",
                    "description": "Body name or journal_id from nx_remoting_list_bodies / nx_remoting_analyze_bodies.",
                },
                "expected_x_mm": {"type": "number", "description": "Expected exact-bbox X size in mm."},
                "expected_y_mm": {"type": "number", "description": "Expected exact-bbox Y size in mm."},
                "expected_z_mm": {"type": "number", "description": "Expected exact-bbox Z size in mm."},
                "tolerance_mm": {
                    "type": "number",
                    "default": 0.01,
                    "description": "Allowed absolute deviation per axis in mm.",
                },
            },
            "required": ["target_body_name", "expected_x_mm", "expected_y_mm", "expected_z_mm"],
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_validate_body_dimensions,
    },
    "nx_remoting_color_thinnest_wall_face": {
        "description": (
            "Estimate the thinnest wall in a target NX body by ranking hole-source face pairs with face bounding boxes, "
            "then verifying candidates with NX exact minimum distance and coloring the best face pair."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "target_body_name": {
                    "type": "string",
                    "default": "",
                    "description": "Optional body name or journal_id. Empty uses the latest solid body in the work part.",
                },
                "color_index": {
                    "type": "integer",
                    "default": 186,
                    "description": "NX color index used to highlight the thinnest wall face pair.",
                },
                "min_candidate_thickness_mm": {
                    "type": "number",
                    "default": 0.01,
                    "description": "Ignore touching/adjacent face pairs at or below this distance.",
                },
                "max_candidates": {
                    "type": "integer",
                    "default": 5000,
                    "description": "Maximum AABB-ranked candidate face pairs to keep before exact NX distance checks.",
                },
                "max_exact_pairs": {
                    "type": "integer",
                    "default": 1500,
                    "description": "Maximum candidate face pairs to verify with NX exact minimum distance.",
                },
                "skip_blend_faces": {
                    "type": "boolean",
                    "default": True,
                    "description": "Skip blend/fillet faces by default so the result is closer to wall thickness than local fillet clearance.",
                },
                "source_hole_faces_only": {
                    "type": "boolean",
                    "default": True,
                    "description": "Use hole-like faces (cylindrical/revolved/conical) as ray sources. Disable for broad all-face scans.",
                },
                "report_candidate_count": {
                    "type": "integer",
                    "default": 12,
                    "description": "Return this many valid candidate face pairs for debugging and validation.",
                },
                "debug_expected_thickness_mm": {
                    "type": "number",
                    "default": 0,
                    "description": "Optional debug-only thickness target; matching candidates are reported separately but are not forced as the answer.",
                },
                "debug_expected_tolerance_mm": {
                    "type": "number",
                    "default": 0.05,
                    "description": "Tolerance around debug_expected_thickness_mm for the debug candidate list.",
                },
                "min_face_uv_margin": {
                    "type": "number",
                    "default": 0,
                    "description": "Optional interior-face margin filter. Use to reject edge/corner clearances while debugging wall thickness.",
                },
                "stable_wall_faces_only": {
                    "type": "boolean",
                    "default": True,
                    "description": "Prefer engineering wall candidates on planar/cylindrical/revolved faces and apply stricter opposing-normal checks.",
                },
                "max_runtime_sec": {
                    "type": "number",
                    "default": 240,
                    "description": "Stop the NX-side wall-thickness scan after this many seconds and return the best partial result if available.",
                },
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_color_thinnest_wall_face,
    },
    "nx_remoting_create_section_slice_report": {
        "description": (
            "Create NX section curves at a requested plane, export an SVG image of the section, "
            "and estimate the thinnest wall in that 2D section."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "target_body_name": {
                    "type": "string",
                    "default": "",
                    "description": "Optional body name or journal_id. Empty uses the latest solid body in the work part.",
                },
                "plane_x_mm": {"type": "number", "default": 0},
                "plane_y_mm": {"type": "number", "default": 58},
                "plane_z_mm": {"type": "number", "default": 0},
                "normal_x": {"type": "number", "default": 0},
                "normal_y": {"type": "number", "default": 1},
                "normal_z": {"type": "number", "default": 0},
                "samples_per_curve": {
                    "type": "integer",
                    "default": 48,
                    "description": "Number of sample points per section curve for image generation and thickness estimation.",
                },
                "min_candidate_thickness_mm": {
                    "type": "number",
                    "default": 0.03,
                    "description": "Ignore sampled point pairs at or below this distance.",
                },
                "output_dir": {
                    "type": "string",
                    "description": "Optional output directory for the generated SVG image.",
                },
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_section_slice_report,
    },
    "nx_remoting_create_basic_sketch": {
        "description": "Create a rectangle sketch through the persistent NX Remoting bridge.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "sketch_name": {"type": "string", "default": "MCP Remoting Sketch"},
                "width_mm": {"type": "number", "default": 50},
                "height_mm": {"type": "number", "default": 50},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_basic_sketch,
    },
    "nx_remoting_create_rectangle_curves": {
        "description": "Create simple rectangle curves through the persistent NX Remoting bridge; useful as a fallback control test.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "default": "MCP Rectangle Curves"},
                "width_mm": {"type": "number", "default": 50},
                "height_mm": {"type": "number", "default": 50},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_rectangle_curves,
    },
    "nx_remoting_create_line_curve": {
        "description": "Create a generic 2D line curve on the absolute XY plane.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "default": "MCP Line"},
                "x1_mm": {"type": "number", "default": 0},
                "y1_mm": {"type": "number", "default": 0},
                "x2_mm": {"type": "number", "default": 50},
                "y2_mm": {"type": "number", "default": 0},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_line_curve,
    },
    "nx_remoting_create_circle_curve": {
        "description": "Create a generic 2D circle curve on the absolute XY plane.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "default": "MCP Circle"},
                "center_x_mm": {"type": "number", "default": 0},
                "center_y_mm": {"type": "number", "default": 0},
                "radius_mm": {"type": "number", "default": 10},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_circle_curve,
    },
    "nx_remoting_create_reference_cross": {
        "description": "Create generic horizontal/vertical reference curves centered at the absolute origin.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "default": "MCP Reference Cross"},
                "size_mm": {"type": "number", "default": 50},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_reference_cross,
    },
    "nx_remoting_create_box_body": {
        "description": (
            "Create a generic rectangular solid body from origin and XYZ dimensions. "
            "This is the first reusable 3D primitive for recipe-based design generation."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "default": "MCP Box Body"},
                "origin_x_mm": {"type": "number", "default": 0},
                "origin_y_mm": {"type": "number", "default": 0},
                "origin_z_mm": {"type": "number", "default": 0},
                "length_mm": {"type": "number", "default": 50},
                "width_mm": {"type": "number", "default": 30},
                "height_mm": {"type": "number", "default": 10},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_box_body,
    },
    "nx_remoting_create_extruded_rectangle": {
        "description": (
            "Create a rectangle sketch/profile and extrude it along +Z into a solid body. "
            "Use this as the first generic sketch-to-solid feature tool."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "default": "MCP Extruded Rectangle"},
                "center_x_mm": {"type": "number", "default": 0},
                "center_y_mm": {"type": "number", "default": 0},
                "origin_z_mm": {"type": "number", "default": 0},
                "width_mm": {"type": "number", "default": 50},
                "height_mm": {"type": "number", "default": 30},
                "depth_mm": {"type": "number", "default": 10},
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_extruded_rectangle,
    },
    "nx_remoting_create_hinge_housing_section": {
        "description": (
            "Create a MECH-informed hinge housing basic section sketch. "
            "Call the MECH DB/API first, then pass extracted wall-thickness values and evidence here."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "section_name": {"type": "string", "default": "MECH Hinge Housing Section"},
                "overall_width_mm": {"type": "number", "default": 80},
                "overall_height_mm": {"type": "number", "default": 12},
                "spring_wall_mm": {
                    "type": "number",
                    "default": 0.38,
                    "description": "Minimum spring-side wall thickness from MECH evidence.",
                },
                "screw_wall_mm": {
                    "type": "number",
                    "default": 0.5,
                    "description": "Minimum wall around central screw area from MECH evidence.",
                },
                "fpcb_floor_mm": {
                    "type": "number",
                    "default": 0.4,
                    "description": "Minimum CTC FPCB floor thickness from MECH evidence.",
                },
                "side_wall_mm": {
                    "type": "number",
                    "default": 0.5,
                    "description": "Side wall thickness for the concept section; defaults to screw_wall_mm.",
                },
                "source_note": {
                    "type": "string",
                    "default": "MECH DB 기준값을 사용한 POC 단면",
                },
                "evidence": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "Short source snippets or titles from the MECH DB search.",
                },
            },
            "additionalProperties": False,
        },
        "handler": tool_nx_remoting_create_hinge_housing_section,
    },
    "nx_remoting_stop": {
        "description": "Stop the persistent NX Remoting bridge channel.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "handler": tool_nx_remoting_stop,
    },
}


def list_tools() -> dict[str, Any]:
    return {
        "tools": [
            {
                "name": name,
                "description": spec["description"],
                "inputSchema": spec["inputSchema"],
            }
            for name, spec in TOOLS.items()
        ]
    }


def call_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    if name not in TOOLS:
        raise ToolError(f"Unknown tool: {name}")
    result = TOOLS[name]["handler"](arguments or {})
    return {
        "content": [
            {
                "type": "text",
                "text": json.dumps(result, ensure_ascii=False, indent=2),
            }
        ],
        "structuredContent": result,
        "isError": False,
    }


def handle_request(message: dict[str, Any]) -> dict[str, Any] | None:
    method = message.get("method")
    request_id = message.get("id")
    params = message.get("params") or {}

    if request_id is None:
        return None

    try:
        if method == "initialize":
            return json_response(
                request_id,
                {
                    "protocolVersion": PROTOCOL_VERSION,
                    "serverInfo": {"name": "nx-local-mcp-demo", "version": "0.1.0"},
                    "capabilities": {"tools": {}},
                },
            )
        if method == "tools/list":
            return json_response(request_id, list_tools())
        if method == "tools/call":
            return json_response(
                request_id,
                call_tool(str(params.get("name", "")), params.get("arguments") or {}),
            )
        return json_error(request_id, -32601, f"Unsupported method: {method}")
    except ToolError as exc:
        return json_response(
            request_id,
            {
                "content": [{"type": "text", "text": str(exc)}],
                "isError": True,
            },
        )
    except Exception as exc:
        log(f"Unexpected error: {exc}")
        return json_error(request_id, -32603, str(exc))


def main() -> None:
    ensure_workspace()
    log(f"workspace={WORKSPACE}")
    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
        except json.JSONDecodeError as exc:
            log(f"Invalid JSON from client: {exc}")
            continue
        response = handle_request(message)
        if response is not None:
            send(response)


if __name__ == "__main__":
    main()

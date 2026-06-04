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
        stderr = ""
        if proc.stderr is not None:
            stderr = proc.stderr.read()
        raise RuntimeError("MCP server closed stdout. stderr: " + stderr)
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
                    "clientInfo": {"name": "codex-remoting-client", "version": "0.1"},
                },
            },
        )

        if action == "info":
            result = call_tool(proc, 2, "nx_remoting_info")
        elif action == "status":
            result = call_tool(proc, 2, "nx_remoting_status")
        elif action in {"bodies", "list-bodies"}:
            result = call_tool(proc, 2, "nx_remoting_list_bodies")
        elif action in {"features", "list-features"}:
            limit = int(sys.argv[2]) if len(sys.argv) > 2 else 20
            result = call_tool(proc, 2, "nx_remoting_list_features", {"limit": limit})
        elif action in {"analyze", "analyze-bodies", "body-info"}:
            target_body_name = sys.argv[2] if len(sys.argv) > 2 else ""
            result = call_tool(
                proc,
                2,
                "nx_remoting_analyze_bodies",
                {"target_body_name": target_body_name},
            )
        elif action in {"validate", "validate-dimensions", "validate-body"}:
            target_body_name = sys.argv[2] if len(sys.argv) > 2 else ""
            expected_x = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
            expected_y = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
            expected_z = float(sys.argv[5]) if len(sys.argv) > 5 else 0.0
            tolerance = float(sys.argv[6]) if len(sys.argv) > 6 else 0.01
            result = call_tool(
                proc,
                2,
                "nx_remoting_validate_body_dimensions",
                {
                    "target_body_name": target_body_name,
                    "expected_x_mm": expected_x,
                    "expected_y_mm": expected_y,
                    "expected_z_mm": expected_z,
                    "tolerance_mm": tolerance,
                },
            )
        elif action in {"thin-wall", "thinnest-wall", "color-thinnest-wall"}:
            target_body_name = sys.argv[2] if len(sys.argv) > 2 else ""
            color_index = int(sys.argv[3]) if len(sys.argv) > 3 else 186
            min_candidate = float(sys.argv[4]) if len(sys.argv) > 4 else 0.01
            max_candidates = int(sys.argv[5]) if len(sys.argv) > 5 else 5000
            max_exact_pairs = int(sys.argv[6]) if len(sys.argv) > 6 else 1500
            skip_blends = (sys.argv[7].lower() not in {"0", "false", "no", "n"}) if len(sys.argv) > 7 else True
            source_holes_only = (sys.argv[8].lower() not in {"0", "false", "no", "n"}) if len(sys.argv) > 8 else True
            report_candidate_count = int(sys.argv[9]) if len(sys.argv) > 9 else 12
            debug_expected_thickness = float(sys.argv[10]) if len(sys.argv) > 10 else 0.0
            debug_expected_tolerance = float(sys.argv[11]) if len(sys.argv) > 11 else 0.05
            min_face_uv_margin = float(sys.argv[12]) if len(sys.argv) > 12 else 0.0
            stable_wall_faces_only = (sys.argv[13].lower() not in {"0", "false", "no", "n"}) if len(sys.argv) > 13 else True
            max_runtime_sec = float(sys.argv[14]) if len(sys.argv) > 14 else 240.0
            result = call_tool(
                proc,
                2,
                "nx_remoting_color_thinnest_wall_face",
                {
                    "target_body_name": target_body_name,
                    "color_index": color_index,
                    "min_candidate_thickness_mm": min_candidate,
                    "max_candidates": max_candidates,
                    "max_exact_pairs": max_exact_pairs,
                    "skip_blend_faces": skip_blends,
                    "source_hole_faces_only": source_holes_only,
                    "report_candidate_count": report_candidate_count,
                    "debug_expected_thickness_mm": debug_expected_thickness,
                    "debug_expected_tolerance_mm": debug_expected_tolerance,
                    "min_face_uv_margin": min_face_uv_margin,
                    "stable_wall_faces_only": stable_wall_faces_only,
                    "max_runtime_sec": max_runtime_sec,
                },
            )
        elif action in {"section-slice", "slice", "section"}:
            target_body_name = sys.argv[2] if len(sys.argv) > 2 else ""
            plane_x = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
            plane_y = float(sys.argv[4]) if len(sys.argv) > 4 else 58.0
            plane_z = float(sys.argv[5]) if len(sys.argv) > 5 else 0.0
            normal_x = float(sys.argv[6]) if len(sys.argv) > 6 else 0.0
            normal_y = float(sys.argv[7]) if len(sys.argv) > 7 else 1.0
            normal_z = float(sys.argv[8]) if len(sys.argv) > 8 else 0.0
            samples_per_curve = int(sys.argv[9]) if len(sys.argv) > 9 else 48
            min_candidate = float(sys.argv[10]) if len(sys.argv) > 10 else 0.03
            output_dir = sys.argv[11] if len(sys.argv) > 11 else ""
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_section_slice_report",
                {
                    "target_body_name": target_body_name,
                    "plane_x_mm": plane_x,
                    "plane_y_mm": plane_y,
                    "plane_z_mm": plane_z,
                    "normal_x": normal_x,
                    "normal_y": normal_y,
                    "normal_z": normal_z,
                    "samples_per_curve": samples_per_curve,
                    "min_candidate_thickness_mm": min_candidate,
                    "output_dir": output_dir,
                },
            )
        elif action == "sketch":
            sketch_name = sys.argv[2] if len(sys.argv) > 2 else "MCP Remoting Sketch"
            width = float(sys.argv[3]) if len(sys.argv) > 3 else 50.0
            height = float(sys.argv[4]) if len(sys.argv) > 4 else width
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_basic_sketch",
                {
                    "sketch_name": sketch_name,
                    "width_mm": width,
                    "height_mm": height,
                },
            )
        elif action == "curves":
            curve_set_name = sys.argv[2] if len(sys.argv) > 2 else "MCP Rectangle Curves"
            width = float(sys.argv[3]) if len(sys.argv) > 3 else 50.0
            height = float(sys.argv[4]) if len(sys.argv) > 4 else width
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_rectangle_curves",
                {
                    "name": curve_set_name,
                    "width_mm": width,
                    "height_mm": height,
                },
            )
        elif action == "line":
            name = sys.argv[2] if len(sys.argv) > 2 else "MCP Line"
            x1 = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
            y1 = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
            x2 = float(sys.argv[5]) if len(sys.argv) > 5 else 50.0
            y2 = float(sys.argv[6]) if len(sys.argv) > 6 else 0.0
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_line_curve",
                {
                    "name": name,
                    "x1_mm": x1,
                    "y1_mm": y1,
                    "x2_mm": x2,
                    "y2_mm": y2,
                },
            )
        elif action == "circle":
            name = sys.argv[2] if len(sys.argv) > 2 else "MCP Circle"
            center_x = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
            center_y = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
            radius = float(sys.argv[5]) if len(sys.argv) > 5 else 10.0
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_circle_curve",
                {
                    "name": name,
                    "center_x_mm": center_x,
                    "center_y_mm": center_y,
                    "radius_mm": radius,
                },
            )
        elif action in {"cross", "reference-cross"}:
            name = sys.argv[2] if len(sys.argv) > 2 else "MCP Reference Cross"
            size = float(sys.argv[3]) if len(sys.argv) > 3 else 50.0
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_reference_cross",
                {
                    "name": name,
                    "size_mm": size,
                },
            )
        elif action in {"box", "box-body"}:
            name = sys.argv[2] if len(sys.argv) > 2 else "MCP Box Body"
            origin_x = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
            origin_y = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
            origin_z = float(sys.argv[5]) if len(sys.argv) > 5 else 0.0
            length = float(sys.argv[6]) if len(sys.argv) > 6 else 50.0
            width = float(sys.argv[7]) if len(sys.argv) > 7 else 30.0
            height = float(sys.argv[8]) if len(sys.argv) > 8 else 10.0
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_box_body",
                {
                    "name": name,
                    "origin_x_mm": origin_x,
                    "origin_y_mm": origin_y,
                    "origin_z_mm": origin_z,
                    "length_mm": length,
                    "width_mm": width,
                    "height_mm": height,
                },
            )
        elif action in {"extrude-rectangle", "extrude-rect"}:
            name = sys.argv[2] if len(sys.argv) > 2 else "MCP Extruded Rectangle"
            center_x = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
            center_y = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
            origin_z = float(sys.argv[5]) if len(sys.argv) > 5 else 0.0
            width = float(sys.argv[6]) if len(sys.argv) > 6 else 50.0
            height = float(sys.argv[7]) if len(sys.argv) > 7 else 30.0
            depth = float(sys.argv[8]) if len(sys.argv) > 8 else 10.0
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_extruded_rectangle",
                {
                    "name": name,
                    "center_x_mm": center_x,
                    "center_y_mm": center_y,
                    "origin_z_mm": origin_z,
                    "width_mm": width,
                    "height_mm": height,
                    "depth_mm": depth,
                },
            )
        elif action in {"hinge", "hinge-section"}:
            section_name = sys.argv[2] if len(sys.argv) > 2 else "MEG Hinge Housing Section"
            width = float(sys.argv[3]) if len(sys.argv) > 3 else 80.0
            height = float(sys.argv[4]) if len(sys.argv) > 4 else 12.0
            spring_wall = float(sys.argv[5]) if len(sys.argv) > 5 else 0.38
            screw_wall = float(sys.argv[6]) if len(sys.argv) > 6 else 0.50
            fpcb_floor = float(sys.argv[7]) if len(sys.argv) > 7 else 0.40
            result = call_tool(
                proc,
                2,
                "nx_remoting_create_hinge_housing_section",
                {
                    "section_name": section_name,
                    "overall_width_mm": width,
                    "overall_height_mm": height,
                    "spring_wall_mm": spring_wall,
                    "screw_wall_mm": screw_wall,
                    "fpcb_floor_mm": fpcb_floor,
                    "side_wall_mm": screw_wall,
                    "source_note": "MEG DB wall-thickness POC defaults or extracted values",
                },
            )
        elif action == "stop":
            result = call_tool(proc, 2, "nx_remoting_stop")
        else:
            raise SystemExit(
                "Usage: remoting_client_via_mcp.py "
                "[info|status|bodies|features|analyze|validate|thin-wall|section-slice|sketch|curves|line|circle|cross|box|extrude-rectangle|hinge-section|stop] [name] ..."
            )

        print(json.dumps(result, ensure_ascii=False, indent=2))
    finally:
        proc.kill()


if __name__ == "__main__":
    main()

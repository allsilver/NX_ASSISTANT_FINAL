# NX local bridge journal.
#
# Run this file inside Siemens NX via Tools -> Journal -> Play whenever you want
# NX to process pending commands from Codex/Cline. This version is intentionally
# short-lived: it reads one command file, executes allowlisted NXOpen actions,
# writes one response file, and exits so NX UI is not blocked.
#
# Command file:
#   workspace/nx_bridge_command.json
# Response file:
#   workspace/nx_bridge_response.json

from __future__ import annotations

import json
import secrets
import traceback
from datetime import datetime
from pathlib import Path

import NXOpen


ROOT = Path(__file__).resolve().parent
WORKSPACE = ROOT / "workspace"
COMMAND_PATH = WORKSPACE / "nx_bridge_command.json"
RESPONSE_PATH = WORKSPACE / "nx_bridge_response.json"
TOKEN_PATH = WORKSPACE / "nx_bridge_token.txt"


class BridgeError(Exception):
    pass


def ensure_token() -> str:
    WORKSPACE.mkdir(parents=True, exist_ok=True)
    if TOKEN_PATH.exists():
        token = TOKEN_PATH.read_text(encoding="utf-8").strip()
        if token:
            return token
    token = secrets.token_urlsafe(32)
    TOKEN_PATH.write_text(token, encoding="utf-8")
    return token


def listing():
    session = NXOpen.Session.GetSession()
    listing_window = session.ListingWindow
    listing_window.Open()
    return listing_window


def enum_value(enum_class, *candidate_names):
    for name in candidate_names:
        if hasattr(enum_class, name):
            return getattr(enum_class, name)
    raise AttributeError("None of these enum names exist: " + ", ".join(candidate_names))


def work_part_summary() -> dict:
    session = NXOpen.Session.GetSession()
    work_part = session.Parts.Work
    display_part = session.Parts.Display
    return {
        "has_work_part": work_part is not None,
        "work_part_name": "" if work_part is None else work_part.Name,
        "work_part_full_path": "" if work_part is None else work_part.FullPath,
        "display_part_name": "" if display_part is None else display_part.Name,
    }


def create_basic_sketch(params: dict) -> dict:
    session = NXOpen.Session.GetSession()
    lw = listing()
    work_part = session.Parts.Work

    if work_part is None:
        raise BridgeError("No work part is currently loaded. Create or open a part first.")

    sketch_name = str(params.get("sketch_name") or "MCP Bridge Sketch")[:60]
    width = float(params.get("width_mm") or params.get("size_mm") or 50.0)
    height = float(params.get("height_mm") or params.get("size_mm") or width)
    width = max(width, 1.0)
    height = max(height, 1.0)
    half_w = width / 2.0
    half_h = height / 2.0

    mark_id = session.SetUndoMark(
        NXOpen.Session.MarkVisibility.Visible,
        "MCP file bridge create basic sketch",
    )

    origin = NXOpen.Point3d(0.0, 0.0, 0.0)
    normal = NXOpen.Vector3d(0.0, 0.0, 1.0)
    plane = work_part.Planes.CreatePlane(
        origin,
        normal,
        NXOpen.SmartObject.UpdateOption.WithinModeling,
    )

    sketch_builder = work_part.Sketches.CreateSketchInPlaceBuilder2(None)
    sketch_builder.PlaneReference = plane
    try:
        sketch_builder.PlaneOption = NXOpen.Sketch.PlaneOption.ExistingPlane
    except Exception:
        pass

    sketch = sketch_builder.Commit()
    sketch_builder.Destroy()

    try:
        sketch.SetName(sketch_name)
    except Exception:
        pass

    view_reorient_false = enum_value(NXOpen.Sketch.ViewReorient, "False", "FalseValue")
    update_model = enum_value(NXOpen.Sketch.UpdateLevel, "Model")
    sketch.Activate(view_reorient_false)

    p1 = NXOpen.Point3d(-half_w, -half_h, 0.0)
    p2 = NXOpen.Point3d(half_w, -half_h, 0.0)
    p3 = NXOpen.Point3d(half_w, half_h, 0.0)
    p4 = NXOpen.Point3d(-half_w, half_h, 0.0)

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

    lw.WriteLine(
        "MCP file bridge created sketch: "
        + sketch_name
        + " ("
        + str(width)
        + " mm x "
        + str(height)
        + " mm)"
    )

    return {
        "created": True,
        "sketch_name": sketch_name,
        "width_mm": width,
        "height_mm": height,
        "work_part": work_part_summary(),
    }


def handle_command(payload: dict) -> dict:
    command = str(payload.get("command") or "")
    params = payload.get("params") or {}

    if command == "status":
        return {
            "bridge": "nx-file-bridge",
            "processed_at": datetime.now().isoformat(timespec="seconds"),
            "work_part": work_part_summary(),
        }
    if command == "report_work_part":
        summary = work_part_summary()
        listing().WriteLine("MCP file bridge work part report: " + json.dumps(summary, ensure_ascii=False))
        return summary
    if command == "create_basic_sketch":
        return create_basic_sketch(params)

    raise BridgeError("Unknown command: " + command)


def write_response(payload: dict) -> None:
    WORKSPACE.mkdir(parents=True, exist_ok=True)
    RESPONSE_PATH.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def main():
    token = ensure_token()
    lw = listing()
    lw.WriteLine("NX file bridge started for one command.")
    lw.WriteLine("Command file: " + str(COMMAND_PATH))
    lw.WriteLine("Response file: " + str(RESPONSE_PATH))

    if not COMMAND_PATH.exists():
        write_response(
            {
                "ok": True,
                "idle": True,
                "message": "No command file found. Write a command and run this journal again.",
                "token_file": str(TOKEN_PATH),
            }
        )
        lw.WriteLine("No command file found. Bridge exiting.")
        return

    try:
        payload = json.loads(COMMAND_PATH.read_text(encoding="utf-8"))
        if payload.get("token") != token:
            raise BridgeError("Invalid bridge token")
        result = handle_command(payload)
        write_response({"ok": True, "result": result})
        try:
            COMMAND_PATH.unlink()
        except Exception:
            pass
        lw.WriteLine("NX file bridge processed command: " + str(payload.get("command")))
    except Exception as error:
        write_response(
            {
                "ok": False,
                "error": str(error),
                "traceback": traceback.format_exc(),
            }
        )
        lw.WriteLine("NX file bridge error: " + str(error))


if __name__ == "__main__":
    main()

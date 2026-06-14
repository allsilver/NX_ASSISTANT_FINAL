from __future__ import annotations

import asyncio
import json
import sys
from typing import Any

from .models import MailMessage
from .quick_delivery_automation import build_parser as build_quick_parser
from .quick_delivery_automation import main_async as run_quick_delivery
from .runtime import ToolRuntime, build_adapter


TOOLS = [
    {
        "name": "send_knox_mail_from_chat",
        "description": "Parse a Korean chat instruction and send a Knox Portal mail.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "text": {"type": "string"},
                "dryRun": {"type": "boolean", "default": True},
                "adapter": {"type": "string", "enum": ["dry-run", "playwright"], "default": "dry-run"},
            },
            "required": ["text"],
        },
    },
    {
        "name": "send_knox_mail",
        "description": "Send a Knox Portal mail from structured fields.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "recipient": {"type": "string"},
                "subject": {"type": "string"},
                "body": {"type": "string"},
                "cc": {"type": "array", "items": {"type": "string"}},
                "dryRun": {"type": "boolean", "default": True},
                "adapter": {"type": "string", "enum": ["dry-run", "playwright"], "default": "dry-run"},
            },
            "required": ["recipient", "body"],
        },
    },
    {
        "name": "prepare_quick_delivery_from_chat",
        "description": "Parse a Korean chat instruction and prepare a Digital World quick/same-day delivery request. The tool returns needs_input when required fields are missing or ambiguous. It never clicks save unless allowSave is true and dryRun is false.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "text": {"type": "string"},
                "reason": {"type": "string"},
                "quickVendor": {"type": "string"},
                "allowInferredReason": {"type": "boolean", "default": False},
                "dryRun": {"type": "boolean", "default": True},
                "allowSave": {"type": "boolean", "default": False},
                "cdp": {"type": "string", "default": "http://127.0.0.1:9242"},
                "profileDir": {"type": "string"},
                "headless": {"type": "boolean", "default": False},
                "show": {"type": "boolean", "default": False},
                "keepOpen": {"type": "boolean", "default": False},
                "fresh": {"type": "boolean", "default": False},
            },
            "required": ["text"],
        },
    },
]


async def handle(request: dict[str, Any]) -> dict[str, Any] | None:
    request_id = request.get("id")
    method = request.get("method")
    params = request.get("params") or {}

    try:
        if method == "initialize":
            return {
                "id": request_id,
                "result": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {"tools": {}},
                    "serverInfo": {"name": "knox-mail-automation", "version": "0.1.0"},
                },
            }
        if method == "notifications/initialized":
            return None
        if method == "tools/list":
            return {"id": request_id, "result": {"tools": TOOLS}}
        if method == "tools/call":
            result = await _call_tool(params)
            return {
                "id": request_id,
                "result": {
                    "content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}],
                    "structuredContent": result,
                },
            }
        return {"id": request_id, "error": {"code": -32601, "message": f"Unknown method: {method}"}}
    except Exception as exc:
        return {"id": request_id, "error": {"code": -32000, "message": str(exc)}}


async def _call_tool(params: dict[str, Any]) -> dict[str, Any]:
    name = params.get("name")
    arguments = params.get("arguments") or {}
    adapter_name = _adapter_name(arguments)
    runtime = ToolRuntime(build_adapter(adapter_name))

    if name == "send_knox_mail_from_chat":
        text = str(arguments["text"])
        return (await runtime.send_knox_mail_from_chat(text)).to_dict()

    if name == "send_knox_mail":
        message = MailMessage(
            recipient=str(arguments["recipient"]),
            subject=arguments.get("subject"),
            body=str(arguments["body"]),
            cc=tuple(arguments.get("cc") or ()),
        )
        return (await runtime.send_knox_mail(message)).to_dict()

    if name == "prepare_quick_delivery_from_chat":
        return await run_quick_delivery(_quick_delivery_args(arguments))

    raise ValueError(f"Unknown tool: {name}")


def _adapter_name(arguments: dict[str, Any]) -> str:
    if arguments.get("dryRun", True):
        return "dry-run"
    return str(arguments.get("adapter") or "playwright")


def _quick_delivery_args(arguments: dict[str, Any]) -> Any:
    args = build_quick_parser().parse_args([])
    args.raw_command = str(arguments["text"])
    args.reason = str(arguments.get("reason") or "")
    args.quick_vendor = str(arguments.get("quickVendor") or "")
    args.allow_inferred_reason = bool(arguments.get("allowInferredReason", False))
    args.allow_save = bool(arguments.get("allowSave", False)) and not bool(arguments.get("dryRun", True))
    args.cdp = str(arguments.get("cdp") or args.cdp)
    args.profile_dir = str(arguments.get("profileDir") or args.profile_dir)
    args.headless = bool(arguments.get("headless", False))
    args.show = bool(arguments.get("show", False))
    args.keep_open = bool(arguments.get("keepOpen", False))
    args.fresh = bool(arguments.get("fresh", False))
    args.artifact_prefix = "quick-delivery-mcp"
    return args


async def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    for line in sys.stdin:
        if not line.strip():
            continue
        response = await handle(json.loads(line))
        if response is not None:
            print(json.dumps(response, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    asyncio.run(main())

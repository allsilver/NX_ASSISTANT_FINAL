from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent
MCP_SERVER = ROOT / "nx_mcp_server.py"
DEFAULT_MEG_URL = "http://127.0.0.1:8766/meg/ask"
DEFAULT_QUESTION = "Spring 부 살두께 중앙 Screw 체결부 주변 살두께 CTC FPCB 바닥부 살두께 Hinge Housing Gap 두께"


try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except AttributeError:
    pass


def post_json(url: str, payload: dict[str, Any], token: str = "") -> dict[str, Any]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    headers = {
        "Content-Type": "application/json; charset=utf-8",
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"

    request = urllib.request.Request(url, data=body, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode("utf-8", errors="replace"))
    except urllib.error.HTTPError as exc:
        error_body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"MEG API HTTP {exc.code}: {error_body}") from exc
    except urllib.error.URLError as exc:
        raise RuntimeError(f"MEG API connection failed: {exc}") from exc


def flatten_text(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, str):
        return value
    if isinstance(value, (int, float, bool)):
        return str(value)
    if isinstance(value, list):
        return "\n".join(flatten_text(item) for item in value)
    if isinstance(value, dict):
        return "\n".join(f"{key}: {flatten_text(item)}" for key, item in value.items())
    return str(value)


def collect_contexts(response: dict[str, Any]) -> list[Any]:
    candidates: list[Any] = []
    for key in ("contexts", "results", "items"):
        value = response.get(key)
        if isinstance(value, list):
            candidates.extend(value)
    if not candidates and isinstance(response.get("answer"), dict):
        answer = response["answer"]
        for key in ("contexts", "results", "items"):
            value = answer.get(key)
            if isinstance(value, list):
                candidates.extend(value)
    return candidates


def first_number_near(text: str, required_terms: list[str], default: float) -> float:
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    target_lines = [
        line for line in lines
        if all(term.lower() in line.lower() for term in required_terms)
    ]
    if not target_lines:
        target_lines = [
            line for line in lines
            if any(term.lower() in line.lower() for term in required_terms)
        ]

    number_pattern = re.compile(r"(?<!\d)(\d+(?:\.\d+)?)\s*(?:mm|㎜)?", re.IGNORECASE)
    for line in target_lines:
        numbers = [float(match.group(1)) for match in number_pattern.finditer(line)]
        plausible = [num for num in numbers if 0.05 <= num <= 10.0]
        if plausible:
            return plausible[0]
    return default


def context_value(contexts: list[Any], required_terms: list[str], default: float) -> float:
    for item in contexts:
        if not isinstance(item, dict):
            continue
        item_name = str(item.get("item") or "")
        category = str(item.get("category_path") or "")
        haystack = f"{item_name} {category}".lower()
        if not all(term.lower() in haystack for term in required_terms):
            continue

        spec_unit = str(item.get("spec_unit") or "").lower()
        spec_value = item.get("spec_value")
        if spec_value is not None and spec_unit in {"mm", "㎜"}:
            try:
                return float(spec_value)
            except (TypeError, ValueError):
                pass

        guide = str(item.get("guide_raw") or "")
        match = re.search(r"(?<!\d)(\d+(?:\.\d+)?)\s*(?:mm|㎜)", guide, re.IGNORECASE)
        if match:
            return float(match.group(1))

    return default


def source_summary(contexts: list[Any], limit: int = 5) -> list[str]:
    summaries: list[str] = []
    for item in contexts[:limit]:
        if isinstance(item, dict):
            title = str(item.get("title") or item.get("source_ref") or item.get("source") or "").strip()
            text = str(item.get("text") or item.get("content") or item.get("summary") or flatten_text(item)).strip()
            line = (title + " " + text).strip()
        else:
            line = flatten_text(item).strip()
        line = re.sub(r"\s+", " ", line)
        if len(line) > 220:
            line = line[:217] + "..."
        if line:
            summaries.append(line)
    return summaries


def extract_standards(meg_response: dict[str, Any]) -> dict[str, Any]:
    contexts = collect_contexts(meg_response)
    text = flatten_text(contexts if contexts else meg_response)

    spring = context_value(contexts, ["spring", "살두께"], 0.0)
    screw = context_value(contexts, ["중앙", "screw", "살두께"], 0.0)
    fpcb = context_value(contexts, ["ctc", "fpcb", "바닥", "살두께"], 0.0)

    if spring <= 0:
        spring = first_number_near(text, ["spring", "살두께"], 0.38)
    if screw <= 0:
        screw = first_number_near(text, ["screw", "살두께"], 0.50)
    if fpcb <= 0:
        fpcb = first_number_near(text, ["fpcb", "바닥", "살두께"], 0.40)

    return {
        "spring_wall_mm": spring,
        "screw_wall_mm": screw,
        "fpcb_floor_mm": fpcb,
        "evidence": source_summary(contexts),
    }


def run_nx_hinge_section(args: argparse.Namespace, standards: dict[str, Any]) -> dict[str, Any]:
    source_note = "MEG_STANDARD: " + " | ".join(standards.get("evidence", [])[:3])
    arguments = {
        "section_name": args.section_name,
        "overall_width_mm": args.width,
        "overall_height_mm": args.height,
        "spring_wall_mm": standards["spring_wall_mm"],
        "screw_wall_mm": standards["screw_wall_mm"],
        "fpcb_floor_mm": standards["fpcb_floor_mm"],
        "side_wall_mm": standards["screw_wall_mm"],
        "source_note": source_note[:800],
        "evidence": standards.get("evidence", []),
    }

    proc = subprocess.Popen(
        [sys.executable, str(MCP_SERVER)],
        cwd=str(ROOT),
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    try:
        send_mcp(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2025-06-18",
                    "capabilities": {},
                    "clientInfo": {"name": "meg-nx-hinge-flow", "version": "0.1"},
                },
            },
        )
        response = send_mcp(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 2,
                "method": "tools/call",
                "params": {
                    "name": "nx_remoting_create_hinge_housing_section",
                    "arguments": arguments,
                },
            },
        )
        result = response.get("result") or {}
        if result.get("isError"):
            return {
                "ok": False,
                "error": result.get("content", [{}])[0].get("text", "MCP tool error"),
                "mcp_arguments": arguments,
            }
        payload = result.get("structuredContent") or {}
        payload["mcp_arguments"] = arguments
        payload["helper_command"] = [sys.executable, str(MCP_SERVER)]
        return payload
    finally:
        proc.kill()


def send_mcp(proc: subprocess.Popen[str], request: dict[str, Any]) -> dict[str, Any]:
    if proc.stdin is None or proc.stdout is None:
        raise RuntimeError("MCP process pipes were not created")
    proc.stdin.write(json.dumps(request, ensure_ascii=False) + "\n")
    proc.stdin.flush()
    line = proc.stdout.readline()
    if not line:
        stderr = ""
        if proc.stderr is not None:
            stderr = proc.stderr.read()
        raise RuntimeError("MCP server closed stdout. stderr: " + stderr)
    return json.loads(line)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Search MEG wall-thickness standards, then create a hinge housing section in NX."
    )
    parser.add_argument("--meg-url", default=DEFAULT_MEG_URL)
    parser.add_argument("--question", default=DEFAULT_QUESTION)
    parser.add_argument("--domain", default="MEG_STANDARD")
    parser.add_argument("--section-name", default="MEG Hinge Housing Section")
    parser.add_argument("--width", type=float, default=80.0)
    parser.add_argument("--height", type=float, default=12.0)
    parser.add_argument("--max-results", type=int, default=7)
    parser.add_argument("--dry-run", action="store_true", help="Search and extract values without creating NX geometry.")
    parser.add_argument(
        "--token-env",
        default="MECH_MCP_TOKEN",
        help="Optional environment variable containing the MEG API bearer token.",
    )
    args = parser.parse_args()

    token = os.environ.get(args.token_env, "")
    meg_payload = {
        "question": args.question,
        "domain": args.domain,
        "max_results": args.max_results,
        "include_prompt": False,
        "compact": True,
        "requester": "meg-nx-flow",
    }

    result: dict[str, Any] = {
        "ok": False,
        "workflow": "meg_db_to_nx_hinge_housing_section",
        "meg_url": args.meg_url,
        "question": args.question,
        "section_name": args.section_name,
    }

    meg_response = post_json(args.meg_url, meg_payload, token)
    standards = extract_standards(meg_response)
    result["standards"] = standards

    if args.dry_run:
        result["ok"] = True
        result["dry_run"] = True
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    nx_response = run_nx_hinge_section(args, standards)
    result["nx_response"] = nx_response
    result["ok"] = bool(nx_response.get("ok"))
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())

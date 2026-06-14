from __future__ import annotations

import argparse
import asyncio
import json
from typing import Any

from .adapters.playwright_portal import PlaywrightPortalMailAdapter
from .runtime import ToolRuntime, build_adapter


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Knox Portal mail automation prototype")
    subparsers = parser.add_subparsers(dest="command", required=True)

    send_chat = subparsers.add_parser("send-chat", help="Parse Korean chat text and send mail")
    send_chat.add_argument("text", help="Korean instruction, e.g. 수신자 서다은에게 \"hi\"라고 적고 발송해줘")
    send_chat.add_argument("--adapter", choices=["dry-run", "playwright"], default="dry-run")
    send_chat.add_argument("--dry-run", action="store_true", help="Force dry-run adapter")
    send_chat.add_argument("--headless", action="store_true", help="Run browser headlessly")
    send_chat.add_argument("--no-headless", action="store_true", help="Run browser visibly")
    send_chat.add_argument("--confirm-before-send", action="store_true", default=True)
    send_chat.add_argument("--no-confirm-before-send", action="store_false", dest="confirm_before_send")
    send_chat.add_argument("--prepare-only", action="store_true", help="Compose the message but never click send")
    send_chat.add_argument("--profile-dir", default=".knox-profile")
    send_chat.add_argument("--browser-executable", default=None)

    inspect = subparsers.add_parser("inspect", help="Open portal and print visible UI element snapshot")
    inspect.add_argument("--adapter", choices=["playwright"], default="playwright")
    inspect.add_argument("--headless", action="store_true")
    inspect.add_argument("--no-headless", action="store_true")
    inspect.add_argument("--profile-dir", default=".knox-profile")
    inspect.add_argument("--browser-executable", default=None)

    return parser


async def run(args: argparse.Namespace) -> dict[str, Any]:
    if args.command == "send-chat":
        adapter_name = "dry-run" if args.dry_run else args.adapter
        headless = args.headless and not args.no_headless
        runtime = ToolRuntime(
            build_adapter(
                adapter_name,
                headless=headless,
                confirm_before_send=args.confirm_before_send,
                profile_dir=args.profile_dir,
                prepare_only=args.prepare_only,
                executable_path=args.browser_executable,
            )
        )
        return (await runtime.send_knox_mail_from_chat(args.text)).to_dict()

    if args.command == "inspect":
        headless = args.headless and not args.no_headless
        adapter = PlaywrightPortalMailAdapter(
            headless=headless,
            confirm_before_send=True,
            profile_dir=args.profile_dir,
            executable_path=args.browser_executable,
        )
        return await adapter.inspect()

    raise ValueError(f"Unknown command: {args.command}")


def main() -> None:
    args = build_parser().parse_args()
    result = asyncio.run(run(args))
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

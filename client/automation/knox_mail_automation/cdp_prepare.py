from __future__ import annotations

import argparse
import asyncio
import json
import os
import socket
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

from .config import PortalConfig
from .parser import parse_chat_command


CREATE_NEW_PROCESS_GROUP = 0x00000200
DETACHED_PROCESS = 0x00000008


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Open Chrome via local CDP, compose a Knox mail draft, and leave send untouched."
    )
    parser.add_argument("text", help="Korean instruction, e.g. 수신자 daeun.seo에게 \"hi\"라고 적고 발송해줘")
    parser.add_argument("--chrome", default=_default_chrome_path())
    parser.add_argument("--profile-dir", default=".knox-cdp-profile")
    parser.add_argument("--port", type=int, default=9229)
    parser.add_argument("--timeout-ms", type=int, default=30000)
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    message = parse_chat_command(args.text)
    profile_dir = Path(args.profile_dir).resolve()
    profile_dir.mkdir(parents=True, exist_ok=True)

    if not _is_port_open("127.0.0.1", args.port):
        _launch_chrome(args.chrome, profile_dir, args.port)
        _wait_for_cdp(args.port, timeout_s=20)

    from playwright.async_api import async_playwright

    config = PortalConfig.load()
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(f"http://127.0.0.1:{args.port}")
        context = browser.contexts[0] if browser.contexts else await browser.new_context()
        page = context.pages[0] if context.pages else await context.new_page()
        page.set_default_timeout(args.timeout_ms)

        mail_url = config.selectors.get("mailUrls", [config.portal_url])[0]
        await page.goto(mail_url, wait_until="domcontentloaded")
        await _click_first(page, config, "composeButtons")
        await _fill_first(page, config, "recipientInputs", message.recipient)
        await page.keyboard.press("Enter")
        if message.subject:
            await _fill_first(page, config, "subjectInputs", message.subject)
        await _fill_body(page, config, message.body)

        screenshot_path = str(Path("knox-mail-prepared.png").resolve())
        await page.screenshot(path=screenshot_path, full_page=True)
        await browser.close()

    return {
        "ok": True,
        "mode": "cdp-prepare-only",
        "message": message.to_dict(),
        "detail": "message composed; send was not clicked",
        "screenshot": screenshot_path,
        "cdp": f"http://127.0.0.1:{args.port}",
    }


async def _fill_body(page: Any, config: PortalConfig, body: str) -> None:
    last_error: Exception | None = None
    for selector in config.selectors.get("bodyInputs", []):
        try:
            if selector.startswith("iframe"):
                locator = page.frame_locator(selector).locator("[contenteditable='true'], body").first
                await locator.fill(body)
                return
            locator = page.locator(selector).first
            await locator.wait_for(state="visible", timeout=5000)
            await locator.fill(body)
            return
        except Exception as exc:
            last_error = exc
    raise RuntimeError(f"Could not find body editor. Last error: {last_error}")


async def _fill_first(page: Any, config: PortalConfig, key: str, value: str) -> None:
    last_error: Exception | None = None
    for selector in config.selectors.get(key, []):
        try:
            locator = page.locator(selector).first
            await locator.wait_for(state="visible", timeout=5000)
            await locator.fill(value)
            return
        except Exception as exc:
            last_error = exc
    raise RuntimeError(f"Could not fill {key}. Last error: {last_error}")


async def _click_first(page: Any, config: PortalConfig, key: str) -> None:
    last_error: Exception | None = None
    for selector in config.selectors.get(key, []):
        try:
            locator = page.locator(selector).first
            await locator.wait_for(state="visible", timeout=5000)
            await locator.click()
            return
        except Exception as exc:
            last_error = exc
    await page.screenshot(path="knox-mail-compose-not-found.png", full_page=True)
    raise RuntimeError(f"Could not click {key}. Last error: {last_error}")


def _launch_chrome(chrome: str | None, profile_dir: Path, port: int) -> None:
    if not chrome:
        raise RuntimeError("Chrome path was not found. Pass --chrome explicitly.")
    args = [
        chrome,
        f"--remote-debugging-port={port}",
        "--remote-debugging-address=127.0.0.1",
        f"--user-data-dir={profile_dir}",
        "--no-first-run",
        "--new-window",
        "http://kor1.samsung.net/mailapp/",
    ]
    subprocess.Popen(
        args,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        stdin=subprocess.DEVNULL,
        creationflags=CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS,
    )


def _wait_for_cdp(port: int, timeout_s: int) -> None:
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/json/version", timeout=1) as response:
                if response.status == 200:
                    return
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            time.sleep(0.25)
    raise RuntimeError(f"Chrome CDP did not start on port {port}")


def _is_port_open(host: str, port: int) -> bool:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.settimeout(0.5)
        return sock.connect_ex((host, port)) == 0


def _default_chrome_path() -> str | None:
    candidates = [
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    ]
    return next((path for path in candidates if os.path.exists(path)), None)


def main() -> None:
    result = asyncio.run(main_async(build_parser().parse_args()))
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

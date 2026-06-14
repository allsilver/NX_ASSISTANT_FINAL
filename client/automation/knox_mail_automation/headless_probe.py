from __future__ import annotations

import argparse
import asyncio
import json
import os
from datetime import datetime
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"


DEFAULT_URLS = [
    "http://kor1.samsung.net/portalapp/home",
    "https://digitalworld.sec.samsung.net/export/forwardEdit.do?_menuId=AWPHvvjaABbwlNIR&_menuF=true",
]


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Probe whether internal sites load in a headless Edge browser")
    parser.add_argument("--url", action="append", dest="urls")
    parser.add_argument("--profile-dir", default=".headless-probe-profile")
    parser.add_argument("--headed", action="store_true")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    profile_dir = (ROOT / args.profile_dir).resolve()
    profile_dir.mkdir(exist_ok=True)
    urls = args.urls or DEFAULT_URLS

    async with async_playwright() as playwright:
        context = await playwright.chromium.launch_persistent_context(
            user_data_dir=str(profile_dir),
            executable_path=_edge_path(),
            headless=not args.headed,
            viewport={"width": 1365, "height": 900},
            args=["--no-first-run"],
        )
        results = []
        try:
            page = context.pages[0] if context.pages else await context.new_page()
            for index, url in enumerate(urls):
                stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
                screenshot = ARTIFACT_DIR / f"headless-probe-{index}-{stamp}.png"
                try:
                    await page.goto(url, wait_until="domcontentloaded", timeout=60000)
                    try:
                        await page.wait_for_load_state("networkidle", timeout=15000)
                    except Exception:
                        pass
                    state = await _state(page)
                    await page.screenshot(path=str(screenshot), full_page=False, timeout=15000)
                    results.append({"ok": True, "inputUrl": url, "state": state, "screenshot": str(screenshot)})
                except Exception as exc:
                    try:
                        state = await _state(page)
                    except Exception:
                        state = {}
                    results.append({"ok": False, "inputUrl": url, "error": str(exc), "state": state})
        finally:
            await context.close()
    return {"headless": not args.headed, "profileDir": str(profile_dir), "results": results}


async def _state(page: Any) -> dict[str, Any]:
    return await page.evaluate(
        """
        () => ({
          url: location.href,
          title: document.title,
          bodyText: document.body ? document.body.innerText.slice(0, 2000) : "",
          userAgent: navigator.userAgent,
          webdriver: navigator.webdriver
        })
        """
    )


def _edge_path() -> str:
    candidates = [
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    ]
    for candidate in candidates:
        if os.path.exists(candidate):
            return candidate
    raise RuntimeError("Could not find Edge or Chrome executable")


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

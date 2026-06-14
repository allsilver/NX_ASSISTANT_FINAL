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

URLS = [
    "http://kor1.samsung.net/portalapp/home",
    "https://digitalworld.sec.samsung.net/export/forwardEdit.do?_menuId=AWPHvvjaABbwlNIR&_menuF=true",
]


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Copy storage state from a headed CDP browser into a fresh headless browser")
    parser.add_argument("--cdp", default="http://127.0.0.1:9236")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    async with async_playwright() as playwright:
        headed = await playwright.chromium.connect_over_cdp(args.cdp)
        headed_context = headed.contexts[0]
        storage_state = await headed_context.storage_state()

        browser = await playwright.chromium.launch(executable_path=_edge_path(), headless=True, args=["--no-first-run"])
        context = await browser.new_context(
            storage_state=storage_state,
            viewport={"width": 1365, "height": 900},
        )
        page = await context.new_page()
        results = []
        try:
            for index, url in enumerate(URLS):
                stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
                screenshot = ARTIFACT_DIR / f"storage-state-headless-{index}-{stamp}.png"
                await page.goto(url, wait_until="domcontentloaded", timeout=60000)
                try:
                    await page.wait_for_load_state("networkidle", timeout=15000)
                except Exception:
                    pass
                state = await _state(page)
                await page.screenshot(path=str(screenshot), full_page=False, timeout=15000)
                results.append({"inputUrl": url, "state": state, "screenshot": str(screenshot)})
        finally:
            await context.close()
            await browser.close()

        return {
            "storage": {
                "cookieCount": len(storage_state.get("cookies", [])),
                "originCount": len(storage_state.get("origins", [])),
            },
            "results": results,
        }


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
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
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

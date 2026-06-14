from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Click a coordinate in an attached CDP browser and capture state")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--x", type=int, required=True)
    parser.add_argument("--y", type=int, required=True)
    parser.add_argument("--screenshot", default="cdp-click-capture.png")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = context.pages[0]
        await page.mouse.click(args.x, args.y)
        await page.wait_for_timeout(2000)
        screenshot = str((ROOT / args.screenshot).resolve())
        await page.screenshot(path=screenshot, full_page=True)
        state = await page.evaluate(
            """
            () => ({
              url: location.href,
              title: document.title,
              bodyText: document.body ? document.body.innerText.slice(0, 1000) : "",
              buttonCount: document.querySelectorAll("button").length,
              inputCount: document.querySelectorAll("input, textarea, [contenteditable='true']").length
            })
            """
        )
        return {"clicked": {"x": args.x, "y": args.y}, "state": state, "screenshot": screenshot}


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

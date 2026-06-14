from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Fill a selector in an attached CDP browser and capture state")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--selector", required=True)
    parser.add_argument("--value", required=True)
    parser.add_argument("--screenshot", default="cdp-fill-capture.png")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        page = browser.contexts[0].pages[0]
        locator = page.locator(args.selector)
        count = await locator.count()
        if count != 1:
            raise RuntimeError(f"Selector must match exactly one element; matched {count}: {args.selector}")
        await locator.fill(args.value)
        screenshot = str((ROOT / args.screenshot).resolve())
        await page.screenshot(path=screenshot, full_page=True)
        state = await page.evaluate(
            """
            () => ({
              url: location.href,
              title: document.title,
              activeElement: document.activeElement ? {
                tag: document.activeElement.tagName,
                id: document.activeElement.id,
                value: document.activeElement.value
              } : null
            })
            """
        )
        return {"filled": {"selector": args.selector, "value": args.value}, "state": state, "screenshot": screenshot}


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

from __future__ import annotations

import argparse
import asyncio
import json


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Close a CDP-controlled browser")
    parser.add_argument("--cdp", default="http://127.0.0.1:9234")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        tabs = []
        for page in browser.contexts[0].pages:
            tabs.append({"url": page.url, "title": await page.title()})
        await browser.close()
        return {"closed": True, "tabs": tabs}


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

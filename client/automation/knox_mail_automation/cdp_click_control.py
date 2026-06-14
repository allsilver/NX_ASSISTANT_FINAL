from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Click one control in an attached CDP browser")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--selector")
    parser.add_argument("--role")
    parser.add_argument("--name")
    parser.add_argument("--text")
    parser.add_argument("--screenshot", default="cdp-click-control.png")
    parser.add_argument("--timeout-ms", type=int, default=15000)
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = context.pages[0]
        page.set_default_timeout(args.timeout_ms)

        before_pages = list(context.pages)
        locator = _locator(page, args)
        count = await locator.count()
        if count != 1:
            raise RuntimeError(f"Control must match exactly one element; matched {count}")

        await locator.click()
        try:
            await page.wait_for_load_state("domcontentloaded", timeout=5000)
        except Exception:
            pass
        await page.wait_for_timeout(3000)

        pages = list(context.pages)
        active_page = pages[-1] if len(pages) > len(before_pages) else page
        screenshot = str((ROOT / args.screenshot).resolve())
        await active_page.screenshot(path=screenshot, full_page=True)
        state = await active_page.evaluate(
            """
            () => ({
              url: location.href,
              title: document.title,
              bodyText: document.body ? document.body.innerText.slice(0, 1200) : "",
              controls: Array.from(document.querySelectorAll("input, textarea, button, a, [contenteditable='true']"))
                .slice(0, 80)
                .map((el, index) => ({
                  index,
                  tag: el.tagName.toLowerCase(),
                  type: el.type || null,
                  id: el.id || null,
                  placeholder: el.getAttribute("placeholder"),
                  aria: el.getAttribute("aria-label"),
                  text: (el.innerText || el.value || "").trim().slice(0, 120)
                }))
            })
            """
        )
        tabs = [{"url": tab.url, "title": await tab.title()} for tab in pages]
        return {"clicked": _description(args), "state": state, "tabs": tabs, "screenshot": screenshot}


def _locator(page, args: argparse.Namespace):
    if args.selector:
        return page.locator(args.selector)
    if args.role and args.name:
        return page.get_by_role(args.role, name=args.name)
    if args.text:
        return page.get_by_text(args.text, exact=True)
    raise ValueError("Pass --selector, --role + --name, or --text")


def _description(args: argparse.Namespace) -> dict[str, str | None]:
    return {"selector": args.selector, "role": args.role, "name": args.name, "text": args.text}


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

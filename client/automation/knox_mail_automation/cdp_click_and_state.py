from __future__ import annotations

import argparse
import asyncio
import json


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Click a CDP page control and print lightweight state")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--page-index", type=int, default=0)
    parser.add_argument("--selector")
    parser.add_argument("--text")
    parser.add_argument("--x", type=int)
    parser.add_argument("--y", type=int)
    parser.add_argument("--wait-ms", type=int, default=5000)
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        page = browser.contexts[0].pages[args.page_index]
        page.set_default_timeout(10000)
        if args.x is not None and args.y is not None:
            await page.mouse.click(args.x, args.y)
            await page.wait_for_timeout(args.wait_ms)
            return await _state(page)
        if args.selector:
            locator = page.locator(args.selector)
        elif args.text:
            locator = page.get_by_text(args.text, exact=True)
        else:
            raise ValueError("Pass --selector, --text, or --x + --y")
        count = await locator.count()
        if count != 1:
            raise RuntimeError(f"Click target must match exactly one element; matched {count}")
        await locator.click()
        await page.wait_for_timeout(args.wait_ms)
        return await _state(page)


async def _state(page) -> dict[str, object]:
    return await page.evaluate(
        """
        () => ({
          url: location.href,
          title: document.title,
          bodyText: document.body ? document.body.innerText.slice(0, 2000) : "",
          visibleButtonText: Array.from(document.querySelectorAll("button, a"))
            .filter((el) => {
              const rect = el.getBoundingClientRect();
              const style = getComputedStyle(el);
              return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
            })
            .map((el) => (el.innerText || el.getAttribute("aria-label") || "").trim())
            .filter(Boolean)
            .slice(0, 80)
        })
        """
    )


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

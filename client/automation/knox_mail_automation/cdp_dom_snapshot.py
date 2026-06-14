from __future__ import annotations

import argparse
import asyncio
import json


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Read interactable DOM summary from an attached CDP page")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        page = browser.contexts[0].pages[0]
        return await page.evaluate(
            """
            () => ({
              url: location.href,
              title: document.title,
              controls: Array.from(document.querySelectorAll("input, textarea, button, a, [contenteditable='true']"))
                .slice(0, 100)
                .map((el, index) => ({
                  index,
                  tag: el.tagName.toLowerCase(),
                  type: el.type || null,
                  id: el.id || null,
                  name: el.getAttribute("name"),
                  placeholder: el.getAttribute("placeholder"),
                  aria: el.getAttribute("aria-label"),
                  text: (el.innerText || el.value || "").trim().slice(0, 120),
                  href: el.getAttribute("href"),
                  contenteditable: el.getAttribute("contenteditable"),
                  className: String(el.className || "").slice(0, 160),
                  role: el.getAttribute("role"),
                  rect: (() => {
                    const rect = el.getBoundingClientRect();
                    return {
                      x: Math.round(rect.x),
                      y: Math.round(rect.y),
                      width: Math.round(rect.width),
                      height: Math.round(rect.height)
                    };
                  })()
                }))
            })
            """
        )


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

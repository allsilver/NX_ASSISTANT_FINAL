from __future__ import annotations

import argparse
import asyncio
import json


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="List visible buttons and links in an attached CDP page")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        page = browser.contexts[0].pages[0]
        return await page.evaluate(
            """
            () => {
              const visible = (el) => {
                const rect = el.getBoundingClientRect();
                const style = getComputedStyle(el);
                return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
              };
              const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
              return {
                url: location.href,
                title: document.title,
                bodyText: document.body ? document.body.innerText.slice(0, 2000) : "",
                controls: Array.from(document.querySelectorAll("button, a, input[type='button'], input[type='submit']"))
                  .filter(visible)
                  .map((el, index) => {
                    const rect = el.getBoundingClientRect();
                    return {
                      index,
                      tag: el.tagName.toLowerCase(),
                      text: label(el),
                      aria: el.getAttribute("aria-label"),
                      id: el.id || null,
                      className: String(el.className || "").slice(0, 140),
                      modal: Boolean(el.closest('[role="dialog"], .modal, .popup, .layer, .cui-dialog, .pt-dialog')),
                      rect: {
                        x: Math.round(rect.x),
                        y: Math.round(rect.y),
                        width: Math.round(rect.width),
                        height: Math.round(rect.height)
                      }
                    };
                  })
              };
            }
            """
        )


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

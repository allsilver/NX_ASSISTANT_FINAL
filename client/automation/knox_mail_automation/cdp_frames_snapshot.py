from __future__ import annotations

import argparse
import asyncio
import json


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Read frames and editable controls from an attached CDP page")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        page = browser.contexts[0].pages[0]
        frames = []
        for index, frame in enumerate(page.frames):
            try:
                data = await frame.evaluate(
                    """
                    () => ({
                      title: document.title,
                      url: location.href,
                      bodyText: document.body ? document.body.innerText.slice(0, 500) : "",
                      controls: Array.from(document.querySelectorAll("input, textarea, button, [contenteditable='true'], body[contenteditable='true']"))
                        .slice(0, 80)
                        .map((el, controlIndex) => ({
                          controlIndex,
                          tag: el.tagName.toLowerCase(),
                          type: el.type || null,
                          id: el.id || null,
                          placeholder: el.getAttribute("placeholder"),
                          aria: el.getAttribute("aria-label"),
                          contenteditable: el.getAttribute("contenteditable"),
                          className: String(el.className || "").slice(0, 120),
                          text: (el.innerText || el.value || "").trim().slice(0, 120),
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
            except Exception as exc:
                data = {"error": str(exc), "url": frame.url}
            frames.append({"frameIndex": index, **data})
        return {"pageUrl": page.url, "pageTitle": await page.title(), "frames": frames}


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

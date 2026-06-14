from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Open a URL in an attached CDP browser and capture lightweight state")
    parser.add_argument("url")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--new-tab", action="store_true")
    parser.add_argument("--screenshot-prefix", default="cdp-open")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = await context.new_page() if args.new_tab or not context.pages else context.pages[0]
        await page.goto(args.url, wait_until="domcontentloaded", timeout=45000)
        try:
            await page.wait_for_load_state("networkidle", timeout=10000)
        except Exception:
            pass
        path = ARTIFACT_DIR / f"{args.screenshot_prefix}.png"
        try:
            await page.screenshot(path=str(path), full_page=False, timeout=15000)
        except Exception as exc:
            path.with_suffix(".error.txt").write_text(str(exc), encoding="utf-8")
        state = await page.evaluate(
            """
            () => ({
              url: location.href,
              title: document.title,
              bodyText: document.body ? document.body.innerText.slice(0, 3000) : "",
              controls: Array.from(document.querySelectorAll("input, textarea, select, button, a, [contenteditable='true']"))
                .filter((el) => {
                  const rect = el.getBoundingClientRect();
                  const style = getComputedStyle(el);
                  return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
                })
                .slice(0, 180)
                .map((el, index) => {
                  const rect = el.getBoundingClientRect();
                  return {
                    index,
                    tag: el.tagName.toLowerCase(),
                    type: el.type || null,
                    id: el.id || null,
                    name: el.getAttribute("name"),
                    value: el.value || null,
                    placeholder: el.getAttribute("placeholder"),
                    aria: el.getAttribute("aria-label"),
                    text: (el.innerText || el.value || "").trim().slice(0, 160),
                    className: String(el.className || "").slice(0, 160),
                    rect: {
                      x: Math.round(rect.x),
                      y: Math.round(rect.y),
                      width: Math.round(rect.width),
                      height: Math.round(rect.height)
                    }
                  };
                })
            })
            """
        )
        return {"state": state, "screenshot": str(path), "tabs": [{"url": p.url, "title": await p.title()} for p in context.pages]}


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

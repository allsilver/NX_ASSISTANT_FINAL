from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Capture a lightweight snapshot for one CDP tab")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--page-index", type=int, default=0)
    parser.add_argument("--screenshot-prefix", default="page-snapshot")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        pages = browser.contexts[0].pages
        page = pages[args.page_index]
        try:
            await page.wait_for_load_state("networkidle", timeout=10000)
        except Exception:
            pass
        screenshot = ARTIFACT_DIR / f"{args.screenshot_prefix}.png"
        try:
            await page.screenshot(path=str(screenshot), full_page=False, timeout=15000)
        except Exception as exc:
            screenshot.with_suffix(".error.txt").write_text(str(exc), encoding="utf-8")
        state = await page.evaluate(
            """
            () => {
              const visible = (el) => {
                const rect = el.getBoundingClientRect();
                const style = getComputedStyle(el);
                return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
              };
              const labelOf = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
              return {
                url: location.href,
                title: document.title,
                bodyText: document.body ? document.body.innerText.slice(0, 5000) : "",
                controls: Array.from(document.querySelectorAll("input, textarea, select, button, a, [contenteditable='true']"))
                  .filter(visible)
                  .slice(0, 300)
                  .map((el, index) => {
                    const rect = el.getBoundingClientRect();
                    const options = el.tagName.toLowerCase() === "select"
                      ? Array.from(el.options).map((opt) => ({ text: opt.text, value: opt.value, selected: opt.selected })).slice(0, 60)
                      : undefined;
                    return {
                      index,
                      tag: el.tagName.toLowerCase(),
                      type: el.type || null,
                      id: el.id || null,
                      name: el.getAttribute("name"),
                      value: el.value || null,
                      placeholder: el.getAttribute("placeholder"),
                      aria: el.getAttribute("aria-label"),
                      text: labelOf(el).slice(0, 240),
                      className: String(el.className || "").slice(0, 160),
                      options,
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
        return {"state": state, "screenshot": str(screenshot)}


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

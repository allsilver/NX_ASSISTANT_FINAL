from __future__ import annotations

import argparse
import asyncio
import json
from datetime import datetime
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"
QUICK_URL_TOKEN = "digitalworld.sec.samsung.net/docDelivery/forwardDocDeliveryEdit.do"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Inspect Digital World quick delivery page structure")
    parser.add_argument("--cdp", default="http://127.0.0.1:9242")
    parser.add_argument("--page-index", type=int)
    parser.add_argument("--prefix", default="quick-delivery-inspect")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = _select_page(context.pages, args.page_index)
        try:
            await page.wait_for_load_state("networkidle", timeout=10000)
        except Exception:
            pass
        screenshot = ARTIFACT_DIR / f"{args.prefix}-{stamp}.png"
        await page.screenshot(path=str(screenshot), full_page=True, timeout=20000)
        snapshot = await page.evaluate(
            """
            () => {
              const visible = (el) => {
                const rect = el.getBoundingClientRect();
                const style = getComputedStyle(el);
                return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
              };
              const textOf = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
              const controlInfo = (el, index) => {
                const rect = el.getBoundingClientRect();
                const tag = el.tagName.toLowerCase();
                return {
                  index,
                  tag,
                  type: el.type || null,
                  id: el.id || null,
                  name: el.getAttribute("name"),
                  value: el.value || null,
                  checked: el.checked || false,
                  placeholder: el.getAttribute("placeholder"),
                  aria: el.getAttribute("aria-label"),
                  text: textOf(el).slice(0, 220),
                  className: String(el.className || "").slice(0, 180),
                  options: tag === "select"
                    ? Array.from(el.options).map((option) => ({
                        text: option.text.trim(),
                        value: option.value,
                        selected: option.selected
                      }))
                    : undefined,
                  rect: {
                    x: Math.round(rect.x),
                    y: Math.round(rect.y),
                    width: Math.round(rect.width),
                    height: Math.round(rect.height)
                  },
                  rowText: (el.closest("tr, .row, li, fieldset, section, article, div")?.innerText || "").trim().slice(0, 500)
                };
              };
              const rows = Array.from(document.querySelectorAll("tr"))
                .filter(visible)
                .map((row, index) => ({
                  index,
                  text: (row.innerText || "").trim().slice(0, 800),
                  controls: Array.from(row.querySelectorAll("input, textarea, select, button, a, [role='button']")).filter(visible).map((el, cindex) => controlInfo(el, cindex))
                }))
                .filter((row) => row.text || row.controls.length);
              const controls = Array.from(document.querySelectorAll("input, textarea, select, button, a, [role='button']"))
                .filter(visible)
                .map((el, index) => controlInfo(el, index));
              return {
                url: location.href,
                title: document.title,
                bodyText: document.body ? document.body.innerText.slice(0, 10000) : "",
                rows,
                controls
              };
            }
            """
        )
    result = {"ok": True, "screenshot": str(screenshot), "snapshot": snapshot}
    result_path = ARTIFACT_DIR / f"{args.prefix}-{stamp}.json"
    result_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return {"ok": True, "resultFile": str(result_path), "screenshot": str(screenshot), "title": snapshot["title"], "url": snapshot["url"]}


def _select_page(pages: list[Any], page_index: int | None) -> Any:
    if page_index is not None:
        return pages[page_index]
    for page in reversed(pages):
        if QUICK_URL_TOKEN in page.url:
            return page
    raise RuntimeError("Quick delivery page was not found in CDP browser")


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()


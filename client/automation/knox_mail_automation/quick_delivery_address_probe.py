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
    parser = argparse.ArgumentParser(description="Probe Digital World quick delivery address-book behavior")
    parser.add_argument("--cdp", default="http://127.0.0.1:9242")
    parser.add_argument("--page-index", type=int)
    parser.add_argument("--section", choices=["sender", "receiver"], default="sender")
    parser.add_argument("--query", default="")
    parser.add_argument("--prefix", default="quick-delivery-address-probe")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = _select_page(context.pages, args.page_index)
        page.set_default_timeout(15000)
        before_pages = list(context.pages)
        clicked = await _click_address_book(page, args.section)
        await page.wait_for_timeout(2500)
        after_pages = list(context.pages)
        target = after_pages[-1] if len(after_pages) > len(before_pages) else page

        if args.query:
            await _try_search(target, args.query)
            await target.wait_for_timeout(2000)

        screenshot = ARTIFACT_DIR / f"{args.prefix}-{args.section}-{stamp}.png"
        await target.screenshot(path=str(screenshot), full_page=True, timeout=20000)
        snapshot = await _snapshot(target)

    result = {
        "ok": True,
        "section": args.section,
        "query": args.query,
        "clicked": clicked,
        "targetUrl": snapshot["url"],
        "targetTitle": snapshot["title"],
        "screenshot": str(screenshot),
        "snapshot": snapshot,
    }
    result_path = ARTIFACT_DIR / f"{args.prefix}-{args.section}-{stamp}.json"
    result_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return {
        "ok": True,
        "section": args.section,
        "query": args.query,
        "resultFile": str(result_path),
        "screenshot": str(screenshot),
        "title": snapshot["title"],
        "url": snapshot["url"],
        "bodyHead": snapshot["bodyText"][:500],
    }


async def _click_address_book(page: Any, section: str) -> dict[str, Any]:
    return await page.evaluate(
        """
        ({ section }) => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
          const buttons = Array.from(document.querySelectorAll("a, button, input[type='button'], [role='button']"))
            .filter((el) => visible(el) && label(el) === "주소록")
            .map((el) => ({ el, rect: el.getBoundingClientRect() }))
            .sort((a, b) => a.rect.y - b.rect.y);
          const target = buttons[section === "sender" ? 0 : 1];
          if (!target) return { clicked: false, count: buttons.length };
          target.el.scrollIntoView({ block: "center", inline: "center" });
          target.el.click();
          return {
            clicked: true,
            count: buttons.length,
            rect: {
              x: Math.round(target.rect.x),
              y: Math.round(target.rect.y),
              width: Math.round(target.rect.width),
              height: Math.round(target.rect.height)
            }
          };
        }
        """,
        {"section": section},
    )


async def _try_search(page: Any, query: str) -> None:
    result = await page.evaluate(
        """
        ({ query }) => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const inputs = Array.from(document.querySelectorAll("input[type='text'], input:not([type]), textarea")).filter(visible);
          const preferred = inputs.find((el) => /검색|성명|상호|업체|주소/.test(el.placeholder || el.title || el.name || el.id || ""));
          const target = preferred || inputs[0];
          if (!target) return { filled: false, inputCount: inputs.length };
          const descriptor = Object.getOwnPropertyDescriptor(target.constructor.prototype, "value");
          if (descriptor && descriptor.set) descriptor.set.call(target, query);
          else target.value = query;
          target.dispatchEvent(new Event("input", { bubbles: true }));
          target.dispatchEvent(new Event("change", { bubbles: true }));
          return { filled: true, inputCount: inputs.length, id: target.id || null, name: target.name || null };
        }
        """,
        {"query": query},
    )
    if result.get("filled"):
        await page.keyboard.press("Enter")
        await page.wait_for_timeout(1000)
        await page.evaluate(
            """
            () => {
              const visible = (el) => {
                const rect = el.getBoundingClientRect();
                const style = getComputedStyle(el);
                return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
              };
              const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
              const target = Array.from(document.querySelectorAll("a, button, input[type='button'], input[type='submit']"))
                .filter((el) => visible(el) && /검색|조회/.test(label(el)))[0];
              if (target) target.click();
            }
            """
        )


async def _snapshot(page: Any) -> dict[str, Any]:
    return await page.evaluate(
        """
        () => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const textOf = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
          const controls = Array.from(document.querySelectorAll("input, textarea, select, button, a, [role='button']"))
            .filter(visible)
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
                text: textOf(el).slice(0, 240),
                rowText: (el.closest("tr, li, .row, div")?.innerText || "").trim().slice(0, 600),
                rect: {
                  x: Math.round(rect.x),
                  y: Math.round(rect.y),
                  width: Math.round(rect.width),
                  height: Math.round(rect.height)
                }
              };
            });
          const rows = Array.from(document.querySelectorAll("tr, li"))
            .filter(visible)
            .map((el, index) => ({ index, text: (el.innerText || "").trim().slice(0, 1000) }))
            .filter((row) => row.text);
          return {
            url: location.href,
            title: document.title,
            bodyText: document.body ? document.body.innerText.slice(0, 8000) : "",
            controls,
            rows
          };
        }
        """
    )


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


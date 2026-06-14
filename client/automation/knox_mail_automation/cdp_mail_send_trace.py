from __future__ import annotations

import argparse
import asyncio
import json
from typing import Any


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Trace Knox mail send confirmation network events")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--wait-ms", type=int, default=15000)
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    events: list[dict[str, Any]] = []

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        page = browser.contexts[0].pages[0]
        page.set_default_timeout(10000)

        def keep_url(url: str) -> bool:
            return any(token in url.lower() for token in ["mail", "formapp", "send", "message", "compose"])

        page.on("console", lambda msg: events.append({"type": "console", "level": msg.type, "text": msg.text[:500]}))
        page.on("pageerror", lambda exc: events.append({"type": "pageerror", "text": str(exc)[:500]}))
        page.on("request", lambda req: keep_url(req.url) and events.append({
            "type": "request",
            "method": req.method,
            "url": req.url[:500],
        }))

        async def on_response(resp):
            if not keep_url(resp.url):
                return
            item: dict[str, Any] = {
                "type": "response",
                "status": resp.status,
                "url": resp.url[:500],
            }
            try:
                ctype = resp.headers.get("content-type", "")
                if "json" in ctype or "text" in ctype or "html" in ctype:
                    body = await resp.text()
                    item["bodyHead"] = body[:600]
            except Exception as exc:
                item["bodyReadError"] = str(exc)[:160]
            events.append(item)

        page.on("response", lambda resp: asyncio.create_task(on_response(resp)))

        state_before = await _state(page)
        if "발신하시겠습니까?" not in state_before["bodyText"]:
            await _click_top_send(page)
            await page.wait_for_timeout(1500)

        modal_state = await _state(page)
        clicked_confirm = False
        if "발신하시겠습니까?" in modal_state["bodyText"]:
            confirm = page.locator("#btn-noti")
            if await confirm.count() == 1:
                await confirm.click()
                clicked_confirm = True
        await page.wait_for_timeout(args.wait_ms)
        state_after = await _state(page)
        return {
            "clickedConfirm": clicked_confirm,
            "before": _small_state(state_before),
            "modal": _small_state(modal_state),
            "after": _small_state(state_after),
            "events": events[-80:],
        }


async def _click_top_send(page: Any) -> None:
    clicked = await page.evaluate(
        """
        () => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const label = (el) => (el.innerText || el.getAttribute("aria-label") || "").trim();
          const candidates = Array.from(document.querySelectorAll("button"))
            .filter((el) => visible(el) && label(el) === "발신")
            .map((el) => ({ el, rect: el.getBoundingClientRect() }))
            .filter(({ rect }) => rect.y < 200)
            .sort((a, b) => a.rect.y - b.rect.y);
          if (!candidates[0]) return false;
          candidates[0].el.click();
          return true;
        }
        """
    )
    if not clicked:
        raise RuntimeError("Could not click top send button")


async def _state(page: Any) -> dict[str, Any]:
    return await page.evaluate(
        """
        () => ({
          url: location.href,
          title: document.title,
          bodyText: document.body ? document.body.innerText.slice(0, 2500) : ""
        })
        """
    )


def _small_state(state: dict[str, Any]) -> dict[str, str]:
    text = state["bodyText"]
    return {
        "url": state["url"],
        "title": state["title"],
        "hasConfirm": str("발신하시겠습니까?" in text),
        "hasRecipient": str("daeun.seo@samsung.com" in text),
        "hasDraft": str("제목을 입력하세요" in text or "받는 사람" in text),
        "textHead": text[:800],
    }


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

from __future__ import annotations

import argparse
import asyncio
import json
import time
from typing import Any


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Wait until a CDP browser appears logged into Knox/Digital World")
    parser.add_argument("--cdp", default="http://127.0.0.1:9234")
    parser.add_argument("--timeout-sec", type=int, default=300)
    parser.add_argument("--url", default="http://kor1.samsung.net/portalapp/home")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    deadline = time.time() + args.timeout_sec
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = context.pages[0] if context.pages else await context.new_page()
        if not page.url or page.url == "about:blank":
            await page.goto(args.url, wait_until="domcontentloaded")
        samples = []
        while time.time() < deadline:
            state = await _state(page)
            samples.append(_small(state))
            if _is_logged_in(state):
                return {"ok": True, "state": state, "samples": samples[-8:]}
            await page.wait_for_timeout(3000)
        return {"ok": False, "state": await _state(page), "samples": samples[-12:]}


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


def _is_logged_in(state: dict[str, Any]) -> bool:
    text = state.get("bodyText", "")
    url = state.get("url", "")
    if "사용자 세션이 만료되었습니다" in text:
        return False
    if "로그인" in text and "비밀번호" in text:
        return False
    if "portalapp/home" in url and ("메일" in text or "Knox Portal" in state.get("title", "")) and "사용자 세션" not in text:
        return True
    if "digitalworld.sec.samsung.net/export/" in url and "반출신청" in text:
        return True
    return False


def _small(state: dict[str, Any]) -> dict[str, str]:
    return {
        "url": state.get("url", ""),
        "title": state.get("title", ""),
        "textHead": state.get("bodyText", "")[:250],
    }


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

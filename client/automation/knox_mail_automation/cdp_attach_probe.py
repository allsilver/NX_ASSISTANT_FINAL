from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path
from urllib.parse import quote


ROOT = Path(__file__).resolve().parent.parent


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Attach to an already-open CDP browser and probe control")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--skip-internal", action="store_true")
    parser.add_argument("--close-browser", action="store_true")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0] if browser.contexts else await browser.new_context()
        page = context.pages[0] if context.pages else await context.new_page()

        result: dict[str, object] = {
            "cdp": args.cdp,
            "initialUrl": page.url,
            "localControl": await _local_control(page),
        }
        if not args.skip_internal:
            result["portalHome"] = await _capture_state(
                page,
                "http://kor1.samsung.net/portalapp/home",
                "cdp-probe-portal-home.png",
            )
        if args.close_browser:
            await browser.close()
        return result


async def _local_control(page) -> dict[str, object]:
    html = """
    <!doctype html>
    <meta charset="utf-8">
    <title>CDP Attach Probe</title>
    <h1>CDP Attach Probe</h1>
    <input id="recipient" aria-label="recipient">
    <textarea id="body" aria-label="body"></textarea>
    <button id="prepare">Prepare</button>
    <output id="result"></output>
    <script>
      prepare.onclick = () => result.textContent = `${recipient.value}::${body.value}`;
    </script>
    """
    await page.goto("data:text/html;charset=utf-8," + quote(html), wait_until="load")
    await page.locator("#recipient").fill("daeun.seo")
    await page.locator("#body").fill("hi")
    await page.locator("#prepare").click()
    text = await page.locator("#result").inner_text()
    screenshot = str((ROOT / "cdp-probe-local.png").resolve())
    await page.screenshot(path=screenshot, full_page=True)
    return {"ok": text == "daeun.seo::hi", "resultText": text, "screenshot": screenshot}


async def _capture_state(page, url: str, screenshot_name: str) -> dict[str, object]:
    await page.goto(url, wait_until="domcontentloaded")
    state = await page.evaluate(
        """
        () => ({
          url: location.href,
          title: document.title,
          bodyText: document.body ? document.body.innerText.slice(0, 1000) : "",
          buttonCount: document.querySelectorAll("button").length,
          inputCount: document.querySelectorAll("input, textarea, [contenteditable='true']").length
        })
        """
    )
    screenshot = str((ROOT / screenshot_name).resolve())
    await page.screenshot(path=screenshot, full_page=True)
    return {"state": state, "screenshot": screenshot}


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

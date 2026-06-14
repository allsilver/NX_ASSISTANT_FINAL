from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Prepare a mail draft in the attached Knox compose window")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--body", default="hi")
    parser.add_argument("--subject", default="")
    parser.add_argument("--screenshot", default="cdp-mail-draft-prepared.png")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    from playwright.async_api import async_playwright

    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        page = browser.contexts[0].pages[0]
        page.set_default_timeout(15000)

        if "formapp" not in page.url:
            raise RuntimeError(f"Expected compose window, got {page.url}")

        # The current draft has the user in CC. Click the row-level "수신" button
        # to move the visible recipient into the To bucket. Coordinates are CSS
        # pixels from the measured DOM rect in cdp_frames_snapshot.py.
        await page.mouse.click(152, 246)
        await page.wait_for_timeout(1000)

        if args.subject:
            await page.locator("#subject").fill(args.subject)

        editor_frame = None
        for frame in page.frames:
            try:
                if await frame.locator("#cafe-note-contents").count() == 1:
                    editor_frame = frame
                    break
            except Exception:
                continue
        if editor_frame is None:
            raise RuntimeError("Could not find mail body editor frame")

        await editor_frame.locator("#cafe-note-contents").fill(args.body)
        await page.wait_for_timeout(1000)

        screenshot = str((ROOT / args.screenshot).resolve())
        await page.screenshot(path=screenshot, full_page=True)

        state = await page.evaluate(
            """
            () => ({
              url: location.href,
              title: document.title,
              subject: document.querySelector("#subject")?.value ?? "",
              topText: document.body ? document.body.innerText.slice(0, 1500) : "",
              sendButtonCount: Array.from(document.querySelectorAll("button")).filter((button) =>
                (button.innerText || button.getAttribute("aria-label") || "").includes("발신")
              ).length
            })
            """
        )
        body_text = await editor_frame.locator("#cafe-note-contents").inner_text()
        return {
            "ok": True,
            "mode": "prepare-only",
            "sent": False,
            "state": state,
            "bodyText": body_text,
            "screenshot": screenshot,
        }


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

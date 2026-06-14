from __future__ import annotations

import argparse
import asyncio
import json
import os
from pathlib import Path
from urllib.parse import quote


ROOT = Path(__file__).resolve().parent.parent


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Step-by-step browser control probe")
    parser.add_argument("--browser-executable", default=_default_browser_path())
    parser.add_argument("--profile-dir", default=".browser-probe-profile")
    parser.add_argument("--headless", action="store_true")
    parser.add_argument("--skip-internal", action="store_true")
    parser.add_argument("--timeout-ms", type=int, default=20000)
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, object]:
    if not args.browser_executable:
        raise RuntimeError("No Chrome/Edge executable found. Pass --browser-executable.")

    from playwright.async_api import async_playwright

    results: dict[str, object] = {
        "browserExecutable": args.browser_executable,
        "profileDir": str(Path(args.profile_dir).resolve()),
    }

    async with async_playwright() as playwright:
        context = await playwright.chromium.launch_persistent_context(
            user_data_dir=args.profile_dir,
            executable_path=args.browser_executable,
            headless=args.headless,
            ignore_https_errors=True,
            args=[
                "--no-first-run",
                "--disable-features=Translate",
            ],
        )
        page = context.pages[0] if context.pages else await context.new_page()
        page.set_default_timeout(args.timeout_ms)

        results["localControl"] = await _probe_local_control(page)
        if not args.skip_internal:
            results["portalHome"] = await _probe_url(
                page,
                "http://kor1.samsung.net/portalapp/home",
                "probe-portal-home.png",
            )
            results["mailApp"] = await _probe_url(
                page,
                "http://kor1.samsung.net/mailapp/",
                "probe-mailapp.png",
                wait_after_load_ms=8000,
            )

        await context.close()

    return results


async def _probe_local_control(page) -> dict[str, object]:
    html = """
    <!doctype html>
    <html lang="ko">
    <head>
      <meta charset="utf-8" />
      <title>Browser Probe</title>
      <style>
        body { font-family: Arial, sans-serif; margin: 32px; }
        label { display: block; margin: 12px 0 4px; }
        input, textarea, button { font: inherit; padding: 8px; width: 320px; }
        textarea { height: 100px; }
        button { width: auto; margin-top: 12px; }
        #result { margin-top: 16px; font-weight: 700; }
      </style>
    </head>
    <body>
      <h1>Browser Probe</h1>
      <label for="recipient">Recipient</label>
      <input id="recipient" />
      <label for="body">Body</label>
      <textarea id="body"></textarea>
      <button id="prepare" type="button">Prepare</button>
      <div id="result"></div>
      <script>
        document.querySelector("#prepare").addEventListener("click", () => {
          const recipient = document.querySelector("#recipient").value;
          const body = document.querySelector("#body").value;
          document.querySelector("#result").textContent = `${recipient}::${body}`;
        });
      </script>
    </body>
    </html>
    """
    await page.goto("data:text/html;charset=utf-8," + quote(html), wait_until="load")
    await page.locator("#recipient").fill("daeun.seo")
    await page.locator("#body").fill("hi")
    await page.locator("#prepare").click()
    result = await page.locator("#result").inner_text()
    screenshot = str((ROOT / "probe-local-control.png").resolve())
    await page.screenshot(path=screenshot, full_page=True)
    return {
        "ok": result == "daeun.seo::hi",
        "resultText": result,
        "screenshot": screenshot,
    }


async def _probe_url(
    page,
    url: str,
    screenshot_name: str,
    wait_after_load_ms: int = 0,
) -> dict[str, object]:
    console_messages: list[str] = []
    page.on(
        "console",
        lambda message: console_messages.append(f"{message.type}: {message.text}"[:300]),
    )
    page.on(
        "pageerror",
        lambda error: console_messages.append(f"pageerror: {error}"[:300]),
    )

    load_error = None
    try:
        await page.goto(url, wait_until="domcontentloaded")
        try:
            await page.wait_for_load_state("networkidle", timeout=5000)
        except Exception:
            pass
        if wait_after_load_ms:
            await page.wait_for_timeout(wait_after_load_ms)
    except Exception as exc:
        load_error = str(exc)

    state = await page.evaluate(
        """
        () => ({
          url: location.href,
          title: document.title,
          bodyText: document.body ? document.body.innerText.slice(0, 1000) : "",
          rootText: document.querySelector("#root") ? document.querySelector("#root").innerText.slice(0, 1000) : "",
          rootChildCount: document.querySelector("#root") ? document.querySelector("#root").childElementCount : null,
          buttonCount: document.querySelectorAll("button").length,
          inputCount: document.querySelectorAll("input, textarea, [contenteditable='true']").length,
          linkCount: document.querySelectorAll("a").length,
          spinnerLikeCount: document.querySelectorAll("[class*='spinner' i], [class*='loading' i], [class*='progress' i]").length
        })
        """
    )
    screenshot = str((ROOT / screenshot_name).resolve())
    await page.screenshot(path=screenshot, full_page=True)
    return {
        "ok": load_error is None,
        "loadError": load_error,
        "state": state,
        "console": console_messages[-20:],
        "screenshot": screenshot,
    }


def _default_browser_path() -> str | None:
    candidates = [
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    ]
    return next((path for path in candidates if os.path.exists(path)), None)


def main() -> None:
    result = asyncio.run(main_async(build_parser().parse_args()))
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

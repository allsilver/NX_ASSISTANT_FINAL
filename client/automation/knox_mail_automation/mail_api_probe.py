from __future__ import annotations

import argparse
import asyncio
import json
from datetime import datetime
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"
MAIL_URL = "http://kor1.samsung.net/formapp/?initModule=mail"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Inspect Knox mail frontend bundles for send API hints")
    parser.add_argument("--cdp", default="http://127.0.0.1:9242")
    parser.add_argument("--max-scripts", type=int, default=80)
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = await context.new_page()
        page.set_default_timeout(20000)
        await page.goto(MAIL_URL, wait_until="domcontentloaded", timeout=60000)
        try:
            await page.wait_for_load_state("networkidle", timeout=15000)
        except Exception:
            pass

        scripts = await page.evaluate(
            """
            () => Array.from(document.scripts)
              .map((script) => script.src)
              .filter(Boolean)
            """
        )
        snippets: list[dict[str, Any]] = []
        for script_url in scripts[: args.max_scripts]:
            try:
                text = await page.evaluate(
                    """
                    async (url) => {
                      const response = await fetch(url, { credentials: 'include' });
                      if (!response.ok) return '';
                      return await response.text();
                    }
                    """,
                    script_url,
                )
            except Exception as exc:
                snippets.append({"script": script_url, "error": str(exc)[:300]})
                continue

            lowered = text.lower()
            tokens = [
                "mails/send",
                "function createmailvo",
                "createmailvo=",
                "createmailvo(",
                "mailapi.sendmail",
                ".sendmail",
                "sendmail:",
                "sendmail(",
                "sendmail,",
                "send_mail",
                "mail/rest",
                "sendmessage",
            ]
            matched = [token for token in tokens if token in lowered]
            if not matched:
                continue
            for token in matched:
                start_at = 0
                found = 0
                while found < 12:
                    index = lowered.find(token, start_at)
                    if index < 0:
                        break
                    window_before = 1400
                    window_after = 14000 if token in {"function createmailvo", "createmailvo="} else 2200
                    start = max(0, index - window_before)
                    end = min(len(text), index + window_after)
                    snippets.append(
                        {
                            "script": script_url,
                            "token": token,
                            "offset": index,
                            "snippet": text[start:end],
                        }
                    )
                    start_at = index + len(token)
                    found += 1

        state = await page.evaluate(
            """
            () => ({
              url: location.href,
              title: document.title,
              scriptCount: document.scripts.length,
              bodyText: document.body ? document.body.innerText.slice(0, 1000) : ''
            })
            """
        )

    result = {
        "ok": True,
        "state": state,
        "scriptCount": len(scripts),
        "matches": len(snippets),
        "snippets": snippets,
    }
    result_path = ARTIFACT_DIR / f"mail-api-probe-{stamp}.json"
    result_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    result["resultFile"] = str(result_path)
    return result


def main() -> None:
    result = asyncio.run(main_async(build_parser().parse_args()))
    print(
        json.dumps(
            {
                "ok": result["ok"],
                "scriptCount": result["scriptCount"],
                "matches": result["matches"],
                "resultFile": result["resultFile"],
            },
            ensure_ascii=True,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()

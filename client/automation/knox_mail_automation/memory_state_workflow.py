from __future__ import annotations

import argparse
import asyncio
import json
import os
import socket
import subprocess
import time
import urllib.error
import urllib.request
from datetime import datetime
from pathlib import Path
from types import SimpleNamespace
from typing import Any

from . import cdp_export_forward, cdp_send_mail


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"
PORTAL_URL = "http://kor1.samsung.net/portalapp/home"
EXPORT_URL = cdp_export_forward.EXPORT_URL
CREATE_NEW_PROCESS_GROUP = 0x00000200
DETACHED_PROCESS = 0x00000008


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Warm up SSO in a visible Edge profile, copy storage state in memory, "
            "send Knox mail headlessly, fill Digital World export headlessly, "
            "then show a visible browser before save."
        )
    )
    parser.add_argument("--broker-profile-dir", default=".nx-mcp-automation-profile")
    parser.add_argument("--handoff-profile-dir", default=".nx-mcp-handoff-profile")
    parser.add_argument("--capture-profile-dir", default=".nx-mcp-mail-capture-profile")
    parser.add_argument("--broker-port", type=int, default=9241)
    parser.add_argument("--handoff-port", type=int, default=9242)
    parser.add_argument("--capture-port", type=int, default=9243)
    parser.add_argument("--login-timeout-sec", type=int, default=300)
    parser.add_argument("--recipient", default="daeun.seo")
    parser.add_argument("--subject", default="성공")
    parser.add_argument("--body", default="test")
    parser.add_argument("--allow-send", action="store_true", help="Actually send Knox mail. Without this, mail send is skipped.")
    parser.add_argument("--destination", default="사외")
    parser.add_argument("--destination-detail", default="대동")
    parser.add_argument("--purpose", default="업무")
    parser.add_argument("--purpose-detail", default="업무")
    parser.add_argument("--return-yn", default="N", choices=["Y", "N"])
    parser.add_argument("--quick-yn", default="N", choices=["Y", "N"])
    parser.add_argument("--item-category", default="자재/금형")
    parser.add_argument("--item-name", default="tape")
    parser.add_argument("--item-model", default="sm")
    parser.add_argument("--item-serial", default="없음")
    parser.add_argument("--item-quantity", default="3")
    parser.add_argument("--width", type=int, default=1365)
    parser.add_argument("--height", type=int, default=900)
    parser.add_argument("--keep-broker-open", action="store_true")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    started_at = datetime.now().strftime("%Y%m%d-%H%M%S")

    async with async_playwright() as playwright:
        broker_browser = await _start_or_attach_broker(playwright, args)
        try:
            broker_context = broker_browser.contexts[0]
            await _warm_up_sites(broker_context, args)
            storage_state = await broker_context.storage_state()
            browser_fingerprint = await _browser_fingerprint(broker_context)
        finally:
            if not args.keep_broker_open:
                await _safe_close_browser(broker_browser)

        headless_browser = await playwright.chromium.launch(
            executable_path=_edge_path(),
            headless=True,
            args=[
                "--no-first-run",
                "--disable-blink-features=AutomationControlled",
                f"--window-size={args.width},{args.height}",
            ],
        )
        headless_context = await headless_browser.new_context(
            storage_state=storage_state,
            viewport={"width": args.width, "height": args.height},
            locale="ko-KR",
            user_agent=browser_fingerprint.get("userAgent") or None,
        )
        await headless_context.add_init_script(
            """
            Object.defineProperty(navigator, 'webdriver', {
              get: () => undefined
            });
            """
        )
        try:
            if args.allow_send:
                mail_result = await _send_mail_headless(playwright, headless_context, args, started_at)
            else:
                mail_result = {
                    "ok": True,
                    "sent": False,
                    "recipient": args.recipient,
                    "subject": args.subject,
                    "detail": "mail skipped; --allow-send was not provided",
                }
            export_headless = await _fill_export_headless(headless_context, args, started_at)
            handoff_state = await headless_context.storage_state()
        finally:
            await headless_context.close()
            await headless_browser.close()

        handoff_result = await _show_export_before_save(playwright, handoff_state, args, started_at)

    payload = {
        "ok": bool(mail_result["ok"] and export_headless["ok"] and handoff_result["ok"]),
        "mode": "headed-sso-warmup-memory-state-headless-work-handoff-before-save",
        "storage": {
            "cookieCount": len(storage_state.get("cookies", [])),
            "originCount": len(storage_state.get("origins", [])),
            "savedToDisk": False,
            "returnedToLlm": False,
        },
        "headlessFingerprint": {
            "usesBrokerUserAgent": bool(browser_fingerprint.get("userAgent")),
            "webdriverMasked": True,
        },
        "mail": mail_result,
        "exportHeadless": export_headless,
        "handoff": handoff_result,
        "security": "storage state was copied in process memory only and was not written to the result file",
    }
    result_path = ARTIFACT_DIR / f"memory-state-workflow-result-{started_at}.json"
    result_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    payload["resultFile"] = str(result_path)
    return payload


async def _start_or_attach_broker(playwright: Any, args: argparse.Namespace) -> Any:
    profile_dir = (ROOT / args.broker_profile_dir).resolve()
    if not _is_port_open("127.0.0.1", args.broker_port):
        profile_dir.mkdir(parents=True, exist_ok=True)
        _launch_edge(
            profile_dir=profile_dir,
            port=args.broker_port,
            urls=[PORTAL_URL, EXPORT_URL],
            width=args.width,
            height=args.height,
            visible=True,
        )
        _wait_for_cdp(args.broker_port, timeout_s=25)
    return await playwright.chromium.connect_over_cdp(f"http://127.0.0.1:{args.broker_port}")


async def _warm_up_sites(context: Any, args: argparse.Namespace) -> None:
    portal_page = await _goto_or_new(context, PORTAL_URL)
    await _wait_until_logged_in(portal_page, args.login_timeout_sec, "portal")

    export_page = await _goto_or_new(context, EXPORT_URL)
    await _wait_until_logged_in(export_page, args.login_timeout_sec, "digitalworld")


async def _browser_fingerprint(context: Any) -> dict[str, Any]:
    page = context.pages[0] if context.pages else await context.new_page()
    return await page.evaluate(
        """
        () => ({
          userAgent: navigator.userAgent,
          platform: navigator.platform,
          webdriver: navigator.webdriver
        })
        """
    )


async def _goto_or_new(context: Any, url: str) -> Any:
    page = next((candidate for candidate in context.pages if _same_site_page(candidate.url, url)), None)
    if page is None:
        page = await context.new_page()
    await page.goto(url, wait_until="domcontentloaded", timeout=60000)
    try:
        await page.wait_for_load_state("networkidle", timeout=15000)
    except Exception:
        pass
    return page


def _same_site_page(current_url: str, target_url: str) -> bool:
    if "kor1.samsung.net" in target_url:
        return "kor1.samsung.net" in current_url
    if "digitalworld.sec.samsung.net/export/" in target_url:
        return "digitalworld.sec.samsung.net/export/" in current_url
    return current_url == target_url


async def _wait_until_logged_in(page: Any, timeout_sec: int, site: str) -> None:
    deadline = time.time() + timeout_sec
    while time.time() < deadline:
        state = await _state(page)
        if _is_logged_in(state, site):
            return
        await page.wait_for_timeout(3000)
    state = await _state(page)
    preview = state["bodyText"][:500].replace("\n", " / ")
    raise RuntimeError(f"{site} login/warm-up did not finish. url={state['url']} title={state['title']} text={preview}")


def _is_logged_in(state: dict[str, str], site: str) -> bool:
    text = state.get("bodyText", "")
    url = state.get("url", "")
    title = state.get("title", "")
    if "사용자 세션이 만료되었습니다" in text:
        return False
    if "로그인" in text and "비밀번호" in text:
        return False
    if site == "portal":
        return "portalapp/home" in url and ("메일" in text or "Knox Portal" in title)
    if site == "digitalworld":
        return "digitalworld.sec.samsung.net/export/" in url and "반출신청" in text
    return False


async def _send_mail_headless(playwright: Any, context: Any, args: argparse.Namespace, stamp: str) -> dict[str, Any]:
    page = await cdp_send_mail._get_or_open_compose_page(context)
    page.set_default_timeout(20000)
    try:
        await cdp_send_mail._ensure_recipient(page, args.recipient)
        await cdp_send_mail._assert_recipient_is_to(page)
    except Exception as exc:
        diagnostic_path = ARTIFACT_DIR / f"memory-state-mail-recipient-error-{stamp}.png"
        state_path = ARTIFACT_DIR / f"memory-state-mail-recipient-error-{stamp}.json"
        await cdp_send_mail._safe_screenshot(page, diagnostic_path)
        state_path.write_text(json.dumps(await _state(page), ensure_ascii=False, indent=2), encoding="utf-8")
        raise RuntimeError(f"mail recipient setup failed; screenshot={diagnostic_path}; state={state_path}; error={exc}") from exc
    await page.locator("#subject").fill(args.subject)
    await cdp_send_mail._fill_body(page, args.body)

    prepared_path = ARTIFACT_DIR / f"memory-state-mail-prepared-{stamp}.png"
    await cdp_send_mail._safe_screenshot(page, prepared_path)

    send_response_task = asyncio.create_task(cdp_send_mail._wait_for_send_response(page))
    await cdp_send_mail._click_send(page)
    result = await cdp_send_mail._finish_send_flow(page, send_response_task)
    fallback_result: dict[str, Any] | None = None
    if not result["ok"]:
        capture = await _capture_mail_send_payload(playwright, await context.storage_state(), args, stamp)
        fallback_result = await _post_mail_payload_headless(page, capture["postData"])
        if fallback_result["ok"]:
            result = {
                "ok": True,
                "detail": f"direct REST send via captured payload status {fallback_result['status']}",
            }

    final_path = ARTIFACT_DIR / f"memory-state-mail-final-{stamp}.png"
    await cdp_send_mail._safe_screenshot(page, final_path)
    state = await _state(page)
    return {
        "ok": result["ok"],
        "sent": result["ok"],
        "recipient": args.recipient,
        "subject": args.subject,
        "detail": result["detail"],
        "fallback": fallback_result,
        "url": state["url"],
        "title": state["title"],
        "screenshots": {
            "prepared": str(prepared_path),
            "final": str(final_path),
        },
    }


async def _capture_mail_send_payload(
    playwright: Any,
    storage_state: dict[str, Any],
    args: argparse.Namespace,
    stamp: str,
) -> dict[str, Any]:
    profile_dir = (ROOT / args.capture_profile_dir).resolve()
    if not _is_port_open("127.0.0.1", args.capture_port):
        profile_dir.mkdir(parents=True, exist_ok=True)
        _launch_edge(
            profile_dir=profile_dir,
            port=args.capture_port,
            urls=["about:blank"],
            width=args.width,
            height=args.height,
            visible=False,
        )
        _wait_for_cdp(args.capture_port, timeout_s=25)

    browser = await playwright.chromium.connect_over_cdp(f"http://127.0.0.1:{args.capture_port}")
    context = browser.contexts[0]
    await context.add_cookies(storage_state.get("cookies", []))
    page = await context.new_page()
    page.set_default_timeout(20000)

    captured: dict[str, Any] = {}

    async def handle_send(route: Any) -> None:
        request = route.request
        captured["postData"] = request.post_data or ""
        captured["url"] = request.url
        await route.fulfill(
            status=200,
            content_type="application/json;charset=utf-8",
            body=json.dumps({"mailID": f"captured-{stamp}", "resultStr": "OK"}),
        )

    await page.route("**/mail/rest/v1/mails/send", handle_send)
    try:
        await page.goto("http://kor1.samsung.net/formapp/?initModule=mail", wait_until="domcontentloaded", timeout=60000)
        try:
            await page.wait_for_load_state("networkidle", timeout=15000)
        except Exception:
            pass
        await cdp_send_mail._ensure_recipient(page, args.recipient)
        await cdp_send_mail._assert_recipient_is_to(page)
        await page.locator("#subject").fill(args.subject)
        await cdp_send_mail._fill_body(page, args.body)
        screenshot_path = ARTIFACT_DIR / f"memory-state-mail-capture-prepared-{stamp}.png"
        await cdp_send_mail._safe_screenshot(page, screenshot_path)
        send_response_task = asyncio.create_task(cdp_send_mail._wait_for_send_response(page))
        await cdp_send_mail._click_send(page)
        await cdp_send_mail._finish_send_flow(page, send_response_task)
        if not captured.get("postData"):
            state = await _state(page)
            raise RuntimeError(f"send payload was not captured; title={state['title']} text={state['bodyText'][:300]}")
        return {
            "ok": True,
            "url": captured.get("url", ""),
            "postData": captured["postData"],
            "screenshot": str(screenshot_path),
        }
    finally:
        await _safe_close_browser(browser)


async def _post_mail_payload_headless(page: Any, post_data: str) -> dict[str, Any]:
    response = await page.evaluate(
        """
        async (postData) => {
          const response = await fetch('/mail/rest/v1/mails/send', {
            method: 'POST',
            credentials: 'include',
            headers: {
              'Content-Type': 'application/json;charset=utf-8'
            },
            body: postData
          });
          const text = await response.text();
          return {
            ok: response.ok,
            status: response.status,
            bodyHead: text.slice(0, 800)
          };
        }
        """,
        post_data,
    )
    return {
        "ok": bool(response.get("ok")),
        "status": response.get("status"),
        "bodyHead": response.get("bodyHead", ""),
    }


async def _fill_export_headless(context: Any, args: argparse.Namespace, stamp: str) -> dict[str, Any]:
    page = await context.new_page()
    page.set_default_timeout(20000)
    await page.goto(EXPORT_URL, wait_until="domcontentloaded", timeout=60000)
    try:
        await page.wait_for_load_state("networkidle", timeout=15000)
    except Exception:
        pass
    await _dismiss_noncritical_notices(page)
    export_args = _export_args(args)
    await cdp_export_forward._fill_export_form(page, export_args)
    screenshot_path = ARTIFACT_DIR / f"memory-state-export-headless-before-save-{stamp}.png"
    await cdp_export_forward._safe_screenshot(page, screenshot_path, full_page=True)
    state = await _state(page)
    return {
        "ok": "digitalworld.sec.samsung.net/export/" in state["url"] and "반출신청" in state["bodyText"],
        "saved": False,
        "url": state["url"],
        "title": state["title"],
        "screenshot": str(screenshot_path),
        "filled": _filled_summary(args),
    }


async def _show_export_before_save(
    playwright: Any,
    storage_state: dict[str, Any],
    args: argparse.Namespace,
    stamp: str,
) -> dict[str, Any]:
    profile_dir = (ROOT / args.handoff_profile_dir).resolve()
    if not _is_port_open("127.0.0.1", args.handoff_port):
        profile_dir.mkdir(parents=True, exist_ok=True)
        _launch_edge(
            profile_dir=profile_dir,
            port=args.handoff_port,
            urls=["about:blank"],
            width=args.width,
            height=args.height,
            visible=True,
        )
        _wait_for_cdp(args.handoff_port, timeout_s=25)

    browser = await playwright.chromium.connect_over_cdp(f"http://127.0.0.1:{args.handoff_port}")
    context = browser.contexts[0]
    await context.add_cookies(storage_state.get("cookies", []))
    page = await context.new_page()
    page.set_default_timeout(20000)
    await page.set_viewport_size({"width": args.width, "height": args.height})
    await page.goto(EXPORT_URL, wait_until="domcontentloaded", timeout=60000)
    try:
        await page.wait_for_load_state("networkidle", timeout=15000)
    except Exception:
        pass
    await _dismiss_noncritical_notices(page)
    await cdp_export_forward._fill_export_form(page, _export_args(args))
    await _dismiss_noncritical_notices(page)
    await page.bring_to_front()
    screenshot_path = ARTIFACT_DIR / f"memory-state-export-visible-before-save-{stamp}.png"
    await cdp_export_forward._safe_screenshot(page, screenshot_path, full_page=True)
    state = await _state(page)
    return {
        "ok": "digitalworld.sec.samsung.net/export/" in state["url"] and "반출신청" in state["bodyText"],
        "saved": False,
        "visibleBrowser": True,
        "cdp": f"http://127.0.0.1:{args.handoff_port}",
        "url": state["url"],
        "title": state["title"],
        "screenshot": str(screenshot_path),
    }


async def _dismiss_noncritical_notices(page: Any) -> None:
    for _ in range(4):
        clicked = await page.evaluate(
            """
            () => {
              const visible = (el) => {
                const rect = el.getBoundingClientRect();
                const style = getComputedStyle(el);
                return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < window.innerHeight
                  && style.visibility !== "hidden" && style.display !== "none";
              };
              const textOf = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
              const noticeRoots = Array.from(document.querySelectorAll('[role="dialog"], .modal, .popup, .layer, .ui-dialog, .pop, .alert, body > div'))
                .filter((el) => visible(el))
                .filter((el) => {
                  const text = (el.innerText || "").slice(0, 1200);
                  return /(공지|안내|장기미반입|조치강화|Notice|Information)/i.test(text);
                })
                .sort((a, b) => b.getBoundingClientRect().width * b.getBoundingClientRect().height - a.getBoundingClientRect().width * a.getBoundingClientRect().height);
              const roots = noticeRoots.length ? noticeRoots : [document.body];
              for (const root of roots) {
                const candidates = Array.from(root.querySelectorAll('button, a, input[type="button"], [role="button"]'))
                  .filter((el) => visible(el))
                  .filter((el) => {
                    const label = textOf(el);
                    return label === "닫기" || label === "Close" || label === "×" || label === "X";
                  })
                  .sort((a, b) => {
                    const ar = a.getBoundingClientRect();
                    const br = b.getBoundingClientRect();
                    return br.y - ar.y || br.x - ar.x;
                  });
                if (candidates[0]) {
                  candidates[0].click();
                  return true;
                }
              }
              return false;
            }
            """
        )
        if not clicked:
            return
        await page.wait_for_timeout(800)


def _export_args(args: argparse.Namespace) -> SimpleNamespace:
    return SimpleNamespace(
        page_index=None,
        destination=args.destination,
        destination_detail=args.destination_detail,
        purpose=args.purpose,
        purpose_detail=args.purpose_detail,
        return_yn=args.return_yn,
        quick_yn=args.quick_yn,
        item_category=args.item_category,
        item_name=args.item_name,
        item_model=args.item_model,
        item_serial=args.item_serial,
        item_quantity=args.item_quantity,
    )


def _filled_summary(args: argparse.Namespace) -> dict[str, Any]:
    return {
        "destination": args.destination,
        "destinationDetail": args.destination_detail,
        "purpose": args.purpose,
        "purposeDetail": args.purpose_detail,
        "returnYn": args.return_yn,
        "quickYn": args.quick_yn,
        "item": {
            "category": args.item_category,
            "name": args.item_name,
            "model": args.item_model,
            "serial": args.item_serial,
            "quantity": args.item_quantity,
        },
    }


async def _state(page: Any) -> dict[str, str]:
    return await page.evaluate(
        """
        () => ({
          url: location.href,
          title: document.title,
          bodyText: document.body ? document.body.innerText.slice(0, 5000) : ""
        })
        """
    )


async def _safe_close_browser(browser: Any) -> None:
    try:
        await browser.close()
    except Exception:
        pass


def _launch_edge(
    *,
    profile_dir: Path,
    port: int,
    urls: list[str],
    width: int,
    height: int,
    visible: bool,
) -> None:
    args = [
        _edge_path(),
        f"--remote-debugging-port={port}",
        "--remote-debugging-address=127.0.0.1",
        f"--user-data-dir={profile_dir}",
        "--no-first-run",
        f"--window-size={width},{height}",
        "--new-window",
        *urls,
    ]
    if not visible:
        args.insert(-len(urls) if urls else len(args), "--window-position=-32000,-32000")
    creationflags = CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS
    startupinfo = None
    if not visible:
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startupinfo.wShowWindow = 0
    subprocess.Popen(
        args,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        stdin=subprocess.DEVNULL,
        creationflags=creationflags,
        startupinfo=startupinfo,
    )


def _wait_for_cdp(port: int, timeout_s: int) -> None:
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/json/version", timeout=1) as response:
                if response.status == 200:
                    return
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            time.sleep(0.25)
    raise RuntimeError(f"Edge CDP did not start on port {port}")


def _is_port_open(host: str, port: int) -> bool:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.settimeout(0.5)
        return sock.connect_ex((host, port)) == 0


def _edge_path() -> str:
    candidates = [
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    ]
    for candidate in candidates:
        if os.path.exists(candidate):
            return candidate
    raise RuntimeError("Could not find Edge or Chrome executable")


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

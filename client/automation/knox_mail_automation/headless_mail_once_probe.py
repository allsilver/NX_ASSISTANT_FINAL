from __future__ import annotations

import argparse
import asyncio
import json
import os
import re
from datetime import datetime
from pathlib import Path
from typing import Any

from . import cdp_send_mail


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"
MAIL_URL = "http://kor1.samsung.net/formapp/?initModule=mail"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Send exactly one Knox mail from a headless persistent Edge profile and collect evidence."
    )
    parser.add_argument("--profile-dir", default=".nx-mcp-automation-profile")
    parser.add_argument("--recipient", default="daeun.seo")
    parser.add_argument("--subject")
    parser.add_argument("--body", default="test")
    parser.add_argument("--width", type=int, default=1365)
    parser.add_argument("--height", type=int, default=900)
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    subject = args.subject or f"성공-headless-{stamp}"
    profile_dir = (ROOT / args.profile_dir).resolve()

    events: list[dict[str, Any]] = []
    send_responses: list[dict[str, Any]] = []

    async with async_playwright() as playwright:
        context = await playwright.chromium.launch_persistent_context(
            user_data_dir=str(profile_dir),
            executable_path=_edge_path(),
            headless=True,
            viewport={"width": args.width, "height": args.height},
            locale="ko-KR",
            args=[
                "--no-first-run",
                "--disable-blink-features=AutomationControlled",
                f"--window-size={args.width},{args.height}",
            ],
        )
        context.on("request", lambda req: _record_request(events, req))
        context.on("requestfailed", lambda req: _record_failed(events, req))
        context.on("response", lambda resp: asyncio.create_task(_record_response(events, send_responses, resp)))

        page = context.pages[0] if context.pages else await context.new_page()
        page.set_default_timeout(20000)
        page.on("console", lambda msg: events.append({"type": "console", "level": msg.type, "text": msg.text[:300]}))
        page.on("pageerror", lambda exc: events.append({"type": "pageerror", "text": str(exc)[:500]}))
        page.on("dialog", lambda dialog: asyncio.create_task(_accept_dialog(events, dialog)))

        try:
            await page.goto(MAIL_URL, wait_until="domcontentloaded", timeout=60000)
            try:
                await page.wait_for_load_state("networkidle", timeout=15000)
            except Exception:
                pass

            initial_state = await _page_state(page)
            if _looks_logged_out(initial_state):
                raise RuntimeError(f"Knox session is not logged in: {initial_state['url']} {initial_state['title']}")

            recipient_result = await _add_recipient_once(page, args.recipient)
            await page.locator("#subject").fill(subject)
            await cdp_send_mail._fill_body(page, args.body)

            prepared_path = ARTIFACT_DIR / f"headless-mail-once-prepared-{stamp}.png"
            await cdp_send_mail._safe_screenshot(page, prepared_path)

            clicked_send = await _click_send_once(page)
            await page.wait_for_timeout(1500)
            clicked_confirm = await _click_send_confirm_once(page)
            await _wait_for_send_signal(send_responses, timeout_ms=30000)
            await page.wait_for_timeout(3000)

            final_path = ARTIFACT_DIR / f"headless-mail-once-final-{stamp}.png"
            await cdp_send_mail._safe_screenshot(page, final_path)
            final_state = await _page_state(page)
            sent_search = await _search_sent_mail(page, subject)

            payload = {
                "ok": bool(send_responses or sent_search.get("matches")),
                "sentAttempted": True,
                "clicks": {
                    "sendButton": clicked_send,
                    "confirmButton": clicked_confirm,
                },
                "recipient": args.recipient,
                "subject": subject,
                "recipientResult": recipient_result,
                "sendResponses": send_responses,
                "sentSearch": sent_search,
                "pageState": {
                    "url": final_state["url"],
                    "title": final_state["title"],
                    "bodyTextHead": final_state["bodyText"][:800],
                },
                "events": events[-120:],
                "screenshots": {
                    "prepared": str(prepared_path),
                    "final": str(final_path),
                },
            }
        finally:
            await context.close()

    result_path = ARTIFACT_DIR / f"headless-mail-once-result-{stamp}.json"
    payload["resultFile"] = str(result_path)
    result_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    return payload


async def _add_recipient_once(page: Any, recipient: str) -> dict[str, Any]:
    before = await _recipient_state(page)
    search_input = page.get_by_placeholder("순간검색 또는 수신인 추가를 클릭해 주세요.")
    if await search_input.count() == 1:
        await search_input.click()
        await search_input.fill(recipient)
        await page.keyboard.press("Enter")
        await page.wait_for_timeout(2500)
        await _dismiss_duplicate_notice(page)
    after = await _recipient_state(page)
    return {"before": before, "after": after}


async def _recipient_state(page: Any) -> dict[str, Any]:
    return await page.evaluate(
        """
        () => {
          const text = document.body ? document.body.innerText : "";
          const count = (label) => {
            const match = text.match(new RegExp(label + "\\\\s*(\\\\d+)"));
            return match ? Number(match[1]) : 0;
          };
          return {
            toCount: count("수신"),
            ccCount: count("참조"),
            bccCount: count("비밀"),
            hasDaeun: text.includes("daeun.seo@samsung.com") || text.includes("daeun.seo") || text.includes("서다은"),
            textHead: text.slice(0, 600)
          };
        }
        """
    )


async def _dismiss_duplicate_notice(page: Any) -> None:
    for _ in range(3):
        clicked = await page.evaluate(
            """
            () => {
              const text = document.body ? document.body.innerText : "";
              if (!/(중복|이미|동일)/.test(text)) return false;
              const visible = (el) => {
                const rect = el.getBoundingClientRect();
                const style = getComputedStyle(el);
                return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < window.innerHeight
                  && style.visibility !== "hidden" && style.display !== "none";
              };
              const labels = ["확인", "닫기"];
              const labelOf = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
              const target = Array.from(document.querySelectorAll("button, a, input[type='button']"))
                .filter((el) => visible(el) && labels.includes(labelOf(el)))
                .sort((a, b) => b.getBoundingClientRect().y - a.getBoundingClientRect().y)[0];
              if (!target) return false;
              target.click();
              return true;
            }
            """
        )
        if not clicked:
            return
        await page.wait_for_timeout(1000)


async def _click_send_once(page: Any) -> bool:
    buttons = page.locator("button").filter(has_text=re.compile(r"^\s*발신\s*$"))
    if await buttons.count() < 1:
        return False
    await buttons.first.click(timeout=10000)
    return True


async def _click_send_confirm_once(page: Any) -> bool:
    state = await _page_state(page)
    if "발신하시겠습니까?" not in state["bodyText"]:
        return False
    btn_noti = page.locator("#btn-noti")
    if await btn_noti.count() == 1:
        await btn_noti.click()
        return True
    clicked = await page.evaluate(
        """
        () => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < window.innerHeight
              && style.visibility !== "hidden" && style.display !== "none";
          };
          const labels = ["확인", "예", "발신"];
          const labelOf = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
          const target = Array.from(document.querySelectorAll("button, a, input[type='button']"))
            .filter((el) => visible(el) && labels.includes(labelOf(el)))
            .sort((a, b) => b.getBoundingClientRect().y - a.getBoundingClientRect().y)[0];
          if (!target) return false;
          target.click();
          return true;
        }
        """
    )
    return bool(clicked)


async def _wait_for_send_signal(send_responses: list[dict[str, Any]], *, timeout_ms: int) -> None:
    deadline = datetime.now().timestamp() + timeout_ms / 1000
    while datetime.now().timestamp() < deadline:
        if send_responses:
            return
        await asyncio.sleep(0.25)


async def _search_sent_mail(page: Any, subject: str) -> dict[str, Any]:
    return await page.evaluate(
        """
        async ({ subject }) => {
          const folderIds = [2, 4, 5, 6, 1, 3];
          const matches = [];
          const errors = [];
          const findMatches = (value, folderID) => {
            if (!value || typeof value !== 'object') return;
            if (Array.isArray(value)) {
              value.forEach((entry) => findMatches(entry, folderID));
              return;
            }
            const text = JSON.stringify(value);
            if (text.includes(subject)) {
              matches.push({
                folderID,
                mailID: value.mailID || value.mailId || value.messageID || null,
                mailSeq: value.mailSeq || null,
                subject: value.subject || value.originalSubject || null,
                date: value.date || value.sentTime || null
              });
            }
            Object.values(value).forEach((entry) => {
              if (entry && typeof entry === 'object') findMatches(entry, folderID);
            });
          };
          for (const folderID of folderIds) {
            try {
              const response = await fetch('/mail/rest/v1/mails/simple/search', {
                method: 'POST',
                credentials: 'include',
                headers: { 'Content-Type': 'application/json;charset=utf-8' },
                body: JSON.stringify({
                  folderID,
                  count: 10,
                  searchVOList: [{ searchField: 'all', keyword: subject }],
                  searchType: 'simple',
                  sortOrder: false,
                  sortType: 'DATE',
                  pageNo: 1,
                  reduceTitlePrefixType: 'ALL'
                })
              });
              const text = await response.text();
              let json = null;
              try { json = JSON.parse(text); } catch {}
              if (json) findMatches(json, folderID);
              else if (text.includes(subject)) matches.push({ folderID, mailID: null, subject, date: null });
              errors.push({ folderID, status: response.status, ok: response.ok, matched: text.includes(subject) });
            } catch (error) {
              errors.push({ folderID, error: String(error).slice(0, 200) });
            }
          }
          return { matches, probes: errors };
        }
        """,
        {"subject": subject},
    )


def _record_request(events: list[dict[str, Any]], request: Any) -> None:
    if _is_relevant_url(request.url):
        events.append({"type": "request", "method": request.method, "url": request.url[:500]})


def _record_failed(events: list[dict[str, Any]], request: Any) -> None:
    if _is_relevant_url(request.url):
        failure = request.failure or {}
        events.append({"type": "requestfailed", "method": request.method, "url": request.url[:500], "failure": failure})


async def _record_response(events: list[dict[str, Any]], send_responses: list[dict[str, Any]], response: Any) -> None:
    if not _is_relevant_url(response.url):
        return
    item: dict[str, Any] = {"type": "response", "status": response.status, "url": response.url[:500]}
    if "/mail/rest/v1/mails/send" in response.url:
        body = ""
        try:
            body = await response.text()
        except Exception:
            pass
        item["mailID"] = _extract_mail_id(body)
        item["bodyHasMailID"] = "mailID" in body
        send_responses.append(
            {
                "status": response.status,
                "ok": response.status < 400,
                "url": response.url,
                "mailID": item["mailID"],
                "bodyHasMailID": item["bodyHasMailID"],
            }
        )
    events.append(item)


def _extract_mail_id(body: str) -> str | None:
    try:
        data = json.loads(body)
    except Exception:
        data = None
    if isinstance(data, dict):
        value = data.get("mailID") or data.get("mailId")
        if value:
            return str(value)
    match = re.search(r'"mailID"\s*:\s*"([^"]+)"', body)
    return match.group(1) if match else None


def _is_relevant_url(url: str) -> bool:
    lower = url.lower()
    return "kor1.samsung.net" in lower and any(token in lower for token in ["mail", "formapp", "employee", "send"])


async def _accept_dialog(events: list[dict[str, Any]], dialog: Any) -> None:
    events.append({"type": "dialog", "message": dialog.message[:500]})
    await dialog.accept()


async def _page_state(page: Any) -> dict[str, Any]:
    return await page.evaluate(
        """
        () => ({
          url: location.href,
          title: document.title,
          bodyText: document.body ? document.body.innerText.slice(0, 3000) : ""
        })
        """
    )


def _looks_logged_out(state: dict[str, Any]) -> bool:
    text = state.get("bodyText", "")
    if "사용자 세션이 만료되었습니다" in text:
        return True
    if "로그인" in text and "비밀번호" in text:
        return True
    return False


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
    result = asyncio.run(main_async(build_parser().parse_args()))
    print(
        json.dumps(
            {
                "ok": result["ok"],
                "sentAttempted": result["sentAttempted"],
                "subject": result["subject"],
                "clicks": result["clicks"],
                "sendResponses": result["sendResponses"],
                "sentSearchMatches": result["sentSearch"].get("matches", []),
                "resultFile": result["resultFile"],
            },
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()


from __future__ import annotations

import argparse
import asyncio
import json
import re
from datetime import datetime
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Prepare and send a Knox mail from an attached CDP browser")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--recipient", default="daeun.seo")
    parser.add_argument("--subject", required=True)
    parser.add_argument("--body", required=True)
    parser.add_argument("--screenshot-prefix", default="mail-send")
    parser.add_argument("--allow-send", action="store_true", help="Actually click send. Without this, only prepare the draft.")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = await _get_or_open_compose_page(context)
        page.set_default_timeout(20000)

        await _ensure_recipient(page, args.recipient)
        await _assert_recipient_is_to(page)
        await page.locator("#subject").fill(args.subject)
        await _fill_body(page, args.body)

        prepared_path = _artifact_path(args.screenshot_prefix, "prepared")
        await _safe_screenshot(page, prepared_path)

        if not args.allow_send:
            state = await _page_state(page)
            payload = {
                "ok": True,
                "sent": False,
                "recipient": args.recipient,
                "subject": args.subject,
                "body": args.body,
                "detail": "prepared only; --allow-send was not provided",
                "state": {
                    "url": state["url"],
                    "title": state["title"],
                    "bodyTextHead": state["bodyText"][:800],
                },
                "screenshots": {
                    "prepared": str(prepared_path),
                    "final": str(prepared_path),
                },
            }
            result_path = _artifact_path(args.screenshot_prefix, "result").with_suffix(".json")
            result_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
            payload["resultFile"] = str(result_path)
            return payload

        send_response_task = asyncio.create_task(_wait_for_send_response(page))
        await _click_send(page)
        result = await _finish_send_flow(page, send_response_task)

        final_path = _artifact_path(args.screenshot_prefix, "final")
        await _safe_screenshot(page, final_path)
        state = await _page_state(page)

        payload = {
            "ok": result["ok"],
            "sent": result["ok"],
            "recipient": args.recipient,
            "subject": args.subject,
            "body": args.body,
            "detail": result["detail"],
            "state": {
                "url": state["url"],
                "title": state["title"],
                "bodyTextHead": state["bodyText"][:800],
            },
            "screenshots": {
                "prepared": str(prepared_path),
                "final": str(final_path),
            },
        }
        result_path = _artifact_path(args.screenshot_prefix, "result").with_suffix(".json")
        result_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        payload["resultFile"] = str(result_path)
        return payload


async def _get_or_open_compose_page(context: Any) -> Any:
    for page in context.pages:
        if "formapp" in page.url and "initModule=mail" in page.url:
            return page

    page = context.pages[0] if context.pages else await context.new_page()
    await page.goto("http://kor1.samsung.net/formapp/?initModule=mail", wait_until="domcontentloaded")
    try:
        await page.locator("#subject").wait_for(state="visible", timeout=12000)
        return page
    except Exception:
        pass

    await page.goto("http://kor1.samsung.net/portalapp/home", wait_until="domcontentloaded")
    await page.wait_for_timeout(5000)
    await _click_visible_button(page, ["메일"], description="mail menu")
    await page.wait_for_timeout(2500)
    await _click_visible_button(page, ["새 메일 쓰기"], description="compose button")
    await page.wait_for_timeout(5000)
    if "formapp" in page.url:
        return page
    for candidate in context.pages:
        if "formapp" in candidate.url and "initModule=mail" in candidate.url:
            return candidate
    raise RuntimeError("Could not open mail compose page")


async def _ensure_recipient(page: Any, recipient: str) -> None:
    state = await _recipient_state(page)
    if _is_requested_recipient_present(state) and state["toCount"] >= 1 and state["ccCount"] == 0:
        return

    if _is_requested_recipient_present(state):
        await _move_existing_recipient_to_to(page)
        await page.wait_for_timeout(1000)
        return

    if recipient.lower() in {"daeun.seo", "daeun.seo@samsung.com", "서다은"}:
        to_me = page.locator("#form-toMeBtn")
        if await to_me.count() == 1:
            await to_me.click()
            await page.wait_for_timeout(1500)
            state = await _recipient_state(page)
            if _is_requested_recipient_present(state) and not (state["toCount"] >= 1 and state["ccCount"] == 0):
                await _move_existing_recipient_to_to(page)
                await page.wait_for_timeout(1000)
            return

    search_input = page.get_by_placeholder("순간검색 또는 수신인 추가를 클릭해 주세요.")
    await _click_unique(search_input, "recipient search input")
    await search_input.fill(recipient)
    await page.keyboard.press("Enter")
    await page.wait_for_timeout(2500)

    state = await _recipient_state(page)
    if not _is_requested_recipient_present(state):
        await page.keyboard.press("Enter")
        await page.wait_for_timeout(2500)

    state = await _recipient_state(page)
    if not _is_requested_recipient_present(state):
        raise RuntimeError("Could not confirm recipient was added")
    if not (state["toCount"] >= 1 and state["ccCount"] == 0):
        await _move_existing_recipient_to_to(page)
        await page.wait_for_timeout(1000)


async def _recipient_state(page: Any) -> dict[str, Any]:
    return await page.evaluate(
        """
        () => {
          const text = document.body ? document.body.innerText : "";
          const toMatch = text.match(/수신\\s*(\\d+)/);
          const ccMatch = text.match(/참조\\s*(\\d+)/);
          const bccMatch = text.match(/비밀\\s*(\\d+)/);
          return {
            text,
            toCount: toMatch ? Number(toMatch[1]) : 0,
            ccCount: ccMatch ? Number(ccMatch[1]) : 0,
            bccCount: bccMatch ? Number(bccMatch[1]) : 0,
            hasDaeun: text.includes("daeun.seo@samsung.com") || text.includes("daeun.seo") || text.includes("서다은"),
          };
        }
        """
    )


def _is_requested_recipient_present(state: dict[str, Any]) -> bool:
    recipient_count = state.get("toCount", 0) + state.get("ccCount", 0) + state.get("bccCount", 0)
    return bool(state.get("hasDaeun") and recipient_count > 0)


async def _move_existing_recipient_to_to(page: Any) -> dict[str, Any]:
    result = await page.evaluate(
        """
        () => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const buttonText = (el) => (el.innerText || el.getAttribute("aria-label") || el.value || "").trim();
          const buttons = Array.from(document.querySelectorAll("button, a, span, div"))
            .filter((el) => visible(el) && buttonText(el) === "수신")
            .map((el) => {
              const rect = el.getBoundingClientRect();
              return { el, rect, className: String(el.className || "") };
            })
            .filter((entry) => entry.rect.y > 225 && entry.rect.x < 260)
            .sort((a, b) => b.rect.y - a.rect.y);
          const target = buttons[0];
          if (!target) {
            return { clicked: false, reason: "visible row-level To button not found" };
          }
          target.el.click();
          return {
            clicked: true,
            rect: {
              x: Math.round(target.rect.x),
              y: Math.round(target.rect.y),
              width: Math.round(target.rect.width),
              height: Math.round(target.rect.height),
            },
            className: target.className,
          };
        }
        """
    )
    if not result.get("clicked"):
        raise RuntimeError(f"Could not move recipient to To: {result}")
    return result


async def _assert_recipient_is_to(page: Any) -> None:
    state = await _recipient_state(page)
    if _is_requested_recipient_present(state) and state["toCount"] >= 1 and state["ccCount"] == 0:
        return
    preview = state["text"][:600].replace("\n", " / ")
    raise RuntimeError(f"Recipient is not safely in To field. State: {preview}")


async def _fill_body(page: Any, body: str) -> None:
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
    await editor_frame.locator("#cafe-note-contents").fill(body)


async def _click_send(page: Any) -> None:
    send_buttons = page.locator("button").filter(has_text=re.compile(r"^\s*발신\s*$"))
    count = await send_buttons.count()
    if count < 1:
        raise RuntimeError("Could not find send button")
    button = send_buttons.first
    await button.click(timeout=10000)
    await page.wait_for_timeout(1200)
    state = await _page_state(page)
    if "발신하시겠습니까?" not in state["bodyText"]:
        await button.focus()
        await page.keyboard.press("Enter")
        await page.wait_for_timeout(1200)
    state = await _page_state(page)
    if "발신하시겠습니까?" not in state["bodyText"]:
        await page.keyboard.press("Control+Enter")
        await page.wait_for_timeout(1200)
    state = await _page_state(page)
    if "발신하시겠습니까?" not in state["bodyText"]:
        await _call_react_click_handler(page, "발신", max_y=200)
    await page.wait_for_timeout(2500)


async def _call_react_click_handler(page: Any, label: str, *, max_y: int | None = None) -> bool:
    result = await page.evaluate(
        """
        ({ label, maxY }) => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < window.innerHeight
              && style.visibility !== "hidden" && style.display !== "none";
          };
          const labelOf = (el) => (el.innerText || el.getAttribute("aria-label") || el.value || "").trim();
          const candidates = Array.from(document.querySelectorAll("button, a, span, div"))
            .filter((el) => visible(el) && labelOf(el) === label)
            .map((el) => ({ el, rect: el.getBoundingClientRect() }))
            .filter((entry) => maxY === null || entry.rect.y < maxY)
            .sort((a, b) => a.rect.y - b.rect.y);
          for (const { el } of candidates) {
            let current = el;
            for (let depth = 0; current && depth < 8; depth += 1, current = current.parentElement) {
              const key = Object.keys(current).find((entry) =>
                entry.startsWith("__reactProps$") || entry.startsWith("__reactEventHandlers$")
              );
              const props = key ? current[key] : null;
              const handlerName = props && ["onClick", "onMouseUp", "onMouseDown"].find((name) => typeof props[name] === "function");
              if (props && handlerName) {
                props[handlerName]({
                  preventDefault() {},
                  stopPropagation() {},
                  target: current,
                  currentTarget: current,
                  nativeEvent: { isTrusted: true }
                });
                return true;
              }
            }
          }
          return false;
        }
        """,
        {"label": label, "maxY": max_y},
    )
    return bool(result)


async def _wait_for_send_response(page: Any) -> dict[str, Any]:
    try:
        response = await page.wait_for_response(
            lambda resp: "/mail/rest/v1/mails/send" in resp.url and resp.request.method == "POST",
            timeout=35000,
        )
        body_head = ""
        try:
            body_head = (await response.text())[:800]
        except Exception:
            pass
        return {
            "ok": response.status < 400,
            "status": response.status,
            "bodyHead": body_head,
        }
    except Exception as exc:
        return {"ok": False, "error": str(exc)}


async def _finish_send_flow(page: Any, send_response_task: asyncio.Task[dict[str, Any]]) -> dict[str, Any]:
    success_tokens = ["발송되었습니다", "전송되었습니다", "메일을 발송", "보냈습니다", "메일이 발송"]
    for _ in range(8):
        if send_response_task.done():
            response = send_response_task.result()
            if response.get("ok"):
                return {"ok": True, "detail": f"send response {response.get('status')}"}
        state = await _page_state(page)
        text = state["bodyText"]
        if any(token in text for token in success_tokens):
            return {"ok": True, "detail": "success text detected"}

        if "메일 쓰기" not in state["title"] and "#subject" not in text:
            return {"ok": True, "detail": "compose page left after send"}

        clicked = False
        if "발신하시겠습니까?" in text:
            btn_noti = page.locator("#btn-noti")
            if await btn_noti.count() == 1:
                await btn_noti.click()
                clicked = True
        if not clicked:
            clicked = await _click_visible_button(
                page,
                ["확인", "예", "보내기", "전송"],
                description="send confirmation",
                modal_or_below_y=150,
            )
        if clicked:
            await page.wait_for_timeout(2500)
            continue
        await page.wait_for_timeout(2000)

    if not send_response_task.done():
        try:
            response = await asyncio.wait_for(send_response_task, timeout=3)
            if response.get("ok"):
                return {"ok": True, "detail": f"send response {response.get('status')}"}
        except Exception:
            pass
    state = await _page_state(page)
    still_compose = "메일 쓰기" in state["title"] or "제목을 입력하세요" in state["bodyText"]
    return {
        "ok": not still_compose,
        "detail": "send flow ended without explicit success text" if not still_compose else "compose page still open after send attempt",
    }


async def _click_visible_button(
    page: Any,
    labels: list[str],
    *,
    description: str,
    modal_or_below_y: int | None = None,
) -> bool:
    result = await page.evaluate(
        """
        ({ labels, modalOrBelowY }) => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < window.innerHeight
              && style.visibility !== "hidden" && style.display !== "none";
          };
          const labelOf = (el) => (el.innerText || el.getAttribute("aria-label") || "").trim();
          const isInModal = (el) => Boolean(el.closest('[role="dialog"], .modal, .popup, .layer, .cui-dialog, .pt-dialog'));
          const candidates = Array.from(document.querySelectorAll("button, a"))
            .filter((el) => visible(el) && labels.includes(labelOf(el)))
            .map((el) => ({ el, label: labelOf(el), rect: el.getBoundingClientRect(), modal: isInModal(el) }))
            .filter((entry) => modalOrBelowY === null || entry.modal || entry.rect.y > modalOrBelowY)
            .sort((a, b) => Number(b.modal) - Number(a.modal) || b.rect.y - a.rect.y);
          const target = candidates[0];
          if (!target) return { clicked: false };
          target.el.click();
          return {
            clicked: true,
            label: target.label,
            modal: target.modal,
            rect: {
              x: Math.round(target.rect.x),
              y: Math.round(target.rect.y),
              width: Math.round(target.rect.width),
              height: Math.round(target.rect.height),
            },
          };
        }
        """,
        {"labels": labels, "modalOrBelowY": modal_or_below_y},
    )
    if result.get("clicked"):
        return True
    if description in {"mail menu", "compose button"}:
        screenshot = ARTIFACT_DIR / f"{description.replace(' ', '-')}-not-found.png"
        await _safe_screenshot(page, screenshot)
        raise RuntimeError(f"{description} not found; screenshot={screenshot}")
    return False


async def _click_unique(locator: Any, description: str) -> None:
    count = await locator.count()
    if count != 1:
        raise RuntimeError(f"{description} matched {count} elements")
    await locator.click()


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


async def _safe_screenshot(page: Any, path: Path) -> None:
    try:
        await page.screenshot(path=str(path), full_page=False, timeout=15000)
    except TypeError:
        await page.screenshot(path=str(path), full_page=False)
    except Exception as exc:
        path.with_suffix(".error.txt").write_text(str(exc), encoding="utf-8")


def _artifact_path(prefix: str, suffix: str) -> Path:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return ARTIFACT_DIR / f"{prefix}-{suffix}-{stamp}.png"


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

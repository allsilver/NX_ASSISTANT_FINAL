from __future__ import annotations

import argparse
import asyncio
import json
import re
import sys
from dataclasses import asdict, dataclass
from datetime import datetime
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"
QUICK_URL = (
    "https://digitalworld.sec.samsung.net/docDelivery/forwardDocDeliveryEdit.do"
    "?_menuId=AWVaGPZgAL-o8AfS&_menuF=true#none"
)
QUICK_URL_TOKEN = "digitalworld.sec.samsung.net/docDelivery/forwardDocDeliveryEdit.do"


@dataclass
class QuickDeliveryRequest:
    sender_name: str
    receiver_company: str
    receiver_name: str
    item_name: str
    item_count: str
    reason: str
    quick_vendor: str
    delivery_kind: str = "quick_same_day"
    send_kind: str = "move"
    tax_kind: str = "company"
    declared_amount: str = "100000"
    remarks: str = ""


@dataclass
class AddressCandidate:
    value: str
    name: str
    phone: str
    address: str
    text: str


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Prepare or save a Digital World quick-delivery request")
    parser.add_argument("--raw-command", default="대동전자 이희정님에게 프론트 50개 발송할거야. 퀵 신청해줘")
    parser.add_argument("--sender-name", default="서다은")
    parser.add_argument("--receiver-company")
    parser.add_argument("--receiver-name")
    parser.add_argument("--item-name")
    parser.add_argument("--item-count")
    parser.add_argument("--reason", default="")
    parser.add_argument("--quick-vendor", default="")
    parser.add_argument("--declared-amount", default="100000")
    parser.add_argument("--remarks", default="")
    parser.add_argument("--allow-inferred-reason", action="store_true")
    parser.add_argument("--allow-save", action="store_true")
    parser.add_argument("--cdp", default="http://127.0.0.1:9242")
    parser.add_argument("--profile-dir", default=str(ROOT / ".nx-mcp-automation-profile"))
    parser.add_argument("--headless", action="store_true")
    parser.add_argument("--keep-open", action="store_true")
    parser.add_argument("--fresh", action="store_true")
    parser.add_argument("--show", action="store_true")
    parser.add_argument("--artifact-prefix", default="quick-delivery")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    ARTIFACT_DIR.mkdir(exist_ok=True)
    request = _build_request(args)
    if not request.reason:
        suggested = _suggest_reason(request)
        return _needs_input(
            "missing_reason",
            "반입사유가 필요합니다. 아래 추천 문구를 그대로 쓸까요, 아니면 다른 사유를 입력할까요?",
            request,
            suggested_reason=suggested,
        )

    from playwright.async_api import async_playwright

    events: list[dict[str, Any]] = []
    save_attempted = False
    browser_or_context: Any | None = None
    close_target: Any | None = None
    try:
        async with async_playwright() as playwright:
            context, close_target = await _open_context(playwright, args)
            browser_or_context = context
            page = await _get_quick_page(context, args.fresh)
            page.set_default_timeout(20000)
            page.on("dialog", lambda dialog: asyncio.create_task(dialog.accept()))
            page.on("response", lambda response: _record_response(events, response))

            await _dismiss_common_overlays(page)
            preflight = await _preflight_quick_form(page, args.artifact_prefix)
            if preflight.get("status") != "ok":
                return preflight

            transport = await _fill_transport_controls(page, request)
            if transport.get("status") == "needs_input":
                return _needs_input(
                    "missing_quick_vendor",
                    "퀵업체를 선택해야 합니다. 사용할 업체를 골라주세요.",
                    request,
                    candidates={"quickVendor": transport.get("options", [])},
                )

            sender = await _resolve_and_select_address(page, "sender", request.sender_name)
            receiver = await _resolve_and_select_address(
                page,
                "receiver",
                request.receiver_name,
                request.receiver_company,
            )
            if sender["status"] != "ok" or receiver["status"] != "ok":
                return _address_needs_input(request, sender, receiver)

            changes = transport.get("changes", []) + await _fill_detail_fields(page, request)
            prepared_path = _artifact_path(args.artifact_prefix, "prepared")
            await _safe_screenshot(page, prepared_path, full_page=True)

            if args.allow_save:
                save_attempted = True
                await _click_save_once(page)
                await page.wait_for_timeout(4000)
                await _handle_confirmations(page)

            if args.show:
                try:
                    await page.bring_to_front()
                except Exception:
                    pass

            final_path = _artifact_path(args.artifact_prefix, "final")
            await _safe_screenshot(page, final_path, full_page=True)
            state = await _collect_state(page)
            status = "saved" if args.allow_save else "prepared"
            result = {
                "ok": True,
                "status": status,
                "saveAttempted": save_attempted,
                "request": asdict(request),
                "selectedAddresses": {
                    "sender": sender.get("selected"),
                    "receiver": receiver.get("selected"),
                },
                "changes": changes,
                "state": state,
                "events": events[-40:],
                "screenshots": {
                    "prepared": str(prepared_path),
                    "final": str(final_path),
                },
            }
            result_path = _artifact_path(args.artifact_prefix, "result").with_suffix(".json")
            result_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
            result["resultFile"] = str(result_path)
            return result
    finally:
        if close_target is not None and not args.keep_open:
            try:
                await close_target.close()
            except Exception:
                pass
        elif browser_or_context is not None and not args.keep_open and not args.cdp:
            try:
                await browser_or_context.close()
            except Exception:
                pass


def _build_request(args: argparse.Namespace) -> QuickDeliveryRequest:
    parsed = _parse_raw_command(args.raw_command)
    receiver_company = args.receiver_company or parsed.get("receiver_company") or ""
    receiver_name = args.receiver_name or parsed.get("receiver_name") or ""
    item_name = args.item_name or parsed.get("item_name") or ""
    item_count = args.item_count or parsed.get("item_count") or ""

    missing = [
        name
        for name, value in [
            ("receiver_company", receiver_company),
            ("receiver_name", receiver_name),
            ("item_name", item_name),
            ("item_count", item_count),
        ]
        if not value
    ]
    if missing:
        raise ValueError(f"Could not parse required fields from request: {', '.join(missing)}")

    reason = args.reason.strip()
    request = QuickDeliveryRequest(
        sender_name=args.sender_name.strip(),
        receiver_company=receiver_company.strip(),
        receiver_name=receiver_name.strip(),
        item_name=item_name.strip(),
        item_count=_normalize_count(item_count),
        reason=reason,
        quick_vendor=args.quick_vendor.strip(),
        declared_amount=re.sub(r"\D", "", args.declared_amount) or "100000",
        remarks=args.remarks.strip(),
    )
    if not request.reason and args.allow_inferred_reason:
        request.reason = _suggest_reason(request)
    return request


def _parse_raw_command(text: str) -> dict[str, str]:
    compact = " ".join(text.strip().split())
    pattern = re.compile(
        r"(?P<company>[가-힣A-Za-z0-9()._-]+)\s+"
        r"(?P<person>[가-힣A-Za-z0-9()._-]+)님에게\s+"
        r"(?P<item>.+?)\s+"
        r"(?P<count>\d+)\s*개"
    )
    match = pattern.search(compact)
    if not match:
        return {}
    return {
        "receiver_company": match.group("company"),
        "receiver_name": match.group("person"),
        "item_name": match.group("item").strip(),
        "item_count": match.group("count"),
    }


def _normalize_count(value: str) -> str:
    digits = re.sub(r"\D", "", value)
    return digits or value.strip()


def _suggest_reason(request: QuickDeliveryRequest) -> str:
    return f"{request.item_name} {request.item_count}개 이동 및 설계 검증용"


def _needs_input(
    code: str,
    question: str,
    request: QuickDeliveryRequest,
    *,
    suggested_reason: str | None = None,
    candidates: dict[str, Any] | None = None,
) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "ok": False,
        "status": "needs_input",
        "code": code,
        "question": question,
        "request": asdict(request),
    }
    if suggested_reason:
        payload["suggestedReason"] = suggested_reason
    if candidates:
        payload["candidates"] = candidates
    return payload


async def _open_context(playwright: Any, args: argparse.Namespace) -> tuple[Any, Any | None]:
    if args.cdp:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        if not browser.contexts:
            raise RuntimeError("No browser context is available through CDP")
        return browser.contexts[0], None

    context = await playwright.chromium.launch_persistent_context(
        args.profile_dir,
        channel="msedge",
        headless=args.headless,
        viewport={"width": 1366, "height": 900},
        args=["--disable-notifications"],
    )
    return context, context


async def _get_quick_page(context: Any, fresh: bool) -> Any:
    if not fresh:
        for page in reversed(context.pages):
            if QUICK_URL_TOKEN in page.url:
                try:
                    await page.goto(QUICK_URL, wait_until="domcontentloaded", timeout=45000)
                except Exception:
                    pass
                return page

    page = await context.new_page()
    await page.goto(QUICK_URL, wait_until="domcontentloaded", timeout=45000)
    try:
        await page.wait_for_load_state("networkidle", timeout=10000)
    except Exception:
        pass
    return page


async def _dismiss_common_overlays(page: Any) -> None:
    await page.evaluate(
        """
        () => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
          const closeLabels = new Set(["오늘 하루 보지 않기", "닫기", "Close"]);
          const buttons = Array.from(document.querySelectorAll("a, button, input[type='button'], input[type='submit']"))
            .filter((el) => visible(el) && closeLabels.has(label(el)))
            .filter((el) => {
              const text = (el.closest("div, section, article, table")?.innerText || "");
              return /공지|Notice|알림/.test(text);
            });
          for (const button of buttons.slice(0, 5)) {
            try { button.click(); } catch (_) {}
          }
        }
        """
    )


async def _preflight_quick_form(page: Any, artifact_prefix: str) -> dict[str, Any]:
    try:
        await page.wait_for_selector("#deliveryKindRadio2", timeout=8000)
        await page.wait_for_selector("#inportKindRadio3", timeout=8000)
        return {"status": "ok"}
    except Exception:
        screenshot = _artifact_path(artifact_prefix, "preflight-not-form")
        await _safe_screenshot(page, screenshot, full_page=True)
        state = await page.evaluate(
            """
            () => ({
              url: location.href,
              title: document.title,
              bodyHead: document.body ? document.body.innerText.slice(0, 2000) : ""
            })
            """
        )
        result = {
            "ok": False,
            "status": "needs_input",
            "code": "quick_form_not_ready",
            "question": "퀵 신청 폼에 도달하지 못했습니다. 로그인 또는 세션 확인이 필요합니다.",
            "state": state,
            "screenshots": {"preflight": str(screenshot)},
        }
        result_path = _artifact_path(artifact_prefix, "preflight-result").with_suffix(".json")
        result_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        result["resultFile"] = str(result_path)
        return result


async def _resolve_and_select_address(
    page: Any,
    section: str,
    name: str,
    company: str = "",
) -> dict[str, Any]:
    await _close_address_layer(page)
    await _click_address_book(page, section)
    await page.wait_for_function(
        "() => document.querySelectorAll('input[name=\"userAddr.selectVal\"]').length > 0",
        timeout=10000,
    )
    await page.wait_for_timeout(700)
    candidates = [AddressCandidate(**entry) for entry in await _address_candidates(page)]
    matches = _match_candidates(candidates, name, company)
    if len(matches) != 1:
        await _close_address_layer(page)
        status = "missing" if not matches else "ambiguous"
        return {
            "status": status,
            "query": {"name": name, "company": company},
            "candidates": [asdict(candidate) for candidate in matches or _near_candidates(candidates, name, company)],
        }

    selected = matches[0]
    await _select_address_candidate(page, selected.value)
    field_id = "senderPerson" if section == "sender" else "toPerson"
    address_id = "senderAddress" if section == "sender" else "address"
    await page.wait_for_function(
        """({ fieldId, addressId }) => {
          const name = document.getElementById(fieldId);
          const address = document.getElementById(addressId);
          return Boolean(
            name && name.value && name.value.trim().length > 0 &&
            address && address.value && address.value.trim().length > 0
          );
        }""",
        arg={"fieldId": field_id, "addressId": address_id},
        timeout=10000,
    )
    return {"status": "ok", "selected": asdict(selected)}


async def _close_address_layer(page: Any) -> None:
    await page.evaluate(
        """
        () => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
          const hasAddressBook = () => document.querySelectorAll('input[name="userAddr.selectVal"]').length > 0;
          if (!hasAddressBook()) return false;
          const close = Array.from(document.querySelectorAll("a, button, input[type='button'], input[type='submit']"))
            .filter((el) => visible(el) && label(el) === "닫기")
            .pop();
          if (close) {
            close.click();
            return true;
          }
          return false;
        }
        """
    )
    await page.wait_for_timeout(500)


async def _click_address_book(page: Any, section: str) -> None:
    result = await page.evaluate(
        """
        ({ section }) => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
          const candidates = Array.from(document.querySelectorAll("a.btn_list, button, input[type='button']"))
            .filter((el) => visible(el) && label(el) === "주소록")
            .map((el) => ({ el, rect: el.getBoundingClientRect() }))
            .sort((a, b) => a.rect.y - b.rect.y);
          const index = section === "sender" ? 0 : 1;
          const target = candidates[index];
          if (!target) return { clicked: false, count: candidates.length, section };
          target.el.scrollIntoView({ block: "center", inline: "center" });
          target.el.click();
          return { clicked: true, count: candidates.length, section };
        }
        """,
        {"section": section},
    )
    if not result.get("clicked"):
        raise RuntimeError(f"Could not click address book: {result}")


async def _address_candidates(page: Any) -> list[dict[str, str]]:
    return await page.evaluate(
        """
        () => {
          const normalize = (text) => (text || "").replace(/\\s+/g, " ").trim();
          return Array.from(document.querySelectorAll('input[name="userAddr.selectVal"]')).map((radio) => {
            const row = radio.closest("tr");
            const cells = row ? Array.from(row.cells).map((cell) => normalize(cell.innerText)) : [];
            const text = normalize(row ? row.innerText : "");
            const nonEmpty = cells.filter(Boolean);
            let name = nonEmpty[0] || "";
            let phone = nonEmpty[1] || "";
            let address = nonEmpty.slice(2).join(" ");
            if (/^선택$/.test(name) && nonEmpty.length > 1) {
              name = nonEmpty[1] || "";
              phone = nonEmpty[2] || "";
              address = nonEmpty.slice(3).join(" ");
            }
            return {
              value: String(radio.value || ""),
              name,
              phone,
              address,
              text
            };
          }).filter((entry) => entry.name || entry.text);
        }
        """
    )


def _match_candidates(
    candidates: list[AddressCandidate],
    name: str,
    company: str = "",
) -> list[AddressCandidate]:
    name_key = _norm(name)
    company_key = _norm(company)
    exact = [
        candidate
        for candidate in candidates
        if _norm(candidate.name) == name_key
        and (not company_key or company_key in _norm(candidate.address) or company_key in _norm(candidate.text))
    ]
    if exact:
        return exact

    contains = [
        candidate
        for candidate in candidates
        if name_key in _norm(candidate.name)
        and (not company_key or company_key in _norm(candidate.address) or company_key in _norm(candidate.text))
    ]
    return contains


def _near_candidates(
    candidates: list[AddressCandidate],
    name: str,
    company: str = "",
) -> list[AddressCandidate]:
    name_key = _norm(name)
    company_key = _norm(company)
    scored: list[tuple[int, AddressCandidate]] = []
    for candidate in candidates:
        haystack = _norm(" ".join([candidate.name, candidate.phone, candidate.address, candidate.text]))
        score = 0
        if name_key and name_key in haystack:
            score += 2
        if company_key and company_key in haystack:
            score += 2
        if score:
            scored.append((score, candidate))
    scored.sort(key=lambda entry: entry[0], reverse=True)
    return [candidate for _, candidate in scored[:8]]


def _norm(value: str) -> str:
    return re.sub(r"\s+", "", value or "").lower()


async def _select_address_candidate(page: Any, value: str) -> None:
    result = await page.evaluate(
        """
        ({ value }) => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
          const radio = Array.from(document.querySelectorAll('input[name="userAddr.selectVal"]'))
            .find((entry) => String(entry.value || "") === String(value));
          if (!radio) return { selected: false, reason: "radio_not_found", value };
          radio.checked = true;
          radio.dispatchEvent(new MouseEvent("click", { bubbles: true }));
          radio.dispatchEvent(new Event("change", { bubbles: true }));
          const confirm = Array.from(document.querySelectorAll("a, button, input[type='button'], input[type='submit']"))
            .filter((el) => visible(el) && label(el) === "확인")
            .pop();
          if (!confirm) return { selected: false, reason: "confirm_not_found", value };
          confirm.click();
          return { selected: true, value };
        }
        """,
        {"value": value},
    )
    if not result.get("selected"):
        raise RuntimeError(f"Could not select address candidate: {result}")
    await page.wait_for_timeout(1000)


async def _fill_transport_controls(page: Any, request: QuickDeliveryRequest) -> dict[str, Any]:
    return await page.evaluate(
        """
        ({ request }) => {
          const changes = [];
          const setNativeValue = (el, value) => {
            if (!el) throw new Error("target element not found");
            const descriptor = Object.getOwnPropertyDescriptor(el.constructor.prototype, "value");
            if (descriptor && descriptor.set) descriptor.set.call(el, value);
            else el.value = value;
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
          };
          const clickRadio = (id, label) => {
            const radio = document.getElementById(id);
            if (!radio) throw new Error(`radio ${id} not found`);
            radio.checked = true;
            radio.dispatchEvent(new MouseEvent("click", { bubbles: true }));
            radio.dispatchEvent(new Event("change", { bubbles: true }));
            changes.push({ field: label, value: id });
          };

          clickRadio("deliveryKindRadio2", "퀵/택배 구분");
          clickRadio("inportKindRadio3", "발송구분");
          clickRadio("taxKindRadio1", "운임구분");
          const quickKind = document.getElementById("quickKind");
          if (quickKind) {
            const options = Array.from(quickKind.options)
              .map((option) => ({ text: option.text.trim(), value: option.value }))
              .filter((option) => option.text && option.value);
            if (!request.quick_vendor) {
              return { status: "needs_input", changes, options };
            }
            const option = Array.from(quickKind.options)
              .find((entry) => entry.text.trim() === request.quick_vendor || entry.value === request.quick_vendor);
            if (!option) {
              return { status: "needs_input", changes, options, requested: request.quick_vendor };
            }
            setNativeValue(quickKind, option.value);
            changes.push({ field: "퀵업체", value: option.text.trim() });
          }
          return { status: "ok", changes };
        }
        """,
        {"request": {"quick_vendor": request.quick_vendor}},
    )


async def _fill_detail_fields(page: Any, request: QuickDeliveryRequest) -> list[dict[str, str]]:
    changes = await page.evaluate(
        """
        ({ request }) => {
          const changes = [];
          const setNativeValue = (el, value) => {
            if (!el) throw new Error("target element not found");
            const descriptor = Object.getOwnPropertyDescriptor(el.constructor.prototype, "value");
            if (descriptor && descriptor.set) descriptor.set.call(el, value);
            else el.value = value;
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
          };
          setNativeValue(document.getElementById("rptAmount"), request.declared_amount);
          changes.push({ field: "신고가격", value: request.declared_amount });
          setNativeValue(document.querySelector('[name="reason"]'), request.reason);
          changes.push({ field: "반입사유", value: request.reason });
          setNativeValue(document.getElementById("importItemInfo"), request.item_name);
          changes.push({ field: "품명", value: request.item_name });
          setNativeValue(document.getElementById("itemCount"), request.item_count);
          changes.push({ field: "수량", value: request.item_count });
          if (request.remarks) {
            setNativeValue(document.getElementById("remarks"), request.remarks);
            changes.push({ field: "요청사항", value: request.remarks });
          }
          return changes;
        }
        """,
        {"request": {
            "declared_amount": request.declared_amount,
            "reason": request.reason,
            "item_name": request.item_name,
            "item_count": request.item_count,
            "remarks": request.remarks,
        }},
    )
    await page.wait_for_timeout(1000)
    return changes


async def _click_save_once(page: Any) -> None:
    result = await page.evaluate(
        """
        () => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
          const saves = Array.from(document.querySelectorAll("a, button, input[type='button'], input[type='submit']"))
            .filter((el) => visible(el) && (el.id === "tempS" || label(el) === "저장"));
          if (saves.length !== 1) return { clicked: false, count: saves.length };
          saves[0].scrollIntoView({ block: "center", inline: "center" });
          saves[0].click();
          return { clicked: true };
        }
        """
    )
    if not result.get("clicked"):
        raise RuntimeError(f"Could not click save exactly once: {result}")


async def _handle_confirmations(page: Any) -> None:
    for _ in range(3):
        clicked = await page.evaluate(
            """
            () => {
              const visible = (el) => {
                const rect = el.getBoundingClientRect();
                const style = getComputedStyle(el);
                return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
              };
              const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
              const candidate = Array.from(document.querySelectorAll("a, button, input[type='button'], input[type='submit']"))
                .filter((el) => visible(el) && ["예", "확인"].includes(label(el)))
                .pop();
              if (!candidate) return false;
              candidate.click();
              return true;
            }
            """
        )
        if not clicked:
            return
        await page.wait_for_timeout(1200)


def _address_needs_input(
    request: QuickDeliveryRequest,
    sender: dict[str, Any],
    receiver: dict[str, Any],
) -> dict[str, Any]:
    candidates = {
        "sender": sender.get("candidates", []),
        "receiver": receiver.get("candidates", []),
    }
    problems = []
    if sender["status"] != "ok":
        problems.append("출발지 주소록")
    if receiver["status"] != "ok":
        problems.append("도착지 주소록")
    return _needs_input(
        "address_resolution",
        f"{', '.join(problems)} 후보를 하나로 확정할 수 없습니다. 사용할 주소를 선택해주세요.",
        request,
        candidates=candidates,
    )


def _record_response(events: list[dict[str, Any]], response: Any) -> None:
    url = response.url
    if "digitalworld.sec.samsung.net" not in url:
        return
    lowered = url.lower()
    if not any(token in lowered for token in ["docdelivery", "delivery", "save", "edit", "forward"]):
        return
    events.append({"status": response.status, "url": url[:500]})


async def _collect_state(page: Any) -> dict[str, Any]:
    return await page.evaluate(
        """
        () => {
          const value = (selector) => {
            const el = document.querySelector(selector);
            return el ? el.value || "" : "";
          };
          return {
            url: location.href,
            title: document.title,
            fields: {
              reason: value('[name="reason"]'),
              rptAmount: value('#rptAmount'),
              quickKind: value('#quickKind'),
              senderPerson: value('#senderPerson'),
              senderZipCode: value('#senderZipCode'),
              senderAddress: value('#senderAddress'),
              senderAddressDetail: value('#senderAddressDetail'),
              toPerson: value('#toPerson'),
              zipCode: value('#zipCode'),
              address: value('#address'),
              addressDetail: value('#addressDetail'),
              importItemInfo: value('#importItemInfo'),
              itemCount: value('#itemCount'),
              remarks: value('#remarks'),
              deliveryKind: document.querySelector('[name="deliveryKindRadio"]:checked')?.value || "",
              inportKind: document.querySelector('[name="inportKindRadio"]:checked')?.value || "",
              taxKind: document.querySelector('[name="taxKindRadio"]:checked')?.value || ""
            },
            bodyHead: document.body ? document.body.innerText.slice(0, 2000) : ""
          };
        }
        """
    )


async def _safe_screenshot(page: Any, path: Path, *, full_page: bool) -> None:
    try:
        await page.screenshot(path=str(path), full_page=full_page, timeout=20000)
    except Exception as exc:
        path.with_suffix(".error.txt").write_text(str(exc), encoding="utf-8")
        await page.screenshot(path=str(path), full_page=False, timeout=10000)


def _artifact_path(prefix: str, suffix: str) -> Path:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return ARTIFACT_DIR / f"{prefix}-{suffix}-{stamp}.png"


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

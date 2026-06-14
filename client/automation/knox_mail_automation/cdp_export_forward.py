from __future__ import annotations

import argparse
import asyncio
import json
from datetime import datetime
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_DIR = ROOT / "artifacts"
EXPORT_URL = "https://digitalworld.sec.samsung.net/export/forwardEdit.do?_menuId=AWPHvvjaABbwlNIR&_menuF=true"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Fill and save a Digital World export-forward request")
    parser.add_argument("--cdp", default="http://127.0.0.1:9231")
    parser.add_argument("--page-index", type=int)
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
    parser.add_argument("--screenshot-prefix", default="export-forward")
    return parser


async def main_async(args: argparse.Namespace) -> dict[str, Any]:
    from playwright.async_api import async_playwright

    ARTIFACT_DIR.mkdir(exist_ok=True)
    events: list[dict[str, Any]] = []
    async with async_playwright() as playwright:
        browser = await playwright.chromium.connect_over_cdp(args.cdp)
        context = browser.contexts[0]
        page = await _get_export_page(context, args.page_index)
        page.set_default_timeout(20000)
        page.on("dialog", lambda dialog: asyncio.create_task(dialog.accept()))
        page.on("response", lambda resp: _keep_event(events, resp))

        await _fill_export_form(page, args)
        prepared_path = _artifact_path(args.screenshot_prefix, "prepared")
        await _safe_screenshot(page, prepared_path, full_page=True)

        await _click_save(page)
        await _handle_confirmations(page)
        await page.wait_for_timeout(5000)
        await _handle_confirmations(page)
        await page.bring_to_front()

        final_path = _artifact_path(args.screenshot_prefix, "final")
        await _safe_screenshot(page, final_path, full_page=True)
        state = await _state(page)

        payload = {
            "ok": True,
            "url": state["url"],
            "title": state["title"],
            "filled": {
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
            },
            "stateHead": state["bodyText"][:1200],
            "events": events[-40:],
            "screenshots": {
                "prepared": str(prepared_path),
                "final": str(final_path),
            },
        }
        result_path = _artifact_path(args.screenshot_prefix, "result").with_suffix(".json")
        result_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        payload["resultFile"] = str(result_path)
        return payload


async def _get_export_page(context: Any, page_index: int | None) -> Any:
    if page_index is not None:
        page = context.pages[page_index]
    else:
        page = next((candidate for candidate in context.pages if "digitalworld.sec.samsung.net/export/" in candidate.url), None)
        if page is None:
            page = await context.new_page()
            await page.goto(EXPORT_URL, wait_until="domcontentloaded", timeout=45000)
    if "digitalworld.sec.samsung.net/export/" not in page.url:
        await page.goto(EXPORT_URL, wait_until="domcontentloaded", timeout=45000)
    try:
        await page.wait_for_load_state("networkidle", timeout=10000)
    except Exception:
        pass
    return page


async def _fill_export_form(page: Any, args: argparse.Namespace) -> None:
    result = await page.evaluate(
        """
        ({
          destination,
          destinationDetail,
          purpose,
          purposeDetail,
          returnYn,
          quickYn,
          itemCategory,
          itemName,
          itemModel,
          itemSerial,
          itemQuantity
        }) => {
          const changes = [];
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const setNativeValue = (el, value) => {
            const descriptor = Object.getOwnPropertyDescriptor(el.constructor.prototype, "value");
            if (descriptor && descriptor.set) descriptor.set.call(el, value);
            else el.value = value;
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
          };
          const sortedSmallest = (entries) => entries.sort((a, b) => {
            const ar = a.getBoundingClientRect();
            const br = b.getBoundingClientRect();
            return ar.height - br.height || ar.width - br.width;
          });
          const rowFor = (label) => {
            const trMatches = Array.from(document.querySelectorAll("tr"))
              .filter((el) => visible(el) && (el.innerText || "").includes(label));
            if (trMatches.length) return sortedSmallest(trMatches)[0];
            const fallbackMatches = Array.from(document.querySelectorAll(".row, li, div"))
              .filter((el) => visible(el) && (el.innerText || "").includes(label));
            return sortedSmallest(fallbackMatches)[0];
          };
          const selectByOption = (optionText, predicate = () => true) => {
            const matches = Array.from(document.querySelectorAll("select"))
              .filter(visible)
              .filter(predicate)
              .filter((select) => Array.from(select.options).some((option) => option.text.trim() === optionText));
            if (matches.length !== 1) {
              throw new Error(`select for option '${optionText}' matched ${matches.length}`);
            }
            const select = matches[0];
            const option = Array.from(select.options).find((entry) => entry.text.trim() === optionText);
            setNativeValue(select, option.value);
            changes.push({ field: `select:${optionText}`, value: option.value });
            return select;
          };
          const inputInRow = (label, selector = "input[type='text'], textarea") => {
            const row = rowFor(label);
            if (!row) throw new Error(`row '${label}' not found`);
            const controls = Array.from(row.querySelectorAll(selector)).filter(visible);
            if (controls.length < 1) throw new Error(`control in row '${label}' not found`);
            return controls[0];
          };
          const setRadioInRow = (label, value) => {
            const row = rowFor(label);
            if (!row) throw new Error(`radio row '${label}' not found`);
            const radio = Array.from(row.querySelectorAll("input[type='radio']")).find((entry) => entry.value === value);
            if (!radio) throw new Error(`radio '${label}' value '${value}' not found`);
            radio.checked = true;
            radio.dispatchEvent(new Event("click", { bubbles: true }));
            radio.dispatchEvent(new Event("change", { bubbles: true }));
            changes.push({ field: `radio:${label}`, value });
          };

          selectByOption(destination, (select) => Array.from(select.options).some((option) => option.text.trim() === "사외"));
          setNativeValue(inputInRow("도착지 상세"), destinationDetail);
          changes.push({ field: "도착지 상세", value: destinationDetail });

          selectByOption(purpose, (select) => Array.from(select.options).some((option) => option.text.trim() === "해외출장"));
          setNativeValue(inputInRow("반출상세목적", "textarea"), purposeDetail);
          changes.push({ field: "반출상세목적", value: purposeDetail });

          setRadioInRow("반입여부", returnYn);
          setRadioInRow("퀵/택배 이용여부", quickYn);

          const itemSelect = selectByOption(itemCategory, (select) => Array.from(select.options).some((option) => option.text.trim() === "자재/금형"));
          const itemRow = itemSelect.closest("tr");
          if (!itemRow) throw new Error("item row not found");
          const textInputs = Array.from(itemRow.querySelectorAll("input[type='text']")).filter(visible);
          if (textInputs.length < 5) throw new Error(`item text inputs matched ${textInputs.length}`);
          setNativeValue(textInputs[0], itemName);
          setNativeValue(textInputs[1], itemModel);
          setNativeValue(textInputs[2], itemSerial);
          setNativeValue(textInputs[3], itemQuantity);
          changes.push({ field: "item", value: [itemCategory, itemName, itemModel, itemSerial, itemQuantity].join(" - ") });
          return changes;
        }
        """,
        {
            "destination": args.destination,
            "destinationDetail": args.destination_detail,
            "purpose": args.purpose,
            "purposeDetail": args.purpose_detail,
            "returnYn": args.return_yn,
            "quickYn": args.quick_yn,
            "itemCategory": args.item_category,
            "itemName": args.item_name,
            "itemModel": args.item_model,
            "itemSerial": args.item_serial,
            "itemQuantity": args.item_quantity,
        },
    )
    if not result:
        raise RuntimeError("No fields were changed")
    await page.wait_for_timeout(1500)


async def _click_save(page: Any) -> None:
    clicked = await page.evaluate(
        """
        () => {
          const visible = (el) => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
          };
          const label = (el) => (el.innerText || el.value || "").trim();
          const candidates = Array.from(document.querySelectorAll("a, button, input[type='button'], input[type='submit']"))
            .filter((el) => visible(el) && label(el) === "저장");
          if (candidates.length !== 1) return { clicked: false, count: candidates.length };
          candidates[0].scrollIntoView({ block: "center", inline: "center" });
          candidates[0].click();
          return { clicked: true };
        }
        """
    )
    if not clicked.get("clicked"):
        raise RuntimeError(f"Could not click save: {clicked}")
    await page.wait_for_timeout(2500)


async def _handle_confirmations(page: Any) -> None:
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
              const label = (el) => (el.innerText || el.value || el.getAttribute("aria-label") || "").trim();
              const preferred = ["예", "확인"];
              const candidates = Array.from(document.querySelectorAll("a, button, input[type='button'], input[type='submit']"))
                .filter((el) => visible(el) && preferred.includes(label(el)))
                .map((el) => ({ el, label: label(el), modal: Boolean(el.closest('[role="dialog"], .modal, .popup, .layer, .ui-dialog, .pop, .alert')), rect: el.getBoundingClientRect() }))
                .sort((a, b) => Number(b.modal) - Number(a.modal) || preferred.indexOf(a.label) - preferred.indexOf(b.label));
              if (!candidates[0]) return false;
              candidates[0].el.click();
              return true;
            }
            """
        )
        if not clicked:
            return
        await page.wait_for_timeout(2000)


def _keep_event(events: list[dict[str, Any]], response: Any) -> None:
    url = response.url
    if "digitalworld.sec.samsung.net" not in url:
        return
    if not any(token in url.lower() for token in ["export", "forward", "save", "edit"]):
        return
    events.append({"status": response.status, "url": url[:500]})


async def _state(page: Any) -> dict[str, Any]:
    return await page.evaluate(
        """
        () => ({
          url: location.href,
          title: document.title,
          bodyText: document.body ? document.body.innerText.slice(0, 5000) : ""
        })
        """
    )


async def _safe_screenshot(page: Any, path: Path, *, full_page: bool) -> None:
    try:
        await page.screenshot(path=str(path), full_page=full_page, timeout=20000)
    except Exception as exc:
        path.with_suffix(".error.txt").write_text(str(exc), encoding="utf-8")
        try:
            await page.screenshot(path=str(path), full_page=False, timeout=10000)
        except Exception as inner_exc:
            path.with_suffix(".fallback-error.txt").write_text(str(inner_exc), encoding="utf-8")


def _artifact_path(prefix: str, suffix: str) -> Path:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return ARTIFACT_DIR / f"{prefix}-{suffix}-{stamp}.png"


def main() -> None:
    print(json.dumps(asyncio.run(main_async(build_parser().parse_args())), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

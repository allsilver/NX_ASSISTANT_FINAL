from __future__ import annotations

import asyncio
from pathlib import Path
from typing import Any

from ..config import PortalConfig
from ..models import MailMessage, SendResult
from .base import MailAdapter


class PlaywrightPortalMailAdapter(MailAdapter):
    def __init__(
        self,
        config: PortalConfig | None = None,
        *,
        profile_dir: str | Path = ".knox-profile",
        headless: bool = False,
        confirm_before_send: bool = True,
        prepare_only: bool = False,
        executable_path: str | None = None,
        slow_mo_ms: int = 60,
        timeout_ms: int = 15000,
    ) -> None:
        self.config = config or PortalConfig.load()
        self.profile_dir = Path(profile_dir)
        self.headless = headless
        self.confirm_before_send = confirm_before_send
        self.prepare_only = prepare_only
        self.executable_path = executable_path
        self.slow_mo_ms = slow_mo_ms
        self.timeout_ms = timeout_ms

    async def send(self, message: MailMessage) -> SendResult:
        try:
            from playwright.async_api import TimeoutError as PlaywrightTimeoutError
            from playwright.async_api import async_playwright
        except ImportError as exc:
            raise RuntimeError(
                "Playwright is not installed. Run: python -m pip install -r requirements.txt"
            ) from exc

        async with async_playwright() as playwright:
            context = await playwright.chromium.launch_persistent_context(
                user_data_dir=str(self.profile_dir),
                executable_path=self.executable_path,
                headless=self.headless,
                slow_mo=self.slow_mo_ms,
            )
            page = context.pages[0] if context.pages else await context.new_page()
            page.set_default_timeout(self.timeout_ms)

            try:
                opened_mail = await self._goto_mail(page)
                if not opened_mail:
                    await page.goto(self.config.portal_url, wait_until="domcontentloaded")
                    await self._open_mail(page)
                await self._open_compose(page)
                await self._fill_message(page, message)

                if self.confirm_before_send:
                    print("Review the composed mail in the browser.")
                    if self.prepare_only:
                        print("Prepare-only mode: the send button will not be clicked.")
                        return SendResult(
                            ok=True,
                            mode="playwright-prepare-only",
                            message=message,
                            detail="message composed; send was not clicked",
                        )
                    print("Press Enter to send, or Ctrl+C to cancel.")
                    await asyncio.to_thread(input)

                await self._click_first(page, "sendButtons")
                await self._click_optional(page, "confirmationButtons", timeout_ms=3000)
                detail = await self._wait_for_success(page)
                return SendResult(ok=True, mode="playwright", message=message, detail=detail)
            except PlaywrightTimeoutError as exc:
                await page.screenshot(path="knox-mail-timeout.png", full_page=True)
                raise RuntimeError(
                    "Timed out while automating Knox Portal. "
                    "Check config/knox-mail.selectors.json and knox-mail-timeout.png."
                ) from exc
            finally:
                await context.close()

    async def inspect(self) -> dict[str, Any]:
        try:
            from playwright.async_api import async_playwright
        except ImportError as exc:
            raise RuntimeError(
                "Playwright is not installed. Run: python -m pip install -r requirements.txt"
            ) from exc

        async with async_playwright() as playwright:
            context = await playwright.chromium.launch_persistent_context(
                user_data_dir=str(self.profile_dir),
                executable_path=self.executable_path,
                headless=self.headless,
                slow_mo=self.slow_mo_ms,
            )
            page = context.pages[0] if context.pages else await context.new_page()
            page.set_default_timeout(self.timeout_ms)
            inspect_url = (self.config.selectors.get("mailUrls") or [self.config.portal_url])[0]
            await page.goto(inspect_url, wait_until="domcontentloaded")
            await page.wait_for_load_state("load", timeout=10000)
            snapshot = await page.locator("body").evaluate(
                """
                () => {
                  const interesting = [
                    ...document.querySelectorAll('button,a,input,textarea,[contenteditable="true"]')
                  ].slice(0, 200);
                  return interesting.map((el) => ({
                    tag: el.tagName.toLowerCase(),
                    text: (el.innerText || el.value || '').trim().slice(0, 80),
                    aria: el.getAttribute('aria-label'),
                    role: el.getAttribute('role'),
                    name: el.getAttribute('name'),
                    id: el.id || null,
                    testid: el.getAttribute('data-testid')
                  }));
                }
                """
            )
            body_text = await page.locator("body").evaluate("() => document.body.innerText.slice(0, 2000)")
            screenshot_path = str(Path("knox-mail-inspect.png").resolve())
            await page.screenshot(path=screenshot_path, full_page=True)
            current_url = page.url
            title = await page.title()
            await context.close()
            return {
                "url": current_url,
                "title": title,
                "bodyText": body_text,
                "screenshot": screenshot_path,
                "elements": snapshot,
            }

    async def _open_mail(self, page: Any) -> None:
        if await self._click_optional(page, "mailEntrypoints", timeout_ms=5000):
            await page.wait_for_load_state("domcontentloaded")

    async def _goto_mail(self, page: Any) -> bool:
        mail_urls = self.config.selectors.get("mailUrls", [])
        for url in mail_urls:
            try:
                await page.goto(url, wait_until="domcontentloaded")
                return True
            except Exception:
                continue
        return False

    async def _open_compose(self, page: Any) -> None:
        await self._click_first(page, "composeButtons")

    async def _fill_message(self, page: Any, message: MailMessage) -> None:
        await self._fill_first(page, "recipientInputs", message.recipient)
        await page.keyboard.press("Enter")

        if message.cc:
            # CC layouts vary widely; keep this explicit until selectors are calibrated.
            print("CC was parsed but no generic CC selector is configured:", ", ".join(message.cc))

        if message.subject:
            await self._fill_first(page, "subjectInputs", message.subject)

        await self._fill_body(page, message.body)

    async def _fill_body(self, page: Any, body: str) -> None:
        selectors = self.config.selectors.get("bodyInputs", [])
        last_error: Exception | None = None
        for selector in selectors:
            try:
                locator = page.locator(selector).first
                if selector.startswith("iframe"):
                    frame = page.frame_locator(selector).locator("[contenteditable='true'], body").first
                    await frame.fill(body)
                    return
                await locator.wait_for(state="visible", timeout=4000)
                await locator.fill(body)
                return
            except Exception as exc:  # Playwright raises specific subclasses per locator path.
                last_error = exc
        raise RuntimeError(f"Could not find body editor. Last error: {last_error}")

    async def _fill_first(self, page: Any, selector_key: str, value: str) -> None:
        selectors = self.config.selectors.get(selector_key, [])
        last_error: Exception | None = None
        for selector in selectors:
            try:
                locator = page.locator(selector).first
                await locator.wait_for(state="visible", timeout=4000)
                await locator.fill(value)
                return
            except Exception as exc:
                last_error = exc
        raise RuntimeError(f"Could not fill {selector_key}. Last error: {last_error}")

    async def _click_first(self, page: Any, selector_key: str) -> None:
        clicked = await self._click_optional(page, selector_key, timeout_ms=self.timeout_ms)
        if not clicked:
            raise RuntimeError(f"Could not click any selector from {selector_key}")

    async def _click_optional(self, page: Any, selector_key: str, timeout_ms: int) -> bool:
        selectors = self.config.selectors.get(selector_key, [])
        for selector in selectors:
            try:
                locator = page.locator(selector).first
                await locator.wait_for(state="visible", timeout=timeout_ms)
                await locator.click()
                return True
            except Exception:
                continue
        return False

    async def _wait_for_success(self, page: Any) -> str | None:
        for selector in self.config.selectors.get("sentSuccessSignals", []):
            try:
                locator = page.locator(selector).first
                await locator.wait_for(state="visible", timeout=5000)
                text = await locator.inner_text(timeout=1000)
                return text.strip() or "success signal detected"
            except Exception:
                continue
        return "send button clicked; no explicit success signal configured"

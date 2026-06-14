from __future__ import annotations

from .adapters import DryRunMailAdapter, MailAdapter
from .adapters.playwright_portal import PlaywrightPortalMailAdapter
from .models import MailMessage, SendResult
from .parser import parse_chat_command


class ToolRuntime:
    def __init__(self, adapter: MailAdapter | None = None) -> None:
        self.adapter = adapter or DryRunMailAdapter()

    async def send_knox_mail(self, message: MailMessage) -> SendResult:
        return await self.adapter.send(message)

    async def send_knox_mail_from_chat(self, text: str) -> SendResult:
        return await self.send_knox_mail(parse_chat_command(text))


def build_adapter(
    adapter_name: str,
    *,
    headless: bool = False,
    confirm_before_send: bool = True,
    profile_dir: str = ".knox-profile",
    prepare_only: bool = False,
    executable_path: str | None = None,
) -> MailAdapter:
    if adapter_name == "dry-run":
        return DryRunMailAdapter()
    if adapter_name == "playwright":
        return PlaywrightPortalMailAdapter(
            headless=headless,
            confirm_before_send=confirm_before_send,
            profile_dir=profile_dir,
            prepare_only=prepare_only,
            executable_path=executable_path,
        )
    raise ValueError(f"Unknown adapter: {adapter_name}")

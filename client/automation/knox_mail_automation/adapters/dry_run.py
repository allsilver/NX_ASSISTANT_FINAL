from __future__ import annotations

from ..models import MailMessage, SendResult
from .base import MailAdapter


class DryRunMailAdapter(MailAdapter):
    async def send(self, message: MailMessage) -> SendResult:
        return SendResult(ok=True, mode="dry-run", message=message)

from __future__ import annotations

from abc import ABC, abstractmethod

from ..models import MailMessage, SendResult


class MailAdapter(ABC):
    @abstractmethod
    async def send(self, message: MailMessage) -> SendResult:
        raise NotImplementedError

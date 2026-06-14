from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Any


@dataclass(frozen=True)
class MailMessage:
    recipient: str
    body: str
    subject: str | None = None
    cc: tuple[str, ...] = ()

    def to_dict(self) -> dict[str, Any]:
        data = asdict(self)
        data["cc"] = list(self.cc)
        return {key: value for key, value in data.items() if value not in (None, "", [])}


@dataclass(frozen=True)
class SendResult:
    ok: bool
    mode: str
    message: MailMessage
    detail: str | None = None

    def to_dict(self) -> dict[str, Any]:
        data: dict[str, Any] = {
            "ok": self.ok,
            "mode": self.mode,
            "message": self.message.to_dict(),
        }
        if self.detail:
            data["detail"] = self.detail
        return data

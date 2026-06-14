from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SELECTOR_PATH = ROOT / "config" / "knox-mail.selectors.json"


@dataclass(frozen=True)
class PortalConfig:
    portal_url: str
    selectors: dict[str, list[str]]

    @classmethod
    def load(cls, path: str | Path | None = None) -> "PortalConfig":
        source = Path(path) if path else DEFAULT_SELECTOR_PATH
        with source.open("r", encoding="utf-8") as file:
            data: dict[str, Any] = json.load(file)

        portal_url = str(data.get("portalUrl") or "http://kor1.samsung.net/portalapp/home")
        selectors = {
            key: value
            for key, value in data.items()
            if isinstance(value, list) and all(isinstance(item, str) for item in value)
        }
        return cls(portal_url=portal_url, selectors=selectors)

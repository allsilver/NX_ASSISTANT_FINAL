from __future__ import annotations

import re

from .models import MailMessage


class CommandParseError(ValueError):
    pass


_QUOTED_BODY_PATTERNS = [
    re.compile(
        r"수신자\s+(?P<recipient>.+?)에게\s+[\"'“”‘’](?P<body>.*?)[\"'“”‘’]\s*(?:라고|라\s*고)?",
        re.DOTALL,
    ),
    re.compile(
        r"(?P<recipient>.+?)에게\s+[\"'“”‘’](?P<body>.*?)[\"'“”‘’]\s*(?:라고|라\s*고)?",
        re.DOTALL,
    ),
]

_UNQUOTED_BODY_PATTERNS = [
    re.compile(
        r"수신자\s+(?P<recipient>.+?)에게\s+(?P<body>.+?)\s*(?:라고|라\s*고)?\s*(?:적고|써서|작성해서|보내|발송|전송)",
        re.DOTALL,
    ),
    re.compile(
        r"(?P<recipient>.+?)에게\s+(?P<body>.+?)\s*(?:라고|라\s*고)?\s*(?:적고|써서|작성해서|보내|발송|전송)",
        re.DOTALL,
    ),
]

_SUBJECT_PATTERNS = [
    re.compile(r"(?:제목|subject)\s*[은는:：]\s*[\"'“”‘’]?(?P<subject>[^\"'“”‘’\n]+)", re.IGNORECASE),
    re.compile(r"[\"'“”‘’](?P<subject>[^\"'“”‘’]+)[\"'“”‘’]\s*(?:제목으로|라는 제목으로)"),
]

_CC_PATTERN = re.compile(r"(?:참조|cc)\s+(?P<cc>.+?)(?:에게|로|,|$)", re.IGNORECASE)


def parse_chat_command(text: str) -> MailMessage:
    normalized = _normalize(text)
    subject = _extract_subject(normalized)
    cc = _extract_cc(normalized)

    for pattern in [*_QUOTED_BODY_PATTERNS, *_UNQUOTED_BODY_PATTERNS]:
        match = pattern.search(normalized)
        if not match:
            continue

        recipient = _clean_recipient(match.group("recipient"))
        body = _clean_body(match.group("body"))
        if recipient and body:
            return MailMessage(recipient=recipient, subject=subject, body=body, cc=cc)

    raise CommandParseError(
        '메일 명령을 이해하지 못했습니다. 예: 수신자 서다은에게 "hi"라고 적고 발송해줘'
    )


def _normalize(text: str) -> str:
    value = re.sub(r"\s+", " ", text.strip())
    value = value.replace("＂", '"').replace("'", "'")
    return value


def _extract_subject(text: str) -> str | None:
    for pattern in _SUBJECT_PATTERNS:
        match = pattern.search(text)
        if match:
            return _clean_subject(match.group("subject"))
    return None


def _extract_cc(text: str) -> tuple[str, ...]:
    match = _CC_PATTERN.search(text)
    if not match:
        return ()
    raw = match.group("cc")
    values = [part.strip() for part in re.split(r"[,;/、]", raw) if part.strip()]
    return tuple(values)


def _clean_recipient(value: str) -> str:
    recipient = value.strip()
    recipient = re.sub(r"^(메일\s*)?수신자\s+", "", recipient)
    recipient = re.sub(r"\s*(?:님|씨)$", "", recipient)
    return recipient.strip()


def _clean_body(value: str) -> str:
    body = value.strip()
    body = re.sub(r"\s*(?:적고|써서|작성해서)?\s*(?:보내줘|발송해줘|전송해줘|보내|발송|전송)\s*$", "", body)
    return body.strip()


def _clean_subject(value: str) -> str:
    subject = value.strip()
    subject = re.sub(r"\s*(?:적고|써서|작성해서)?\s*(?:보내줘|발송해줘|전송해줘|보내|발송|전송)\s*$", "", subject)
    return subject.strip()

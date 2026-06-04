# router/llm_router.py
# 1차 라우터 — 질문이 어떤 MCP인지만 분류
# 프롬프트 최소화: "DB야? NX야? 브라우저야? 잡담이야?" 만 판단
# 최근 1턴 히스토리만 전달 (맥락 파악 최소 필요)

import json
import os
import re
from pathlib import Path

_ROUTER_PROMPT_PATH = Path(__file__).parent / "prompts" / "router.txt"


def _load_prompt() -> str:
    if _ROUTER_PROMPT_PATH.exists():
        return _ROUTER_PROMPT_PATH.read_text(encoding="utf-8")
    # fallback 기본 프롬프트
    return """너는 질문 분류기다. 아래 질문이 어떤 종류인지만 JSON으로 출력하라. 설명 없음.

intent 종류:
- "db_search"   : 설계 표준, DB 검색, 수치/규격 관련 질문
- "nx_control"  : NX CAD 제어, 모델링, NX 작업 관련 질문
- "browser"     : 브라우저/웹 자동화, 포털, PLM 관련 질문
- "chat"        : 인사, 잡담, 사용법 문의, 기타

[이전 대화]
{history}

질문: {question}
출력 (JSON만):
"""


def route(question: str, history_text: str, llm) -> dict:
    """
    1차 라우팅 수행.

    Returns:
        {"intent": "db_search" | "nx_control" | "browser" | "chat"}
    """
    prompt_template = _load_prompt()
    prompt = prompt_template.format(
        question=question,
        history=history_text or "없음"
    )

    try:
        from langchain_core.prompts import ChatPromptTemplate
        from langchain_core.output_parsers import StrOutputParser

        chain = ChatPromptTemplate.from_template("{prompt}") | llm | StrOutputParser()
        result = chain.invoke({"prompt": prompt})

        # JSON 파싱
        m = re.search(r'\{[^{}]+\}', result, re.DOTALL)
        if m:
            parsed = json.loads(m.group())
            intent = parsed.get("intent", "chat")
            if intent in ("db_search", "nx_control", "browser", "chat"):
                return {"intent": intent}

        print(f"⚠️  라우터 JSON 파싱 실패 → chat 폴백. raw: {result[:80]!r}")
        return {"intent": "chat"}

    except Exception as e:
        print(f"⚠️  1차 라우터 실패 → chat 폴백: {e}")
        return {"intent": "chat"}

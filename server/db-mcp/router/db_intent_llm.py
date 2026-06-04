# router/db_intent_llm.py
# 2차 DB LLM — db_search로 분류된 질문의 도메인 파악 + 쿼리 재작성
# 최근 2턴 히스토리 전달 (후속 질문 맥락 파악)

import json
import re
from pathlib import Path

_DB_INTENT_PROMPT_PATH = Path(__file__).parent / "prompts" / "db_intent.txt"


def _load_prompt() -> str:
    if _DB_INTENT_PROMPT_PATH.exists():
        return _DB_INTENT_PROMPT_PATH.read_text(encoding="utf-8")
    return """너는 DB 검색 전문가다. 질문을 분석해서 JSON만 출력하라. 설명 없음.

[등록된 도메인]
{domain_list}

판단 기준:
- domain: 질문과 가장 관련 있는 도메인 키 (위 목록에서 선택)
- rewritten_query: 오타 교정, 유의어 반영, 이전 대화 맥락을 포함한 검색에 최적화된 쿼리
- case:
    1 = 특정 항목의 수치/조건을 묻는 질문
    2 = 관련 항목 전체 목록을 묻는 질문

[이전 대화]
{history}

질문: {question}
출력 (JSON만):
"""


def analyze(question: str, history_text: str, domain_registry: dict, llm) -> dict:
    """
    2차 DB intent 분석.

    Returns:
        {
            "domain": "MEG_STANDARD",
            "rewritten_query": "재작성된 쿼리",
            "case": 1
        }
    """
    # 도메인 목록 텍스트 생성
    domain_lines = []
    for key, cfg in domain_registry.items():
        if key.startswith("__"):
            continue
        name = cfg.get("display_name", key)
        desc = cfg.get("description", "")
        domain_lines.append(f"- {key} ({name}): {desc}")
    domain_list_text = "\n".join(domain_lines)

    # 기본 도메인 (첫 번째 도메인)
    default_domain = next(
        (k for k in domain_registry if not k.startswith("__")),
        "MEG_STANDARD"
    )

    prompt_template = _load_prompt()
    prompt = prompt_template.format(
        domain_list=domain_list_text,
        question=question,
        history=history_text or "없음"
    )

    try:
        from langchain_core.prompts import ChatPromptTemplate
        from langchain_core.output_parsers import StrOutputParser

        chain = ChatPromptTemplate.from_template("{prompt}") | llm | StrOutputParser()
        result = chain.invoke({"prompt": prompt})

        m = re.search(r'\{[^{}]+\}', result, re.DOTALL)
        if m:
            parsed = json.loads(m.group())
            return {
                "domain":          parsed.get("domain", default_domain),
                "rewritten_query": parsed.get("rewritten_query", question),
                "case":            int(parsed.get("case", 1)),
            }

        print(f"⚠️  DB intent JSON 파싱 실패 → 기본값 사용. raw: {result[:80]!r}")

    except Exception as e:
        print(f"⚠️  2차 DB LLM 실패 → 기본값 사용: {e}")

    return {
        "domain":          default_domain,
        "rewritten_query": question,
        "case":            1,
    }

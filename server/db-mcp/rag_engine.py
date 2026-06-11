# rag_engine.py
# RAG 파이프라인 — MEG_ChatBot_claude v3.2에서 이식
# 변경사항:
#   - setup_design_bot() 시그니처 단순화 (NX Assistant 용도에 맞게)
#   - Query Planner 제거 (LLM 라우터가 대신 처리)
#   - domain_key, case는 외부(router)에서 받아서 처리

import os
import re
import json
import time as _time
import threading as _threading
from pathlib import Path
from langchain_core.prompts import ChatPromptTemplate
from langchain_core.output_parsers import StrOutputParser
from langchain_core.documents import Document

os.environ["no_proxy"] = "localhost,127.0.0.1"
os.environ["NO_PROXY"] = "localhost,127.0.0.1"

CHAT_HISTORY_TURNS   = 3
RERANK_TOP_N         = 7
EXPAND_TOP_SECTION   = 3
MAX_DOCS_PER_SECTION = 15
SECTION_SCORE_THRESHOLD = 0.3

_SRC_DIR      = Path(__file__).parent
_PROJECT_ROOT = _SRC_DIR.parent  # server/
DATA_ROOT     = _PROJECT_ROOT / "data"   # server/data/

RERANKER_MODEL_PATH = _PROJECT_ROOT / "models" / "bge-reranker-v2-m3"
_reranker = None


# ── Cross-encoder ────────────────────────────────────────────────
def _get_reranker():
    global _reranker
    if _reranker is not None:
        return _reranker
    if not RERANKER_MODEL_PATH.exists():
        print(f"⚠️  Reranker 모델 없음: {RERANKER_MODEL_PATH}")
        return None
    try:
        from sentence_transformers import CrossEncoder
        _reranker = CrossEncoder(str(RERANKER_MODEL_PATH))
        print("✅ Cross-encoder 로드 완료")
        return _reranker
    except Exception as e:
        print(f"⚠️  Cross-encoder 로드 실패: {e}")
        return None


def _preload_reranker():
    _get_reranker()

_threading.Thread(target=_preload_reranker, daemon=True).start()


def _rerank_docs(docs: list, query: str, top_n: int = RERANK_TOP_N) -> list:
    if not docs:
        return []
    reranker = _get_reranker()
    if reranker is None:
        return [(0.0, doc) for doc in docs[:top_n]]
    try:
        pairs  = [(query, doc.page_content) for doc in docs]
        scores = reranker.predict(pairs)
        ranked = sorted(zip(scores, docs), key=lambda x: x[0], reverse=True)
        return [(float(score), doc) for score, doc in ranked[:top_n]]
    except Exception as e:
        print(f"⚠️  Reranking 오류: {e}")
        return [(0.0, doc) for doc in docs[:top_n]]


# ── 섹션 확장 조회 ───────────────────────────────────────────────
def _expand_docs_by_full_path(
    scored_docs:      list,
    vector_dbs:       dict,
    top_section_n:    int   = EXPAND_TOP_SECTION,
    score_threshold:  float = SECTION_SCORE_THRESHOLD,
    max_docs_per_sec: int   = MAX_DOCS_PER_SECTION,
) -> list:
    if not scored_docs:
        return []

    all_scores    = [s for s, _ in scored_docs]
    use_threshold = any(s != 0.0 for s in all_scores)

    if use_threshold:
        top1_score = scored_docs[0][0]
        selected_paths: list[str] = []
        seen_paths:     set[str]  = set()

        for score, doc in scored_docs:
            path = doc.metadata.get("full_path", "")
            if not path or path in seen_paths:
                continue
            if top1_score - score > score_threshold:
                continue
            selected_paths.append(path)
            seen_paths.add(path)
            if len(selected_paths) >= top_section_n:
                break
    else:
        selected_paths = list(dict.fromkeys(
            doc.metadata.get("full_path", "")
            for _, doc in scored_docs
            if doc.metadata.get("full_path", "")
        ))[:top_section_n]

    if not selected_paths:
        return [doc for _, doc in scored_docs]

    expanded_docs = []
    seen_contents: set[str] = set()

    for path in selected_paths:
        for vdb in vector_dbs.values():
            try:
                result = vdb.get(
                    where={"full_path": path},
                    include=["documents", "metadatas"],
                )
                for content, meta in zip(
                    result.get("documents", []),
                    result.get("metadatas", []),
                ):
                    if content and content not in seen_contents:
                        seen_contents.add(content)
                        expanded_docs.append(
                            Document(page_content=content, metadata=meta or {})
                        )
                        section_count = len([
                            d for d in expanded_docs
                            if d.metadata.get("full_path") == path
                        ])
                        if section_count >= max_docs_per_sec:
                            break
            except Exception as e:
                print(f"⚠️  섹션 확장 실패 [{path}]: {e}")

    if not expanded_docs:
        return [doc for _, doc in scored_docs]

    print(f"✅ 섹션 확장: {len(selected_paths)}개 섹션 → {len(expanded_docs)}개 문서")
    return expanded_docs


# ── Context 구성 ─────────────────────────────────────────────────
def _build_context(docs: list, use_guide_raw: bool = True) -> str:
    parts = []
    for doc in docs:
        meta          = doc.metadata or {}
        text          = doc.page_content
        guide_raw     = meta.get("guide_raw", "")
        reason        = meta.get("Reason", "")
        item          = meta.get("item", "") or meta.get("Item", "")
        category_path = meta.get("category_path", "") or meta.get("full_path", "")

        if category_path:
            path_parts = [p.strip() for p in category_path.split(">") if p.strip()]
            short_path = " > ".join(path_parts[-2:]) if len(path_parts) >= 2 else category_path
            header = f"[섹션: {short_path}]"
            if item:
                header += f"\n[항목: {item}]"
            text = f"{header}\n{text}"

        if use_guide_raw and guide_raw:
            text += f"\n[원문 수치: {guide_raw}]"
        if reason and str(reason).strip():
            text += f"\n[이유: {str(reason).strip()}]"
        parts.append(text)
    return "\n\n".join(parts)


# ── 히스토리 포맷 ────────────────────────────────────────────────
def _format_history(chat_history: list) -> str:
    if not chat_history:
        return ""
    recent = chat_history[-(CHAT_HISTORY_TURNS * 2):]
    lines  = []
    for m in recent:
        if m["role"] == "user":
            lines.append(f"사용자: {m['content']}")
        else:
            ans = m["content"].strip()
            # 이전 답변은 첫 줄만 (수치 혼입 방지)
            first = ans.split("\n")[0][:100]
            lines.append(f"어시스턴트: {first}...")
    return "\n".join(lines)


# ── 프롬프트 로드 ────────────────────────────────────────────────
def _load_prompt_template(prompt_file: str) -> str:
    prompt_path = _SRC_DIR / "prompts" / prompt_file
    if not prompt_path.exists():
        raise FileNotFoundError(f"프롬프트 파일 없음: {prompt_path}")
    return prompt_path.read_text(encoding="utf-8")


# ── 이미지 경로 탐색 (MEG_ChatBot_claude _find_image_paths 이식) ───
#   파일명 규칙: "{db_key} {full_path 마지막 2개 세그먼트}.png"
#   예) db_key=foldable, full_path="Damper 부 설계 > Damper Front > Damper Front 설계 Flip"
#       → "foldable Damper Front Damper Front 설계 Flip.png"
#   리랭크 점수를 min-max 정규화해 관련성 %(score_pct) 부여, % 내림차순 정렬.
def _image_base_dir(domain_key: str) -> Path:
    return DATA_ROOT / domain_key / "image"


def _find_image_paths(scored_docs: list, image_base_dir: Path) -> list:
    """(score, doc) 리스트 → [{path, name, score_pct}] (관련성 순). 파일 없으면 스킵."""
    if not scored_docs:
        return []

    all_scores = [score for score, _ in scored_docs]
    use_score  = any(s != 0.0 for s in all_scores)
    min_s = min(all_scores) if use_score else 0.0
    max_s = max(all_scores) if use_score else 0.0

    def to_pct(score: float):
        if max_s == min_s:
            return 100
        return round((score - min_s) / (max_s - min_s) * 100)

    results: list = []
    seen:    set  = set()

    for score, doc in scored_docs:
        meta      = doc.metadata or {}
        db_key    = str(meta.get("db_key", "")).strip()
        full_path = str(meta.get("full_path", "")).strip()
        if not db_key or not full_path:
            continue

        segments = [s.strip() for s in full_path.split(">") if s.strip()]
        if not segments:
            continue
        last_two  = " ".join(segments[-2:]) if len(segments) >= 2 else segments[0]
        file_stem = f"{db_key} {last_two}"

        if file_stem in seen:
            continue
        seen.add(file_stem)

        candidate = image_base_dir / f"{file_stem}.png"
        if candidate.exists():
            results.append({
                "path":      str(candidate),
                "name":      candidate.name,
                "score_pct": to_pct(score) if use_score else None,
            })

    results.sort(
        key=lambda x: x["score_pct"] if x["score_pct"] is not None else -1,
        reverse=True,
    )
    return results


# ── SearchClient ─────────────────────────────────────────────────
class SearchClient:
    def __init__(self, retriever_fn, vector_dbs: dict, use_reranker: bool = True):
        self._retriever    = retriever_fn
        self._vector_dbs   = vector_dbs
        self._use_reranker = use_reranker

    def search(self, query: str):
        """Returns (pure_docs, scored_docs)"""
        raw_docs = self._retriever(query)
        if self._use_reranker:
            scored_docs = _rerank_docs(raw_docs, query)
        else:
            scored_docs = [(0.0, d) for d in raw_docs]

        if self._vector_dbs:
            pure_docs = _expand_docs_by_full_path(scored_docs, self._vector_dbs)
        else:
            pure_docs = [d for _, d in scored_docs]

        return pure_docs, scored_docs


# ── 메인 팩토리 ──────────────────────────────────────────────────
def setup_design_bot(
    retriever,
    domain_config:  dict,
    vector_dbs:     dict = None,
    use_think:      bool = False,
    model_override: str  = None,
    exp_config:     dict = None,
    domain_key:     str  = "",
):
    """
    RAG 핸들러 반환.

    Args:
        retriever:      검색 함수 또는 vector_dbs dict (하위 호환)
        domain_config:  domain_registry의 해당 도메인 설정
        vector_dbs:     섹션 확장용 ChromaDB dict
        use_think:      Thinking 모드 여부
        model_override: 모델 강제 지정
        exp_config:     실험 설정 (settings.json의 experiment 섹션)
    """
    if isinstance(retriever, dict):
        from retrievers.vector_retriever import VectorRetriever
        _vdbs     = retriever
        retriever = VectorRetriever(_vdbs).search
        if vector_dbs is None:
            vector_dbs = _vdbs

    if exp_config is None:
        exp_config = {}

    fixed         = exp_config.get("fixed", {})
    model_name    = model_override or domain_config.get("model", "gauss:o4-instruct")
    use_reranker  = fixed.get("reranker", True)
    use_guide_raw = fixed.get("guide_raw", True)
    num_ctx       = fixed.get("num_ctx", 4096)

    from llm import get_llm
    llm = get_llm(model_name, num_ctx=num_ctx, exp_config=exp_config)

    no_think_prefix = "" if use_think else "/no_think\n"

    # 기본 답변 프롬프트/체인
    #   answer_prompt : 원본 템플릿(GPT 컴포즈 전용). /no_think 등 Gauss 전용 토큰 미포함.
    #   answer_chain  : (/no_think + 템플릿) | Gauss. Gauss 답변 생성용.
    #   ※ 검색·컨텍스트·invoke_input 은 rag_handler 에서 공유 → 프롬프트/검색 수정 시 Gauss·GPT 양쪽 자동 반영.
    answer_prompt_file = domain_config.get("prompt_file", "MECH_STANDARD.txt")
    _answer_template   = _load_prompt_template(answer_prompt_file)
    answer_prompt      = ChatPromptTemplate.from_template(_answer_template)
    answer_chain       = ChatPromptTemplate.from_template(no_think_prefix + _answer_template) | llm | StrOutputParser()

    # 케이스별 전용 프롬프트/체인
    case_prompts: dict[int, object] = {}
    case_chains:  dict[int, object] = {}
    for case_str, case_pf in domain_config.get("case_prompts", {}).items():
        try:
            _ct = _load_prompt_template(case_pf)
            case_prompts[int(case_str)] = ChatPromptTemplate.from_template(_ct)
            case_chains[int(case_str)]  = ChatPromptTemplate.from_template(no_think_prefix + _ct) | llm | StrOutputParser()
            print(f"✅ Case {case_str} 체인 로드: {case_pf}")
        except Exception as e:
            print(f"⚠️  Case {case_str} 체인 로드 실패: {e}")

    search_client = SearchClient(retriever, vector_dbs, use_reranker)
    image_base_dir = _image_base_dir(domain_key)   # server/data/<domain_key>/image
    print(f"✅ RAG 봇 초기화 완료 (model={model_name}, reranker={use_reranker}, image_dir={image_base_dir})")

    def rag_handler(query: str, chat_history: list = None, case: int = 1, synonym_hint: str = "", compose_only: bool = False):
        """반환: (답변_또는_프롬프트 문자열, images[{path,name,score_pct}])"""
        raw_docs = search_client._retriever(query)
        if use_reranker:
            scored_docs = _rerank_docs(raw_docs, query)
        else:
            scored_docs = [(0.0, d) for d in raw_docs]

        if vector_dbs:
            pure_docs = _expand_docs_by_full_path(scored_docs, vector_dbs)
        else:
            pure_docs = [d for _, d in scored_docs]

        if not pure_docs:
            return "관련 표준을 찾지 못했습니다. 질문을 다시 입력해주세요.", []

        # 이미지: 리랭크된 scored_docs 기준 (점수 → 관련성 %)
        images = _find_image_paths(scored_docs, image_base_dir)

        context_text = _build_context(pure_docs, use_guide_raw=use_guide_raw)
        history_text = _format_history(chat_history or [])

        invoke_input = {
            "context":      context_text,
            "question":     query,
            "case":         str(case),
            "synonym_hint": synonym_hint,
            "chat_history": history_text,
        }

        # GPT(컴포즈 전용): Gauss 호출 없이 "완성된 프롬프트 문자열"만 반환. (이미지는 동일하게 반환)
        if compose_only:
            selected_prompt = case_prompts.get(case, answer_prompt)
            msgs = selected_prompt.format_messages(**invoke_input)
            return "\n\n".join(getattr(m, "content", str(m)) for m in msgs), images

        selected_chain = case_chains.get(case, answer_chain)
        return selected_chain.invoke(invoke_input), images

    def rag_handler_stream(query: str, chat_history: list = None, case: int = 1, synonym_hint: str = ""):
        raw_docs = search_client._retriever(query)
        if use_reranker:
            scored_docs = _rerank_docs(raw_docs, query)
        else:
            scored_docs = [(0.0, d) for d in raw_docs]

        if vector_dbs:
            pure_docs = _expand_docs_by_full_path(scored_docs, vector_dbs)
        else:
            pure_docs = [d for _, d in scored_docs]

        if not pure_docs:
            yield "관련 표준을 찾지 못했습니다. 질문을 다시 입력해주세요."
            return

        context_text = _build_context(pure_docs, use_guide_raw=use_guide_raw)
        history_text = _format_history(chat_history or [])

        invoke_input = {
            "context":      context_text,
            "question":     query,
            "case":         str(case),
            "synonym_hint": synonym_hint,
            "chat_history": history_text,
        }

        selected_chain = case_chains.get(case, answer_chain)
        for chunk in selected_chain.stream(invoke_input):
            yield chunk

    rag_handler.stream = rag_handler_stream
    return rag_handler

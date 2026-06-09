# server.py
# DB MCP HTTP 서버
# 엔드포인트:
#   GET  /health          — 서버 상태 확인
#   GET  /mech/domains     — 등록된 도메인 목록
#   GET  /mech/dbkeys      — 도메인별 선택 가능한 db_key 목록 (카드용)
#   POST /mech/ask         — RAG 검색 + 답변 생성 (db_keys 로 범위 지정)
#   POST /mech/route       — 1차 라우팅만 (intent 분류)

from __future__ import annotations

import json
import os
import sys
import logging
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs

# 경로 설정
ROOT = Path(__file__).parent
DATA_ROOT = ROOT.parent / "data"   # server/data/  (vector_store 와 동일 규칙)
sys.path.insert(0, str(ROOT))

HOST  = os.getenv("DB_MCP_HOST", "127.0.0.1")
PORT  = int(os.getenv("DB_MCP_PORT", "8766"))
TOKEN = os.getenv("DB_MCP_TOKEN", "dev-only-token-change-me")

# 설정 파일 로드
_SETTINGS_PATH = ROOT.parent / "config" / "settings.json"
_settings: dict = {}
if _SETTINGS_PATH.exists():
    with open(_SETTINGS_PATH, encoding="utf-8") as f:
        _settings = json.load(f)
    TOKEN = _settings.get("db_mcp_token", TOKEN)

# domain_registry 로드
_DOMAIN_REGISTRY_PATH = ROOT / "domain_registry.json"
_domain_registry: dict = {}
if _DOMAIN_REGISTRY_PATH.exists():
    with open(_DOMAIN_REGISTRY_PATH, encoding="utf-8") as f:
        _domain_registry = json.load(f)

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(message)s",
)
logger = logging.getLogger(__name__)

# ── 봇 캐시 (도메인별 RAG 봇 인스턴스) ─────────────────────────
_bot_cache: dict = {}


def _resolve_db_options(domain_key: str) -> dict:
    """도메인의 선택 가능한 db_key 목록.
    우선순위: data/{domain}/db_registry_{domain}.json
              → domain_registry 의 db_keys → 도메인명 단일.
    반환: {db_key: {display_name, description, default}}
    """
    reg_path = DATA_ROOT / domain_key / f"db_registry_{domain_key}.json"
    if reg_path.exists():
        try:
            with open(reg_path, encoding="utf-8") as f:
                raw = json.load(f)
            return {
                k: {
                    "display_name": v.get("display_name", k),
                    "description":  v.get("description", ""),
                    "default":      bool(v.get("default", False)),
                }
                for k, v in raw.items()
            }
        except Exception as e:
            logger.error(f"db_registry 읽기 실패 [{domain_key}]: {e}")

    # fallback: domain_registry 의 db_keys 또는 도메인명 단일
    dc   = _domain_registry.get(domain_key, {})
    keys = dc.get("db_keys", [domain_key])
    return {k: {"display_name": k, "description": "", "default": False} for k in keys}


def _get_or_load_bot(domain_key: str, db_keys: list):
    cache_key = f"{domain_key}:{','.join(sorted(db_keys))}"
    if cache_key in _bot_cache:
        return _bot_cache[cache_key]

    domain_config = _domain_registry.get(domain_key)
    if not domain_config:
        raise ValueError(f"알 수 없는 도메인: {domain_key}")

    from vector_store import load_multiple_vector_dbs
    from rag_engine import setup_design_bot
    from retrievers.vector_retriever import VectorRetriever

    vector_dbs = load_multiple_vector_dbs(domain_key, db_keys)

    retriever  = VectorRetriever(vector_dbs).search
    exp_config = _settings.get("experiment", {})

    bot = setup_design_bot(
        retriever     = retriever,
        domain_config = domain_config,
        vector_dbs    = vector_dbs,
        exp_config    = exp_config,
    )
    _bot_cache[cache_key] = bot
    logger.info(f"봇 로드 완료: {cache_key}")
    return bot


# ── 라우터 ───────────────────────────────────────────────────────
def _get_router_llm():
    """라우터용 LLM (Gauss 경량 모델 사용)"""
    from llm import get_llm
    router_model = _settings.get("router_model", "gauss:o4-instruct")
    return get_llm(router_model, exp_config=_settings.get("experiment", {}))


# ── HTTP 핸들러 ──────────────────────────────────────────────────
class Handler(BaseHTTPRequestHandler):

    def log_message(self, format, *args):
        logger.info(f"{self.client_address[0]} - {format % args}")

    def _client_ip(self) -> str:
        return self.client_address[0]

    def _auth(self) -> bool:
        auth = self.headers.get("Authorization", "")
        return auth == f"Bearer {TOKEN}"

    def _send_json(self, status: HTTPStatus, data: dict):
        body = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status.value)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _send_error(self, status: HTTPStatus, message: str):
        self._send_json(status, {"error": message})

    def _read_body(self) -> dict | None:
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0 or length > 256_000:
            return None
        return json.loads(self.rfile.read(length).decode("utf-8"))

    def do_GET(self):
        parsed = urlparse(self.path)

        if parsed.path == "/health":
            self._send_json(HTTPStatus.OK, {
                "status": "ok",
                "service": "nx-assistant-db-mcp",
                "domains": [k for k in _domain_registry if not k.startswith("__")],
            })
            return

        if parsed.path == "/mech/domains":
            if not self._auth():
                self._send_error(HTTPStatus.UNAUTHORIZED, "unauthorized")
                return
            domains = [
                {
                    "key":          k,
                    "display_name": v.get("display_name", k),
                    "description":  v.get("description", ""),
                }
                for k, v in _domain_registry.items()
                if not k.startswith("__")
            ]
            self._send_json(HTTPStatus.OK, {"domains": domains})
            return

        if parsed.path == "/mech/dbkeys":
            if not self._auth():
                self._send_error(HTTPStatus.UNAUTHORIZED, "unauthorized")
                return
            qs     = parse_qs(parsed.query)
            domain = (qs.get("domain", [""])[0]).strip()
            if not domain or domain not in _domain_registry:
                self._send_error(HTTPStatus.BAD_REQUEST, "valid domain required")
                return
            options = _resolve_db_options(domain)
            items = [
                {
                    "key":          k,
                    "display_name": v["display_name"],
                    "description":  v["description"],
                    "default":      v["default"],
                }
                for k, v in options.items()
            ]
            self._send_json(HTTPStatus.OK, {"domain": domain, "db_options": items})
            return

        self._send_error(HTTPStatus.NOT_FOUND, "not found")

    def do_POST(self):
        parsed = urlparse(self.path)

        if not self._auth():
            self._send_error(HTTPStatus.UNAUTHORIZED, "unauthorized")
            return

        payload = self._read_body()
        if payload is None:
            self._send_error(HTTPStatus.BAD_REQUEST, "invalid body")
            return

        # ── /mech/route — 1차 라우팅만 ───────────────────────────
        if parsed.path == "/mech/route":
            question     = str(payload.get("question", "")).strip()
            history_text = str(payload.get("history", ""))
            if not question:
                self._send_error(HTTPStatus.BAD_REQUEST, "question required")
                return
            try:
                from router.llm_router import route
                llm    = _get_router_llm()
                result = route(question, history_text, llm)
                self._send_json(HTTPStatus.OK, result)
            except Exception as e:
                logger.error(f"라우팅 오류: {e}")
                self._send_error(HTTPStatus.INTERNAL_SERVER_ERROR, str(e))
            return

        # ── /mech/ask — 전체 RAG 파이프라인 ──────────────────────
        if parsed.path == "/mech/ask":
            question     = str(payload.get("question", "")).strip()
            domain_key   = str(payload.get("domain", "")).strip()
            rewritten_q  = str(payload.get("rewritten_query", question)).strip() or question
            case         = int(payload.get("case", 1))
            history      = payload.get("history", [])        # [{role, content}, ...]
            synonym_hint = str(payload.get("synonym_hint", ""))

            if not question:
                self._send_error(HTTPStatus.BAD_REQUEST, "question required")
                return

            # 도메인 미지정 시 기본값
            if not domain_key or domain_key not in _domain_registry:
                domain_key = next(
                    (k for k in _domain_registry if not k.startswith("__")),
                    None
                )
            if not domain_key:
                self._send_error(HTTPStatus.BAD_REQUEST, "domain not found")
                return

            # db_keys 결정: 요청값 ∩ 허용목록, 없으면 전체
            options     = _resolve_db_options(domain_key)
            allowed     = list(options.keys())
            req_db_keys = payload.get("db_keys")
            if isinstance(req_db_keys, list) and req_db_keys:
                db_keys = [k for k in req_db_keys if k in allowed]
                if not db_keys:
                    db_keys = allowed   # 유효한 선택이 없으면 전체로 fallback
            else:
                db_keys = allowed

            try:
                bot    = _get_or_load_bot(domain_key, db_keys)
                answer = bot(
                    rewritten_q,
                    chat_history = history,
                    case         = case,
                    synonym_hint = synonym_hint,
                )
                self._send_json(HTTPStatus.OK, {
                    "question":        question,
                    "rewritten_query": rewritten_q,
                    "domain":          domain_key,
                    "db_keys":         db_keys,
                    "case":            case,
                    "answer":          answer,
                })
            except Exception as e:
                logger.error(f"RAG 오류 [{domain_key}]: {e}")
                self._send_error(HTTPStatus.INTERNAL_SERVER_ERROR, str(e))
            return

        self._send_error(HTTPStatus.NOT_FOUND, "not found")


def main():
    if TOKEN == "dev-only-token-change-me":
        logger.warning("⚠️  기본 토큰 사용 중. 배포 전 settings.json에서 변경하세요.")

    server = ThreadingHTTPServer((HOST, PORT), Handler)
    logger.info(f"DB MCP 서버 시작: http://{HOST}:{PORT}")
    logger.info(f"도메인: {[k for k in _domain_registry if not k.startswith('__')]}")
    server.serve_forever()


if __name__ == "__main__":
    main()

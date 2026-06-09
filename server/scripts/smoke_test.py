#!/usr/bin/env python3
# scripts/smoke_test.py
# DB MCP 서버 단독 점검 스크립트 (표준 라이브러리만 사용)
#
# 단계:
#   1) GET  /health         — 서버가 떠 있는가 (인증 불필요)
#   2) GET  /mech/domains   — 도메인이 등록됐는가 (인증)
#   3) POST /mech/ask       — RAG 파이프라인이 도는가 (인증, data/models 필요)
#
# 사용:
#   python scripts/smoke_test.py
#   python scripts/smoke_test.py --url http://127.0.0.1:8766 --domain MECH_STANDARD --question "사이드키 돌출량 알려줘"
#
# 토큰: --token 인자 > 환경변수 DB_MCP_TOKEN > ../config/settings.json 의 db_mcp_token 순으로 사용.

import argparse
import json
import os
import sys
import urllib.request
import urllib.error
from pathlib import Path

_SCRIPT_DIR = Path(__file__).resolve().parent
_SETTINGS   = _SCRIPT_DIR.parent / "config" / "settings.json"


def _load_token(cli_token: str | None) -> str:
    if cli_token:
        return cli_token
    env = os.getenv("DB_MCP_TOKEN")
    if env:
        return env
    if _SETTINGS.exists():
        try:
            with open(_SETTINGS, encoding="utf-8") as f:
                return json.load(f).get("db_mcp_token", "")
        except Exception:
            pass
    return ""


def _request(method: str, url: str, token: str | None, payload: dict | None, timeout: int):
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        body = resp.read().decode("utf-8")
        return resp.status, json.loads(body) if body else {}


def _step(name: str):
    print(f"\n{'─'*60}\n▶ {name}\n{'─'*60}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url",      default="http://127.0.0.1:8766")
    ap.add_argument("--token",    default=None)
    ap.add_argument("--domain",   default="MECH_STANDARD")
    ap.add_argument("--question", default="사이드키 돌출량 표준 알려줘")
    ap.add_argument("--timeout",  type=int, default=120)
    ap.add_argument("--skip-ask", action="store_true", help="data/models 미준비 시 ask 단계 생략")
    args = ap.parse_args()

    base  = args.url.rstrip("/")
    token = _load_token(args.token)
    if not token:
        print("⚠️  토큰을 찾지 못했습니다. --token 또는 DB_MCP_TOKEN 또는 settings.json 의 db_mcp_token 필요.")

    # 1) health
    _step("1/3  GET /health")
    try:
        status, body = _request("GET", f"{base}/health", None, None, 10)
        print(f"  status={status}")
        print(f"  {json.dumps(body, ensure_ascii=False)}")
        if status != 200:
            print("❌ 서버 응답 비정상. 서버 실행 여부 확인.")
            sys.exit(1)
        print(f"  등록 도메인: {body.get('domains', [])}")
    except urllib.error.URLError as e:
        print(f"❌ 서버에 연결 실패: {e}")
        print("   → start_db_server.ps1 로 서버를 먼저 띄우세요.")
        sys.exit(1)

    # 2) domains
    _step("2/3  GET /mech/domains")
    try:
        status, body = _request("GET", f"{base}/mech/domains", token, None, 10)
        print(f"  status={status}")
        if status == 401:
            print("❌ 인증 실패(401). 토큰이 settings.json 과 일치하는지 확인.")
            sys.exit(1)
        for d in body.get("domains", []):
            print(f"   - {d.get('key')} : {d.get('display_name')} — {d.get('description','')}")
    except Exception as e:
        print(f"❌ domains 실패: {e}")
        sys.exit(1)

    # 3) ask
    if args.skip_ask:
        print("\n(--skip-ask 지정 → ask 단계 생략)")
        print("\n✅ health/domains 정상. ask 는 data/models 준비 후 다시 테스트하세요.")
        return

    _step(f"3/3  POST /mech/ask  (domain={args.domain})")
    print(f"  질문: {args.question}")
    payload = {
        "question": args.question,
        "domain":   args.domain,
        "case":     1,
        "history":  [],
    }
    try:
        status, body = _request("POST", f"{base}/mech/ask", token, payload, args.timeout)
        print(f"  status={status}")
        if status != 200:
            print(f"❌ ask 실패: {json.dumps(body, ensure_ascii=False)}")
            print("   흔한 원인: ChromaDB(data/) 또는 reranker(models/) 미배치, Gauss 키 미설정, Ollama 미실행.")
            sys.exit(1)
        ans = body.get("answer", "")
        print(f"\n  [답변]\n{ans}\n")
        print("✅ 전체 파이프라인 정상 동작.")
    except Exception as e:
        print(f"❌ ask 요청 중 예외: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()

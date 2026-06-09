#!/usr/bin/env python3
# scripts/check_retrieval.py
# LLM 을 거치지 않고 "벡터 검색(retrieval)" 단계만 점검한다.
# 도메인을 전부 순회하며 DB 경로 / 문서 수(count) / 검색 결과를 그대로 출력.
#
# 사용:
#   python scripts/check_retrieval.py
#   python scripts/check_retrieval.py --query "사이드키 돌출량 기준"
#   python scripts/check_retrieval.py --domain MECH_STANDARD --query "C-Clip 눌림량" --k 5

import sys
import json
import argparse
from pathlib import Path

ROOT  = Path(__file__).resolve().parent.parent   # server/
DBMCP = ROOT / "db-mcp"
sys.path.insert(0, str(DBMCP))

import vector_store  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--query",  default="설계 표준 기준")
    ap.add_argument("--k",      type=int, default=3)
    ap.add_argument("--domain", default=None, help="특정 도메인만 (기본: 전체 순회)")
    args = ap.parse_args()

    registry = json.loads((DBMCP / "domain_registry.json").read_text(encoding="utf-8"))
    domains  = {k: v for k, v in registry.items() if not k.startswith("__")}
    if args.domain:
        if args.domain not in domains:
            print(f"❌ domain_registry 에 '{args.domain}' 없음. 가능: {list(domains)}")
            sys.exit(1)
        domains = {args.domain: domains[args.domain]}

    print(f"임베딩 모델 : {vector_store.EMBEDDING_MODEL}")
    print(f"DATA_ROOT   : {vector_store.DATA_ROOT}")
    print(f"쿼리        : {args.query!r}")
    print(f"DATA_ROOT 존재: {vector_store.DATA_ROOT.exists()}")

    total_ok = 0
    for domain_key, cfg in domains.items():
        db_keys = cfg.get("db_keys", [domain_key])
        print("\n" + "=" * 72)
        print(f"[도메인] {domain_key}   (db_keys={db_keys})")
        for db_key in db_keys:
            persist = vector_store._get_persist_dir(domain_key, db_key)
            print(f"\n  · db_key = {db_key}")
            print(f"    경로  : {persist}")
            print(f"    존재  : {persist.exists()}")
            if not persist.exists():
                print("    ❌ ChromaDB 폴더 없음")
                print("       → data 미배치 / 폴더명 불일치(MEG_STANDARD→MECH_STANDARD?) /")
                print("         임베딩 모델 폴더명 불일치 중 하나입니다.")
                continue

            db = vector_store.load_vector_db(domain_key, db_key)
            if db is None:
                print("    ❌ 로드 실패")
                continue

            # ① 문서 수 — '데이터가 연결됐는지' 의 핵심 지표
            try:
                count = db._collection.count()
                print(f"    문서 수: {count}")
                if count == 0:
                    print("    ⚠️  DB 는 열렸지만 문서가 0개. (구축이 안 됐거나 빈 DB)")
            except Exception as e:
                print(f"    문서 수 확인 실패: {e}")

            # ② 실제 검색 — ollama 임베딩 서버 필요
            try:
                hits = db.similarity_search_with_score(args.query, k=args.k)
                print(f"    검색 결과: {len(hits)}건  (score 작을수록 유사)")
                for i, (doc, score) in enumerate(hits, 1):
                    snippet = " ".join(doc.page_content.split())[:90]
                    print(f"      {i}. score={score:.4f} | {snippet}")
                if hits:
                    total_ok += 1
            except Exception as e:
                print(f"    ❌ 검색 중 오류(ollama 미실행 / 모델 불일치 의심): {e}")

    print("\n" + "=" * 72)
    print(f"검색 결과가 1건 이상 나온 db_key: {total_ok}개")
    print("문서 수가 >0 이면 데이터 연결은 정상입니다. 검색 0건이면 임베딩 모델/쿼리를 의심하세요.")


if __name__ == "__main__":
    main()

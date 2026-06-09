# vector_store.py
# ChromaDB 구축 및 로드 모듈 (MEG_ChatBot_claude에서 이식)

import os
import glob
import json
import shutil
import traceback
from datetime import datetime
from pathlib import Path

import pandas as pd
from langchain_core.documents import Document
from langchain_chroma import Chroma
from langchain_ollama import OllamaEmbeddings

# ── 임베딩 모델 설정 ──────────────────────────────────────────────
EMBEDDING_MODEL = "qwen3-embedding:4b"

EMBED_BATCH_SIZE = 100

ROOT       = Path(__file__).parent
DATA_ROOT  = ROOT.parent / "data"   # server/data/


def _get_persist_dir(domain_key: str, db_key: str, model: str = None) -> Path:
    m       = model or EMBEDDING_MODEL
    db_name = m.replace(":", "_").replace("-", "_")
    return DATA_ROOT / domain_key / "chroma_db" / db_key / db_name


def prepare_knowledge_base(domain_key: str, db_key: str):
    """ChromaDB 신규 구축"""
    file_pattern = str(DATA_ROOT / domain_key / "parsed_result" / db_key / "parsed_result_*.xlsx")
    all_files    = glob.glob(file_pattern)

    if not all_files:
        raise FileNotFoundError(
            f"변환 결과 파일 없음: {file_pattern}\n"
            f"먼저 전처리 스크립트를 실행하세요."
        )

    persist_dir = _get_persist_dir(domain_key, db_key)
    if persist_dir.exists():
        shutil.rmtree(persist_dir)
    persist_dir.mkdir(parents=True, exist_ok=True)

    embeddings = OllamaEmbeddings(model=EMBEDDING_MODEL)
    print(f"ChromaDB 구축 중 [{domain_key}/{db_key}] (임베딩 모델: {EMBEDDING_MODEL})")

    combined_df = pd.concat(
        [pd.read_excel(f, engine="openpyxl") for f in all_files],
        ignore_index=True,
    )

    documents = []
    for _, row in combined_df.iterrows():
        content = None
        meta    = {}

        if "search_text" in combined_df.columns and pd.notna(row.get("search_text")):
            content = str(row["search_text"]).strip()
        elif "Text" in combined_df.columns and pd.notna(row.get("Text")):
            content = str(row["Text"]).strip()

        if not content:
            continue

        # 나머지 컬럼 전부 metadata로
        for col in combined_df.columns:
            if col not in ("search_text", "Text") and pd.notna(row.get(col)):
                meta[col] = str(row[col])

        documents.append(Document(page_content=content, metadata=meta))

    print(f"총 {len(documents)}개 문서 벡터화 중...")

    # 배치 처리
    for i in range(0, len(documents), EMBED_BATCH_SIZE):
        batch = documents[i:i + EMBED_BATCH_SIZE]
        if i == 0:
            db = Chroma.from_documents(
                documents   = batch,
                embedding   = embeddings,
                persist_directory = str(persist_dir),
            )
        else:
            db.add_documents(batch)
        print(f"  {min(i + EMBED_BATCH_SIZE, len(documents))}/{len(documents)} 완료")

    print(f"✅ ChromaDB 구축 완료: {persist_dir}")
    return db


def load_vector_db(domain_key: str, db_key: str) -> Chroma | None:
    persist_dir = _get_persist_dir(domain_key, db_key)
    if not persist_dir.exists():
        print(f"⚠️  ChromaDB 없음: {persist_dir}")
        return None
    embeddings = OllamaEmbeddings(model=EMBEDDING_MODEL)
    return Chroma(
        persist_directory  = str(persist_dir),
        embedding_function = embeddings,
    )


def load_multiple_vector_dbs(domain_key: str, db_keys: list) -> dict:
    """복수 DB 로드 → {db_key: Chroma} dict"""
    result = {}
    for db_key in db_keys:
        try:
            db = load_vector_db(domain_key, db_key)
            if db:
                result[db_key] = db
                print(f"✅ DB 로드: {domain_key}/{db_key}")
        except Exception as e:
            print(f"⚠️  DB 로드 실패 [{domain_key}/{db_key}]: {e}")
    return result

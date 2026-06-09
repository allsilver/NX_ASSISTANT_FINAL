# DB MCP 서버 (server/) — 서버 PC 전용

NX Assistant의 RAG 검색을 담당하는 HTTP 서버입니다. **서버 PC 1대에만** 배포해서 상시 실행합니다.
이 폴더(`server/`)만 통째로 서버 PC로 옮기면 됩니다. (`client/`는 필요 없음)

---

## 1. 폴더 구조

```
server/
├── db-mcp/
│   ├── server.py            ← HTTP 서버 진입점 (/health, /mech/domains, /mech/route, /mech/ask)
│   ├── rag_engine.py        ← RAG 파이프라인 (reranker → 섹션확장 → Gauss 답변)
│   ├── vector_store.py      ← ChromaDB 로드 (data/ 에서 읽음)
│   ├── domain_registry.json ← 도메인 목록 (MECH_STANDARD / MECHA_DFM / CMF_DFC / CMF_ISSUE)
│   ├── llm/                 ← get_llm() + GaussLLM
│   ├── retrievers/          ← vector_retriever
│   ├── router/              ← 1차 라우터(현재 미사용) / 2차 db_intent
│   └── prompts/             ← 도메인별 답변 프롬프트 (MECH_STANDARD*.txt 등)
├── config/
│   ├── settings.example.json  ← 템플릿 (복사해서 settings.json 작성)
│   └── settings.json          ← 실제 키 (직접 생성, gitignore)
├── scripts/
│   ├── start_db_server.ps1    ← 서버 실행
│   └── smoke_test.py          ← 단독 점검 (health→domains→ask)
├── data/      ← 직접 배치 (아래 2-③ 참고, gitignore)
├── models/    ← 직접 배치 (아래 2-④ 참고, gitignore)
└── requirements.txt
```

> 경로 규칙: `data/`, `models/` 는 **`server/` 바로 아래**에 둡니다. 코드가 이 위치를 기준으로 찾습니다.

---

## 2. 최초 1회 준비 (로컬 PC)

### ① Python 의존성 설치
```powershell
cd server
pip install -r requirements.txt
```

### ② Ollama 임베딩 모델 준비
```powershell
ollama pull qwen3-embedding:4b
ollama serve   # 별도 창에서 상시 실행
```
> 임베딩 모델명은 `db-mcp/vector_store.py` 의 `EMBEDDING_MODEL` 과 일치해야 합니다 (기본 `qwen3-embedding:4b`).

### ③ data/ 배치 — **중요: 폴더명을 MECH_STANDARD로 변경**
기존 MEG_ChatBot_claude 의 `data/` 를 가져오되, NX 는 이제 도메인 키가 `MEG_STANDARD` → `MECH_STANDARD` 로 바뀌었습니다.
**폴더 이름을 맞춰야 매칭됩니다.**

```
server/data/
└── MECH_STANDARD/          ← (구) MEG_STANDARD 폴더를 이 이름으로 변경
    ├── chroma_db/
    │   └── MECH_STANDARD/   ← db_key 폴더도 MECH_STANDARD 로
    │       └── qwen3_embedding_4b/   ← 임베딩 모델 폴더 (콜론·하이픈은 _ 로)
    └── parsed_result/
        └── MECH_STANDARD/
            └── parsed_result_*.xlsx
```

도메인을 추가하려면(`MECHA_DFM`, `CMF_DFC`, `CMF_ISSUE`) 같은 구조로 폴더를 더 만들면 됩니다. 우선 `MECH_STANDARD` 하나만 있어도 그 도메인은 테스트 가능합니다.

### ④ models/ 배치
```
server/models/
└── bge-reranker-v2-m3/     ← MEG_ChatBot_claude 의 models/ 에서 그대로 복사
```

### ⑤ settings.json 작성
```powershell
copy config\settings.example.json config\settings.json
```
그런 다음 `config/settings.json` 을 열어:
- `db_mcp_token` — 임의의 긴 토큰. **클라이언트의 `DB_MCP_TOKEN` 환경변수와 동일해야 함.**
- `experiment.gauss` — MEG_ChatBot_claude 의 `src/experiment_config.json` 의 `gauss` 섹션(access_key/secret_key/models)을 그대로 복사.

---

## 3. 서버 실행
```powershell
cd server
.\scripts\start_db_server.ps1                      # 0.0.0.0:8766 (사내망 공개)
# 또는 로컬만:
.\scripts\start_db_server.ps1 -BindHost 127.0.0.1
# 또는 직접:
python db-mcp\server.py
```
`DB MCP 서버 시작: http://...:8766` 과 등록 도메인 목록이 뜨면 정상.

---

## 4. 단독 점검 (스모크 테스트)
서버를 띄운 상태에서 **다른 터미널**에서:
```powershell
cd server
python scripts\smoke_test.py
```
- `1/3 health` → 서버 떠 있는지
- `2/3 domains` → 도메인 등록 + 토큰 인증
- `3/3 ask` → 실제 RAG 답변 (data/models/Ollama/Gauss 모두 필요)

data/models 가 아직 없으면 1·2단계만 확인:
```powershell
python scripts\smoke_test.py --skip-ask
```

특정 질문/도메인 테스트:
```powershell
python scripts\smoke_test.py --domain MECH_STANDARD --question "C-Clip 눌림량 기준"
```

---

## 5. 엔드포인트 요약

| 메서드 | 경로 | 인증 | 용도 |
|---|---|---|---|
| GET  | `/health`        | ✕ | 상태 + 도메인 목록 |
| GET  | `/mech/domains`  | ○ | 도메인 상세 |
| POST | `/mech/ask`      | ○ | RAG 검색+답변 (1차 배포에서 사용) |
| POST | `/mech/route`    | ○ | intent 분류 (1차 배포 미사용, 2차용 보존) |

> 1차 배포는 1차 라우터를 쓰지 않습니다. 클라이언트가 도메인을 직접 지정해 `/mech/ask` 를 호출합니다.

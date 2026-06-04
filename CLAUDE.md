# NX Assistant — Claude 작업 메모 (최종)

> 마지막 업데이트: 2026-06-04
> 이 파일은 Claude가 프로젝트를 파악하고 작업하면서 누적 저장하는 파일입니다.

---

## 1. 프로젝트 개요

Siemens NX (기계설계 CAD) 안에서 동작하는 AI 어시스턴트.
- NX HEROS 리본의 "AI Assistant" 버튼 → WinForms+WebView2 앱 실행
- 각 사용자 PC에서 독립 실행 (중앙 서버 아님)
- LLM: Gauss API (REST) 또는 GPT (WebView2 Worker, 개인 계정)
- DB MCP 서버(중앙 PC 1대)를 통한 RAG 기반 설계 표준 검색

---

## 2. 배포 구조

### 서버 PC 1대
- `server/db-mcp/server.py` 상시 실행 (포트 8766)
- ChromaDB 데이터 보유
- RAG 파이프라인 처리
- Gauss API 호출

### 사용자 PC (20명 → 100명+)
- `NxAssistant.exe` (자체포함 빌드, ~162MB)
- `NxAssistantLauncher.dll` (NX 연동)
- `install.bat` 한 번 실행으로 설치 완료
- 코드 불필요, Python 불필요

---

## 3. 폴더 구조

```
NX_Assistant/
├── server/                         ← 서버 PC 전용
│   ├── db-mcp/
│   │   ├── server.py               ← HTTP 서버 (GET /health, /meg/domains, POST /meg/route, /meg/ask)
│   │   ├── rag_engine.py           ← RAG 파이프라인
│   │   ├── vector_store.py         ← ChromaDB 구축/로드
│   │   ├── domain_registry.json    ← 도메인 목록
│   │   ├── llm/
│   │   │   ├── __init__.py         ← get_llm() 팩토리
│   │   │   └── gauss_llm.py        ← GaussLLM (LangChain BaseLLM)
│   │   ├── router/
│   │   │   ├── llm_router.py       ← 1차 라우터 (intent 분류만, 최근 1턴)
│   │   │   ├── db_intent_llm.py    ← 2차 DB LLM (도메인+쿼리재작성, 최근 2턴)
│   │   │   └── prompts/
│   │   │       ├── router.txt
│   │   │       └── db_intent.txt
│   │   ├── retrievers/
│   │   │   └── vector_retriever.py
│   │   └── prompts/                ← 도메인별 답변 프롬프트
│   │       ├── MEG_STANDARD.txt
│   │       ├── MEG_STANDARD_case1.txt
│   │       ├── MEG_STANDARD_case2.txt
│   │       ├── MECHA_DFM.txt
│   │       ├── CMF_DFC.txt
│   │       └── CMF_ISSUE.txt
│   ├── scripts/
│   │   └── start_db_server.ps1
│   ├── config/
│   │   └── settings.json           ← Gauss API 키 (gitignore!)
│   └── requirements.txt
│
├── client/                         ← 사용자 PC 전용
│   ├── app/
│   │   ├── ui/
│   │   │   ├── AssistantForm.cs    ← 메인 채팅 UI
│   │   │   └── WorkerForm.cs       ← GPT WebView2 워커
│   │   ├── providers/
│   │   │   ├── ILlmProvider.cs     ← 인터페이스
│   │   │   ├── GaussProvider.cs    ← Gauss REST
│   │   │   └── GptProvider.cs      ← WorkerForm 래핑
│   │   ├── history/
│   │   │   └── HistoryManager.cs   ← 대화 히스토리 (N턴 자르기)
│   │   ├── mcp/
│   │   │   ├── DbMcpClient.cs      ← DB 서버 HTTP 요청
│   │   │   └── NxMcpClient.cs      ← NX MCP 호출
│   │   ├── router/
│   │   │   └── RouterClient.cs     ← 전체 흐름 조율
│   │   ├── Program.cs
│   │   └── NxAssistant.csproj
│   ├── nx-launcher/
│   │   └── src/NxAssistantLauncher.cs
│   ├── nx-customization/
│   │   └── startup/nx_assistant_button.men
│   ├── nx-mcp/                     ← 각자 PC NX 제어
│   │   ├── nx_mcp_server.py
│   │   └── remoting_client_via_mcp.py
│   └── install.bat
│
├── deploy/
├── docs/
├── runtime/
├── logs/
└── .gitignore
```

---

## 4. 전체 대화 흐름

```
[사용자 질문]
      ↓
[AssistantForm] — 히스토리 관리
      ↓
[RouterClient.HandleAsync()]
      ↓
[DbMcpClient.RouteAsync()]  ← POST /meg/route (1차 라우터, 히스토리 1턴)
      ↓
intent 분기:
  db_search  → DbMcpClient.AskAsync()  ← POST /meg/ask (RAG 전체)
  nx_control → NxMcpClient + Provider.ChatAsync()
  chat       → Provider.ChatAsync()
      ↓
[AssistantForm] — 답변 표시 + 히스토리 추가
```

---

## 5. 서버 내부 RAG 흐름 (/meg/ask)

```
질문 + 히스토리
      ↓
[2차 DB LLM] (db_intent_llm.py) — 도메인 파악 + 쿼리 재작성 (히스토리 2턴)
      ↓
[VectorRetriever] — ChromaDB k=20 검색
      ↓
[Cross-encoder Reranker] — bge-reranker-v2-m3 (top-7)
      ↓
[섹션 확장] — full_path 기준 상위 3섹션 전체 문서
      ↓
[_build_context()] — 섹션 헤더 + 수치 원문 병기
      ↓
[도메인별 프롬프트 선택] — domain_registry.json 기준
      ↓
[Gauss LLM] — 최종 답변 (히스토리 3턴)
```

---

## 6. 히스토리 관리 전략

| 단계 | 전달 턴 수 | 이유 |
|---|---|---|
| 1차 라우터 | 1턴 | intent 분류만, 최소 필요 |
| 2차 DB LLM | 2턴 | 후속 질문 맥락 파악 |
| 답변 LLM | 3턴 | 풍부한 맥락 필요 |

새 채팅 타이밍:
- 사용자가 "초기화" 버튼 클릭
- NX Assistant 창 닫고 재시작
→ 앱 히스토리 + GPT Worker 새 채팅 동기화

---

## 7. 웹 배포 구조 (향후)

```
NX 배포: GaussProvider + GptProvider 선택 가능
웹 배포: Gauss만 (GPT WebView2 방식 웹 불가)
공유: server/db-mcp/server.py (동일한 RAG 서버)
```

---

## 8. 파일별 수정 가이드

| 수정 내용 | 파일 |
|---|---|
| 채팅 UI 레이아웃/버튼/색상 | `client/app/ui/AssistantForm.cs` |
| GPT Worker 동작 (로그인/주차/응답) | `client/app/ui/WorkerForm.cs` |
| Gauss API 호출 방식 | `client/app/providers/GaussProvider.cs` |
| 전체 대화 흐름 조율 | `client/app/router/RouterClient.cs` |
| DB MCP 통신 | `client/app/mcp/DbMcpClient.cs` |
| 히스토리 관리 | `client/app/history/HistoryManager.cs` |
| 1차 라우터 LLM 로직 | `server/db-mcp/router/llm_router.py` |
| 2차 DB intent LLM | `server/db-mcp/router/db_intent_llm.py` |
| RAG 파이프라인 | `server/db-mcp/rag_engine.py` |
| DB MCP HTTP 서버 | `server/db-mcp/server.py` |
| 도메인/모델 설정 | `server/db-mcp/domain_registry.json` |
| Gauss API 키 | `server/config/settings.json` (gitignore) |
| 라우터 프롬프트 | `server/db-mcp/router/prompts/router.txt` |
| DB intent 프롬프트 | `server/db-mcp/router/prompts/db_intent.txt` |
| 도메인별 답변 프롬프트 | `server/db-mcp/prompts/*.txt` |
| NX HEROS 버튼 | `client/nx-customization/startup/nx_assistant_button.men` |
| NX DLL 런처 | `client/nx-launcher/src/NxAssistantLauncher.cs` |

---

## 9. 2차 개선 예정 사항

- [ ] Rolling Summary 히스토리 (10턴 초과 시 자동 요약)
- [ ] Hybrid Retriever (vector + BM25, RRF alpha=0.7)
- [ ] 브라우저 MCP 구현 (2차 브라우저 intent LLM)
- [ ] Gauss 스트리밍 (SSE → WinForms 실시간 출력)
- [ ] DB 서버 Windows 서비스 등록 (NSSM)
- [ ] 사용자 인증 강화
- [ ] 자동 업데이트

---

## 10. 환경변수

| 변수 | 용도 | 기본값 |
|---|---|---|
| `NX_ASSISTANT_HOME` | 프로젝트 루트 | DLL 위치 기준 |
| `NX_ASSISTANT_DB_MCP_URL` | DB 서버 주소 | `http://127.0.0.1:8766` |
| `DB_MCP_TOKEN` | DB 서버 인증 토큰 | settings.json에서 로드 |
| `DB_MCP_HOST` | 서버 바인딩 IP | `0.0.0.0` |
| `DB_MCP_PORT` | 서버 포트 | `8766` |
| `NX_ASSISTANT_EXE` | 앱 exe 경로 강제 지정 | RelativeExePath |

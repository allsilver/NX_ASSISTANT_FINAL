# NX Assistant — Claude 작업 메모 (프로젝트 설명서)

> 이 파일은 프로젝트의 구조·환경·규칙을 설명하는 정적 문서입니다.
> 진행 상황과 할 일은 PROGRESS.md를 참조하세요.

---

## 1. 프로젝트 개요

Siemens NX(기계설계 CAD) 안에서 동작하는 AI 어시스턴트.
- NX HEROS 리본의 "AI Assistant" 버튼 → WinForms+WebView2 앱 실행
- 사용자(삼성 기구설계팀)가 설계 표준을 자연어로 질문 → RAG 기반 답변
- LLM 2종: GPT(WebView2, 개인계정) / Gauss(사내 API, 서버 경유)
- 레포: allsilver/NX_ASSISTANT_FINAL (public)

---

## 2. 배포 구조 (배포 대상이 다름 - 폴더 분리 필수)

### (1) 클라이언트 — 사용자 각 PC에 배포
- `client/app/` → NxAssistant.exe
- WinForms + WebView2 앱

### (2) 서버 — 서버 PC 1대에만 배포·실행
- `server/db-mcp/server.py` 상시 실행 (포트 8766)
- 설계 데이터(ChromaDB) 보유, RAG 처리, Gauss API 호출
- pip + Gauss API + 임베딩/reranker 모델 필요 → 로컬/서버 PC에서만 가능

→ ZIP 배포 시 (1)과 (2)를 명확히 나눠서 각각 독립 테스트 가능하게 한다.

---

## 3. LLM별 답변 작성 위치 (목표 구조)

```
질문 → 서버에서 검색
  ├─ [Gauss 선택] 서버에서 Gauss API로 답변 작성 → 클라이언트에 answer 반환
  └─ [GPT 선택]   서버는 검색결과만 반환 → 클라이언트의 GPT가 답변 작성
```

- 검색은 항상 **서버** (데이터가 서버에만 있음)
- 답변 작성: Gauss는 서버에서(Gauss API가 서버PC에서만 호출 가능), GPT는 클라이언트에서

---

## 4. 화면 흐름 (1차 배포 UI)

```
AI 선택 (Gauss/GPT)         ← 앱 시작 시, 최초 1회
   ↓
분야 선택 (DB조회/NX제어/자동화)
   ↓ (DB조회 선택 시)
DB 도메인 선택 (설계수순서/DFC/CMF/DFM)
   ↓
채팅
```

- LLM 선택은 앱 전역 1개 설정. 도메인 바꿔도 유지. GPT 워커 1개 공유(로그인 1회).
- 1차 배포: 라우터 없음. 사용자가 모드/도메인 직접 선택.

---

## 5. 폴더 구조 (주요 파일)

```
NX_ASSISTANT_FINAL/
├── client/app/                     ← 사용자 PC 전용
│   ├── ui/
│   │   ├── UiKit.cs                 ← 공통 UI 컴포넌트 (Palette, 카드, 아이콘, 버튼 등)
│   │   ├── MainForm.cs              ← 화면 전환 + 4개 View
│   │   └── WorkerForm.cs            ← GPT WebView2 워커 (로그인/채팅)
│   ├── providers/
│   │   ├── ILlmProvider.cs          ← 인터페이스
│   │   ├── GaussProvider.cs         ← Gauss (서버 경유)
│   │   ├── GptProvider.cs           ← GPT (WorkerForm 래핑, userWorker만)
│   │   └── LlmSession.cs            ← 앱 전역 LLM 선택 관리
│   ├── mcp/
│   │   ├── DbMcpClient.cs           ← DB 서버 HTTP 요청 (/meg/ask 등)
│   │   └── NxMcpClient.cs           ← NX MCP 호출
│   ├── history/HistoryManager.cs    ← 대화 히스토리
│   ├── router/RouterClient.cs       ← (1차 배포 미사용, 2차 라우터용 보존)
│   ├── Program.cs                   ← MainForm 실행
│   └── NxAssistant.csproj
│
├── server/db-mcp/                  ← 서버 PC 전용 (RAG)
│   ├── server.py                    ← HTTP 서버 (/health, /meg/domains, /meg/route, /meg/ask)
│   ├── rag_engine.py                ← RAG 파이프라인
│   ├── vector_store.py              ← ChromaDB 구축/로드
│   ├── domain_registry.json         ← 도메인 목록
│   ├── llm/gauss_llm.py             ← GaussLLM (LangChain)
│   ├── router/                      ← 라우터 LLM (2차용)
│   ├── retrievers/vector_retriever.py
│   └── prompts/                     ← 도메인별 답변 프롬프트
│
├── CLAUDE.md                        ← (이 파일) 프로젝트 설명서
└── PROGRESS.md                      ← 진행상황 + 할 일
```

※ data/, models/ 폴더는 무거워서 깃헙 미포함. 로컬/서버 PC에만 존재.
※ src/ 폴더(구버전 키워드매칭)는 사용 안 함.

---

## 6. 도메인 키

| 키 | 표시명 | 설명 |
|---|---|---|
| MECH_STANDARD | 설계수순서 | 기구 설계 표준 체크리스트 |
| CMF_DFC | DFC | Design For Cost (재료비 절감안) |
| CMF_ISSUE | CMF | CMF 문제/이력 |
| MECHA_DFM | DFM | 공정 설계 표준 (제조 고려) |

※ 현재 서버 코드엔 일부가 옛 이름(MEG_STANDARD)으로 되어 있어 MECH로 변경 예정.
  (클라이언트 MainForm은 이미 MECH_STANDARD 사용)

---

## 7. 환경 제약

| 항목 | VDI | 로컬 PC |
|---|---|---|
| GPT (chatgpt.com) | ✅ | ✅ |
| Gauss API (sr-cloud.com) | ❌ | ✅ |
| pip / PyPI | ❌ | ✅ |
| NuGet | ❌ | ✅ |
| github push/pull | 불안정 | - |
| dotnet build | ✅ (메모리 옵션 필수) | ✅ |
| RAM | 8GB (가용 2.4GB) | - |

→ DB MCP 서버는 로컬/서버 PC에서만 실행. VDI는 C# 작업 + GPT 테스트.

---

## 8. 빌드 / 환경변수

### 빌드 (VDI 필수 옵션 - 메모리 제한)
```
cd client/app
dotnet build -p:UseSharedCompilation=false -m:1 --disable-build-servers
.\bin\Debug\net8.0-windows\NxAssistant.exe
```
OutOfMemory 시: `dotnet build-server shutdown` → `Get-Process dotnet|Stop-Process -Force` → 재시도.

### 환경변수 (VDI, User 영구)
| 변수 | 용도 |
|---|---|
| NX_ASSISTANT_MODE=vdi | 우회 모드 (라우터 건너뜀) |
| WEBVIEW2_CORE_DLL | WebView2 Core dll 경로 (NuGet 우회) |
| WEBVIEW2_WINFORMS_DLL | WebView2 WinForms dll 경로 |
| WEBVIEW2_LOADER_DLL | WebView2 Loader dll 경로 |
| NX_ASSISTANT_DB_MCP_URL | DB 서버 주소 (기본 http://127.0.0.1:8766) |
| DB_MCP_TOKEN | DB 서버 인증 토큰 |

### 로그
- 위치: `%LOCALAPPDATA%\NX_Assistant\logs\nx-assistant.log`
- 읽기(한글): `Get-Content ... -Encoding UTF8`

---

## 9. UI 설계 원칙 (반복 실패 후 확립)

- **텍스트에 고정 Height/Width 금지** (잘림의 근본 원인). AutoSize=true 사용.
- **고정 높이 카드 안에 Percent 여백 금지** (Percent가 콘텐츠를 0으로 짓눌러 잘림).
  → 카드도 AutoSize, 또는 콘텐츠를 Dock=Top 스택으로.
- **동적 Margin 계산 중앙정렬 금지** (타이밍 어긋남). Dock=Fill+TextAlign 또는 Resize 핸들러.
- **커스텀 OnPaint 컨트롤은 바깥 Margin이 높이를 찌부러뜨릴 수 있음** → 부모 높이 충분히.
- 영문 디센더(g,s 등)는 고정높이 라벨에서 꼬리 잘림 → AutoSize 필수.
- Light 테마 색: 배경#eef1f5, 표면#ffffff, 테두리#c8d0dc, 강조(남색)#1a3a6b,
  텍스트#1a1f2e, 흐린텍스트#5a6478, GPT그린#10a37f.

---

## 10. 작업 방식 / 규칙

- 프로젝트에 깃헙 연결됨 → Claude가 project_knowledge_search로 코드 읽기 가능.
  (단, data/models 등 깃헙 미포함 파일, 미커밋 로컬 변경은 못 봄 → Get-Content로 보여줘야 함)
- Claude는 수정본을 만들어 파일로 전달 → 사용자가 VDI/로컬에 반영. (직접 파일 수정 불가)
- 코드 수정 전달: 부분수정은 [파일경로/찾을부분/교체], 경로 항상 명시. 많으면 파일 통째로.
- ZIP은 Claude가 직접 못 만듦 → PowerShell 스크립트 제공 → 사용자 실행.
- GitHub Desktop으로 push (터미널 push는 보안 차단).
- 독립 테스트 파일로 검증 후 메인 통합.

---

## 11. 기술 스택

- 클라이언트: C# (WinForms + WebView2, .NET 8)
- 서버: Python (http.server 기반 DB MCP, 포트 8766)
- LLM: Gauss(사내 REST API), GPT(WebView2 개인계정)
- RAG: ChromaDB, Ollama 임베딩(qwen3-embedding:4b), bge-reranker-v2-m3, LangChain

---

## 12. 배포 단계 / 향후 방향

- **1차 배포(20명)**: 라우터 없음. 모드 직접 선택. LLM Gauss/GPT.
- **2차 배포(1000명)**: 1차 라우터 추가 (RouterClient 로직 보존, 호출만 안 함).
- **DB 내부 라우터**: DB조회 모드 안 도메인 판단 LLM (도메인 확장 대비).
- **MCP 전환 계획**: 현재 HTTP REST → 나중에 MCP로. DB MCP/NX MCP 만들 때
  tool 단위 분리 + 인터페이스 추상화로 교체 쉽게 설계.

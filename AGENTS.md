# NX Assistant — Codex 작업 메모 (프로젝트 설명서)

> 이 파일은 프로젝트의 구조·환경·규칙을 설명하는 정적 문서입니다.
> 진행 상황과 할 일은 PROGRESS.md를 참조하세요.
>
> **버전 v2.6** (2026-06-11): 데모 3종 완성 — DB조회(실동작 RAG) + NX제어/자동화(스크립트·fake) + 브랜딩(로고/아이콘).
> NX제어=`NxControlSession`(기본 스크립트, `NX_CONTROL_REAL=1`로 실제 브리지), 자동화=`AutomationSession`(`NX_AUTOMATION_FAKE=1`로 데모).
> **다음 v3**: 실배포 형태로 정리(환경변수→설정파일 일원화, `ILlmSession`→`IChatSession`/`LlmSession`→`DbQuerySession` 리네이밍, 런처 정식화). 계획은 PROGRESS.md "v3 정리 계획".

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
(앱 시작) DB 서버 연결 확인 ── 실패(배포 환경) → "서버 연결 불가" 화면(진입 차단)
   │                         └ VDI(NX_ASSISTANT_MODE=vdi)는 체크 예외
   ↓ (연결 OK)
AI 선택 (Gauss/GPT)         ← 최초 1회
   ↓
분야 선택 (DB조회/NX제어/자동화)
   ↓ (DB조회 선택 시)
DB 도메인 선택 (설계수순서/DFC/CMF/DFM)
   ↓ (서버 /mech/dbkeys 옵션이 2개 이상인 도메인 = 설계수순서/DFM 만)
관심분야(db_key) 복수선택   ← 도메인당 최초 1회. 선택은 로컬 저장 → 이후 자동 스킵(유지)
   ↓
채팅 ── (⚙ 설정) → 설정 (관심분야 재설정 / 외부 AI 재로그인)
```

- LLM 선택은 앱 전역 1개 설정. 도메인 바꿔도 유지. GPT 워커 1개 공유(로그인 1회).
- 어느 도메인이 복수 DB인지는 **하드코딩하지 않고 서버 /mech/dbkeys 결과로 판단**(옵션 ≥2 → 페이지).
- "관심분야 재설정"은 저장값이 있어도 페이지를 다시 띄움(force).
- 1차 배포: 라우터 없음. 사용자가 모드/도메인 직접 선택.

---

## 5. 폴더 구조 (주요 파일)

```
NX_ASSISTANT_FINAL/
├── client/app/                     ← 사용자 PC 전용
│   ├── ui/
│   │   ├── UiKit.cs                 ← 공통 UI 컴포넌트 (Palette, 카드, 아이콘, 버튼 등)
│   │   ├── MainForm.cs              ← 화면 전환 셸 (시작 시 서버 연결확인, db_key/설정 흐름 배선)
│   │   ├── AiSelectView.cs          ← 화면 0: AI 선택 (순수 UI)
│   │   ├── FieldSelectView.cs       ← 화면 1: 분야 선택 (순수 UI)
│   │   ├── DomainSelectView.cs      ← 화면 2: DB 도메인 선택 (순수 UI, 도메인 목록은 현재 하드코딩)
│   │   ├── DbKeySelectView.cs       ← 화면 2.5: db_key 복수선택 (본앱 배선 완료, 서버 /mech/dbkeys 기반)
│   │   ├── ChatView.cs              ← 화면 3: 채팅 (ILlmSession 의존, ⚙→설정 콜백)
│   │   ├── SettingsView.cs          ← 설정 화면 (관심분야 재설정 / 외부 AI 재로그인)
│   │   └── WorkerForm.cs            ← GPT WebView2 워커 (로그인/채팅)
│   ├── providers/
│   │   ├── ILlmProvider.cs          ← 인터페이스
│   │   ├── ILlmSession.cs           ← 채팅이 의존하는 세션 인터페이스 (ChatView ↔ Mock 분리)
│   │   ├── GaussProvider.cs         ← Gauss (서버 경유)
│   │   ├── GptProvider.cs           ← GPT (WorkerForm 래핑, userWorker만)
│   │   └── LlmSession.cs            ← 앱 전역 LLM 선택 관리 (ILlmSession 구현)
│   ├── mcp/
│   │   ├── DbMcpClient.cs           ← DB 서버 HTTP 요청 (/mech/ask 등). 응답 images[] base64 파싱
│   │   ├── DbKeyOption.cs           ← db_key 메타 레코드 (카드/프리뷰 공유, 네트워크 의존 없음)
│   │   ├── RagImage.cs              ← 검색 표준 이미지 레코드 (name/score_pct/PNG바이트, 프리뷰 공유)
│   │   └── NxMcpClient.cs           ← NX MCP 호출
│   ├── history/HistoryManager.cs    ← 대화 히스토리
│   ├── config/DbKeySelectionStore.cs← 도메인별 선택 db_key 로컬 저장 (%LOCALAPPDATA%\NX_Assistant\db_selection.json)
│   ├── router/RouterClient.cs       ← (1차 배포 미사용, 2차 라우터용 보존)
│   ├── Program.cs                   ← MainForm 실행 (AppIcon.Load = exe 아이콘 추출)
│   ├── ui/assets/                   ← 카드 로고 PNG (gauss_logo/chatgpt_logo, EmbeddedResource)
│   ├── assets/                      ← nx_assistant.ico(작업표시줄/제목줄, <ApplicationIcon>), galaxy_ai_logo.png(NX버튼 소스 참고)
│   └── NxAssistant.csproj
│
├── client/nx-customization/        ← NX 시작 시 로드되는 커스터마이제이션 (코덱스 원본)
│   ├── startup/                    ← .rtb(리본 탭) → .grb(그룹) → .men(버튼) + nx_assistant_galaxy.bmp + README
│   ├── bitmaps/                    ← ai_sparkle.bmp, nx_assistant_galaxy.bmp
│   └── application/nx_assistant.men ← 메뉴앱 fallback 정의
│
├── client/nx-launcher/             ← NX 버튼 → 앱 실행 런처 소스 (우리 전용, 정식 재설치는 시연 후 TODO)
│
├── client/ui-preview/              ← UI 프리뷰 (개발 전용, 서버/WebView2 없음)
│   ├── UiPreview.csproj            ← app/ 의 순수 UI 파일을 링크해 컴파일
│   ├── Program.cs                  ← PreviewShell 실행
│   ├── PreviewShell.cs             ← MainForm 흐름을 mock 으로 흉내 (전 페이지 연결)
│   └── MockLlmSession.cs           ← ILlmSession mock (가짜 답변)
│
├── server/                         ← 서버 PC 전용 (RAG). 이 폴더만 서버 PC로 복사
│   ├── db-mcp/
│   │   ├── server.py                ← HTTP 서버 (/health, /mech/domains, /mech/dbkeys, /mech/route, /mech/ask)
│   │   ├── rag_engine.py            ← RAG 파이프라인 (models = server/models)
│   │   ├── vector_store.py          ← ChromaDB 로드 (data = server/data)
│   │   ├── domain_registry.json     ← 도메인 목록 (MECH_STANDARD 등)
│   │   ├── llm/gauss_llm.py         ← GaussLLM (LangChain)
│   │   ├── router/                  ← 라우터 LLM (2차용)
│   │   ├── retrievers/vector_retriever.py
│   │   └── prompts/                 ← 도메인별 답변 프롬프트 (MECH_STANDARD*.txt 등)
│   ├── config/
│   │   ├── settings.example.json    ← 템플릿
│   │   └── settings.json            ← 실제 키 (직접 생성, gitignore)
│   ├── scripts/
│   │   ├── start_db_server.ps1      ← 서버 실행
│   │   ├── smoke_test.py            ← 단독 점검 (health→domains→ask)
│   │   └── check_retrieval.py       ← 벡터 검색만 점검 (LLM 없이, 도메인별)
│   ├── data/                        ← 직접 배치 (gitignore)
│   ├── models/                      ← 직접 배치 (gitignore)
│   ├── requirements.txt
│   └── README_SERVER.md             ← 서버 실행/테스트 가이드
│
├── client/
│   └── README_CLIENT.md             ← 클라 빌드/실행 가이드
├── AGENTS.md                        ← (이 파일) 프로젝트 설명서
├── DEV_ENVIRONMENT.md               ← 개발 환경 정리
└── PROGRESS.md                      ← 진행상황 + 할 일
```

※ data/, models/ 는 무거워서 깃헙 미포함. **`server/` 바로 아래**에 배치 (코드가 이 위치 기준).
※ src/ 폴더(구버전 키워드매칭)는 사용 안 함.

---

## 6. 도메인 키

| 키 | 표시명 | 설명 |
|---|---|---|
| MECH_STANDARD | 설계수순서 | 기구 설계 표준 체크리스트 |
| CMF_DFC | DFC | Design For Cost (재료비 절감안) |
| CMF_ISSUE | CMF | CMF 문제/이력 |
| MECHA_DFM | DFM | 공정 설계 표준 (제조 고려) |

※ 서버·클라 모두 MECH_STANDARD 로 통일 완료 (엔드포인트도 /mech/...).
※ 도메인 하위 DB 선택: `data/{도메인}/db_registry_{도메인}.json` 을 서버가 자동 인식.
  사용자가 카드에서 복수 선택 → ask 의 db_keys 로 검색 범위 지정. (없으면 전체)
  MECH_STANDARD db_key: mobile/foldable/water_proof/wearable,
  MECHA_DFM: cam_design/jig_design/metal_design/mold_design, CMF_*: 단일.

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
| DB_MCP_TOKEN | DB 서버 인증 토큰 (Bearer). **서버 유효토큰 = settings.json.db_mcp_token 우선**. 불일치 시 401. |
| NX_ASSISTANT_WEBVIEW2_EXE | **NX 버튼이 띄울 앱 exe 전체경로.** 설치된 코덱스 스파이크 런처가 읽는 변수(≠ `NX_ASSISTANT_EXE`). 데모용 현재 빌드 연결에 사용. |
| NX_ASSISTANT_FAKE_DBPROMPT=1 | (테스트) 서버 없이 예시 RAG 프롬프트로 GPT 분기 검증. **실서버 테스트 시 끌 것** |
| NX_ASSISTANT_SHOW_WORKER=1 | (디버그) GPT 워커 창을 띄워 관찰 (평소엔 화면 밖 parking) |

### NX 버튼 → 앱 실행 (현재 = 임시 연결, 시연용)
- 설치된 런처는 코덱스 **experiments 스파이크 런처**. exe 경로를 `NX_ASSISTANT_WEBVIEW2_EXE`(있으면) → 없으면 하드코딩된 스파이크 publish 경로 순으로 잡음.
- 그래서 우리 앱 연결 = `setx NX_ASSISTANT_WEBVIEW2_EXE "<...>\bin\Debug\net8.0-windows\NxAssistant.exe"`.
- **환경변수 상속 주의**: NX는 시작 시점 환경을 상속 → 변수 바꾸면 **로그오프/재로그인**(또는 explorer 재시작) 후 NX 실행해야 반영됨. (DB_MCP_TOKEN도 동일)
- 정식화(시연 후): `client/nx-launcher` 우리 런처로 빌드·재설치 → NxAssistant.exe 직접 실행, 스파이크 의존 제거.

### 로그 (2종, 위치 다름)
- **앱 로그**: `%LOCALAPPDATA%\NX_Assistant\logs\nx-assistant.log` (앱 동작/오류)
- **런처 로그**: `<프로젝트루트>\logs\nx-launcher.log` (NX가 **어떤 exe를 띄웠는지** — `Started WebView2 Assistant: <경로>`)

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

- 프로젝트에 깃헙 연결됨 → Codex가 project_knowledge_search로 코드 읽기 가능.
  (단, data/models 등 깃헙 미포함 파일, 미커밋 로컬 변경은 못 봄 → Get-Content로 보여줘야 함)
- Codex는 수정본을 만들어 파일로 전달 → 사용자가 VDI/로컬에 반영. (직접 파일 수정 불가)
- 코드 수정 전달: 부분수정은 [파일경로/찾을부분/교체], 경로 항상 명시. 많으면 파일 통째로.
- ZIP은 Codex가 직접 못 만듦 → PowerShell 스크립트 제공 → 사용자 실행.
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

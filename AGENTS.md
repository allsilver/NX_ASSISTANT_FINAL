# NX Assistant — Codex 작업 메모 (프로젝트 설명서)

> 이 파일은 프로젝트의 구조·환경·규칙을 설명하는 정적 문서입니다.
> 진행 상황과 할 일은 PROGRESS.md를 참조하세요.
>
> **버전 v3.1** (2026-06-14): 구파일 정리(ILlmSession/LlmSession/MockLlmSession 삭제) + NX 런처 정식화(csproj+config파일 기반).
> 세션 3형제: `DbQuerySession`(DB조회, `IChatSession` 구현) / `NxControlSession`(NX제어) / `AutomationSession`(자동화).
> NX 런처: `client/nx-launcher/` — `launcher.json`의 `NxAssistantExe`에 경로 지정, 환경변수 불필요.

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
   │                         └ VDI(Mode=vdi)는 체크 예외
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
│   │   ├── ChatView.cs              ← 화면 3: 채팅 (IChatSession 의존, ⚙→설정 콜백)
│   │   ├── SettingsView.cs          ← 설정 화면 (관심분야 재설정 / 외부 AI 재로그인)
│   │   └── WorkerForm.cs            ← GPT WebView2 워커 (로그인/채팅)
│   ├── providers/
│   │   ├── IChatSession.cs          ← 채팅 세션 인터페이스 (ChatView ↔ Mock 분리)
│   │   ├── ILlmProvider.cs          ← LLM 프로바이더 인터페이스
│   │   ├── DbQuerySession.cs        ← DB조회 세션 (IChatSession 구현, Gauss/GPT 분기)
│   │   ├── NxControlSession.cs      ← NX제어 세션 (IChatSession 구현, 브리지 실행)
│   │   ├── AutomationSession.cs     ← 자동화 세션 (IChatSession 구현, knox_mail_automation 실행)
│   │   ├── GaussProvider.cs         ← Gauss (서버 경유)
│   │   ├── GptProvider.cs           ← GPT (WorkerForm 래핑, userWorker만)
│   │   ├── IToolRouter.cs           ← 도구 라우팅 인터페이스 (2차용)
│   │   └── ToolCall.cs              ← 도구 호출 정보
│   ├── mcp/
│   │   ├── DbMcpClient.cs           ← DB 서버 HTTP 요청 (/mech/ask 등). 응답 images[] base64 파싱
│   │   ├── DbKeyOption.cs           ← db_key 메타 레코드 (카드/프리뷰 공유, 네트워크 의존 없음)
│   │   ├── RagImage.cs              ← 검색 표준 이미지 레코드 (name/score_pct/PNG바이트, 프리뷰 공유)
│   │   └── NxMcpClient.cs           ← NX MCP 호출
│   ├── history/HistoryManager.cs    ← 대화 히스토리
│   ├── config/DbKeySelectionStore.cs← 도메인별 선택 db_key 로컬 저장 (%LOCALAPPDATA%\NX_Assistant\db_selection.json)
│   ├── router/RouterClient.cs       ← (1차 배포 미사용, 2차 라우터용 보존)
│   ├── AppConfig.cs                 ← 앱 전역 설정 (우선순위: appsettings.json > 환경변수 > 기본값)
│   ├── Program.cs                   ← MainForm 실행 (AppIcon.Load = exe 아이콘 추출)
│   ├── appsettings.json             ← 기본 설정 (커밋, 안정)
│   ├── appsettings.local.json       ← 로컬 설정 (gitignore, DbMcpToken/AutomationPython 등)
│   ├── ui/assets/                   ← 카드 로고 PNG (gauss_logo/chatgpt_logo, EmbeddedResource)
│   ├── assets/                      ← nx_assistant.ico(작업표시줄/제목줄, <ApplicationIcon>)
│   └── NxAssistant.csproj
│
├── client/nx-launcher/             ← NX 버튼 → 앱 실행 런처 (NXOpen DLL)
│   ├── NxAssistantLauncher.csproj  ← 빌드 정의 (NXOpen 참조: C:\SCAD\NX2406\NXBIN\managed)
│   ├── launcher.json               ← 설치 시 NxAssistantExe 경로 채워넣기
│   ├── install.ps1                 ← 빌드 후 DLL을 nx-customization/application/에 복사
│   └── src/
│       └── NxAssistantLauncher.cs  ← DLL 진입점 (ApplicationEnter → OpenAssistantFromNx)
│
├── client/nx-customization/        ← NX 시작 시 로드되는 커스터마이제이션 (설치 원본)
│   ├── startup/                    ← .rtb(리본 탭) → .grb(그룹) → .men(버튼) + bitmaps
│   ├── bitmaps/
│   └── application/nx_assistant.men ← NX_ASSISTANT_OPEN_ACTION 등록 (NxAssistantLauncher.dll이 처리)
│
├── client/nx-mcp/                  ← NX 브리지 (repo 내장, 빌드 시 exe 옆으로 복사)
│   ├── verify_remoting_ready.py    ← NX 제어 명령 실행 스크립트
│   ├── remoting_bridge/            ← bin/NxMcpSessionServer.dll + NxMcpSessionClient.exe
│   └── remoting_client_via_mcp.py
│
├── client/automation/              ← Knox 자동화 툴 (repo 내장, 빌드 시 exe 옆으로 복사)
│   ├── knox_mail_automation/       ← quick_delivery_automation.py 등
│   └── requirements.txt
│
├── client/ui-preview/              ← UI 프리뷰 (개발 전용, 서버/WebView2 없음)
│   ├── UiPreview.csproj            ← app/ 의 순수 UI 파일을 링크해 컴파일
│   ├── Program.cs
│   ├── PreviewShell.cs             ← MockChatSession 으로 전 페이지 연결
│   └── MockChatSession.cs          ← IChatSession mock (가짜 답변)
│
├── server/                         ← 서버 PC 전용 (RAG). 이 폴더만 서버 PC로 복사
│   ├── db-mcp/
│   │   ├── server.py                ← HTTP 서버 (/health, /mech/domains, /mech/dbkeys, /mech/ask)
│   │   ├── rag_engine.py
│   │   ├── vector_store.py
│   │   ├── domain_registry.json
│   │   ├── llm/gauss_llm.py
│   │   └── prompts/
│   ├── config/settings.json         ← 실제 키 (직접 생성, gitignore)
│   ├── scripts/
│   └── README_SERVER.md
│
├── AGENTS.md                        ← (이 파일) 프로젝트 설명서
├── CLAUDE.md                        ← Claude Code 전용 설명서 (동일 내용)
├── DEV_ENVIRONMENT.md               ← 개발 환경 정리
└── PROGRESS.md                      ← 진행상황 + 할 일
```

※ data/, models/ 는 무거워서 깃헙 미포함. **`server/` 바로 아래**에 배치.

---

## 6. 세션 3형제 (IChatSession 구현체)

| 세션 | 분야 | 동작 |
|---|---|---|
| `DbQuerySession` | DB조회 | Gauss: 서버 /mech/ask → 답변. GPT: 서버에서 프롬프트 조립 → GPT 답변 |
| `NxControlSession` | NX제어 | 자연어→키워드 매핑 → `verify_remoting_ready.py {flag}` 실행 (nx-mcp/ 폴더) |
| `AutomationSession` | 자동화 | `knox_mail_automation.quick_delivery_automation` 실행 (automation/ 폴더) |

- 전부 `IChatSession` 구현 → `ChatView`가 동일하게 소비
- `MockChatSession`(ui-preview/)도 동일 인터페이스 → 프리뷰에서 Mock으로 대체

---

## 7. 도메인 키

| 키 | 표시명 | 설명 |
|---|---|---|
| MECH_STANDARD | 설계수순서 | 기구 설계 표준 체크리스트 |
| CMF_DFC | DFC | Design For Cost (재료비 절감안) |
| CMF_ISSUE | CMF | CMF 문제/이력 |
| MECHA_DFM | DFM | 공정 설계 표준 (제조 고려) |

---

## 8. 환경 제약

| 항목 | VDI | 로컬 PC |
|---|---|---|
| GPT (chatgpt.com) | ✅ | ✅ |
| Gauss API (sr-cloud.com) | ❌ | ✅ |
| pip / PyPI | ❌ | ✅ |
| NuGet | ❌ | ✅ |
| dotnet build | ✅ (메모리 옵션 필수) | ✅ |
| NX 실행 | ❌ | ✅ |

→ DB MCP 서버는 로컬/서버 PC에서만 실행. VDI는 C# 작업 + GPT 테스트.

---

## 9. 빌드 / 설정

### 앱 빌드 (VDI 필수 옵션)
```
cd client/app
dotnet build -p:UseSharedCompilation=false -m:1 --disable-build-servers
.\bin\Debug\net8.0-windows\NxAssistant.exe
```

### NX 런처 빌드 및 설치
```
cd client/nx-launcher
dotnet build NxAssistantLauncher.csproj -p:UseSharedCompilation=false -m:1 --disable-build-servers
# 또는 install.ps1 실행 (빌드 + DLL 복사 자동화)
.\install.ps1
```
설치 후 `application/launcher.json`의 `NxAssistantExe` 경로 확인.

### 주요 설정 (appsettings.json + appsettings.local.json)
| 키 | 기본값 | 설명 |
|---|---|---|
| DbMcpUrl | http://127.0.0.1:8766 | DB 서버 주소 |
| DbMcpToken | "" | DB 서버 인증 토큰 (서버 settings.json과 일치) |
| NxBridgeDir | "nx-mcp" | NX 브리지 폴더 (상대경로 = exe 옆) |
| NxBridgePython | "python" | NX 브리지 실행 Python |
| AutomationDir | "automation" | Knox 자동화 툴 폴더 |
| AutomationPython | "python" | 자동화 실행 Python (venv 경로 권장) |
| AutomationCdp | "" | Edge CDP URL (Knox SSO 로그인 브라우저) |
| Mode | "" | "vdi" 설정 시 서버 체크 예외 |

### 런처 설정 (nx-customization/application/launcher.json)
| 키 | 설명 |
|---|---|
| NxAssistantExe | NxAssistant.exe 전체 경로. 비워두면 launcher.json 옆의 NxAssistant.exe |

---

## 10. NX 런처 동작 원리

```
NX HEROS 버튼 클릭
  → NX가 NxAssistantLauncher.dll의 ApplicationEnter() 호출
  → ResolveExePath(): 환경변수 NX_ASSISTANT_EXE → launcher.json → DLL 옆 NxAssistant.exe
  → 이미 열려있으면 창 포커스(BringExistingWindow)
  → 없으면 Process.Start(NxAssistant.exe)
```

**설치 위치**: `F:\AX_TF\NX_Assistant\apps\nx-customization\application\`
- `NxAssistantLauncher.dll` ← 빌드 결과물 복사
- `launcher.json` ← NxAssistantExe 경로 채워넣기
- `nx_assistant.men` ← NX_ASSISTANT_OPEN_ACTION 등록 (기존 파일 유지)

---

## 11. UI 설계 원칙

- **텍스트에 고정 Height/Width 금지** (잘림의 근본 원인). AutoSize=true 사용.
- **고정 높이 카드 안에 Percent 여백 금지** → 카드도 AutoSize, 또는 콘텐츠를 Dock=Top 스택으로.
- **동적 Margin 계산 중앙정렬 금지** → Dock=Fill+TextAlign 또는 Resize 핸들러.
- Light 테마 색: 배경#eef1f5, 표면#ffffff, 테두리#c8d0dc, 강조(남색)#1a3a6b,
  텍스트#1a1f2e, 흐린텍스트#5a6478, GPT그린#10a37f.

---

## 12. 작업 방식 / 규칙

- 프로젝트에 깃헙 연결됨 → Codex가 project_knowledge_search로 코드 읽기 가능.
- Codex는 수정본을 만들어 파일로 전달 → 사용자가 VDI/로컬에 반영.
- 코드 수정 전달: 부분수정은 [파일경로/찾을부분/교체], 경로 항상 명시.
- ZIP은 Codex가 직접 못 만듦 → PowerShell 스크립트 제공 → 사용자 실행.
- GitHub Desktop으로 push (터미널 push는 보안 차단).

---

## 13. 기술 스택

- 클라이언트: C# (WinForms + WebView2, .NET 8)
- NX 런처: C# (NXOpen DLL, .NET 8, `C:\SCAD\NX2406\NXBIN\managed` 참조)
- 서버: Python (http.server 기반 DB MCP, 포트 8766)
- LLM: Gauss(사내 REST API), GPT(WebView2 개인계정)
- RAG: ChromaDB, Ollama 임베딩(qwen3-embedding:4b), bge-reranker-v2-m3, LangChain

---

## 14. 배포 단계 / 향후 방향

- **1차 배포(20명)**: 라우터 없음. 모드 직접 선택. LLM Gauss/GPT.
- **2차 배포(1000명)**: 라우터 추가 (RouterClient 로직 보존, 호출만 안 함).
- **DB 내부 라우터**: DB조회 모드 안 도메인 판단 LLM.
- **MCP 전환 계획**: 현재 HTTP REST → 나중에 MCP로.

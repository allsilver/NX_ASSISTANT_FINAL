# NX Assistant 개발 진행상황

> 최종 업데이트: 2026-06-14 · **버전: v3.1** (구파일 정리 + NX 런처 정식화 + AGENTS.md v3 갱신)
> 진행 상황과 다음 할 일만 기록합니다. 프로젝트 구조·환경·규칙은 CLAUDE.md 참조.

---

## 🟡 진행 중 / 백로그

### NX 연동 + 브랜딩 (2026-06-11 완료, 시연 가능 상태)
- [x] **Gauss/GPT 카드 로고 교체**: 실제 로고 PNG(흰배경 제거·트림) 임베드, `BrandLogo`가 비율맞춤 렌더, 카드 기존 텍스트 라벨 제거(글자는 이미지에 포함). 앱+프리뷰 csproj 둘 다 EmbeddedResource.
- [x] **작업표시줄/제목줄 아이콘**: 코덱스 최종 승인본 `nx-assistant-...-v3-taskbar-fit.ico`(NX+별)를 `client/app/assets/nx_assistant.ico`로 가져와 `<ApplicationIcon>` 연결 + `MainForm.Icon = AppIcon.Load()`. (재정리하며 누락됐던 것 복구)
- [x] **창 제목 "NX Assistant"** 로 변경 (코덱스 원래대로 + 런처 BringExistingWindow 가 찾는 제목과 일치).
- [x] **NX 커스터마이제이션 파일 repo 반입**: startup(.rtb/.grb/.men/galaxy.bmp/README), bitmaps, application/.men — 코덱스 원본 그대로(기존엔 .men 하나뿐).
- [x] **NX 버튼 → 우리 앱 실행 연결 (로컬 검증 완료)**: HEROS "AI Assistant" → 현재 빌드 앱이 뜸.
  - 핵심: 설치된 런처(코덱스 experiments 스파이크 런처)는 `NX_ASSISTANT_WEBVIEW2_EXE` 환경변수를 읽음(`NX_ASSISTANT_EXE` 아님!). 이 변수에 우리 exe 전체경로 지정 → 버튼이 우리 앱 실행. (환경변수 상속 위해 로그오프/재로그인 필요)
- [x] **GPT 경로 401 해결 (로컬)**: 클라 `DB_MCP_TOKEN`(Bearer) = 서버 유효토큰(`settings.json.db_mcp_token` 우선) 일치 + NX-실행 앱이 토큰 상속받도록. 인증은 Gauss/GPT 공통(서버 `_auth`).
- [x] **NX제어 데모(스크립트)**: `NxControlSession` — 기본은 스크립트 응답(면 offset / 선 extrude / 엣지 blend), `NX_CONTROL_REAL=1` 시 실제 브리지(`verify_remoting_ready.py`). `ChatView` 인사말 화면별 커스텀 가능하게 옵션 추가.
- [x] **자동화 데모**: `AutomationSession` — `NX_AUTOMATION_FAKE=1` 시 단계 멘트+요약(실제 브라우저는 수동 전환), 아니면 코덱스 knox 퀵신청 툴(`quick_delivery_automation`) 실행.
- [x] **DB조회 프롬프트 단순화**: `MECH_STANDARD`(+case1/2)·`CMF_DFC` — 케이스/형식 강제·마크다운 금지 제거, GPT 자유 답변. (※ 챗봇 정규화 시 전면 재작성 예정. 원본은 서버 `.orig` 백업.)

### 🎯 v3 정리 계획 (실배포 형태로 — 기구팀 100명 NX 내장 배포가 목표)
- [x] **Phase 1 — v2.6 커밋**: 데모 완성 상태 안전 기준점.
- [x] **Phase 2 — 클라이언트 설정 일원화** ⭐ (v3): `AppConfig`(`client/app/AppConfig.cs`) + `appsettings.json` 도입. 우선순위 **설정파일 > 환경변수 > 기본값**. 앱 내 `GetEnvironmentVariable` 직접 읽기 **0개**(전부 AppConfig 경유). 경로형(`NxBridgeDir`/`AutomationDir`)은 상대경로면 앱 exe 폴더 기준 해석 → 배포 안전. 100명 배포 시 보통 `DbMcpUrl`/`DbMcpToken`만 채우면 됨.
- [x] **Phase 2 — 데모 코드 삭제** (v3): `NxControlSession` 스크립트 응답·`AutomationSession` fake/스크린샷 경로 통째 제거 → **실제 브리지/툴 호출만** 남김. 데모 토글 env(`NX_AUTOMATION_FAKE`/`NX_CONTROL_REAL`/`NX_AUTOMATION_SCREENSHOT`) 삭제.
- [x] **툴 흡수 (v3, VDI)**: NX 브리지(codex nx-local)·자동화(knox_mail_automation, MEG repo handoff)를 repo로 흡수 → `client/nx-mcp/`(DLL 포함)·`client/automation/`. 빌드 시 exe 옆 복사(csproj), appsettings 상대경로로 해석 → repo self-contained(F드라이브·외부 repo 의존 제거). NX DLL은 버전 안 맞을 때만 재빌드(로컬).
- [x] **Phase 3 — 프롬프트 원본 백업** (v3): `.orig` → `server/db-mcp/prompts/_backup/*.original.txt`로 보존(삭제 안 함). 추후 프롬프트 전면 재작성 시 참고.
- [x] **Phase 2 — 리네이밍(혼동 제거)** (v3): `ILlmSession`→`IChatSession`, `LlmSession`→`DbQuerySession`, `MockLlmSession`→`MockChatSession`. 파일명·타입·참조 전부. 3형제 대칭(DbQuery/NxControl/Automation).
- [~] **GPT 워커 안정화 (진행중)** (v3): 세션 만료 감지 추가(ProbeAsync가 "세션 만료"/disabled composer 감지) + **전송 직전 재확인**(ChatAsync/ChatStreamAsync가 보내기 전 재probe → 만료면 silently 통과 대신 "재로그인" 안내). ※ 만료 DOM 정규식은 실제 페이지로 튜닝 필요. 자동재시도·멈춤 워치독은 잔여.
- [ ] **Phase 4 — 토글/플래그 문서화**: 개발 플래그(Mode/ShowWorker/FakeDbPrompt) 분류 문서화.
- [x] **Phase 5 — NX 런처 정식화** (v3.1): 스파이크 런처 의존 제거 → `client/nx-launcher/` csproj 생성, `launcher.json` 기반 경로 설정(환경변수 불필요), `install.ps1` 설치 자동화. NX DLL 경로: `C:\SCAD\NX2406\NXBIN\managed`.
- [ ] 미완성 기능은 차차 기능별 완성(NX제어 실제 OpenNX API, 자동화 실제 Playwright 등 — LLM이 도구/API 판단).

### 스트리밍 채팅 UX (V2.4 완료, 2026-06-11)
- [x] 스트리밍 이벤트 인터페이스(ChatEvent: Status/Token/Done) + ChatView 소비
- [x] GPT 점진 스트리밍 인프라(워커 폴링) + 단계 멘트(실제 await에 묶음) + 최소표시시간
- [x] 화면 밖(parking)에서도 동작하게 Page Visibility 우회 주입(AddScriptToExecuteOnDocumentCreated)
- [x] 완료 후 액션바(복사/좋아요/싫어요, Segoe MDL2 아이콘, 페이드인) + 드래그 선택/복사 + 줄바꿈 CRLF 정규화
- [x] 멀티턴 응답 감지: **마커(data-nx-seen) 기반**(가상화/동일텍스트 안전) + 35초 워치독(멈춤 방지)
- [x] **GPT 답변 서식 보존**: 완료 시 DOM→마크다운 추출 → **문단 단위로 서식 입혀 페이드인**(RichTextBox, 선택/복사 유지). 코드블록은 mono.
  - 최종 방식 결정: 부분(토큰/줄) 점진 렌더는 **번쩍임/싱크 불안정** → "생성 중엔 멘트만, **완료 후 문단별 페이드인**"으로 확정(안정).
  - 번쩍임 방지: RichTextBox `HideSelection=true`(페이드 시 선택 하이라이트 숨김). 꼬리 여백: 블록 사이만 줄바꿈 + 행 하단 여백 축소.
- [ ] Gauss SSE 토큰 스트리밍(차주 로컬, MEG_Chatbot_claude 구현 참고). 현재 Gauss는 한 번에(완료 후 서식 렌더는 동일 적용).

### GPT 워커 안정성 백로그 (웹 스크래핑 특성 — 다음에)
- [ ] **자동 재시도 1회**: 첫 응답 감지 실패 시 사용자 개입 없이 1회 자동 재전송.
- [ ] **워커 헬스체크/자가복구**: WebView2 무응답 시 페이지 리로드 후 재시도.
- [ ] **로그인 만료 감지**: 응답 자리에 로그인 화면이 뜨면 "재로그인 필요" 안내로 전환.
- [ ] **전송 검증**: 전송 직후 생성이 실제 시작됐는지(예: stop 버튼) 확인해 send 실패 조기 포착.
- [ ] (참고) 증상: 대화가 길어진 뒤 GPT는 답하는데 앱이 못 받고 멈춤 → 마커 방식으로 1차 해결. 추가 안정장치는 위 항목.

### UI / 이미지 백로그 (다음 개선)
- [ ] **창 크기 전체 재조정**: 이미지가 들어가니 기본 창이 작음. 메인 창 + **GPT 로그인 창** 크기 같이 키우기.
- [ ] **이미지-답변 정합성**: 현재 이미지는 리랭크된 문서 전체에서 뽑힘(LLM이 실제 답변에 쓴 문서만은 아님). "자료 없음"류 답변에도 무관 이미지가 붙을 수 있음. → 절대 점수 임계값으로 거르기(meg도 알던 한계).
- [ ] **관련성 % 계산**: 현재 리랭크 점수 min-max 정규화(이미지 적으면 최저=0%). 절대 점수(sigmoid 등) 기반으로 바꾸면 더 정직. 표시 자리는 이미 준비됨.

---

## ✅ 완료된 작업

### 검색 이미지 출력 (서버 + 클라) — 로컬 실서버 검증 완료 (2026-06-11, V2.5)
MEG_ChatBot_claude의 `_find_image_paths` 로직을 이식.
- **서버**(`rag_engine.py` / `server.py`):
  - `_find_image_paths(scored_docs)` 이식. 파일명 규칙 = `"{db_key} {full_path 마지막 2세그먼트}.png"`, 이미지 폴더 = `server/data/<domain>/image`.
  - `setup_design_bot(domain_key=...)` 추가 → 도메인별 이미지 폴더 지정. `rag_handler`가 `(텍스트, 이미지[])` 반환.
  - `/mech/ask` 응답에 `images:[{name, score_pct, data(base64)}]` 추가 (Gauss·GPT 공통, 멀티스레드 안전 — 봇 stash 대신 직접 반환). 최대 4장(`IMAGE_MAX`).
- **클라**(`RagImage.cs` 신규, `DbMcpClient` / `GaussProvider` / `ILlmSession`(ChatEvent.Images) / `LlmSession` / `ChatView`):
  - 응답의 base64 이미지 파싱 → `ChatEvent.ImageList`로 ChatView 전달 → 답변 아래 PictureBox(폭 맞춤·비율 유지) + 캡션 `(관련성 X%) 파일명`.
  - **이미지 클릭 → 모달리스 팝업 확대**(여러 창·채팅 동시 사용, Esc/클릭 닫기).
  - 마우스 휠 스크롤: 자식 컨트롤이 휠 가로채던 문제를 **앱 메시지 필터**로 해결. 바닥 **스페이서 행**으로 컴포저 위 여백 확보(FlowLayoutPanel은 bottom padding이 스크롤에 안 잡힘).
  - 프리뷰(Mock)에 가짜 PNG 2장으로 렌더 검증 경로 추가.
- 검증: 프리뷰(UI) + **로컬 Gauss 실서버(실이미지) 검증 완료.**


### GPT 분기(서버 검색→GPT 답변) + 홈 뒤로가기 — VDI 검증 완료 (2026-06-10, V2.3)

> 핵심 원칙: **검색·컨텍스트·프롬프트 조립은 서버(rag_engine) 한 곳.** Gauss는 그 프롬프트로 답변 생성, GPT는 같은 프롬프트를 받아 답변 생성. → 프롬프트/검색 로직을 바꾸면 Gauss·GPT 양쪽 자동 반영(어느 LLM 코드도 안 건드림).
> **VDI 가짜 프롬프트(`NX_ASSISTANT_FAKE_DBPROMPT`)로 클라 파이프라인 검증 완료** — 질문→프롬프트 수신→GPT 답변 정상, 워커 창 숨김 상태에서도 응답 읽기 정상. **서버의 실제 프롬프트 조립은 차주 로컬(GPT+서버)에서 검증 예정.**

- **서버**:
  - `rag_engine.setup_design_bot`: 원본 프롬프트(answer_prompt/case_prompts)와 Gauss 체인(`/no_think`+템플릿|llm) 분리. `rag_handler(..., compose_only)` 추가 — compose_only면 검색·컨텍스트·invoke_input 은 그대로 하고 **Gauss 호출 대신 완성 프롬프트 문자열 반환**. (`/no_think` 는 Gauss 전용이라 GPT 프롬프트엔 미포함)
  - `server.py /mech/ask`: `for_gpt` 플래그 추가 → true면 `{prompt}`, 아니면 `{answer}` 반환.
- **클라이언트**:
  - `DbMcpClient.GetGptPromptAsync()` (/mech/ask, for_gpt=true → prompt 파싱).
  - `LlmSession.AskAsync`: **GPT면** 서버에서 프롬프트 받아 → GPT 워커가 답변 생성. **Gauss면** 기존대로. 도메인/db_keys 를 LlmSession 에도 보관(GPT 프롬프트 요청용). ChatView 는 변경 없음.
  - 1차: 대화 히스토리는 빈 값으로 전송(컨텍스트+현재 질문). GPT 웹 세션 자체 맥락에 의존.
- **홈 뒤로가기**: `FieldSelectView` 에 ← 추가 → AI 모델 재선택(`ShowAiSelect`). MainForm/PreviewShell 배선.
- **DB세부선택 문구**: `DbKeyPrompts` 도메인 분기 제거 → 전 도메인 공통 "어떤 항목에 관심이 있나요?" / "선택한 범위로 검색합니다. (복수선택 가능)". 카드만 서버 db json 으로 달라짐.
- **테스트/진단 플래그** (DEV_ENVIRONMENT.md 2장):
  - `NX_ASSISTANT_FAKE_DBPROMPT=1` : 서버 없이 예시 프롬프트로 GPT 분기 검증(VDI용). **실서버 테스트 시 끌 것.**
  - `NX_ASSISTANT_SHOW_WORKER=1` : GPT 워커 창을 띄워 관찰(디버그).
  - `WorkerForm.ChatAsync` 단계별 로그(`%LOCALAPPDATA%\NX_Assistant\logs\nx-assistant.log`).
- **남은 작업 / 주의**:
  - [ ] 차주 로컬(GPT+서버)에서 GPT 분기 실서버 검증(`FAKE_DBPROMPT` 끄고 실제 프롬프트 수신).
  - [ ] 긴 RAG 컨텍스트를 ChatGPT 웹 입력창에 붙일 때 길이/붙여넣기 이슈 가능 → 길면 컨텍스트 길이 제한 검토.


### DB세부선택 UI 확정 + 본앱 통합 + 설정 페이지 + 서버 가드 — 코드 작성 (2026-06-10)

> 프리뷰에서 DB세부선택(관심분야) 카드 UI 확정(여러 번 반복 — 레이아웃 함정은 DEV_ENVIRONMENT.md 3-3 참조).
> 본앱 배선 + 설정 페이지 + 서버 미연결 가드까지 코드 반영. **로컬 DB 서버 띄운 상태의 실동작 검증은 아직(서버 연결돼야 카드가 채워짐).**

- **DB세부선택 UI 확정** (`DbKeySelectView.cs`): 2열 50/50 그리드(상단 고정·중앙정렬, 하단 스페이서), 카드 선택색(흰→옅은하늘+남색테두리), 우하단 "N개 선택됨", 제목/안내(고정아님-AutoSize 한 줄 라벨 줄단위 쌓기로 balloon/clip 모두 회피), 메뉴경로 칩 블록, 도메인 선택 페이지와 제목 높이 정렬.
- **본앱 통합**:
  - `DbMcpClient.GetDbKeysAsync()` (GET /mech/dbkeys), `GaussProvider.DbKeys` + /mech/ask 에 db_keys 동봉, `LlmSession.SetDbKeys()`.
  - `MainForm`: 도메인 선택 → (서버 옵션 ≥2면) 관심분야 페이지 → 채팅. **복수 DB 도메인 하드코딩 제거 → 서버 /mech/dbkeys 결과로 판단**. 선택은 `DbKeySelectionStore` 로컬 저장 → **도메인당 1회만 표시, 이후 유지**.
  - `DbKeyPrompts.For()` 도메인별 문구를 MainForm/PreviewShell 공용으로.
- **설정 페이지** (`SettingsView.cs`): "관심분야 재설정"(force 재표시) / "외부 AI 재로그인"(현재 AI선택 재진입-임시). 채팅 ⚙ → 설정 연결. ChatView 에 onSettings 콜백 추가.
- **서버 미연결 가드** (`MainForm.StartUp`): 앱 시작 시 `/health` 확인 → 실패 시 "서버 연결 불가" 화면으로 진입 차단. **VDI(`NX_ASSISTANT_MODE=vdi`)는 예외.**
- **남은 작업**:
  - [ ] 로컬 DB 서버 띄우고 본앱 실동작 검증(설계수순서/DFM 카드, 나머지 스킵, 1회표시·유지, 서버 끄면 차단화면).
  - [ ] (TODO) 도메인 목록 서버화(/mech/domains), AiSelectView 모델 라벨 정리, 설정 동작 자연스럽게 — 아래 목록 참조.


### UI 프리뷰 + 화면 분리 리팩토링 — 코드 작성 (VDI 빌드·렌더 확인 전) (2026-06-10)

> UI 를 많이 손볼 예정이라, 서버 없이 전 페이지를 띄워 빠르게 보고 고치는 프리뷰를 먼저 만듦.
> **아직 VDI 빌드/렌더 확인 안 함.** db_keys "본 앱 통합"(MainForm 배선, GaussProvider/LlmSession/DbMcpClient db_keys)은
> UI 확정 후로 미룸 — 지금은 동작 안 바뀌는 리팩토링 + 프리뷰만.

- **구조 리팩토링 (동작 불변)**:
  - `MainForm.cs` 의 4개 View 를 파일 분리 → `AiSelectView.cs` / `FieldSelectView.cs` / `DomainSelectView.cs` / `ChatView.cs`
  - `ChatView` 가 `LlmSession`(서버/WebView2 의존) 대신 신규 `ILlmSession` 인터페이스에 의존 → 프리뷰서 Mock 주입 가능
  - `LlmSession : ILlmSession` 구현 (멤버는 그대로)
  - `DbKeyOption` 레코드를 `mcp/DbKeyOption.cs` 로 분리 (네트워크 의존 없음 → 프리뷰 공유)
- **신규: UI 프리뷰 프로젝트** `client/ui-preview/` (별도 csproj, WebView2/서버 없음)
  - `app/` 의 순수 UI 파일(UiKit, 4 View, DbKeySelectView, ILlmSession, DbKeyOption, DbKeySelectionStore)을 **링크**해 공유
  - `PreviewShell` 이 MainForm 흐름을 mock 으로 재현: AI선택 → 분야 → 도메인 → **DB세부선택(mock 옵션)** → 채팅
  - `MockLlmSession` 가짜 답변 / db_key 는 mock 옵션(MECH_STANDARD·MECHA_DFM 복수, CMF_* 단일→건너뜀)
  - 실행: `cd client/ui-preview ; dotnet run`
- **개발 방식 문서화**: DEV_ENVIRONMENT.md 3-1(UI 독립 테스트 우선) / 3-2(진행 전 확인) 추가
- **남은 작업**:
  - [ ] VDI 에서 `client/ui-preview` 빌드/실행 → 전 페이지 + DB선택 카드 UI 확인·수정 (반복)
  - [ ] (UI 확정 후) db_keys 본 앱 통합: MainForm 에 DB세부선택 배선 + GaussProvider/LlmSession/DbMcpClient db_keys + ask 동봉
  - [ ] 본 앱(`client/app`) 빌드도 통과하는지 확인 (리팩토링 후)

### db_keys 서버측 (2026-06-09, 이전)

> 서버는 이미 `/mech/dbkeys` + ask 의 db_keys 수용 완료 (아래 "DB 선택(db_key) 방식" 참조).
> 클라 통합은 위 UI 확정 후 진행.


### 1차 client 연결 (Gauss DB조회) — 코드 작성 (2026-06-09)

- **csproj VDI(dll)/로컬(NuGet) 자동 분기**: `WEBVIEW2_CORE_DLL` 환경변수 유무로 분기 → 로컬 빌드 성공 확인
- **GaussProvider**: 없는 엔드포인트 `/gauss/chat` → `/mech/ask` 호출로 수정. `Domain` 프로퍼티로 도메인 전달
- **LlmSession.SetDomain** 추가 / **MainForm.ShowChat** 에서 도메인 주입 (`_session.SetDomain(_domain)`)
- DEV_ENVIRONMENT(로컬 빌드 환경 + 로컬 전용 보존 항목), README_CLIENT(빌드 분기) 갱신
- **1차 client test 성공 (2026-06-09)**: 로컬에서 서버+클라(Gauss) DB조회 답변 왕복 확인.
  (csproj 분기를 dll 파일 Exists 기준으로 변경해 빌드 안정화 / 401 은 클라 DB_MCP_TOKEN ↔ 서버 settings.json 토큰 일치 + 서버 재시작으로 해결)
- **다음 작업**: GPT 분기(2차 client test) / 카드 복수선택 UI(db_keys 전달)

### DB 선택(db_key) 방식 — 서버측 구현 (2026-06-09)

- **db_registry 자동 인식**: 서버가 `data/{도메인}/db_registry_{도메인}.json` 을 읽어
  선택 가능한 db_key 목록을 결정 (없으면 domain_registry db_keys → 도메인명 fallback)
- **GET /mech/dbkeys?domain=X** 신규: 카드용 db_key 목록 (key/display_name/description/default)
- **POST /mech/ask 가 db_keys 수용**: 클라가 보낸 db_keys 로 검색 범위 지정 (요청 ∩ 허용, 없으면 전체)
- bot 캐시를 `도메인+db_keys 조합` 키로 변경 (조합별 캐시 분리)
- default 플래그는 db_registry 에서 읽음 (예: water_proof 공통 → 기본 체크)
- **검증**: dbkeys 응답/ default 반영/ fallback 정상 동작 확인 (실검색은 로컬에서)
- **남은 작업(클라)**: 카드 복수선택 페이지 + 로컬 저장 + 설정-재설정 메뉴 + ask 에 db_keys 동봉

### DB MCP 서버 정비 + 배포 분리 (2026-06-09)

- **MEG → MECH 전면 변경** (서버·클라 양쪽 동시):
  - 엔드포인트 `/meg/ask`·`/meg/route`·`/meg/domains` → `/mech/...` (server.py, DbMcpClient.cs)
  - 도메인 키 `MEG_STANDARD` → `MECH_STANDARD` (domain_registry.json, db_intent_llm.py, rag_engine 기본값)
  - 프롬프트 파일명 `MEG_STANDARD*.txt` → `MECH_STANDARD*.txt`
  - nx-mcp POC 설명문의 `MEG` → `MECH`
  - 클라 MainForm(MECH_STANDARD)과 서버 domain_registry 정합성 일치 확인
  - (원본 레포명 `MEG_ChatBot_claude`, 폴더 `meg_chatbot`은 고유명사라 유지)
- **data/models 경로 통일 → `server/` 아래**:
  - vector_store `DATA_ROOT = ROOT.parent/"data"`, rag_engine `_PROJECT_ROOT=_SRC_DIR.parent`(models)
  - server.py settings 경로도 `ROOT.parent/"config"/"settings.json"`로 단순화
  - .gitignore도 `server/data/`·`server/models/`로 변경
  - → `server/` 폴더만 통째로 서버 PC에 옮기면 data/models가 함께 따라감
- **서버 독립 테스트 진입점 추가**:
  - `server/scripts/smoke_test.py` (health→domains→ask 단계별, 표준 라이브러리만)
  - `server/scripts/check_retrieval.py` (LLM 없이 벡터 검색만 도메인별 점검 — db_key/경로/문서수 확인)
  - `server/config/settings.example.json` (실제 settings.json 템플릿)
  - `server/README_SERVER.md` / `client/README_CLIENT.md` (배포 단위별 실행/테스트 가이드)
  - `start_db_server.ps1`의 `$Host`(PS 예약변수) → `$BindHost` 버그 수정
- **검증**: 서버 실제 구동 → `/health`, `/mech/domains`, 토큰 인증 정상.
  도메인 4종(MECH_STANDARD/MECHA_DFM/CMF_DFC/CMF_ISSUE) 등록 확인.
  (ask 단계는 로컬 PC에서 data/models 배치 후 검증 예정)

### 1차 배포 UI 통합 (커밋 9f30fb4)

- **신규 UI 4화면 완성** (단독 테스트 → 본 프로젝트 통합):
  - 화면0 AI 선택: Gauss/GPT 가로형 큰 카드 2개 ("환영합니다!" + 체크리스트 설명)
  - 화면1 분야 선택: DB조회 / NX제어 / 자동화
  - 화면2 DB 도메인: 설계수순서 / DFC / CMF / DFM
  - 화면3 채팅: AI=배경 텍스트(말풍선X), 나=오른쪽 남색 말풍선,
    상단바 2줄(뒤로/홈/설정 + 대화초기화/동의어/LLM드롭다운), 입력창 여러줄
- **파일 구성** (빠른 1차배포 위해 2파일로 묶음, 추후 분리 예정):
  - `client/app/ui/UiKit.cs` — 공통 컴포넌트 전부
  - `client/app/ui/MainForm.cs` — 화면 전환 + 4개 View
  - 기존 `AssistantForm.cs` 삭제, `Program.cs`는 MainForm 실행하도록 수정
- **GPT 실제 연결**:
  - `LlmSession.cs` (신규) — 앱 전역 LLM 선택 관리
  - `GptProvider.cs` — userWorker만 사용 (라우터 1차배포 미사용)
  - GPT 로그인 자동 처리: 이미 로그인됨 → 창 안 띄움 / 필요 시 → 띄우고 완료 감지 후 자동 숨김
  - 채팅에서 선택된 LLM으로 실제 답변 호출
- **검증 완료**: 4화면 전환, GPT 로그인 자동처리, GPT 답변, LLM 토글, 뒤로/홈
- **커밋 9f30fb4** "feat: 신규 UI 통합 (4화면 + LLM 세션), AssistantForm 대체"
  (push는 GitHub Desktop으로)

### 이전 완료 (UI 통합 전)

- VDI 개발환경 (.NET 8, 메모리제한 빌드), WebView2 로컬 dll 참조(NuGet 우회), csproj 환경변수화
- GPT WinForm 채팅 동작 (로그인 감지)
- throttling 해결: WorkerForm에 `--disable-background-timer-throttling` 등 3개 플래그
  → 화면 밖 워커도 GPT 응답 정상 읽음 (검증 완료)
- 단독 UI 테스트 4개 → 본 프로젝트 통합 완료

---

## 📋 다음 작업 (DB MCP 연결 — 1차배포 나머지)

> 주의: DB MCP 내부 동작은 로컬에서 직접 띄워봐야 정확히 파악 가능.
> 코드만 보고 추측하지 말고, 로컬 테스트로 실제 동작 확인 후 진행할 것.

### 즉시
1. [x] 프로젝트 폴더 구조 전체 확인
2. [x] MEG → MECH 네이밍 일괄 변경 (서버·클라 완료)
3. [x] data/models 경로 server/ 아래로 통일 + 서버 테스트 진입점 정비
4. [ ] ZIP 패키징 (Claude가 NX_ASSISTANT_FINAL 통째 ZIP 생성 → 전달)
       - server/ : 서버 PC 배포 단위 (README_SERVER.md, smoke_test.py 포함)
       - client/ : 사용자 PC 배포 단위 (README_CLIENT.md 포함)
5. [ ] 로컬 PC: ZIP 풀고 server/data/, server/models/ 배치
       - **(구) MEG_STANDARD 폴더 → MECH_STANDARD 로 이름 변경 필요** (README_SERVER 2-③)
       - settings.json 작성 (Gauss 키는 meg_chatbot experiment_config.json에서)
       - 서버 실행 → smoke_test.py 로 ask 까지 검증
6. [ ] 로컬 PC: WinForm 앱 ↔ 서버 통합 테스트 (Gauss로 DB 조회)
7. [ ] (위 테스트 결과 보고) C# ↔ 서버 연결 수정 — 실제 동작 확인 후 결정

### 다음 버전 (챗봇 정규화)
8. [ ] 쿼리 재작성 LLM 추가 (Gauss)
9. [ ] Gauss/GPT 답변 분기:
       - Gauss → 서버에서 답변 작성 (지금 구조)
       - GPT → 서버는 검색결과만 반환 → winform GPT가 답변 작성
10. [ ] meg_chatbot의 검증된 검색 로직을 필요한 것만 골라서 가져오기

### 나중에 다듬기 (급하지 않음)
- [ ] GPT 로그인 창 깜빡임 제거 (첫 ProbeAsync 전 페이지 로딩 대기)
- [x] LLM 선택 → 홈 화면 뒤로가기 버튼 (2026-06-10 완료: 홈에서 ← 누르면 AI 모델 재선택)
- [ ] **도메인 목록 서버화** (다우니 요청): 현재 `DomainSelectView.cs`에 4개 도메인(키·이름·설명·아이콘) 하드코딩.
      서버 `GET /mech/domains`(key/display_name/description 제공)로 받아오도록 변경. 단 아이콘(IconKind)은 서버에 없으니 "도메인키→아이콘" 매핑만 클라에 유지.
      → 같이 볼 것: `DbKeyPrompts.For()`(도메인별 페이지 문구)도 도메인 키에 묶여 있어, 도메인 동적화 시 처리 방안 필요.
- [ ] `AiSelectView` 의 "gauss:o4-instruct 모델" 라벨이 하드코딩 — 서버 registry(`available_models`)와 어긋날 수 있음. 표시용이라 급하진 않으나 정리 대상.
- [ ] 설정 페이지: GPT 로그아웃 / 재로그인 버튼
- [ ] 설정 페이지 동작 자연스럽게 다듬기 (다우니 요청): "관심분야 재설정"·"외부 AI 재로그인"의 진입/복귀 흐름 매끄럽게.
      현재 외부 AI 재로그인 = AI 선택 화면 재진입(임시). GPT 세션만 조용히 재로그인하는 방식 등 검토.
- [x] 실제 Gauss/GPT 로고 이미지 교체 (2026-06-11 완료: PNG 임베드 + BrandLogo 렌더)
- [ ] UI 파일 분리 리팩토링 (UiKit/MainForm을 기능별로)

### 2차 배포 (1000명)
- [ ] 1차 라우터 추가 (RouterClient에 로직 보존됨)
  - 임시채팅 격리 버그 해결 필요: ResetTemporaryChatAsync가 기존 세션 못 지움
    → 2번째 질문부터 응답 못읽음 + 이전 맥락 유지(격리 안됨)
- [ ] DB 내부 라우터 (도메인 판단 LLM)
- [ ] Rolling Summary 히스토리, Hybrid Retriever, Gauss 스트리밍(SSE)
- [ ] Gauss rate limit 대응, DB MCP 멀티워커

### 운영/확장성 — 나중에 확인 (사용자 늘어날 때)

> 매 질문마다 서버에서 ollama 임베딩 + reranker 가 GPU 를 사용함.
> 단, 무거운 답변 생성은 Gauss(외부 API)가 하므로 로컬 GPU 부담은 임베딩+reranker 로 한정됨.

- [ ] **로컬 PC GPU 사양 파악** → qwen3-embedding:4b(양자화 시 VRAM ~3GB 안팎) + reranker(bge-reranker-v2-m3, ~0.5B) 의 VRAM·추론속도 가늠
- [ ] **부하 테스트**: 동시 5/10/20명 질문 시 응답시간 곡선 측정 → 단일 PC 가 쾌적하게 감당하는 인원 산정
       (등록 100명 ≠ 동시 요청 100개. 실제 동시 요청은 보통 한 자릿수)
- [ ] **병목 후보 점검**:
       ① GPU 에서 임베딩+reranker 직렬 처리(동시 폭주 시 큐잉·지연)
       ② Gauss API rate limit (외부, 동시 답변 생성 몰릴 때)
       ③ 단일 DB MCP 서버 프로세스 한계
- [ ] **확장 카드(필요 시 점진 적용)**:
       OLLAMA_NUM_PARALLEL 병렬 설정 / DB MCP 멀티워커 / 자주 묻는 질문 임베딩 캐싱 /
       reranker 경량화·조건부 실행 / 더 작은 임베딩 모델로 교체 / GPU 증설·임베딩 서버 분리

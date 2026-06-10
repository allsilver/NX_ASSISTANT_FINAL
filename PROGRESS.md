# NX Assistant 개발 진행상황

> 최종 업데이트: 2026-06-10
> 진행 상황과 다음 할 일만 기록합니다. 프로젝트 구조·환경·규칙은 CLAUDE.md 참조.

---

## ✅ 완료된 작업

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
- [ ] LLM 선택 → 홈 화면에 뒤로가기 버튼
- [ ] **도메인 목록 서버화** (다우니 요청): 현재 `DomainSelectView.cs`에 4개 도메인(키·이름·설명·아이콘) 하드코딩.
      서버 `GET /mech/domains`(key/display_name/description 제공)로 받아오도록 변경. 단 아이콘(IconKind)은 서버에 없으니 "도메인키→아이콘" 매핑만 클라에 유지.
      → 같이 볼 것: `DbKeyPrompts.For()`(도메인별 페이지 문구)도 도메인 키에 묶여 있어, 도메인 동적화 시 처리 방안 필요.
- [ ] `AiSelectView` 의 "gauss:o4-instruct 모델" 라벨이 하드코딩 — 서버 registry(`available_models`)와 어긋날 수 있음. 표시용이라 급하진 않으나 정리 대상.
- [ ] 설정 페이지: GPT 로그아웃 / 재로그인 버튼
- [ ] 설정 페이지 동작 자연스럽게 다듬기 (다우니 요청): "관심분야 재설정"·"외부 AI 재로그인"의 진입/복귀 흐름 매끄럽게.
      현재 외부 AI 재로그인 = AI 선택 화면 재진입(임시). GPT 세션만 조용히 재로그인하는 방식 등 검토.
- [ ] 실제 Gauss/GPT 로고 이미지 교체 (현재 임시 직접그림)
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

# NX Assistant 개발 진행상황

> 최종 업데이트: 2026-06-09
> 진행 상황과 다음 할 일만 기록합니다. 프로젝트 구조·환경·규칙은 CLAUDE.md 참조.

---

## ✅ 완료된 작업

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
- [ ] 설정 페이지: GPT 로그아웃 / 재로그인 버튼
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
